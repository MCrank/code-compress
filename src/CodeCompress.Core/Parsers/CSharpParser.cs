using System.Text;
using CodeCompress.Core.Models;
using TreeSitter;

namespace CodeCompress.Core.Parsers;

public sealed class CSharpParser : ILanguageParser
{
    private const string SymbolQuery = """
        [
          (namespace_declaration name: (_) @name body: (declaration_list) @body) @decl
          (file_scoped_namespace_declaration name: (_) @name) @decl
          (class_declaration name: (identifier) @name body: (declaration_list) @body) @decl
          (struct_declaration name: (identifier) @name body: (declaration_list) @body) @decl
          (interface_declaration name: (identifier) @name body: (declaration_list) @body) @decl
          (record_declaration name: (identifier) @name body: (declaration_list) @body) @decl
          (record_declaration name: (identifier) @name) @decl
          (enum_declaration name: (identifier) @name body: (_) @body) @decl
          (method_declaration name: (identifier) @name body: (block) @body) @decl
          (method_declaration name: (identifier) @name) @decl
          (constructor_declaration name: (identifier) @name body: (block) @body) @decl
          (constructor_declaration name: (identifier) @name) @decl
          (destructor_declaration name: (identifier) @name body: (block) @body) @decl
          (destructor_declaration name: (identifier) @name) @decl
          (operator_declaration body: (block) @body) @decl
          (operator_declaration) @decl
          (property_declaration name: (identifier) @name) @decl
          (indexer_declaration accessors: (_) @body) @decl
        ]
        """;

    private static readonly HashSet<string> ContainerNodeTypes = new(StringComparer.Ordinal)
    {
        "class_declaration", "struct_declaration", "interface_declaration", "record_declaration"
    };

    private static readonly HashSet<string> StopNodeTypes = new(StringComparer.Ordinal)
    {
        "namespace_declaration", "file_scoped_namespace_declaration", "compilation_unit"
    };

    private static readonly string[] CSharpModifiers =
    [
        "public", "private", "protected", "internal", "static", "abstract",
        "sealed", "partial", "readonly", "file", "required", "new",
        "virtual", "override", "async", "extern", "unsafe", "volatile"
    ];

    public string LanguageId => "csharp";

    public IReadOnlyList<string> FileExtensions { get; } = [".cs"];

    public ParseResult Parse(string filePath, ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty)
            return new ParseResult([], []);

        var bytes = content.ToArray();
        var text = Encoding.UTF8.GetString(bytes);
        var lines = text.Split('\n');

        using var language = new Language("c-sharp");
        using var parser = new Parser(language);
        using var tree = parser.Parse(text);
        if (tree is null)
            return new ParseResult([], []);

        var symbols = new List<SymbolInfo>();
        var deps = new List<DependencyInfo>();

        ExtractDependencies(tree.RootNode, language, deps);

        // Pass 1: collect all declarations into a map (startIndex → (decl, name, body))
        var declMap = new Dictionary<int, (Node Decl, Node? Name, Node? Body)>();
        using var symQuery = new Query(language, SymbolQuery);
        foreach (var match in symQuery.Execute(tree.RootNode).Matches)
        {
            Node? decl = null, name = null, body = null;
            foreach (var cap in match.Captures)
            {
                switch (cap.Name)
                {
                    case "decl": decl = cap.Node; break;
                    case "name": name = cap.Node; break;
                    case "body": body = cap.Node; break;
                }
            }
            if (decl is null) continue;

            var key = decl.StartIndex;
            if (!declMap.TryGetValue(key, out var existing))
                declMap[key] = (decl, name, body);
            else
                declMap[key] = (existing.Decl, existing.Name ?? name, existing.Body ?? body);
        }

        // Pass 2: build symbols in document order
        foreach (var (_, (decl, nameNode, body)) in declMap.OrderBy(kv => kv.Key))
        {
            var symbolName = nameNode?.Text ?? ExtractOperatorName(decl, bytes);
            if (string.IsNullOrEmpty(symbolName)) continue;

            // Prefix finalizer name with ~
            if (decl.Type == "destructor_declaration")
                symbolName = "~" + symbolName;

            // Prepend explicit interface specifier (e.g., IDisposable.Dispose)
            if (decl.Type == "method_declaration" && nameNode is not null)
                symbolName = ExtractExplicitInterfaceName(decl, nameNode.Text);

            var kind = GetKind(decl.Type);
            var byteOffset = decl.StartIndex;
            var byteLength = decl.EndIndex - decl.StartIndex;
            var lineStart = decl.StartPosition.Row + 1;
            var lineEnd = decl.EndPosition.Row + 1;
            var sig = ExtractSignature(decl, body, bytes);
            var parentName = FindParentName(decl, declMap);
            var vis = DeriveVisibility(sig, parentName is not null);
            var doc = ExtractDocComment(lines, lineStart);

            int? bodyLineStart = null, bodyLineEnd = null;
            if (body is not null)
            {
                var bls = body.StartPosition.Row + 2;
                var ble = body.EndPosition.Row;
                if (bls <= ble)
                {
                    bodyLineStart = bls;
                    bodyLineEnd = ble;
                }
            }

            symbols.Add(new SymbolInfo(
                Name: symbolName,
                Kind: kind,
                Signature: sig,
                ParentSymbol: parentName,
                ByteOffset: byteOffset,
                ByteLength: byteLength,
                LineStart: lineStart,
                LineEnd: lineEnd,
                Visibility: vis,
                DocComment: doc,
                BodyLineStart: bodyLineStart,
                BodyLineEnd: bodyLineEnd));
        }

        // Extract primary constructor parameters (record and class)
        ExtractPrimaryConstructorParams(tree.RootNode, language, bytes, declMap, symbols);

        return new ParseResult(symbols, deps);
    }

    private static void ExtractDependencies(Node root, Language language, List<DependencyInfo> deps)
    {
        using var q = new Query(language, "(using_directive) @u");
        foreach (var node in q.Execute(root).Captures.Select(cap => cap.Node))
        {
            var nodeText = node.Text.Trim();

            // Handle "global using X;" and "using X;"
            string content;
            if (nodeText.StartsWith("global using ", StringComparison.Ordinal))
                content = nodeText["global using ".Length..].TrimEnd(';').Trim();
            else if (nodeText.StartsWith("using ", StringComparison.Ordinal))
                content = nodeText["using ".Length..].TrimEnd(';').Trim();
            else
                continue;

            if (content.StartsWith("static ", StringComparison.Ordinal))
                content = content["static ".Length..].Trim();

            string? alias = null;
            var eqIdx = content.IndexOf('=', StringComparison.Ordinal);
            if (eqIdx >= 0)
            {
                alias = content[..eqIdx].Trim();
                content = content[(eqIdx + 1)..].Trim();
            }

            if (!string.IsNullOrWhiteSpace(content))
                deps.Add(new DependencyInfo(RequirePath: content, Alias: alias));
        }
    }

    private static void ExtractPrimaryConstructorParams(
        Node root,
        Language language,
        byte[] bytes,
        Dictionary<int, (Node Decl, Node? Name, Node? Body)> declMap,
        List<SymbolInfo> symbols)
    {
        using var q = new Query(language, "(parameter_list (parameter) @param)");
        foreach (var param in q.Execute(root).Captures.Select(cap => cap.Node))
        {
            // grandparent must be record_declaration or class_declaration
            var grandParent = param.Parent?.Parent;
            if (grandParent is null) continue;

            var gType = grandParent.Type;
            if (gType is not ("record_declaration" or "class_declaration")) continue;

            var gKey = grandParent.StartIndex;
            if (!declMap.TryGetValue(gKey, out var entry) || entry.Name is null) continue;

            var typeName = entry.Name.Text;
            var paramName = ExtractParamName(param.Text.Trim());
            if (string.IsNullOrEmpty(paramName)) continue;

            var sig = Encoding.UTF8.GetString(bytes, param.StartIndex, param.EndIndex - param.StartIndex).Trim();

            symbols.Add(new SymbolInfo(
                Name: paramName,
                Kind: SymbolKind.Constant,
                Signature: sig,
                ParentSymbol: typeName,
                ByteOffset: param.StartIndex,
                ByteLength: param.EndIndex - param.StartIndex,
                LineStart: param.StartPosition.Row + 1,
                LineEnd: param.EndPosition.Row + 1,
                Visibility: Visibility.Public,
                DocComment: null));
        }

        // Handle "params T[] name" parameters — the C# grammar places these as raw children
        // of parameter_list (not wrapped in a parameter node)
        using var plq = new Query(language, "(parameter_list) @pl");
        foreach (var pl in plq.Execute(root).Captures.Select(cap => cap.Node))
        {
            var parent = pl.Parent;
            if (parent?.Type is not ("record_declaration" or "class_declaration")) continue;
            if (!declMap.TryGetValue(parent.StartIndex, out var entry) || entry.Name is null) continue;
            var typeName = entry.Name.Text;

            var children = pl.Children;
            for (var i = 0; i < children.Count; i++)
            {
                if (children[i].Type != "params") continue;

                // Found "params" keyword — find the trailing identifier (the parameter name)
                var sigStart = children[i].StartIndex;
                var sigEnd = children[i].EndIndex;
                var paramName = "";
                for (var j = i + 1; j < children.Count; j++)
                {
                    if (children[j].Type is "," or ")") break;
                    sigEnd = children[j].EndIndex;
                    if (children[j].Type == "identifier")
                        paramName = children[j].Text;
                }

                if (string.IsNullOrEmpty(paramName)) continue;

                var sig = Encoding.UTF8.GetString(bytes, sigStart, sigEnd - sigStart).Trim();
                symbols.Add(new SymbolInfo(
                    Name: paramName,
                    Kind: SymbolKind.Constant,
                    Signature: sig,
                    ParentSymbol: typeName,
                    ByteOffset: sigStart,
                    ByteLength: sigEnd - sigStart,
                    LineStart: children[i].StartPosition.Row + 1,
                    LineEnd: children[i].StartPosition.Row + 1,
                    Visibility: Visibility.Public,
                    DocComment: null));
            }
        }
    }

    private static string ExtractParamName(string paramText)
    {
        var text = paramText;
        var eqIdx = text.IndexOf('=', StringComparison.Ordinal);
        if (eqIdx >= 0) text = text[..eqIdx].TrimEnd();
        var lastSpace = text.LastIndexOf(' ');
        return lastSpace >= 0 ? text[(lastSpace + 1)..] : text;
    }

    private static string ExtractSignature(Node decl, Node? body, byte[] bytes)
    {
        var start = decl.StartIndex;

        // Skip leading attribute_list nodes to get the actual declaration start
        var children = decl.Children;
        foreach (var child in children)
        {
            if (child.Type == "attribute_list")
                start = child.EndIndex;
            else
                break;
        }
        // Skip any whitespace/newlines that follow the attributes
        while (start < bytes.Length && bytes[start] is (byte)'\r' or (byte)'\n' or (byte)' ' or (byte)'\t')
            start++;

        int end;
        if (body is not null)
        {
            end = body.StartIndex;
        }
        else
        {
            end = decl.EndIndex;
            // For methods/constructors/destructors strip the expression body — keep only the signature
            if (decl.Type is "method_declaration" or "constructor_declaration" or "destructor_declaration")
            {
                foreach (var child in children)
                {
                    if (child.Type == "arrow_expression_clause")
                    {
                        end = child.StartIndex;
                        break;
                    }
                }
            }
        }

        if (end <= start) end = decl.EndIndex;
        return Encoding.UTF8.GetString(bytes, start, end - start).TrimEnd();
    }

    private static string ExtractExplicitInterfaceName(Node methodDecl, string name)
    {
        var children = methodDecl.Children;
        foreach (var child in children)
        {
            if (child.Type == "explicit_interface_specifier")
                return child.Text + name;
        }
        return name;
    }

    private static string? FindParentName(
        Node decl,
        Dictionary<int, (Node Decl, Node? Name, Node? Body)> declMap)
    {
        var current = decl.Parent;
        while (current is not null)
        {
            if (ContainerNodeTypes.Contains(current.Type))
            {
                return declMap.TryGetValue(current.StartIndex, out var e) && e.Name is not null
                    ? e.Name.Text
                    : null;
            }

            if (StopNodeTypes.Contains(current.Type))
                return null;

            current = current.Parent;
        }

        return null;
    }

    private static string? ExtractOperatorName(Node decl, byte[] bytes)
    {
        if (decl.Type == "indexer_declaration")
            return "this[]";

        if (decl.Type != "operator_declaration")
            return null;

        var text = Encoding.UTF8.GetString(bytes, decl.StartIndex, decl.EndIndex - decl.StartIndex);
        var opIdx = text.IndexOf("operator", StringComparison.Ordinal);
        if (opIdx < 0) return null;

        var afterOp = text[(opIdx + "operator".Length)..].TrimStart();
        var parenIdx = afterOp.IndexOf('(', StringComparison.Ordinal);
        return parenIdx > 0 ? $"operator {afterOp[..parenIdx].Trim()}" : null;
    }

    private static SymbolKind GetKind(string nodeType) => nodeType switch
    {
        "namespace_declaration" or "file_scoped_namespace_declaration" => SymbolKind.Module,
        "class_declaration" or "struct_declaration" => SymbolKind.Class,
        "interface_declaration" => SymbolKind.Interface,
        "record_declaration" => SymbolKind.Record,
        "enum_declaration" => SymbolKind.Enum,
        "method_declaration" or "constructor_declaration" or "destructor_declaration"
            or "operator_declaration" or "indexer_declaration" => SymbolKind.Method,
        "property_declaration" => SymbolKind.Constant,
        _ => SymbolKind.Function
    };

    private static Visibility DeriveVisibility(string signature, bool isNested)
    {
        var tokens = signature.Split(' ', 8, StringSplitOptions.RemoveEmptyEntries);
        var hasPrivate = false;
        var hasProtected = false;
        var hasInternal = false;
        var hasPublic = false;
        var hasFile = false;

        foreach (var token in tokens)
        {
            if (!Array.Exists(CSharpModifiers, m => string.Equals(m, token, StringComparison.Ordinal)))
                break;

            switch (token)
            {
                case "private": hasPrivate = true; break;
                case "protected": hasProtected = true; break;
                case "internal": hasInternal = true; break;
                case "public": hasPublic = true; break;
                case "file": hasFile = true; break;
            }
        }

        if (hasPrivate && hasProtected) return Visibility.Private;
        if (hasProtected && hasInternal) return Visibility.Public;
        if (hasPrivate) return Visibility.Private;
        if (hasProtected) return Visibility.Private;
        if (hasFile) return Visibility.Private;
        if (hasPublic || hasInternal) return Visibility.Public;
        return isNested ? Visibility.Private : Visibility.Public;
    }

    private static string? ExtractDocComment(string[] lines, int lineStart)
    {
        var result = new List<string>();
        var idx = lineStart - 2; // 0-indexed line before declaration

        while (idx >= 0)
        {
            var line = lines[idx].TrimEnd('\r').Trim();
            if (line.StartsWith("///", StringComparison.Ordinal))
            {
                result.Insert(0, line);
                idx--;
            }
            else
            {
                break;
            }
        }

        return result.Count > 0 ? string.Join("\n", result) : null;
    }
}

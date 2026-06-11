using System.Text;
using CodeCompress.Core.Models;
using TreeSitter;

namespace CodeCompress.Core.Parsers;

public sealed class TypeScriptJavaScriptParser : ILanguageParser
{
    private const string TsSymbolQuery = """
        [
          (class_declaration name: (_) @name body: (class_body) @body) @decl
          (abstract_class_declaration name: (_) @name body: (class_body) @body) @decl
          (interface_declaration name: (_) @name body: (interface_body) @body) @decl
          (enum_declaration name: (_) @name body: (enum_body) @body) @decl
          (type_alias_declaration name: (_) @name) @decl
          (function_declaration name: (identifier) @name body: (statement_block) @body) @decl
          (generator_function_declaration name: (identifier) @name body: (statement_block) @body) @decl
          (method_definition name: (_) @name body: (statement_block) @body) @decl
          (method_signature name: (_) @name) @decl
          (lexical_declaration (variable_declarator name: (identifier) @name value: (_) @body)) @decl
          (lexical_declaration (variable_declarator name: (identifier) @name)) @decl
        ]
        """;

    private const string JsSymbolQuery = """
        [
          (class_declaration name: (_) @name body: (class_body) @body) @decl
          (function_declaration name: (identifier) @name body: (statement_block) @body) @decl
          (generator_function_declaration name: (identifier) @name body: (statement_block) @body) @decl
          (method_definition name: (_) @name body: (statement_block) @body) @decl
          (lexical_declaration (variable_declarator name: (identifier) @name value: (_) @body)) @decl
          (lexical_declaration (variable_declarator name: (identifier) @name)) @decl
        ]
        """;

    private static readonly HashSet<string> ContainerNodeTypes = new(StringComparer.Ordinal)
    {
        "class_declaration", "abstract_class_declaration", "interface_declaration"
    };

    public string LanguageId => "typescript";

    public IReadOnlyList<string> FileExtensions { get; } = [".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs"];

    public ParseResult Parse(string filePath, ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty)
            return new ParseResult([], []);

        var bytes = content.ToArray();
        var text = Encoding.UTF8.GetString(bytes);
        var lines = text.Split('\n');

        var ext = Path.GetExtension(filePath);
        string langId;
        if (string.Equals(ext, ".tsx", StringComparison.OrdinalIgnoreCase))
            langId = "tsx";
        else if (string.Equals(ext, ".ts", StringComparison.OrdinalIgnoreCase))
            langId = "typescript";
        else
            langId = "javascript";
        var isTypeScript = langId is "typescript" or "tsx";

        using var language = new Language(langId);
        using var parser = new Parser(language);
        using var tree = parser.Parse(text);
        if (tree is null)
            return new ParseResult([], []);

        var symbols = new List<SymbolInfo>();
        var deps = new List<DependencyInfo>();

        ExtractImports(tree.RootNode, language, deps);
        ExtractRequires(tree.RootNode, language, deps);

        var symbolQuery = isTypeScript ? TsSymbolQuery : JsSymbolQuery;

        // Pass 1: collect declarations into map (startIndex → (decl, name, body))
        var declMap = new Dictionary<int, (Node Decl, Node? Name, Node? Body)>();
        using var symQuery = new Query(language, symbolQuery);
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
            var symbolName = nameNode?.Text;
            if (string.IsNullOrEmpty(symbolName)) continue;

            // Only capture lexical_declaration at module scope (not inside function bodies)
            if (decl.Type == "lexical_declaration" && !IsModuleLevel(decl))
                continue;

            var kind = GetKind(decl, body);
            var lineStart = decl.StartPosition.Row + 1;
            var lineEnd = decl.EndPosition.Row + 1;
            var sig = ExtractSignature(decl, body, bytes);
            var parentName = FindParentName(decl, declMap);
            var vis = IsExported(decl) ? Visibility.Public : Visibility.Private;
            var doc = ExtractJsDocComment(lines, lineStart);

            int? bodyLineStart = null, bodyLineEnd = null;
            if (body is not null && body.Type is "statement_block" or "class_body" or "interface_body")
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
                ByteOffset: decl.StartIndex,
                ByteLength: decl.EndIndex - decl.StartIndex,
                LineStart: lineStart,
                LineEnd: lineEnd,
                Visibility: vis,
                DocComment: doc,
                BodyLineStart: bodyLineStart,
                BodyLineEnd: bodyLineEnd));
        }

        return new ParseResult(symbols, deps);
    }

    private static void ExtractImports(Node root, Language language, List<DependencyInfo> deps)
    {
        using var q = new Query(language, "(import_statement source: (string (string_fragment) @path)) @import");
        foreach (var cap in q.Execute(root).Captures.Where(c => c.Name == "path"))
            deps.Add(new DependencyInfo(RequirePath: cap.Node.Text, Alias: null));
    }

    private static void ExtractRequires(Node root, Language language, List<DependencyInfo> deps)
    {
        using var q = new Query(language, "(call_expression function: (identifier) @fn arguments: (arguments (string (string_fragment) @path)))");
        string? pendingFn = null;
        foreach (var cap in q.Execute(root).Captures)
        {
            switch (cap.Name)
            {
                case "fn":
                    pendingFn = cap.Node.Text;
                    break;
                case "path":
                    if (pendingFn == "require")
                        deps.Add(new DependencyInfo(RequirePath: cap.Node.Text, Alias: null));
                    pendingFn = null;
                    break;
            }
        }
    }

    private static bool IsModuleLevel(Node decl)
    {
        var parent = decl.Parent;
        if (parent is null) return true;
        return parent.Type is "program" or "export_statement";
    }

    private static bool IsExported(Node decl)
    {
        var parent = decl.Parent;
        return parent?.Type == "export_statement";
    }

    private static SymbolKind GetKind(Node decl, Node? body) => decl.Type switch
    {
        "class_declaration" or "abstract_class_declaration" => SymbolKind.Class,
        "interface_declaration" => SymbolKind.Interface,
        "enum_declaration" => SymbolKind.Enum,
        "type_alias_declaration" => SymbolKind.Type,
        "function_declaration" or "generator_function_declaration" => SymbolKind.Function,
        "method_definition" or "method_signature" => SymbolKind.Method,
        "lexical_declaration" => body?.Type is "arrow_function" or "function_expression" or "generator_function"
            ? SymbolKind.Function : SymbolKind.Constant,
        _ => SymbolKind.Function
    };

    private static string ExtractSignature(Node decl, Node? body, byte[] bytes)
    {
        var start = decl.StartIndex;
        int end;

        if (body is not null && body.Type is "statement_block" or "class_body" or "interface_body" or "enum_body")
            end = body.StartIndex;
        else
            end = decl.EndIndex;

        if (end <= start) end = decl.EndIndex;
        return Encoding.UTF8.GetString(bytes, start, end - start).TrimEnd().TrimEnd(';');
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
            // Stop at module scope
            if (current.Type is "program" or "statement_block")
                return null;

            current = current.Parent;
        }
        return null;
    }

    private static string? ExtractJsDocComment(string[] lines, int lineStart)
    {
        var idx = lineStart - 2;
        if (idx < 0) return null;

        var line = lines[idx].TrimEnd('\r').Trim();

        // Single-line JSDoc
        if (line.StartsWith("/**", StringComparison.Ordinal) && line.Contains("*/", StringComparison.Ordinal))
            return line;

        // Multi-line JSDoc ending on this line
        if (!line.Contains("*/", StringComparison.Ordinal))
            return null;

        var result = new List<string> { line };
        idx--;
        while (idx >= 0)
        {
            line = lines[idx].TrimEnd('\r').Trim();
            result.Insert(0, line);
            if (line.StartsWith("/**", StringComparison.Ordinal))
                return string.Join("\n", result);
            idx--;
        }
        return null;
    }
}

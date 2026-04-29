using System.Text;
using CodeCompress.Core.Models;
using TreeSitter;

namespace CodeCompress.Core.Parsers;

public sealed class PythonParser : ILanguageParser
{
    private const string SymbolQuery = """
        [
          (class_definition name: (identifier) @name body: (block) @body) @decl
          (function_definition name: (identifier) @name body: (block) @body) @decl
        ]
        """;

    private static readonly HashSet<string> ContainerNodeTypes = new(StringComparer.Ordinal)
    {
        "class_definition"
    };

    public string LanguageId => "python";

    public IReadOnlyList<string> FileExtensions { get; } = [".py", ".pyi"];

    public ParseResult Parse(string filePath, ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty)
            return new ParseResult([], []);

        var bytes = content.ToArray();
        var text = Encoding.UTF8.GetString(bytes);

        using var language = new Language("python");
        using var parser = new Parser(language);
        using var tree = parser.Parse(text);
        if (tree is null)
            return new ParseResult([], []);

        var symbols = new List<SymbolInfo>();
        var deps = new List<DependencyInfo>();

        ExtractImports(tree.RootNode, language, deps);

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

        foreach (var (_, (decl, nameNode, body)) in declMap.OrderBy(kv => kv.Key))
        {
            var symbolName = nameNode?.Text;
            if (string.IsNullOrEmpty(symbolName)) continue;

            // Use decorated_definition as effective node if the decl is decorated
            var effectiveNode = decl.Parent?.Type == "decorated_definition" ? decl.Parent : decl;

            var kind = GetKind(decl);
            var lineStart = effectiveNode.StartPosition.Row + 1;
            var lineEnd = effectiveNode.EndPosition.Row + 1;
            var sig = ExtractSignature(effectiveNode, body, bytes);
            var parentName = FindParentName(decl, declMap);
            var vis = symbolName.StartsWith('_') ? Visibility.Private : Visibility.Public;

            int? bodyLineStart = null, bodyLineEnd = null;
            if (body is not null)
            {
                var bls = body.StartPosition.Row + 2;
                var ble = body.EndPosition.Row;
                if (bls <= ble) { bodyLineStart = bls; bodyLineEnd = ble; }
            }

            symbols.Add(new SymbolInfo(
                Name: symbolName,
                Kind: kind,
                Signature: sig,
                ParentSymbol: parentName,
                ByteOffset: effectiveNode.StartIndex,
                ByteLength: effectiveNode.EndIndex - effectiveNode.StartIndex,
                LineStart: lineStart,
                LineEnd: lineEnd,
                Visibility: vis,
                DocComment: null,
                BodyLineStart: bodyLineStart,
                BodyLineEnd: bodyLineEnd));
        }

        ExtractModuleConstants(tree.RootNode, bytes, symbols);

        return new ParseResult(symbols, deps);
    }

    private static void ExtractImports(Node root, Language language, List<DependencyInfo> deps)
    {
        using var q = new Query(language, "[(import_statement) @imp (import_from_statement) @imp]");
        foreach (var cap in q.Execute(root).Captures)
        {
            var nodeText = cap.Node.Text.Trim();
            if (nodeText.StartsWith("import ", StringComparison.Ordinal))
            {
                var rest = nodeText["import ".Length..];
                foreach (var part in rest.Split(','))
                {
                    var name = part.Trim().Split(' ')[0];
                    if (!string.IsNullOrEmpty(name))
                        deps.Add(new DependencyInfo(RequirePath: name, Alias: null));
                }
            }
            else if (nodeText.StartsWith("from ", StringComparison.Ordinal))
            {
                var rest = nodeText["from ".Length..];
                var importIdx = rest.IndexOf(" import ", StringComparison.Ordinal);
                if (importIdx >= 0)
                {
                    var modulePath = rest[..importIdx].Trim();
                    var cleanPath = modulePath.TrimStart('.');
                    if (string.IsNullOrEmpty(cleanPath)) cleanPath = modulePath;
                    deps.Add(new DependencyInfo(RequirePath: cleanPath.Replace('.', '/'), Alias: null));
                }
            }
        }
    }

    private static void ExtractModuleConstants(Node root, byte[] bytes, List<SymbolInfo> symbols)
    {
        var children = root.Children;
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            string? name = null;

            if (child.Type is "assignment" or "annotated_assignment")
            {
                name = FindFirstIdentifier(child);
            }
            else if (child.Type == "expression_statement")
            {
                // Older grammar versions wrap assignment in expression_statement
                var inner = child.Children.Count > 0 ? child.Children[0] : null;
                if (inner?.Type == "assignment")
                    name = FindFirstIdentifier(inner);
            }

            if (name is null || !IsAllCaps(name)) continue;

            var sig = Encoding.UTF8.GetString(bytes, child.StartIndex, child.EndIndex - child.StartIndex).Trim();
            symbols.Add(new SymbolInfo(
                Name: name,
                Kind: SymbolKind.Constant,
                Signature: sig,
                ParentSymbol: null,
                ByteOffset: child.StartIndex,
                ByteLength: child.EndIndex - child.StartIndex,
                LineStart: child.StartPosition.Row + 1,
                LineEnd: child.EndPosition.Row + 1,
                Visibility: Visibility.Public,
                DocComment: null));
        }
    }

    private static string? FindFirstIdentifier(Node node)
    {
        var children = node.Children;
        for (var i = 0; i < children.Count; i++)
        {
            if (children[i].Type == "identifier")
                return children[i].Text;
        }
        return null;
    }

    private static bool IsAllCaps(string name) =>
        name.Length >= 1 && char.IsUpper(name[0]) &&
        name.All(c => char.IsUpper(c) || c == '_' || char.IsDigit(c));

    private static SymbolKind GetKind(Node decl)
    {
        if (decl.Type == "class_definition") return SymbolKind.Class;

        var current = decl.Parent;
        while (current is not null)
        {
            if (current.Type == "class_definition") return SymbolKind.Method;
            if (current.Type == "module") break;
            current = current.Parent;
        }
        return SymbolKind.Function;
    }

    private static string ExtractSignature(Node effectiveNode, Node? body, byte[] bytes)
    {
        var start = effectiveNode.StartIndex;
        var end = body is not null ? body.StartIndex : effectiveNode.EndIndex;
        if (end <= start) end = effectiveNode.EndIndex;
        return Encoding.UTF8.GetString(bytes, start, end - start).TrimEnd();
    }

    private static string? FindParentName(
        Node decl,
        Dictionary<int, (Node Decl, Node? Name, Node? Body)> declMap)
    {
        var current = decl.Parent;
        while (current is not null)
        {
            if (ContainerNodeTypes.Contains(current.Type))
                return declMap.TryGetValue(current.StartIndex, out var e) && e.Name is not null
                    ? e.Name.Text : null;
            if (current.Type == "module") return null;
            current = current.Parent;
        }
        return null;
    }
}

using System.Text;
using CodeCompress.Core.Models;
using TreeSitter;

namespace CodeCompress.Core.Parsers;

public sealed class JavaParser : ILanguageParser
{
    private const string SymbolQuery = """
        [
          (class_declaration name: (identifier) @name body: (class_body) @body) @decl
          (interface_declaration name: (identifier) @name body: (interface_body) @body) @decl
          (enum_declaration name: (identifier) @name body: (enum_body) @body) @decl
          (record_declaration name: (identifier) @name body: (class_body) @body) @decl
          (annotation_type_declaration name: (identifier) @name body: (annotation_type_body) @body) @decl
          (method_declaration name: (identifier) @name body: (block) @body) @decl
          (method_declaration name: (identifier) @name) @decl
          (constructor_declaration name: (identifier) @name body: (constructor_body) @body) @decl
          (field_declaration declarator: (variable_declarator name: (identifier) @name)) @decl
        ]
        """;

    private static readonly HashSet<string> ContainerNodeTypes = new(StringComparer.Ordinal)
    {
        "class_declaration", "interface_declaration", "enum_declaration", "record_declaration"
    };

    public string LanguageId => "java";

    public IReadOnlyList<string> FileExtensions { get; } = [".java"];

    public ParseResult Parse(string filePath, ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty)
            return new ParseResult([], []);

        var bytes = content.ToArray();
        var text = Encoding.UTF8.GetString(bytes);
        var lines = text.Split('\n');

        using var language = new Language("java");
        using var parser = new Parser(language);
        using var tree = parser.Parse(text);
        if (tree is null)
            return new ParseResult([], []);

        var symbols = new List<SymbolInfo>();
        var deps = new List<DependencyInfo>();

        ExtractDependencies(tree.RootNode, language, deps);

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

            // Static final fields only for constants
            if (decl.Type == "field_declaration" && !IsStaticFinal(decl))
                continue;

            var kind = GetKind(decl);
            var lineStart = decl.StartPosition.Row + 1;
            var lineEnd = decl.EndPosition.Row + 1;
            var sig = ExtractSignature(decl, body, bytes);
            var parentName = FindParentName(decl, declMap);
            var vis = DeriveVisibility(decl);
            var doc = ExtractJavadocComment(lines, decl);

            int? bodyLineStart = null, bodyLineEnd = null;
            if (body is not null && body.Type is "class_body" or "interface_body" or "enum_body" or "block" or "constructor_body" or "annotation_type_body")
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

    private static void ExtractDependencies(Node root, Language language, List<DependencyInfo> deps)
    {
        using var q = new Query(language, "[(package_declaration) @node (import_declaration) @node]");
        foreach (var cap in q.Execute(root).Captures)
        {
            var nodeText = cap.Node.Text.Trim().TrimEnd(';');
            if (nodeText.StartsWith("package ", StringComparison.Ordinal))
            {
                var path = nodeText["package ".Length..].Trim();
                deps.Add(new DependencyInfo(RequirePath: path, Alias: null));
            }
            else if (nodeText.StartsWith("import ", StringComparison.Ordinal))
            {
                var rest = nodeText["import ".Length..].Trim();
                if (rest.StartsWith("static ", StringComparison.Ordinal))
                    rest = rest["static ".Length..].Trim();
                deps.Add(new DependencyInfo(RequirePath: rest, Alias: null));
            }
        }
    }

    private static bool IsStaticFinal(Node fieldDecl)
    {
        var children = fieldDecl.Children;
        for (var i = 0; i < children.Count; i++)
        {
            if (children[i].Type == "modifiers")
            {
                var modText = children[i].Text;
                return modText.Contains("static", StringComparison.Ordinal) &&
                       modText.Contains("final", StringComparison.Ordinal);
            }
        }
        return false;
    }

    private static SymbolKind GetKind(Node decl) => decl.Type switch
    {
        "class_declaration" => SymbolKind.Class,
        "interface_declaration" => SymbolKind.Interface,
        "enum_declaration" => SymbolKind.Enum,
        "record_declaration" => SymbolKind.Record,
        "annotation_type_declaration" => SymbolKind.Type,
        "method_declaration" or "constructor_declaration" => SymbolKind.Method,
        "field_declaration" => SymbolKind.Constant,
        _ => SymbolKind.Method
    };

    private static string ExtractSignature(Node decl, Node? body, byte[] bytes)
    {
        var start = decl.StartIndex;
        int end;

        if (body is not null)
            end = body.StartIndex;
        else
        {
            end = decl.EndIndex;
            // Strip trailing semicolon for bodyless declarations
            while (end > start && bytes[end - 1] is (byte)';' or (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t')
                end--;
        }

        if (end <= start) end = decl.EndIndex;
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
            if (current.Type == "program") return null;
            current = current.Parent;
        }
        return null;
    }

    private static Visibility DeriveVisibility(Node decl)
    {
        var children = decl.Children;
        for (var i = 0; i < children.Count; i++)
        {
            if (children[i].Type == "modifiers")
            {
                var modText = children[i].Text;
                if (modText.Contains("public", StringComparison.Ordinal)) return Visibility.Public;
                return Visibility.Private;
            }
        }
        return Visibility.Private;
    }

    private static string? ExtractJavadocComment(string[] lines, Node decl)
    {
        // Walk backwards from the line before the declaration (skipping annotations in the decl itself)
        // Find the actual first line of the declaration (annotations may be on preceding lines)
        var declStartRow = decl.StartPosition.Row;

        // Look at the line directly before the declaration
        var idx = declStartRow - 1;
        if (idx < 0) return null;

        var line = lines[idx].TrimEnd('\r').Trim();

        // Single-line Javadoc: /** ... */
        if (line.StartsWith("/**", StringComparison.Ordinal) && line.Contains("*/", StringComparison.Ordinal))
            return line;

        // End of multi-line Javadoc
        if (!line.Contains("*/", StringComparison.Ordinal)) return null;

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

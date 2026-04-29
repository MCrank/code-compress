using System.Text;
using CodeCompress.Core.Models;
using TreeSitter;

namespace CodeCompress.Core.Parsers;

public sealed class RustParser : ILanguageParser
{
    private const string SymbolQuery = """
        [
          (struct_item name: (type_identifier) @name body: (field_declaration_list) @body) @decl
          (struct_item name: (type_identifier) @name) @decl
          (enum_item name: (type_identifier) @name body: (enum_variant_list) @body) @decl
          (trait_item name: (type_identifier) @name body: (declaration_list) @body) @decl
          (impl_item type: (type_identifier) @name body: (declaration_list) @body) @decl
          (function_item name: (identifier) @name body: (block) @body) @decl
          (const_item name: (identifier) @name) @decl
          (static_item name: (identifier) @name) @decl
          (type_item name: (type_identifier) @name) @decl
          (mod_item name: (identifier) @name body: (declaration_list) @body) @decl
          (mod_item name: (identifier) @name) @decl
          (macro_definition name: (identifier) @name) @decl
        ]
        """;

    private static readonly HashSet<string> ContainerNodeTypes = new(StringComparer.Ordinal)
    {
        "impl_item", "trait_item"
    };

    public string LanguageId => "rust";

    public IReadOnlyList<string> FileExtensions { get; } = [".rs"];

    public ParseResult Parse(string filePath, ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty)
            return new ParseResult([], []);

        var bytes = content.ToArray();
        var text = Encoding.UTF8.GetString(bytes);
        var lines = text.Split('\n');

        using var language = new Language("rust");
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

            // impl_item entries are only containers for parent resolution — not emitted as symbols
            if (decl.Type == "impl_item") continue;

            var kind = GetKind(decl);
            var lineStart = decl.StartPosition.Row + 1;
            var lineEnd = decl.EndPosition.Row + 1;
            var sig = ExtractSignature(decl, body, bytes);
            var parentName = FindParentName(decl, declMap);
            if (decl.Type == "function_item" && parentName is not null)
                kind = SymbolKind.Method;
            var vis = DeriveVisibility(decl, bytes);
            var doc = ExtractDocComment(lines, decl);

            int? bodyLineStart = null, bodyLineEnd = null;
            if (body is not null && body.Type is "block" or "field_declaration_list" or "declaration_list")
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
        using var q = new Query(language, "(use_declaration argument: (_) @path)");
        foreach (var cap in q.Execute(root).Captures)
            deps.Add(new DependencyInfo(RequirePath: cap.Node.Text.Trim(), Alias: null));
    }

    private static SymbolKind GetKind(Node decl) => decl.Type switch
    {
        "struct_item" => SymbolKind.Class,
        "enum_item" => SymbolKind.Enum,
        "trait_item" => SymbolKind.Interface,
        "function_item" => SymbolKind.Function,
        "const_item" or "static_item" => SymbolKind.Constant,
        "type_item" => SymbolKind.Type,
        "mod_item" => SymbolKind.Module,
        "macro_definition" => SymbolKind.Function,
        _ => SymbolKind.Function
    };

    private static string ExtractSignature(Node decl, Node? body, byte[] bytes)
    {
        // Include preceding attribute_item siblings in the signature
        var attrs = GetPrecedingAttributes(decl);

        var start = decl.StartIndex;
        int end;

        if (body is not null && body.Type is "block" or "field_declaration_list" or "declaration_list" or "enum_variant_list")
            end = body.StartIndex;
        else
        {
            end = decl.EndIndex;
            // Strip trailing semicolons
            while (end > start && bytes[end - 1] is (byte)';' or (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t')
                end--;
        }

        if (end <= start) end = decl.EndIndex;
        var baseSig = Encoding.UTF8.GetString(bytes, start, end - start).TrimEnd();

        return attrs.Count > 0
            ? string.Join(" ", attrs) + " " + baseSig
            : baseSig;
    }

    private static List<string> GetPrecedingAttributes(Node decl)
    {
        var result = new List<string>();
        var parent = decl.Parent;
        if (parent is null) return result;

        var children = parent.Children;
        var declIdx = -1;
        for (var i = 0; i < children.Count; i++)
        {
            if (children[i].StartIndex == decl.StartIndex)
            {
                declIdx = i;
                break;
            }
        }

        if (declIdx < 0) return result;

        for (var i = declIdx - 1; i >= 0; i--)
        {
            if (children[i].Type == "attribute_item")
                result.Insert(0, children[i].Text.Trim());
            else
                break;
        }

        return result;
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
            if (current.Type == "source_file") return null;
            current = current.Parent;
        }
        return null;
    }

    private static Visibility DeriveVisibility(Node decl, byte[] bytes)
    {
        // Look at text immediately after start for pub/pub(...)
        var children = decl.Children;
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            if (child.Type == "visibility_modifier")
            {
                var visText = Encoding.UTF8.GetString(bytes, child.StartIndex, child.EndIndex - child.StartIndex).Trim();
                if (visText == "pub") return Visibility.Public;
                return Visibility.Private; // pub(crate), pub(super), etc.
            }
            // Stop at first non-attribute child that isn't a visibility modifier
            if (child.Type != "attribute_item") break;
        }
        return Visibility.Private;
    }

    private static string? ExtractDocComment(string[] lines, Node decl)
    {
        var result = new List<string>();
        var idx = decl.StartPosition.Row - 1;

        // Skip preceding attribute_item lines
        while (idx >= 0)
        {
            var line = lines[idx].TrimEnd('\r').Trim();
            if (line.StartsWith("#[", StringComparison.Ordinal))
                idx--;
            else
                break;
        }

        while (idx >= 0)
        {
            var line = lines[idx].TrimEnd('\r').Trim();
            if (line.StartsWith("///", StringComparison.Ordinal) || line.StartsWith("//!", StringComparison.Ordinal))
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

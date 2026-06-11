using System.Text;
using CodeCompress.Core.Models;
using TreeSitter;

namespace CodeCompress.Core.Parsers;

public sealed class RubyParser : ILanguageParser
{
    private const string SymbolQuery = """
        [
          (class name: (_) @name body: (_) @body) @decl
          (class name: (_) @name) @decl
          (module name: (_) @name body: (_) @body) @decl
          (module name: (_) @name) @decl
          (method name: (_) @name body: (_) @body) @decl
          (method name: (_) @name) @decl
          (singleton_method name: (_) @name body: (_) @body) @decl
          (singleton_method name: (_) @name) @decl
        ]
        """;

    private const string ConstantQuery =
        "(assignment left: (constant) @name right: (_) @body) @decl";

    private static readonly HashSet<string> ContainerNodeTypes = new(StringComparer.Ordinal)
    {
        "class", "module", "singleton_class"
    };

    public string LanguageId => "ruby";

    public IReadOnlyList<string> FileExtensions { get; } = [".rb"];

    public ParseResult Parse(string filePath, ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty)
            return new ParseResult([], []);

        var bytes = content.ToArray();
        var text = Encoding.UTF8.GetString(bytes);
        var lines = text.Split('\n');

        using var language = new Language("ruby");
        using var parser = new Parser(language);
        using var tree = parser.Parse(text);
        if (tree is null)
            return new ParseResult([], []);

        var symbols = new List<SymbolInfo>();

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

            var kind = GetKind(decl);
            var lineStart = decl.StartPosition.Row + 1;
            var lineEnd = decl.EndPosition.Row + 1;
            var sig = ExtractSignature(decl, body, bytes);
            var parentName = FindParentName(decl, declMap);
            var doc = ExtractDocComment(lines, decl);

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
                ByteOffset: decl.StartIndex,
                ByteLength: decl.EndIndex - decl.StartIndex,
                LineStart: lineStart,
                LineEnd: lineEnd,
                Visibility: Visibility.Public,
                DocComment: doc,
                BodyLineStart: bodyLineStart,
                BodyLineEnd: bodyLineEnd));
        }

        ExtractConstants(tree.RootNode, language, bytes, lines, declMap, symbols);

        return new ParseResult(symbols, []);
    }

    private static void ExtractConstants(
        Node root,
        Language language,
        byte[] bytes,
        string[] lines,
        Dictionary<int, (Node Decl, Node? Name, Node? Body)> declMap,
        List<SymbolInfo> symbols)
    {
        using var q = new Query(language, ConstantQuery);
        foreach (var match in q.Execute(root).Matches)
        {
            Node? decl = null, name = null;
            foreach (var cap in match.Captures)
            {
                switch (cap.Name)
                {
                    case "decl": decl = cap.Node; break;
                    case "name": name = cap.Node; break;
                }
            }
            if (decl is null || name is null) continue;

            // Skip if already indexed as part of the symbol query
            if (declMap.ContainsKey(decl.StartIndex)) continue;

            var symbolName = name.Text;
            if (string.IsNullOrEmpty(symbolName)) continue;

            var parentName = FindParentName(decl, declMap);
            var rawSig = Encoding.UTF8.GetString(bytes, decl.StartIndex, decl.EndIndex - decl.StartIndex).Trim();
            var sig = rawSig.Length > 256 ? string.Concat(rawSig.AsSpan(0, 253), "...") : rawSig;

            symbols.Add(new SymbolInfo(
                Name: symbolName,
                Kind: SymbolKind.Constant,
                Signature: sig,
                ParentSymbol: parentName,
                ByteOffset: decl.StartIndex,
                ByteLength: decl.EndIndex - decl.StartIndex,
                LineStart: decl.StartPosition.Row + 1,
                LineEnd: decl.EndPosition.Row + 1,
                Visibility: Visibility.Public,
                DocComment: ExtractDocComment(lines, decl)));
        }
    }

    private static SymbolKind GetKind(Node decl) => decl.Type switch
    {
        "class" => SymbolKind.Class,
        "module" => SymbolKind.Module,
        "method" => IsInsideContainer(decl) ? SymbolKind.Method : SymbolKind.Function,
        "singleton_method" => IsInsideContainer(decl) ? SymbolKind.Method : SymbolKind.Function,
        _ => SymbolKind.Function
    };

    private static bool IsInsideContainer(Node decl)
    {
        var current = decl.Parent;
        while (current is not null)
        {
            if (ContainerNodeTypes.Contains(current.Type)) return true;
            if (current.Type == "program") return false;
            current = current.Parent;
        }
        return false;
    }

    private static string ExtractSignature(Node decl, Node? body, byte[] bytes)
    {
        var start = decl.StartIndex;
        var end = body is not null ? body.StartIndex : decl.EndIndex;
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

    private static string? ExtractDocComment(string[] lines, Node decl)
    {
        var result = new List<string>();
        var idx = decl.StartPosition.Row - 1;

        while (idx >= 0)
        {
            var line = lines[idx].TrimEnd('\r').Trim();
            if (line.StartsWith('#'))
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

using System.Text;
using CodeCompress.Core.Models;
using TreeSitter;

namespace CodeCompress.Core.Parsers;

public sealed class GoParser : ILanguageParser
{
    private const string SymbolQuery = """
        [
          (package_clause (package_identifier) @name) @decl
          (function_declaration name: (identifier) @name body: (block) @body) @decl
          (method_declaration name: (_) @name body: (block) @body) @decl
          (type_declaration (type_spec name: (type_identifier) @name type: (struct_type) @body)) @decl
          (type_declaration (type_spec name: (type_identifier) @name type: (interface_type) @body)) @decl
          (type_declaration (type_spec name: (type_identifier) @name)) @decl
          (const_spec name: (identifier) @name) @decl
          (var_spec name: (identifier) @name) @decl
        ]
        """;

    public string LanguageId => "go";

    public IReadOnlyList<string> FileExtensions { get; } = [".go"];

    public ParseResult Parse(string filePath, ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty)
            return new ParseResult([], []);

        var bytes = content.ToArray();
        var text = Encoding.UTF8.GetString(bytes);
        var lines = text.Split('\n');

        using var language = new Language("go");
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

            // Skip type_declaration that already has a more-specific match (struct/interface)
            // The no-body type_spec pattern would duplicate struct/interface entries
            if (decl.Type == "type_declaration" && body is not null &&
                body.Type is not "struct_type" and not "interface_type")
                continue;

            var kind = GetKind(decl, body);
            var lineStart = decl.StartPosition.Row + 1;
            var lineEnd = decl.EndPosition.Row + 1;
            var sig = ExtractSignature(decl, body, bytes);
            var parentName = ExtractGoReceiverType(decl);
            var vis = DeriveVisibility(symbolName);
            var doc = ExtractDocComment(lines, decl);

            int? bodyLineStart = null, bodyLineEnd = null;
            if (body is not null && body.Type is "block" or "struct_type" or "interface_type")
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

    private static void ExtractImports(Node root, Language language, List<DependencyInfo> deps)
    {
        using var q = new Query(language, "(import_spec path: (interpreted_string_literal) @path)");
        foreach (var pathNode in q.Execute(root).Captures.Select(cap => cap.Node))
        {
            var pathText = pathNode.Text.Trim('"');

            // Check for alias in parent import_spec
            string? alias = null;
            var parent = pathNode.Parent;
            if (parent is not null)
            {
                var children = parent.Children;
                if (children.Count > 0 && children[0].Type == "package_identifier")
                    alias = children[0].Text;
            }

            deps.Add(new DependencyInfo(RequirePath: pathText, Alias: alias));
        }
    }

    private static SymbolKind GetKind(Node decl, Node? body)
    {
        if (decl.Type == "package_clause") return SymbolKind.Module;
        if (decl.Type is "const_spec" or "var_spec") return SymbolKind.Constant;

        if (decl.Type == "type_declaration")
        {
            if (body?.Type == "interface_type") return SymbolKind.Interface;
            if (body?.Type == "struct_type") return SymbolKind.Class;
            return SymbolKind.Type;
        }

        if (decl.Type == "method_declaration") return SymbolKind.Method;
        return SymbolKind.Function;
    }

    private static string ExtractSignature(Node decl, Node? body, byte[] bytes)
    {
        var start = decl.StartIndex;
        int end;

        if (body is not null && body.Type is "block" or "struct_type" or "interface_type")
            end = body.StartIndex;
        else
            end = decl.EndIndex;

        if (end <= start) end = decl.EndIndex;
        return Encoding.UTF8.GetString(bytes, start, end - start).TrimEnd();
    }

    private static string? ExtractGoReceiverType(Node decl)
    {
        if (decl.Type != "method_declaration") return null;

        // Receiver is the first parameter_list child (after the `func` keyword token)
        var children = decl.Children;
        for (var i = 0; i < children.Count; i++)
        {
            if (children[i].Type != "parameter_list") continue;

            var receiverText = children[i].Text.Trim('(', ')').Trim();
            var parts = receiverText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return null;
            return parts[^1].TrimStart('*');
        }
        return null;
    }

    private static Visibility DeriveVisibility(string name) =>
        !string.IsNullOrEmpty(name) && char.IsUpper(name[0]) ? Visibility.Public : Visibility.Private;

    private static string? ExtractDocComment(string[] lines, Node decl)
    {
        var result = new List<string>();
        var idx = decl.StartPosition.Row - 1;

        while (idx >= 0)
        {
            var line = lines[idx].TrimEnd('\r').Trim();
            if (line.StartsWith("//", StringComparison.Ordinal))
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

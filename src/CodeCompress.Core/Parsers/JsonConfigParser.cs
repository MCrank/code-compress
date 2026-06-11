using System.Text;
using CodeCompress.Core.Models;
using TreeSitter;

namespace CodeCompress.Core.Parsers;

public sealed class JsonConfigParser : ILanguageParser
{
    public string LanguageId => "json-config";

    public IReadOnlyList<string> FileExtensions { get; } = [".json"];

    public ParseResult Parse(string filePath, ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty)
            return new ParseResult([], []);

        var bytes = content.ToArray();
        var text = Encoding.UTF8.GetString(bytes);

        using var language = new Language("json");
        using var parser = new Parser(language);
        using var tree = parser.Parse(text);
        if (tree is null)
            return new ParseResult([], []);

        var rootObject = FindRootObject(tree.RootNode);
        if (rootObject is null)
            return new ParseResult([], []);

        var symbols = new List<SymbolInfo>();
        TraverseObject(rootObject, null, null, symbols, text);
        return new ParseResult(symbols, []);
    }

    private static Node? FindRootObject(Node node)
    {
        if (node.Type == "object") return node;

        var children = node.Children;
        for (var i = 0; i < children.Count; i++)
        {
            if (children[i].Type == "object") return children[i];
        }
        return null;
    }

    private static void TraverseObject(
        Node objectNode,
        string? parentQualifiedName,
        string? parentSymbolName,
        List<SymbolInfo> symbols,
        string text)
    {
        var children = objectNode.Children;
        for (var i = 0; i < children.Count; i++)
        {
            if (children[i].Type == "pair")
                ProcessPair(children[i], parentQualifiedName, parentSymbolName, symbols, text);
        }
    }

    private static void ProcessPair(
        Node pairNode,
        string? parentQualifiedName,
        string? parentSymbolName,
        List<SymbolInfo> symbols,
        string text)
    {
        Node? keyNode = null, valueNode = null;
        var children = pairNode.Children;
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            if (child.Type == "string" && keyNode is null)
            {
                keyNode = child;
            }
            else if (keyNode is not null && child.Type != ":")
            {
                valueNode = child;
                break;
            }
        }

        if (keyNode is null) return;

        var rawKey = keyNode.Text;
        var keyText = rawKey.Length >= 2 && rawKey[0] == '"' && rawKey[^1] == '"'
            ? rawKey[1..^1]
            : rawKey;

        var qualifiedName = parentQualifiedName is null ? keyText : $"{parentQualifiedName}:{keyText}";

        // tree-sitter node offsets are UTF-16 char offsets (parser receives a C# string).
        // Convert to UTF-8 byte offsets for accurate ByteOffset storage.
        var byteOffset = Encoding.UTF8.GetByteCount(text.AsSpan(0, keyNode.StartIndex));
        var endCharIndex = valueNode?.EndIndex ?? pairNode.EndIndex;
        var byteEnd = Encoding.UTF8.GetByteCount(text.AsSpan(0, endCharIndex));
        var byteLength = Math.Max(byteEnd - byteOffset, 1);

        var lineStart = keyNode.StartPosition.Row + 1;
        var lineEnd = (valueNode?.EndPosition.Row ?? pairNode.EndPosition.Row) + 1;
        var signature = BuildSignature(qualifiedName, valueNode);

        symbols.Add(new SymbolInfo(
            Name: qualifiedName,
            Kind: SymbolKind.ConfigKey,
            Signature: signature,
            ParentSymbol: parentSymbolName,
            ByteOffset: byteOffset,
            ByteLength: byteLength,
            LineStart: lineStart,
            LineEnd: lineEnd,
            Visibility: Visibility.Public,
            DocComment: null));

        if (valueNode?.Type == "object")
            TraverseObject(valueNode, qualifiedName, qualifiedName, symbols, text);
    }

    private static string BuildSignature(string qualifiedName, Node? valueNode)
    {
        if (valueNode is null) return $"{qualifiedName}: <unknown>";

        return valueNode.Type switch
        {
            "string" => $"{qualifiedName}: {TruncateString(valueNode.Text, 80)}",
            "number" => $"{qualifiedName}: {valueNode.Text}",
            "true" => $"{qualifiedName}: true",
            "false" => $"{qualifiedName}: false",
            "null" => $"{qualifiedName}: null",
            "object" => BuildObjectSignature(qualifiedName, valueNode),
            "array" => BuildArraySignature(qualifiedName, valueNode),
            _ => $"{qualifiedName}: <unknown>"
        };
    }

    private static string TruncateString(string value, int maxLength)
    {
        if (value.Length <= maxLength) return value;
        return string.Concat(value.AsSpan(0, maxLength - 3), "...");
    }

    private static string BuildObjectSignature(string qualifiedName, Node objectNode)
    {
        var count = 0;
        var children = objectNode.Children;
        for (var i = 0; i < children.Count; i++)
        {
            if (children[i].Type == "pair") count++;
        }
        return $"{qualifiedName}: {{ ... }} ({count} {(count == 1 ? "key" : "keys")})";
    }

    private static string BuildArraySignature(string qualifiedName, Node arrayNode)
    {
        var count = 0;
        var children = arrayNode.Children;
        for (var i = 0; i < children.Count; i++)
        {
            var t = children[i].Type;
            if (t is "object" or "array" or "string" or "number" or "true" or "false" or "null")
                count++;
        }
        return $"{qualifiedName}: [ ... ] ({count} {(count == 1 ? "item" : "items")})";
    }
}

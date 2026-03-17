using System.Text;
using System.Text.Json;
using CodeCompress.Core.Models;

namespace CodeCompress.Core.Parsers;

public sealed class JsonConfigParser : ILanguageParser
{
    public string LanguageId => "json-config";

    public IReadOnlyList<string> FileExtensions { get; } = [".json"];

    public ParseResult Parse(string filePath, ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty)
        {
            return new ParseResult([], []);
        }

        var text = Encoding.UTF8.GetString(content);
        var lineByteOffsets = ComputeLineByteOffsets(content);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return new ParseResult([], []);
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new ParseResult([], []);
            }

            var symbols = new List<SymbolInfo>();
            var searchStart = 0;
            TraverseObject(doc.RootElement, null, null, symbols, text, lineByteOffsets, content.Length, ref searchStart);
            return new ParseResult(symbols, []);
        }
    }

    private static void TraverseObject(
        JsonElement element,
        string? parentQualifiedName,
        string? parentSymbolName,
        List<SymbolInfo> symbols,
        string text,
        int[] lineByteOffsets,
        int contentLength,
        ref int searchStart)
    {
        foreach (var property in element.EnumerateObject())
        {
            var qualifiedName = parentQualifiedName is null
                ? property.Name
                : $"{parentQualifiedName}:{property.Name}";

            var (keyByteOffset, keyCharIndex) = FindPropertyKeyOffset(text, property.Name, searchStart);
            var lineStart = FindLineForByteOffset(keyByteOffset, lineByteOffsets);
            var signature = BuildSignature(qualifiedName, property.Value);
            var byteLength = EstimateByteLength(property, contentLength, keyByteOffset);

            // Advance searchStart (char index) past this property key so next search finds the next occurrence
            if (keyCharIndex > searchStart)
            {
                searchStart = keyCharIndex + property.Name.Length;
            }

            symbols.Add(new SymbolInfo(
                qualifiedName,
                SymbolKind.ConfigKey,
                signature,
                parentSymbolName,
                keyByteOffset,
                byteLength,
                lineStart,
                lineStart,
                Visibility.Public,
                null));

            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                TraverseObject(property.Value, qualifiedName, qualifiedName, symbols, text, lineByteOffsets, contentLength, ref searchStart);
            }
        }
    }

    private static (int ByteOffset, int CharIndex) FindPropertyKeyOffset(string text, string propertyName, int searchStartChar)
    {
        // Search for "propertyName" pattern in the JSON text starting from searchStartChar (char index)
        var needle = $"\"{propertyName}\"";
        var charIndex = text.IndexOf(needle, searchStartChar, StringComparison.Ordinal);
        if (charIndex < 0)
        {
            // Fallback: search from beginning
            charIndex = text.IndexOf(needle, StringComparison.Ordinal);
        }

        if (charIndex < 0)
        {
            return (0, 0);
        }

        // Convert char index to byte offset for symbol storage
        var byteOffset = Encoding.UTF8.GetByteCount(text.AsSpan(0, charIndex));
        return (byteOffset, charIndex);
    }

    private static int FindLineForByteOffset(int byteOffset, int[] lineByteOffsets)
    {
        // Binary search for the line containing this byte offset
        var line = 1;
        for (var i = 0; i < lineByteOffsets.Length; i++)
        {
            if (lineByteOffsets[i] <= byteOffset)
            {
                line = i + 1; // 1-based
            }
            else
            {
                break;
            }
        }

        return line;
    }

    private static string BuildSignature(string qualifiedName, JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => $"{qualifiedName}: \"{value.GetString()}\"",
            JsonValueKind.Number => $"{qualifiedName}: {value.GetRawText()}",
            JsonValueKind.True => $"{qualifiedName}: true",
            JsonValueKind.False => $"{qualifiedName}: false",
            JsonValueKind.Null => $"{qualifiedName}: null",
            JsonValueKind.Object => BuildObjectSignature(qualifiedName, value),
            JsonValueKind.Array => $"{qualifiedName}: [ ... ] ({value.GetArrayLength()} {(value.GetArrayLength() == 1 ? "item" : "items")})",
            _ => $"{qualifiedName}: <unknown>"
        };
    }

    private static string BuildObjectSignature(string qualifiedName, JsonElement value)
    {
        var count = 0;
        foreach (var _ in value.EnumerateObject())
        {
            count++;
        }

        return $"{qualifiedName}: {{ ... }} ({count} {(count == 1 ? "key" : "keys")})";
    }

    private static int EstimateByteLength(JsonProperty property, int contentLength, int byteOffset)
    {
        var raw = property.Value.GetRawText();
        var keyBytes = Encoding.UTF8.GetByteCount(property.Name);
        // key + quotes + colon + space + value
        var estimated = keyBytes + 4 + Encoding.UTF8.GetByteCount(raw);

        if (byteOffset + estimated > contentLength)
        {
            estimated = contentLength - byteOffset;
        }

        return Math.Max(estimated, 1);
    }

    private static int[] ComputeLineByteOffsets(ReadOnlySpan<byte> content)
    {
        var offsets = new List<int> { 0 };
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] == (byte)'\n')
            {
                offsets.Add(i + 1);
            }
        }

        return offsets.ToArray();
    }
}

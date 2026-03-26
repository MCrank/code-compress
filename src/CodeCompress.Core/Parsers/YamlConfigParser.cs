using System.Text;
using CodeCompress.Core.Models;
using YamlDotNet.RepresentationModel;

namespace CodeCompress.Core.Parsers;

public sealed class YamlConfigParser : ILanguageParser
{
    private const int MaxYamlBytes = 5 * 1024 * 1024; // 5 MB — defense against YAML bombs
    private const int MaxTraversalDepth = 64;
    private const int MaxScalarValueLength = 200;

    public string LanguageId => "yaml-config";

    public IReadOnlyList<string> FileExtensions { get; } = [".yaml", ".yml"];

    public ParseResult Parse(string filePath, ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty || content.Length > MaxYamlBytes)
        {
            return new ParseResult([], []);
        }

        var text = Encoding.UTF8.GetString(content);
        var lineByteOffsets = ComputeLineByteOffsets(content);

        YamlStream yamlStream;
        try
        {
            yamlStream = new YamlStream();
            using var reader = new StringReader(text);
            yamlStream.Load(reader);
        }
        catch (YamlDotNet.Core.YamlException)
        {
            return new ParseResult([], []);
        }

        if (yamlStream.Documents.Count == 0)
        {
            return new ParseResult([], []);
        }

        var rootNode = yamlStream.Documents[0].RootNode;

        if (rootNode is not YamlMappingNode rootMapping)
        {
            return new ParseResult([], []);
        }

        var symbols = new List<SymbolInfo>();
        TraverseMapping(rootMapping, null, null, symbols, lineByteOffsets, content.Length, 0);
        return new ParseResult(symbols, []);
    }

    private static void TraverseMapping(
        YamlMappingNode mapping,
        string? parentQualifiedName,
        string? parentSymbolName,
        List<SymbolInfo> symbols,
        int[] lineByteOffsets,
        int contentLength,
        int depth)
    {
        if (depth > MaxTraversalDepth)
        {
            return;
        }

        foreach (var entry in mapping.Children)
        {
            if (entry.Key is not YamlScalarNode keyNode || keyNode.Value is null)
            {
                continue;
            }

            var keyName = keyNode.Value;
            var qualifiedName = parentQualifiedName is null
                ? keyName
                : $"{parentQualifiedName}:{keyName}";

            var byteOffset = ComputeByteOffset((int)keyNode.Start.Line, (int)keyNode.Start.Column, lineByteOffsets);
            var lineStart = (int)keyNode.Start.Line;
            var signature = BuildSignature(qualifiedName, entry.Value);
            var byteLength = EstimateByteLength(entry.Value, byteOffset, lineByteOffsets, contentLength);

            symbols.Add(new SymbolInfo(
                qualifiedName,
                SymbolKind.ConfigKey,
                signature,
                parentSymbolName,
                byteOffset,
                byteLength,
                lineStart,
                lineStart,
                Visibility.Public,
                null));

            switch (entry.Value)
            {
                case YamlMappingNode childMapping:
                    TraverseMapping(childMapping, qualifiedName, qualifiedName, symbols, lineByteOffsets, contentLength, depth + 1);
                    break;

                case YamlSequenceNode sequenceNode:
                    TraverseSequence(sequenceNode, qualifiedName, symbols, lineByteOffsets, contentLength, depth + 1);
                    break;
            }
        }
    }

    private static void TraverseSequence(
        YamlSequenceNode sequence,
        string parentQualifiedName,
        List<SymbolInfo> symbols,
        int[] lineByteOffsets,
        int contentLength,
        int depth)
    {
        if (depth > MaxTraversalDepth)
        {
            return;
        }

        // Determine if array contains mappings (object array) or scalars
        var hasMappings = sequence.Children.Any(item => item is YamlMappingNode);

        if (!hasMappings)
        {
            // Scalar array — already handled as summary by the parent's signature
            return;
        }

        // Object array — index each item's children with zero-based index
        for (var i = 0; i < sequence.Children.Count; i++)
        {
            if (sequence.Children[i] is not YamlMappingNode itemMapping)
            {
                continue;
            }

            var itemQualifiedName = $"{parentQualifiedName}:{i}";
            var itemByteOffset = ComputeByteOffset((int)itemMapping.Start.Line, (int)itemMapping.Start.Column, lineByteOffsets);
            var itemLineStart = (int)itemMapping.Start.Line;
            var itemByteLength = EstimateByteLength(itemMapping, itemByteOffset, lineByteOffsets, contentLength);

            // Add the array item itself as a symbol
            var itemKeyCount = itemMapping.Children.Count;
            var itemSignature = $"{itemQualifiedName}: {{ ... }} ({itemKeyCount} {(itemKeyCount == 1 ? "key" : "keys")})";

            symbols.Add(new SymbolInfo(
                itemQualifiedName,
                SymbolKind.ConfigKey,
                itemSignature,
                parentQualifiedName,
                itemByteOffset,
                itemByteLength,
                itemLineStart,
                itemLineStart,
                Visibility.Public,
                null));

            TraverseMapping(itemMapping, itemQualifiedName, itemQualifiedName, symbols, lineByteOffsets, contentLength, depth + 1);
        }
    }

    private static string BuildSignature(string qualifiedName, YamlNode value)
    {
        return value switch
        {
            YamlMappingNode mappingNode => BuildMappingSignature(qualifiedName, mappingNode),
            YamlSequenceNode sequenceNode => BuildSequenceSignature(qualifiedName, sequenceNode),
            YamlScalarNode scalarNode => BuildScalarSignature(qualifiedName, scalarNode),
            _ => $"{qualifiedName}: <unknown>"
        };
    }

    private static string BuildMappingSignature(string qualifiedName, YamlMappingNode mapping)
    {
        var count = mapping.Children.Count;
        return $"{qualifiedName}: {{ ... }} ({count} {(count == 1 ? "key" : "keys")})";
    }

    private static string BuildSequenceSignature(string qualifiedName, YamlSequenceNode sequence)
    {
        var count = sequence.Children.Count;
        return $"{qualifiedName}: [ ... ] ({count} {(count == 1 ? "item" : "items")})";
    }

    private static string BuildScalarSignature(string qualifiedName, YamlScalarNode scalar)
    {
        var value = scalar.Value;

        // Handle null values (null, ~, or empty with no tag)
        if (value is null or "null" or "~" || (value.Length == 0 && scalar.Tag.IsEmpty))
        {
            return $"{qualifiedName}: null";
        }

        // Handle booleans
        if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
        {
            return $"{qualifiedName}: true";
        }

        if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
        {
            return $"{qualifiedName}: false";
        }

        // Handle numbers — if it parses as a number, show without quotes
        if ((double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _)
            && !value.StartsWith('0'))
            || (value.StartsWith('0') && value.Length == 1))
        {
            var numDisplay = value.Length > MaxScalarValueLength
                ? string.Concat(value.AsSpan(0, MaxScalarValueLength), "...")
                : value;
            return $"{qualifiedName}: {numDisplay}";
        }

        // Everything else is a string — truncate long values to limit prompt injection surface
        var display = value.Length > MaxScalarValueLength
            ? string.Concat(value.AsSpan(0, MaxScalarValueLength), "...")
            : value;
        return $"{qualifiedName}: \"{display}\"";
    }

    private static int ComputeByteOffset(int line, int column, int[] lineByteOffsets)
    {
        // YamlDotNet uses 1-based line and column numbers
        if (line < 1 || line > lineByteOffsets.Length)
        {
            return 0;
        }

        var lineOffset = lineByteOffsets[line - 1];
        return lineOffset + (column - 1);
    }

    private static int EstimateByteLength(YamlNode node, int nodeByteOffset, int[] lineByteOffsets, int contentLength)
    {
        var endLine = (int)node.End.Line;
        var endColumn = (int)node.End.Column;

        var endByteOffset = ComputeByteOffset(endLine, endColumn, lineByteOffsets);
        var estimated = endByteOffset - nodeByteOffset;

        if (estimated <= 0)
        {
            // Fallback: use remainder of line
            var startLine = (int)node.Start.Line;
            if (startLine >= 1 && startLine <= lineByteOffsets.Length)
            {
                var nextLineOffset = startLine < lineByteOffsets.Length
                    ? lineByteOffsets[startLine]
                    : contentLength;
                estimated = nextLineOffset - nodeByteOffset;
            }
        }

        if (nodeByteOffset + estimated > contentLength)
        {
            estimated = contentLength - nodeByteOffset;
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

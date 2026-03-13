using System.Text;
using System.Text.RegularExpressions;
using CodeCompress.Core.Models;

namespace CodeCompress.Core.Parsers;

public sealed partial class BlazorRazorParser : ILanguageParser
{
    private readonly CSharpParser _csharpParser = new();

    public string LanguageId => "blazor";

    public IReadOnlyList<string> FileExtensions { get; } = [".razor"];

    [GeneratedRegex(@"^@page\s+""([^""]+)""")]
    private static partial Regex PageDirectivePattern();

    [GeneratedRegex(@"^@inject\s+([\w.<>\[\],\s]+?)\s+(\w+)\s*$")]
    private static partial Regex InjectDirectivePattern();

    [GeneratedRegex(@"^@using\s+(?:(\w[\w.]*)\s*=\s*)?(.+?)\s*$")]
    private static partial Regex UsingDirectivePattern();

    [GeneratedRegex(@"^@inherits\s+([\w.<>\[\],]+)")]
    private static partial Regex InheritsDirectivePattern();

    [GeneratedRegex(@"^@implements\s+([\w.<>\[\],]+)")]
    private static partial Regex ImplementsDirectivePattern();

    [GeneratedRegex(@"^@(code|functions)(?:\s*\{|\s*$)")]
    private static partial Regex CodeBlockStartPattern();

    public ParseResult Parse(string filePath, ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty)
        {
            return new ParseResult([], []);
        }

        var text = Encoding.UTF8.GetString(content);
        var lines = text.Split('\n');
        var lineByteOffsets = ComputeLineByteOffsets(content);

        var componentName = Path.GetFileNameWithoutExtension(filePath);

        var symbols = new List<SymbolInfo>();
        var dependencies = new List<DependencyInfo>();

        // Add top-level component symbol
        symbols.Add(new SymbolInfo(
            componentName,
            SymbolKind.Class,
            $"Razor component {componentName}",
            ParentSymbol: null,
            ByteOffset: 0,
            ByteLength: content.Length,
            LineStart: 1,
            LineEnd: lines.Length,
            Visibility.Public,
            DocComment: null));

        // Scan lines for directives
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var lineNumber = i + 1;
            var lineByteOffset = lineByteOffsets[i];
            var lineByteLength = Encoding.UTF8.GetByteCount(line);

            ParsePageDirective(line, lineNumber, lineByteOffset, lineByteLength, componentName, symbols);
            ParseInjectDirective(line, lineNumber, lineByteOffset, lineByteLength, componentName, symbols, dependencies);
            ParseUsingDirective(line, dependencies);
            ParseInheritsDirective(line, lineNumber, lineByteOffset, lineByteLength, componentName, symbols);
            ParseImplementsDirective(line, lineNumber, lineByteOffset, lineByteLength, componentName, symbols);
        }

        // Extract and parse all @code/@functions blocks
        var codeBlocks = ExtractCodeBlocks(lines, lineByteOffsets);
        foreach (var codeBlock in codeBlocks)
        {
            var wrapperPrefix = $"internal class {componentName} {{\n";
            var wrapperSuffix = "\n}";
            var wrappedCode = wrapperPrefix + codeBlock.Content + wrapperSuffix;
            var wrappedBytes = Encoding.UTF8.GetBytes(wrappedCode);
            var wrapperPrefixBytes = Encoding.UTF8.GetByteCount(wrapperPrefix);

            var csharpResult = _csharpParser.Parse(filePath, wrappedBytes);

            foreach (var symbol in csharpResult.Symbols)
            {
                // Filter out the synthetic wrapper class
                if (symbol.Kind == SymbolKind.Class && symbol.Name == componentName && symbol.ParentSymbol is null)
                {
                    continue;
                }

                // Remap byte offsets and line numbers
                var remappedByteOffset = symbol.ByteOffset - wrapperPrefixBytes + codeBlock.ContentStartByteOffset;
                var remappedLineStart = symbol.LineStart + codeBlock.ContentStartLine - 2;
                var remappedLineEnd = symbol.LineEnd + codeBlock.ContentStartLine - 2;

                // Reparent symbols that were children of the synthetic class to the component
                var parentSymbol = symbol.ParentSymbol == componentName
                    ? componentName
                    : symbol.ParentSymbol;

                symbols.Add(symbol with
                {
                    ByteOffset = remappedByteOffset,
                    LineStart = remappedLineStart,
                    LineEnd = remappedLineEnd,
                    ParentSymbol = parentSymbol
                });
            }

            dependencies.AddRange(csharpResult.Dependencies);
        }

        return new ParseResult(symbols, dependencies);
    }

    private static void ParsePageDirective(
        string line,
        int lineNumber,
        int lineByteOffset,
        int lineByteLength,
        string componentName,
        List<SymbolInfo> symbols)
    {
        var match = PageDirectivePattern().Match(line);
        if (!match.Success)
        {
            return;
        }

        var directive = $"@page \"{match.Groups[1].Value}\"";
        symbols.Add(new SymbolInfo(
            directive,
            SymbolKind.Constant,
            directive,
            componentName,
            lineByteOffset,
            lineByteLength,
            lineNumber,
            lineNumber,
            Visibility.Public,
            DocComment: null));
    }

    private static void ParseInjectDirective(
        string line,
        int lineNumber,
        int lineByteOffset,
        int lineByteLength,
        string componentName,
        List<SymbolInfo> symbols,
        List<DependencyInfo> dependencies)
    {
        var match = InjectDirectivePattern().Match(line);
        if (!match.Success)
        {
            return;
        }

        var typeName = match.Groups[1].Value.Trim();
        var propertyName = match.Groups[2].Value;
        var signature = $"@inject {typeName} {propertyName}";

        symbols.Add(new SymbolInfo(
            propertyName,
            SymbolKind.Constant,
            signature,
            componentName,
            lineByteOffset,
            lineByteLength,
            lineNumber,
            lineNumber,
            Visibility.Public,
            DocComment: null));

        dependencies.Add(new DependencyInfo(typeName, Alias: null));
    }

    private static void ParseUsingDirective(
        string line,
        List<DependencyInfo> dependencies)
    {
        var match = UsingDirectivePattern().Match(line);
        if (!match.Success)
        {
            return;
        }

        var alias = match.Groups[1].Success ? match.Groups[1].Value : null;
        var namespaceName = match.Groups[2].Value.Trim();

        dependencies.Add(new DependencyInfo(namespaceName, alias));
    }

    private static void ParseInheritsDirective(
        string line,
        int lineNumber,
        int lineByteOffset,
        int lineByteLength,
        string componentName,
        List<SymbolInfo> symbols)
    {
        var match = InheritsDirectivePattern().Match(line);
        if (!match.Success)
        {
            return;
        }

        var baseType = match.Groups[1].Value;
        var signature = $"@inherits {baseType}";

        symbols.Add(new SymbolInfo(
            signature,
            SymbolKind.Constant,
            signature,
            componentName,
            lineByteOffset,
            lineByteLength,
            lineNumber,
            lineNumber,
            Visibility.Public,
            DocComment: null));
    }

    private static void ParseImplementsDirective(
        string line,
        int lineNumber,
        int lineByteOffset,
        int lineByteLength,
        string componentName,
        List<SymbolInfo> symbols)
    {
        var match = ImplementsDirectivePattern().Match(line);
        if (!match.Success)
        {
            return;
        }

        var interfaceName = match.Groups[1].Value;
        var signature = $"@implements {interfaceName}";

        symbols.Add(new SymbolInfo(
            signature,
            SymbolKind.Constant,
            signature,
            componentName,
            lineByteOffset,
            lineByteLength,
            lineNumber,
            lineNumber,
            Visibility.Public,
            DocComment: null));
    }

    private static List<CodeBlockInfo> ExtractCodeBlocks(string[] lines, int[] lineByteOffsets)
    {
        var blocks = new List<CodeBlockInfo>();
        var searchStart = 0;

        while (searchStart < lines.Length)
        {
            // Find next @code/@functions directive
            var i = searchStart;
            var found = false;
            while (i < lines.Length)
            {
                var line = lines[i].TrimEnd('\r');
                if (CodeBlockStartPattern().IsMatch(line))
                {
                    found = true;
                    break;
                }

                i++;
            }

            if (!found)
            {
                break;
            }

            var directiveLine = lines[i].TrimEnd('\r');

            // Find the opening brace - it may be on this line or a subsequent line
            var braceLineIndex = i;
            var braceCharIndex = directiveLine.IndexOf('{', StringComparison.Ordinal);

            if (braceCharIndex < 0)
            {
                // Scan forward for the opening brace
                braceLineIndex = -1;
                for (var j = i + 1; j < lines.Length; j++)
                {
                    var scanLine = lines[j].TrimEnd('\r');
                    if (string.IsNullOrWhiteSpace(scanLine))
                    {
                        continue;
                    }

                    braceCharIndex = scanLine.IndexOf('{', StringComparison.Ordinal);
                    if (braceCharIndex >= 0)
                    {
                        braceLineIndex = j;
                        break;
                    }

                    // Non-empty line without brace means malformed @code block
                    break;
                }

                if (braceLineIndex < 0)
                {
                    searchStart = i + 1;
                    continue;
                }
            }

            // Content starts on the line after the opening brace
            var contentStartLineIndex = braceLineIndex + 1;
            if (contentStartLineIndex >= lines.Length)
            {
                blocks.Add(new CodeBlockInfo(contentStartLineIndex + 1, lineByteOffsets[^1], contentStartLineIndex, string.Empty));
                searchStart = contentStartLineIndex;
                continue;
            }

            // 1-based line number for content start
            var contentStartLine = contentStartLineIndex + 1;
            var contentStartByteOffset = lineByteOffsets[contentStartLineIndex];

            // Track brace depth using state machine
            var depth = 1;
            var state = LexerState.Normal;
            var endLineIndex = -1;

            for (var j = contentStartLineIndex; j < lines.Length && depth > 0; j++)
            {
                var currentLine = lines[j].TrimEnd('\r');

                var k = 0;
                while (k < currentLine.Length && depth > 0)
                {
                    var ch = currentLine[k];
                    var nextCh = k + 1 < currentLine.Length ? currentLine[k + 1] : '\0';

                    switch (state)
                    {
                        case LexerState.Normal:
                            if (ch == '/' && nextCh == '/')
                            {
                                state = LexerState.InLineComment;
                                k += 2;
                                continue;
                            }
                            else if (ch == '/' && nextCh == '*')
                            {
                                state = LexerState.InBlockComment;
                                k += 2;
                                continue;
                            }
                            else if (ch == '"' && k > 0 && currentLine[k - 1] == '@')
                            {
                                state = LexerState.InVerbatimString;
                            }
                            else if (ch == '"')
                            {
                                state = LexerState.InString;
                            }
                            else if (ch == '\'')
                            {
                                state = LexerState.InChar;
                            }
                            else if (ch == '{')
                            {
                                depth++;
                            }
                            else if (ch == '}')
                            {
                                depth--;
                                if (depth == 0)
                                {
                                    endLineIndex = j;
                                }
                            }
                            break;

                        case LexerState.InString:
                            if (ch == '\\')
                            {
                                k += 2;
                                continue;
                            }
                            else if (ch == '"')
                            {
                                state = LexerState.Normal;
                            }
                            break;

                        case LexerState.InVerbatimString:
                            if (ch == '"' && nextCh == '"')
                            {
                                k += 2;
                                continue;
                            }
                            else if (ch == '"')
                            {
                                state = LexerState.Normal;
                            }
                            break;

                        case LexerState.InChar:
                            if (ch == '\\')
                            {
                                k += 2;
                                continue;
                            }
                            else if (ch == '\'')
                            {
                                state = LexerState.Normal;
                            }
                            break;

                        case LexerState.InLineComment:
                            break;

                        case LexerState.InBlockComment:
                            if (ch == '*' && nextCh == '/')
                            {
                                state = LexerState.Normal;
                                k += 2;
                                continue;
                            }
                            break;
                    }

                    k++;
                }

                // Line comment ends at end of line
                if (state == LexerState.InLineComment)
                {
                    state = LexerState.Normal;
                }
            }

            if (endLineIndex < 0)
            {
                // Unmatched braces — treat entire remaining content as code block
                endLineIndex = lines.Length - 1;
            }

            // Extract content lines (from contentStartLineIndex up to but not including the closing brace line)
            var contentBuilder = new StringBuilder();
            var lastContentLineIndex = endLineIndex - 1;
            for (var j = contentStartLineIndex; j <= lastContentLineIndex; j++)
            {
                contentBuilder.Append(lines[j]);
                if (j < lastContentLineIndex)
                {
                    contentBuilder.Append('\n');
                }
            }

            var extractedContent = contentBuilder.ToString();

            // Handle case where there's no content between braces
            if (contentStartLineIndex > lastContentLineIndex)
            {
                extractedContent = string.Empty;
            }

            blocks.Add(new CodeBlockInfo(
                contentStartLine,
                contentStartByteOffset,
                endLineIndex,
                extractedContent));

            // Skip past this code block for the next iteration
            searchStart = endLineIndex + 1;
        }

        return blocks;
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

        return [.. offsets];
    }

    private sealed record CodeBlockInfo(
        int ContentStartLine,
        int ContentStartByteOffset,
        int EndLineIndex,
        string Content);

    private enum LexerState
    {
        Normal,
        InString,
        InVerbatimString,
        InChar,
        InLineComment,
        InBlockComment
    }
}

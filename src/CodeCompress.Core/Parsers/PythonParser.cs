using System.Text;
using System.Text.RegularExpressions;
using CodeCompress.Core.Models;

namespace CodeCompress.Core.Parsers;

public sealed partial class PythonParser : ILanguageParser
{
    public string LanguageId => "python";

    public IReadOnlyList<string> FileExtensions { get; } = [".py", ".pyi"];

    [GeneratedRegex(@"^\s*import\s+(.+)$")]
    private static partial Regex ImportPattern();

    [GeneratedRegex(@"^\s*from\s+([\w.]+)\s+import\s+(.+)$")]
    private static partial Regex FromImportPattern();

    [GeneratedRegex(@"^(\s*)(?:@\w+.*\n)*\s*(?:async\s+)?def\s+(\w+)\s*\((.*)$")]
    private static partial Regex DefPattern();

    [GeneratedRegex(@"^(\s*)class\s+(\w+)(.*):\s*$")]
    private static partial Regex ClassPattern();

    [GeneratedRegex(@"^([A-Z][A-Z_0-9]+)\s*(?::\s*\w[^=]*)?\s*=\s*(.+)$")]
    private static partial Regex ConstantPattern();

    public ParseResult Parse(string filePath, ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty)
        {
            return new ParseResult([], []);
        }

        var text = Encoding.UTF8.GetString(content);
        var lines = text.Split('\n');
        var symbols = new List<SymbolInfo>();
        var dependencies = new List<DependencyInfo>();
        var lineByteOffsets = ComputeLineByteOffsets(content);
        var pendingSymbols = new List<PendingSymbol>();
        var decoratorLines = new List<string>();
        var inTripleQuote = false;
        var tripleQuoteChar = '"';

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.Trim();
            var lineNumber = i + 1;
            var byteOffset = lineByteOffsets[i];
            var indent = GetIndentLevel(line);

            // Handle triple-quoted strings (docstrings and multi-line strings)
            if (inTripleQuote)
            {
                var endMarker = new string(tripleQuoteChar, 3);
                if (trimmed.Contains(endMarker, StringComparison.Ordinal))
                {
                    inTripleQuote = false;
                }

                continue;
            }

            // Check for triple-quote start
            if (trimmed.StartsWith("\"\"\"", StringComparison.Ordinal) || trimmed.StartsWith("'''", StringComparison.Ordinal))
            {
                tripleQuoteChar = trimmed[0];
                var endMarker = new string(tripleQuoteChar, 3);
                // Check if it closes on the same line (after the opening)
                var afterOpen = trimmed[3..];
                if (!afterOpen.Contains(endMarker, StringComparison.Ordinal))
                {
                    inTripleQuote = true;
                }

                // If this is a docstring for a pending symbol, capture it
                if (pendingSymbols.Count > 0)
                {
                    var lastPending = pendingSymbols[^1];
                    if (lastPending.DocComment is null && lastPending.LineStart == lineNumber - 1)
                    {
                        // Extract first meaningful line of docstring
                        var docText = afterOpen.TrimEnd(tripleQuoteChar, ' ');
                        if (string.IsNullOrWhiteSpace(docText) && i + 1 < lines.Length)
                        {
                            docText = lines[i + 1].Trim().TrimEnd(tripleQuoteChar, ' ');
                        }

                        if (!string.IsNullOrWhiteSpace(docText))
                        {
                            lastPending.DocComment = docText;
                        }
                    }
                }

                continue;
            }

            // Single-line comments
            if (trimmed.StartsWith('#'))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            // Close pending symbols whose indentation scope has ended
            ClosePendingSymbols(indent, lineNumber, byteOffset, line, pendingSymbols, symbols);

            // Collect decorators
            if (trimmed.StartsWith('@'))
            {
                decoratorLines.Add(trimmed);
                continue;
            }

            _ = TryMatchImport(trimmed, dependencies)
                || TryMatchFromImport(trimmed, dependencies)
                || TryMatchClass(line, trimmed, lineNumber, byteOffset, decoratorLines, pendingSymbols)
                || TryMatchDef(line, trimmed, lineNumber, byteOffset, decoratorLines, pendingSymbols)
                || TryMatchConstant(trimmed, lineNumber, byteOffset, symbols);

            decoratorLines.Clear();
        }

        // Close any remaining pending symbols
        if (pendingSymbols.Count > 0)
        {
            var lastLine = lines.Length;
            var lastOffset = lineByteOffsets.Length > lastLine - 1 ? lineByteOffsets[lastLine - 1] : lineByteOffsets[^1];
            var lastLineText = lines.Length > 0 ? lines[^1] : string.Empty;
            ClosePendingSymbols(0, lastLine, lastOffset, lastLineText, pendingSymbols, symbols);
        }

        return new ParseResult(symbols, dependencies);
    }

    private static bool TryMatchImport(string trimmed, List<DependencyInfo> dependencies)
    {
        var match = ImportPattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var modules = match.Groups[1].Value.Split(',');
        foreach (var mod in modules)
        {
            var cleaned = mod.Trim().Split(' ')[0]; // Handle "import os as operating_system"
            if (!string.IsNullOrEmpty(cleaned))
            {
                dependencies.Add(new DependencyInfo(RequirePath: cleaned, Alias: null));
            }
        }

        return true;
    }

    private static bool TryMatchFromImport(string trimmed, List<DependencyInfo> dependencies)
    {
        var match = FromImportPattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var modulePath = match.Groups[1].Value;
        // Convert relative imports: .entity -> entity, ..models -> models
        var cleanPath = modulePath.TrimStart('.');
        if (string.IsNullOrEmpty(cleanPath))
        {
            cleanPath = modulePath; // Keep dots if no module name follows
        }

        dependencies.Add(new DependencyInfo(RequirePath: cleanPath.Replace('.', '/'), Alias: null));
        return true;
    }

    private static bool TryMatchClass(
        string line, string trimmed, int lineNumber, int byteOffset,
        List<string> decoratorLines, List<PendingSymbol> pendingSymbols)
    {
        var match = ClassPattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var name = match.Groups[2].Value;
        var rest = match.Groups[3].Value.Trim();
        var indent = GetIndentLevel(line);

        var signatureBuilder = new StringBuilder();
        foreach (var decorator in decoratorLines)
        {
            signatureBuilder.Append(decorator).Append(' ');
        }

        signatureBuilder.Append("class ").Append(name);
        if (!string.IsNullOrEmpty(rest))
        {
            signatureBuilder.Append(rest.TrimEnd(':').TrimEnd());
        }

        var signature = signatureBuilder.ToString().TrimEnd();
        var visibility = name.StartsWith('_') ? Visibility.Private : Visibility.Public;
        var parentName = GetCurrentParentName(pendingSymbols, indent);

        pendingSymbols.Add(new PendingSymbol(
            Name: name,
            Kind: SymbolKind.Class,
            Signature: signature,
            ParentName: parentName,
            ByteOffset: byteOffset,
            LineStart: lineNumber,
            Visibility: visibility,
            DocComment: null,
            IndentLevel: indent,
            IsContainer: true));

        return true;
    }

    private static bool TryMatchDef(
        string line, string trimmed, int lineNumber, int byteOffset,
        List<string> decoratorLines, List<PendingSymbol> pendingSymbols)
    {
        // Match def or async def
        if (!trimmed.Contains("def ", StringComparison.Ordinal))
        {
            return false;
        }

        var indent = GetIndentLevel(line);

        // Extract the function name
        var defIdx = trimmed.IndexOf("def ", StringComparison.Ordinal);
        if (defIdx < 0)
        {
            return false;
        }

        var afterDef = trimmed[(defIdx + 4)..];
        var parenIdx = afterDef.IndexOf('(', StringComparison.Ordinal);
        if (parenIdx < 0)
        {
            return false;
        }

        var name = afterDef[..parenIdx].Trim();
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        // Build signature
        var signatureBuilder = new StringBuilder();
        foreach (var decorator in decoratorLines)
        {
            signatureBuilder.Append(decorator).Append(' ');
        }

        // Trim the colon and body from the signature
        var sigLine = trimmed.TrimEnd();
        var colonIdx = sigLine.LastIndexOf(':');
        if (colonIdx > parenIdx + defIdx + 4)
        {
            sigLine = sigLine[..colonIdx].TrimEnd();
        }

        signatureBuilder.Append(sigLine);
        var signature = signatureBuilder.ToString();

        var parentName = GetCurrentParentName(pendingSymbols, indent);
        var kind = parentName is not null ? SymbolKind.Method : SymbolKind.Function;
        var visibility = name.StartsWith('_') ? Visibility.Private : Visibility.Public;

        pendingSymbols.Add(new PendingSymbol(
            Name: name,
            Kind: kind,
            Signature: signature,
            ParentName: parentName,
            ByteOffset: byteOffset,
            LineStart: lineNumber,
            Visibility: visibility,
            DocComment: null,
            IndentLevel: indent,
            IsContainer: false));

        return true;
    }

    private static bool TryMatchConstant(
        string trimmed, int lineNumber, int byteOffset, List<SymbolInfo> symbols)
    {
        // Only match at module level (no indentation check needed — constants are top-level)
        var match = ConstantPattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var name = match.Groups[1].Value;

        symbols.Add(new SymbolInfo(
            Name: name,
            Kind: SymbolKind.Constant,
            Signature: trimmed.Trim(),
            ParentSymbol: null,
            ByteOffset: byteOffset,
            ByteLength: Encoding.UTF8.GetByteCount(trimmed),
            LineStart: lineNumber,
            LineEnd: lineNumber,
            Visibility: Visibility.Public,
            DocComment: null));

        return true;
    }

    private static void ClosePendingSymbols(
        int currentIndent, int lineNumber, int byteOffset,
        string line, List<PendingSymbol> pendingSymbols, List<SymbolInfo> symbols)
    {
        for (var i = pendingSymbols.Count - 1; i >= 0; i--)
        {
            var pending = pendingSymbols[i];
            if (currentIndent <= pending.IndentLevel && lineNumber > pending.LineStart)
            {
                var byteLength = byteOffset - pending.ByteOffset;
                if (byteLength <= 0)
                {
                    byteLength = Encoding.UTF8.GetByteCount(line);
                }

                symbols.Add(new SymbolInfo(
                    Name: pending.Name,
                    Kind: pending.Kind,
                    Signature: pending.Signature,
                    ParentSymbol: pending.ParentName,
                    ByteOffset: pending.ByteOffset,
                    ByteLength: byteLength,
                    LineStart: pending.LineStart,
                    LineEnd: lineNumber - 1,
                    Visibility: pending.Visibility,
                    DocComment: pending.DocComment));

                pendingSymbols.RemoveAt(i);
            }
        }
    }

    private static string? GetCurrentParentName(List<PendingSymbol> pendingSymbols, int currentIndent)
    {
        for (var i = pendingSymbols.Count - 1; i >= 0; i--)
        {
            if (pendingSymbols[i].IndentLevel < currentIndent && pendingSymbols[i].Kind == SymbolKind.Class)
            {
                return pendingSymbols[i].Name;
            }
        }

        return null;
    }

    private static int GetIndentLevel(string line)
    {
        var count = 0;
        foreach (var ch in line)
        {
            if (ch == ' ')
            {
                count++;
            }
            else if (ch == '\t')
            {
                count += 4;
            }
            else
            {
                break;
            }
        }

        return count;
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

    private sealed class PendingSymbol(
        string Name, SymbolKind Kind, string Signature, string? ParentName,
        int ByteOffset, int LineStart, Visibility Visibility, string? DocComment,
        int IndentLevel, bool IsContainer)
    {
        public string Name { get; } = Name;
        public SymbolKind Kind { get; } = Kind;
        public string Signature { get; } = Signature;
        public string? ParentName { get; } = ParentName;
        public int ByteOffset { get; } = ByteOffset;
        public int LineStart { get; } = LineStart;
        public Visibility Visibility { get; } = Visibility;
        public string? DocComment { get; set; } = DocComment;
        public int IndentLevel { get; } = IndentLevel;
        public bool IsContainer { get; } = IsContainer;
    }
}

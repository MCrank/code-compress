using System.Text;
using System.Text.RegularExpressions;
using CodeCompress.Core.Models;

namespace CodeCompress.Core.Parsers;

public sealed partial class LuauParser : ILanguageParser
{
    public string LanguageId => "luau";

    public IReadOnlyList<string> FileExtensions { get; } = [".luau", ".lua"];

    // ── Regex patterns (source-generated) ───────────────────────

    [GeneratedRegex(@"^local\s+(\w+)\s*=\s*\{\s*\}\s*::\s*(\w+)\s*$")]
    private static partial Regex ModuleClassPattern();

    [GeneratedRegex(@"^function\s+(\w+)[:\.](\w+)\s*\(([^)]*)\)\s*(?::\s*(.+))?$")]
    private static partial Regex MethodPattern();

    [GeneratedRegex(@"^local\s+function\s+(\w+)\s*\(([^)]*)\)\s*(?::\s*(.+))?$")]
    private static partial Regex LocalFunctionPattern();

    [GeneratedRegex(@"^function\s+(\w+)\s*\(([^)]*)\)\s*(?::\s*(.+))?$")]
    private static partial Regex TopLevelFunctionPattern();

    [GeneratedRegex(@"^return\s+(\w+)\s*$")]
    private static partial Regex ModuleReturnPattern();

    // Nesting: count block structures that need an `end`
    // Each of these is one block: function(...), if...then, for...do, while...do, standalone do
    [GeneratedRegex(@"\bfunction\s*[\w.:]*\s*\(")]
    private static partial Regex FunctionOpenerPattern();

    [GeneratedRegex(@"\bif\b")]
    private static partial Regex IfPattern();

    [GeneratedRegex(@"\bfor\b")]
    private static partial Regex ForPattern();

    [GeneratedRegex(@"\bwhile\b")]
    private static partial Regex WhilePattern();

    // standalone `do` — not preceded by `for...` or `while...` on the same line
    // We'll handle this by counting: do keywords minus for/while keywords
    [GeneratedRegex(@"\bdo\b")]
    private static partial Regex DoPattern();

    [GeneratedRegex(@"\bend\b")]
    private static partial Regex EndKeywordPattern();

    [GeneratedRegex(@"\buntil\b")]
    private static partial Regex UntilKeywordPattern();

    [GeneratedRegex(@"\brepeat\b")]
    private static partial Regex RepeatPattern();

    // `elseif` also contains `if` but does NOT open a new block
    [GeneratedRegex(@"\belseif\b")]
    private static partial Regex ElseIfPattern();

    public ParseResult Parse(string filePath, ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty)
        {
            return new ParseResult([], []);
        }

        var text = Encoding.UTF8.GetString(content);
        var lines = text.Split('\n');
        var symbols = new List<SymbolInfo>();
        var pendingSymbols = new List<PendingSymbol>();
        var lineByteOffsets = ComputeLineByteOffsets(content);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.Trim();
            var lineNumber = i + 1;

            // Skip empty lines and comments
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("--", StringComparison.Ordinal))
            {
                UpdateNesting(trimmed, lineNumber, lineByteOffsets[i], pendingSymbols, symbols);
                continue;
            }

            // Try match patterns in priority order
            // Module class, return, and top-level constructs only match at the top level
            var atTopLevel = pendingSymbols.Count == 0;

            _ = (atTopLevel && TryMatchModuleClass(trimmed, lineNumber, lineByteOffsets[i], symbols))
                || TryMatchMethod(trimmed, lineNumber, lineByteOffsets[i], pendingSymbols)
                || TryMatchLocalFunction(trimmed, lineNumber, lineByteOffsets[i], pendingSymbols)
                || TryMatchTopLevelFunction(trimmed, lineNumber, lineByteOffsets[i], pendingSymbols)
                || (atTopLevel && TryMatchModuleReturn(trimmed, lineNumber, lineByteOffsets[i], symbols));

            // Always update nesting for lines that affect block depth
            UpdateNesting(trimmed, lineNumber, lineByteOffsets[i], pendingSymbols, symbols);
        }

        // Post-process: determine exported class name for visibility
        var exportedName = symbols
            .FirstOrDefault(s => s.Kind == SymbolKind.Export)?.Name;

        for (var i = 0; i < symbols.Count; i++)
        {
            if (symbols[i].Kind == SymbolKind.Method
                && symbols[i].ParentSymbol is not null
                && string.Equals(symbols[i].ParentSymbol, exportedName, StringComparison.Ordinal))
            {
                symbols[i] = symbols[i] with { Visibility = Visibility.Public };
            }
        }

        return new ParseResult(symbols, []);
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

    private static bool TryMatchModuleClass(
        string trimmed, int lineNumber, int byteOffset, List<SymbolInfo> symbols)
    {
        var match = ModuleClassPattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        symbols.Add(new SymbolInfo(
            Name: match.Groups[1].Value,
            Kind: SymbolKind.Class,
            Signature: trimmed,
            ParentSymbol: null,
            ByteOffset: byteOffset,
            ByteLength: Encoding.UTF8.GetByteCount(trimmed),
            LineStart: lineNumber,
            LineEnd: lineNumber,
            Visibility: Visibility.Public,
            DocComment: null));

        return true;
    }

    private static bool TryMatchMethod(
        string trimmed, int lineNumber, int byteOffset, List<PendingSymbol> pending)
    {
        var match = MethodPattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        pending.Add(new PendingSymbol(
            Name: match.Groups[2].Value,
            Kind: SymbolKind.Method,
            Signature: trimmed,
            ParentSymbol: match.Groups[1].Value,
            ByteOffset: byteOffset,
            LineStart: lineNumber,
            Visibility: Visibility.Public,
            NestingDepth: 0)); // Will be incremented by UpdateNesting

        return true;
    }

    private static bool TryMatchLocalFunction(
        string trimmed, int lineNumber, int byteOffset, List<PendingSymbol> pending)
    {
        var match = LocalFunctionPattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        pending.Add(new PendingSymbol(
            Name: match.Groups[1].Value,
            Kind: SymbolKind.Function,
            Signature: trimmed,
            ParentSymbol: null,
            ByteOffset: byteOffset,
            LineStart: lineNumber,
            Visibility: Visibility.Private,
            NestingDepth: 0));

        return true;
    }

    private static bool TryMatchTopLevelFunction(
        string trimmed, int lineNumber, int byteOffset, List<PendingSymbol> pending)
    {
        var match = TopLevelFunctionPattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        pending.Add(new PendingSymbol(
            Name: match.Groups[1].Value,
            Kind: SymbolKind.Function,
            Signature: trimmed,
            ParentSymbol: null,
            ByteOffset: byteOffset,
            LineStart: lineNumber,
            Visibility: Visibility.Public,
            NestingDepth: 0));

        return true;
    }

    private static bool TryMatchModuleReturn(
        string trimmed, int lineNumber, int byteOffset, List<SymbolInfo> symbols)
    {
        var match = ModuleReturnPattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        symbols.Add(new SymbolInfo(
            Name: match.Groups[1].Value,
            Kind: SymbolKind.Export,
            Signature: trimmed,
            ParentSymbol: null,
            ByteOffset: byteOffset,
            ByteLength: Encoding.UTF8.GetByteCount(trimmed),
            LineStart: lineNumber,
            LineEnd: lineNumber,
            Visibility: Visibility.Public,
            DocComment: null));

        return true;
    }

    private static void UpdateNesting(
        string trimmed, int lineNumber, int byteOffset,
        List<PendingSymbol> pending, List<SymbolInfo> symbols)
    {
        if (pending.Count == 0)
        {
            return;
        }

        var opens = CountBlockOpens(trimmed);
        var closes = EndKeywordPattern().Count(trimmed) + UntilKeywordPattern().Count(trimmed);

        var delta = opens - closes;

        for (var i = pending.Count - 1; i >= 0; i--)
        {
            var p = pending[i];
            p.NestingDepth += delta;

            if (p.NestingDepth <= 0)
            {
                CompletePendingSymbol(p, lineNumber, byteOffset, trimmed, symbols);
                pending.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Counts the number of block-opening statements on a line.
    /// In Luau: function, if, for, while each open one block needing `end`.
    /// `do` after `for`/`while` is part of the same statement (not extra).
    /// Standalone `do` opens its own block.
    /// `repeat` opens a block closed by `until`.
    /// `elseif` contains `if` but does NOT open a new block.
    /// </summary>
    private static int CountBlockOpens(string line)
    {
        var functions = FunctionOpenerPattern().Count(line);
        var ifs = IfPattern().Count(line) - ElseIfPattern().Count(line);
        var fors = ForPattern().Count(line);
        var whiles = WhilePattern().Count(line);
        var dos = DoPattern().Count(line);
        var repeats = RepeatPattern().Count(line);

        // `do` keywords that are part of `for...do` or `while...do` are not standalone
        var standaloneDos = Math.Max(0, dos - fors - whiles);

        return functions + ifs + fors + whiles + standaloneDos + repeats;
    }

    private static void CompletePendingSymbol(
        PendingSymbol pending, int endLineNumber, int endByteOffset,
        string endLine, List<SymbolInfo> symbols)
    {
        var byteLength = endByteOffset + Encoding.UTF8.GetByteCount(endLine) - pending.ByteOffset;

        symbols.Add(new SymbolInfo(
            Name: pending.Name,
            Kind: pending.Kind,
            Signature: pending.Signature,
            ParentSymbol: pending.ParentSymbol,
            ByteOffset: pending.ByteOffset,
            ByteLength: byteLength,
            LineStart: pending.LineStart,
            LineEnd: endLineNumber,
            Visibility: pending.Visibility,
            DocComment: null));
    }

    private sealed class PendingSymbol(
        string Name,
        SymbolKind Kind,
        string Signature,
        string? ParentSymbol,
        int ByteOffset,
        int LineStart,
        Visibility Visibility,
        int NestingDepth)
    {
        public string Name { get; } = Name;
        public SymbolKind Kind { get; } = Kind;
        public string Signature { get; } = Signature;
        public string? ParentSymbol { get; } = ParentSymbol;
        public int ByteOffset { get; } = ByteOffset;
        public int LineStart { get; } = LineStart;
        public Visibility Visibility { get; } = Visibility;
        public int NestingDepth { get; set; } = NestingDepth;
    }
}

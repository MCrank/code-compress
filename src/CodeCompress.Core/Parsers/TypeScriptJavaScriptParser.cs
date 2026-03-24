using System.Text;
using System.Text.RegularExpressions;
using CodeCompress.Core.Models;

namespace CodeCompress.Core.Parsers;

public sealed partial class TypeScriptJavaScriptParser : ILanguageParser
{
    public string LanguageId => "typescript";

    public IReadOnlyList<string> FileExtensions { get; } = [".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs"];

    // import ... from "..." or import "..."
    [GeneratedRegex(@"^\s*import\s+.*?from\s+[""']([^""']+)[""']|^\s*import\s+[""']([^""']+)[""']")]
    private static partial Regex EsmImportPattern();

    // const/let/var X = require("...")
    [GeneratedRegex(@"(?:const|let|var)\s+.*?=\s*require\s*\(\s*[""']([^""']+)[""']\s*\)")]
    private static partial Regex CommonJsRequirePattern();

    // export default class/function/...
    [GeneratedRegex(@"^\s*export\s+default\s+")]
    private static partial Regex ExportDefaultPattern();

    // export (with or without default)
    [GeneratedRegex(@"^\s*export\s+")]
    private static partial Regex ExportPattern();

    // class Name or abstract class Name
    [GeneratedRegex(@"(?:^|\s)(?:abstract\s+)?class\s+(\w+)\s*(.*)$")]
    private static partial Regex ClassPattern();

    // interface Name
    [GeneratedRegex(@"(?:^|\s)interface\s+(\w+)\s*(.*)$")]
    private static partial Regex InterfacePattern();

    // enum Name
    [GeneratedRegex(@"(?:^|\s)enum\s+(\w+)\s*(.*)$")]
    private static partial Regex EnumPattern();

    // type Name = ...
    [GeneratedRegex(@"(?:^|\s)type\s+(\w+)\s*(.*)$")]
    private static partial Regex TypeAliasPattern();

    // function name( or async function name(
    [GeneratedRegex(@"(?:^|\s)(?:async\s+)?function\s+(\w+)\s*(<.*?>)?\s*\(")]
    private static partial Regex FunctionPattern();

    // const/let/var name = (...) => or const/let/var name = async (...) =>
    [GeneratedRegex(@"^\s*(?:export\s+)?(?:const|let|var)\s+(\w+)\s*(?::\s*[^=]+)?\s*=\s*(?:async\s+)?\(")]
    private static partial Regex ArrowFunctionPattern();

    // const/let/var NAME = value (non-function constants)
    [GeneratedRegex(@"^\s*(?:export\s+)?(?:const|let|var)\s+(\w+)\s*(?::\s*[^=]+)?\s*=\s*(.+)$")]
    private static partial Regex ConstPattern();

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
        var parentStack = new List<PendingType>();
        var jsdocLines = new List<string>();
        var inBlockComment = false;
        var inJsdoc = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.Trim();
            var lineNumber = i + 1;
            var byteOffset = lineByteOffsets[i];

            // Block comments and JSDoc
            if (inBlockComment || inJsdoc)
            {
                if (inJsdoc)
                {
                    jsdocLines.Add(trimmed);
                }

                if (trimmed.Contains("*/", StringComparison.Ordinal))
                {
                    inBlockComment = false;
                    inJsdoc = false;
                }

                continue;
            }

            if (trimmed.StartsWith("/**", StringComparison.Ordinal))
            {
                jsdocLines.Clear();
                jsdocLines.Add(trimmed);
                if (!trimmed.Contains("*/", StringComparison.Ordinal))
                {
                    inJsdoc = true;
                }

                continue;
            }

            if (trimmed.StartsWith("/*", StringComparison.Ordinal))
            {
                if (!trimmed.Contains("*/", StringComparison.Ordinal))
                {
                    inBlockComment = true;
                }

                jsdocLines.Clear();
                continue;
            }

            if (trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                jsdocLines.Clear();
                UpdateBraceDepth(trimmed, lineNumber, byteOffset, parentStack, symbols);
                continue;
            }

            // Try imports first
            var matched = TryMatchImport(trimmed, dependencies)
                          || TryMatchClass(trimmed, lineNumber, byteOffset, jsdocLines, parentStack)
                          || TryMatchInterface(trimmed, lineNumber, byteOffset, jsdocLines, parentStack)
                          || TryMatchEnum(trimmed, lineNumber, byteOffset, jsdocLines, parentStack)
                          || TryMatchTypeAlias(trimmed, lineNumber, byteOffset, jsdocLines, symbols)
                          || TryMatchFunction(trimmed, lineNumber, byteOffset, jsdocLines, parentStack, symbols)
                          || TryMatchArrowFunction(trimmed, lineNumber, byteOffset, jsdocLines, symbols)
                          || TryMatchMethod(trimmed, lineNumber, byteOffset, jsdocLines, parentStack, symbols)
                          || TryMatchConst(trimmed, lineNumber, byteOffset, jsdocLines, symbols);

            if (matched)
            {
                jsdocLines.Clear();
            }

            UpdateBraceDepth(trimmed, lineNumber, byteOffset, parentStack, symbols);
        }

        return new ParseResult(symbols, dependencies);
    }

    private static bool TryMatchImport(string trimmed, List<DependencyInfo> dependencies)
    {
        var esmMatch = EsmImportPattern().Match(trimmed);
        if (esmMatch.Success)
        {
            var path = esmMatch.Groups[1].Success ? esmMatch.Groups[1].Value : esmMatch.Groups[2].Value;
            dependencies.Add(new DependencyInfo(RequirePath: path, Alias: null));
            return true;
        }

        var cjsMatch = CommonJsRequirePattern().Match(trimmed);
        if (cjsMatch.Success)
        {
            dependencies.Add(new DependencyInfo(RequirePath: cjsMatch.Groups[1].Value, Alias: null));
            return true;
        }

        return false;
    }

    private static bool TryMatchClass(
        string trimmed, int lineNumber, int byteOffset,
        List<string> jsdocLines, List<PendingType> parentStack)
    {
        var match = ClassPattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var name = match.Groups[1].Value;
        var isExported = ExportPattern().IsMatch(trimmed);
        var visibility = isExported ? Visibility.Public : Visibility.Private;

        // Build signature: everything up to the opening brace
        var signature = trimmed;
        var braceIdx = FindOpenBraceInCode(signature);
        if (braceIdx >= 0)
        {
            signature = signature[..braceIdx].TrimEnd();
        }

        var docComment = BuildDocComment(jsdocLines);
        var parentName = GetCurrentParentName(parentStack);
        var currentDepth = GetCurrentBraceDepth(parentStack);

        parentStack.Add(new PendingType(
            Name: name,
            Kind: SymbolKind.Class,
            Signature: signature,
            ParentName: parentName,
            ByteOffset: byteOffset,
            LineStart: lineNumber,
            Visibility: visibility,
            DocComment: docComment,
            BraceDepthAtDeclaration: currentDepth,
            IsContainer: true));

        return true;
    }

    private static bool TryMatchInterface(
        string trimmed, int lineNumber, int byteOffset,
        List<string> jsdocLines, List<PendingType> parentStack)
    {
        var match = InterfacePattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var name = match.Groups[1].Value;
        var isExported = ExportPattern().IsMatch(trimmed);
        var visibility = isExported ? Visibility.Public : Visibility.Private;

        var signature = trimmed;
        var braceIdx = FindOpenBraceInCode(signature);
        if (braceIdx >= 0)
        {
            signature = signature[..braceIdx].TrimEnd();
        }

        var docComment = BuildDocComment(jsdocLines);
        var currentDepth = GetCurrentBraceDepth(parentStack);

        parentStack.Add(new PendingType(
            Name: name,
            Kind: SymbolKind.Interface,
            Signature: signature,
            ParentName: null,
            ByteOffset: byteOffset,
            LineStart: lineNumber,
            Visibility: visibility,
            DocComment: docComment,
            BraceDepthAtDeclaration: currentDepth,
            IsContainer: true));

        return true;
    }

    private static bool TryMatchEnum(
        string trimmed, int lineNumber, int byteOffset,
        List<string> jsdocLines, List<PendingType> parentStack)
    {
        var match = EnumPattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var name = match.Groups[1].Value;
        var isExported = ExportPattern().IsMatch(trimmed);
        var visibility = isExported ? Visibility.Public : Visibility.Private;

        var signature = trimmed;
        var braceIdx = FindOpenBraceInCode(signature);
        if (braceIdx >= 0)
        {
            signature = signature[..braceIdx].TrimEnd();
        }

        var docComment = BuildDocComment(jsdocLines);
        var currentDepth = GetCurrentBraceDepth(parentStack);

        parentStack.Add(new PendingType(
            Name: name,
            Kind: SymbolKind.Enum,
            Signature: signature,
            ParentName: null,
            ByteOffset: byteOffset,
            LineStart: lineNumber,
            Visibility: visibility,
            DocComment: docComment,
            BraceDepthAtDeclaration: currentDepth,
            IsContainer: true));

        return true;
    }

    private static bool TryMatchTypeAlias(
        string trimmed, int lineNumber, int byteOffset,
        List<string> jsdocLines, List<SymbolInfo> symbols)
    {
        var match = TypeAliasPattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var name = match.Groups[1].Value;
        var isExported = ExportPattern().IsMatch(trimmed);
        var visibility = isExported ? Visibility.Public : Visibility.Private;
        var docComment = BuildDocComment(jsdocLines);

        symbols.Add(new SymbolInfo(
            Name: name,
            Kind: SymbolKind.Type,
            Signature: trimmed.Trim(),
            ParentSymbol: null,
            ByteOffset: byteOffset,
            ByteLength: Encoding.UTF8.GetByteCount(trimmed),
            LineStart: lineNumber,
            LineEnd: lineNumber,
            Visibility: visibility,
            DocComment: docComment));

        return true;
    }

    private static bool TryMatchFunction(
        string trimmed, int lineNumber, int byteOffset,
        List<string> jsdocLines, List<PendingType> parentStack, List<SymbolInfo> symbols)
    {
        var match = FunctionPattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        // Don't match method-like functions inside class bodies
        if (IsInsideTypeBody(parentStack))
        {
            return false;
        }

        var name = match.Groups[1].Value;
        var isExported = ExportPattern().IsMatch(trimmed);
        var visibility = isExported ? Visibility.Public : Visibility.Private;

        var signature = trimmed;
        var braceIdx = FindOpenBraceInCode(signature);
        if (braceIdx >= 0)
        {
            signature = signature[..braceIdx].TrimEnd();
        }

        var docComment = BuildDocComment(jsdocLines);

        if (trimmed.Contains('{', StringComparison.Ordinal))
        {
            var currentDepth = GetCurrentBraceDepth(parentStack);
            parentStack.Add(new PendingType(
                Name: name,
                Kind: SymbolKind.Function,
                Signature: signature,
                ParentName: null,
                ByteOffset: byteOffset,
                LineStart: lineNumber,
                Visibility: visibility,
                DocComment: docComment,
                BraceDepthAtDeclaration: currentDepth,
                IsContainer: false));
        }
        else
        {
            symbols.Add(new SymbolInfo(
                Name: name,
                Kind: SymbolKind.Function,
                Signature: signature,
                ParentSymbol: null,
                ByteOffset: byteOffset,
                ByteLength: Encoding.UTF8.GetByteCount(trimmed),
                LineStart: lineNumber,
                LineEnd: lineNumber,
                Visibility: visibility,
                DocComment: docComment));
        }

        return true;
    }

    private static bool TryMatchArrowFunction(
        string trimmed, int lineNumber, int byteOffset,
        List<string> jsdocLines, List<SymbolInfo> symbols)
    {
        var match = ArrowFunctionPattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var name = match.Groups[1].Value;
        var isExported = ExportPattern().IsMatch(trimmed);
        var visibility = isExported ? Visibility.Public : Visibility.Private;
        var docComment = BuildDocComment(jsdocLines);

        // Arrow functions are typically single-line or expression-bodied
        // Treat as single-line symbol
        var signature = trimmed.Trim();
        if (signature.Length > 120)
        {
            signature = signature[..120];
        }

        symbols.Add(new SymbolInfo(
            Name: name,
            Kind: SymbolKind.Function,
            Signature: signature,
            ParentSymbol: null,
            ByteOffset: byteOffset,
            ByteLength: Encoding.UTF8.GetByteCount(trimmed),
            LineStart: lineNumber,
            LineEnd: lineNumber,
            Visibility: visibility,
            DocComment: docComment));

        return true;
    }

    private static bool TryMatchMethod(
        string trimmed, int lineNumber, int byteOffset,
        List<string> jsdocLines, List<PendingType> parentStack, List<SymbolInfo> symbols)
    {
        if (!IsInsideTypeBody(parentStack))
        {
            return false;
        }

        // Match: name(, async name(, get name(, static name(, private name(, etc.
        var parenPos = FindMethodParenOpen(trimmed);
        if (parenPos < 0)
        {
            return false;
        }

        var prefix = trimmed[..parenPos].TrimEnd();
        var parts = prefix.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var name = parts[^1];

        // Skip if name starts with a keyword that's not a valid method name
        if (name is "if" or "for" or "while" or "switch" or "return" or "new" or "throw" or "catch")
        {
            return false;
        }

        // Handle getter
        if (name == "get" || (parts.Length >= 2 && parts[^2] == "get"))
        {
            // Need next token after 'get'
            // Already handled by parenPos logic — the name IS the token before (
        }

        var parentName = GetCurrentParentName(parentStack);
        var visibility = DeriveMethodVisibility(parts);
        var docComment = BuildDocComment(jsdocLines);

        var signature = trimmed.Trim();
        var braceIdx = FindOpenBraceInCode(signature);
        if (braceIdx >= 0)
        {
            signature = signature[..braceIdx].TrimEnd();
        }

        if (trimmed.Contains('{', StringComparison.Ordinal))
        {
            var currentDepth = GetCurrentBraceDepth(parentStack);
            parentStack.Add(new PendingType(
                Name: name,
                Kind: SymbolKind.Method,
                Signature: signature,
                ParentName: parentName,
                ByteOffset: byteOffset,
                LineStart: lineNumber,
                Visibility: visibility,
                DocComment: docComment,
                BraceDepthAtDeclaration: currentDepth,
                IsContainer: false));
        }
        else
        {
            // Interface method or abstract method (no body)
            symbols.Add(new SymbolInfo(
                Name: name,
                Kind: SymbolKind.Method,
                Signature: signature,
                ParentSymbol: parentName,
                ByteOffset: byteOffset,
                ByteLength: Encoding.UTF8.GetByteCount(trimmed),
                LineStart: lineNumber,
                LineEnd: lineNumber,
                Visibility: visibility,
                DocComment: docComment));
        }

        return true;
    }

    private static bool TryMatchConst(
        string trimmed, int lineNumber, int byteOffset,
        List<string> jsdocLines, List<SymbolInfo> symbols)
    {
        var match = ConstPattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var name = match.Groups[1].Value;
        var value = match.Groups[2].Value.TrimEnd(';');

        // Skip if the value starts with ( — likely an arrow function already matched
        // or a function call assigned to a variable
        if (value.TrimStart().StartsWith('(') || value.TrimStart().StartsWith("async (", StringComparison.Ordinal))
        {
            return false;
        }

        // Skip if value starts with class/function/new — those are other constructs
        if (value.TrimStart().StartsWith("class ", StringComparison.Ordinal) ||
            value.TrimStart().StartsWith("function ", StringComparison.Ordinal))
        {
            return false;
        }

        var isExported = ExportPattern().IsMatch(trimmed);
        var visibility = isExported ? Visibility.Public : Visibility.Private;
        var docComment = BuildDocComment(jsdocLines);

        symbols.Add(new SymbolInfo(
            Name: name,
            Kind: SymbolKind.Constant,
            Signature: trimmed.Trim().TrimEnd(';'),
            ParentSymbol: null,
            ByteOffset: byteOffset,
            ByteLength: Encoding.UTF8.GetByteCount(trimmed),
            LineStart: lineNumber,
            LineEnd: lineNumber,
            Visibility: visibility,
            DocComment: docComment));

        return true;
    }

    private static Visibility DeriveMethodVisibility(string[] parts)
    {
        return parts.Any(p => p is "private" or "#") ? Visibility.Private : Visibility.Public;
    }

    private static int FindMethodParenOpen(string line)
    {
        var angleBracketDepth = 0;
        var pos = 0;

        while (pos < line.Length)
        {
            var ch = line[pos];

            switch (ch)
            {
                case '<':
                    angleBracketDepth++;
                    pos++;
                    break;
                case '>':
                    if (angleBracketDepth > 0)
                    {
                        angleBracketDepth--;
                    }

                    pos++;
                    break;
                case '(' when angleBracketDepth == 0:
                    if (pos > 0 && IsMethodParenPreceder(line[pos - 1]))
                    {
                        return pos;
                    }

                    pos++;
                    break;
                default:
                    pos++;
                    break;
            }
        }

        return -1;
    }

    private static bool IsMethodParenPreceder(char ch)
    {
        return char.IsLetterOrDigit(ch) || ch == '_' || ch == '>';
    }

    private static int FindOpenBraceInCode(string text)
    {
        var inString = false;
        var stringChar = '"';
        var inTemplate = false;
        var pos = 0;

        while (pos < text.Length)
        {
            var ch = text[pos];

            if (inString)
            {
                if (ch == '\\')
                {
                    pos += 2;
                    continue;
                }

                if (ch == stringChar)
                {
                    inString = false;
                }

                pos++;
                continue;
            }

            if (inTemplate)
            {
                if (ch == '\\')
                {
                    pos += 2;
                    continue;
                }

                if (ch == '`')
                {
                    inTemplate = false;
                }

                pos++;
                continue;
            }

            switch (ch)
            {
                case '"' or '\'':
                    inString = true;
                    stringChar = ch;
                    pos++;
                    break;
                case '`':
                    inTemplate = true;
                    pos++;
                    break;
                case '{':
                    return pos;
                default:
                    pos++;
                    break;
            }
        }

        return -1;
    }

    private static bool IsInsideTypeBody(List<PendingType> parentStack)
    {
        var currentDepth = GetCurrentBraceDepth(parentStack);

        for (var i = parentStack.Count - 1; i >= 0; i--)
        {
            var entry = parentStack[i];

            if (!entry.IsContainer)
            {
                return false;
            }

            if (entry.IsContainer)
            {
                return currentDepth == entry.BraceDepthAtDeclaration + 1;
            }
        }

        return false;
    }

    private static void UpdateBraceDepth(
        string line, int lineNumber, int byteOffset,
        List<PendingType> parentStack, List<SymbolInfo> symbols)
    {
        var (opens, closes) = CountBraces(line);
        if (opens == 0 && closes == 0)
        {
            return;
        }

        var effectiveDepth = GetCurrentBraceDepth(parentStack);
        effectiveDepth += opens;

        for (var c = 0; c < closes; c++)
        {
            effectiveDepth--;

            for (var j = parentStack.Count - 1; j >= 0; j--)
            {
                if (parentStack[j].BraceDepthAtDeclaration == effectiveDepth)
                {
                    CompletePendingType(parentStack[j], lineNumber, byteOffset, line, symbols);
                    parentStack.RemoveAt(j);
                    break;
                }
            }
        }

        foreach (var pending in parentStack)
        {
            pending.CurrentBraceDepth = effectiveDepth;
        }
    }

    private static (int Opens, int Closes) CountBraces(string line)
    {
        var opens = 0;
        var closes = 0;
        var inString = false;
        var stringChar = '"';
        var inTemplate = false;
        var pos = 0;

        while (pos < line.Length)
        {
            var ch = line[pos];

            if (inString)
            {
                if (ch == '\\')
                {
                    pos += 2;
                    continue;
                }

                if (ch == stringChar)
                {
                    inString = false;
                }

                pos++;
                continue;
            }

            if (inTemplate)
            {
                if (ch == '\\')
                {
                    pos += 2;
                    continue;
                }

                if (ch == '`')
                {
                    inTemplate = false;
                }

                // Template literal braces inside ${} are tricky — skip for now
                pos++;
                continue;
            }

            switch (ch)
            {
                case '/' when pos + 1 < line.Length && line[pos + 1] == '/':
                    return (opens, closes);
                case '"' or '\'':
                    inString = true;
                    stringChar = ch;
                    pos++;
                    break;
                case '`':
                    inTemplate = true;
                    pos++;
                    break;
                case '{':
                    opens++;
                    pos++;
                    break;
                case '}':
                    closes++;
                    pos++;
                    break;
                default:
                    pos++;
                    break;
            }
        }

        return (opens, closes);
    }

    private static void CompletePendingType(
        PendingType pending, int endLineNumber, int endByteOffset,
        string endLine, List<SymbolInfo> symbols)
    {
        var byteLength = endByteOffset + Encoding.UTF8.GetByteCount(endLine) - pending.ByteOffset;

        symbols.Add(new SymbolInfo(
            Name: pending.Name,
            Kind: pending.Kind,
            Signature: pending.Signature,
            ParentSymbol: pending.ParentName,
            ByteOffset: pending.ByteOffset,
            ByteLength: byteLength,
            LineStart: pending.LineStart,
            LineEnd: endLineNumber,
            Visibility: pending.Visibility,
            DocComment: pending.DocComment));
    }

    private static string? BuildDocComment(List<string> jsdocLines)
    {
        if (jsdocLines.Count == 0)
        {
            return null;
        }

        return string.Join("\n", jsdocLines);
    }

    private static string? GetCurrentParentName(List<PendingType> parentStack)
    {
        for (var i = parentStack.Count - 1; i >= 0; i--)
        {
            if (parentStack[i].IsContainer)
            {
                return parentStack[i].Name;
            }
        }

        return null;
    }

    private static int GetCurrentBraceDepth(List<PendingType> parentStack)
    {
        if (parentStack.Count == 0)
        {
            return 0;
        }

        return parentStack[^1].CurrentBraceDepth;
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

    private sealed class PendingType(
        string Name,
        SymbolKind Kind,
        string Signature,
        string? ParentName,
        int ByteOffset,
        int LineStart,
        Visibility Visibility,
        string? DocComment,
        int BraceDepthAtDeclaration,
        bool IsContainer)
    {
        public string Name { get; } = Name;
        public SymbolKind Kind { get; } = Kind;
        public string Signature { get; } = Signature;
        public string? ParentName { get; } = ParentName;
        public int ByteOffset { get; } = ByteOffset;
        public int LineStart { get; } = LineStart;
        public Visibility Visibility { get; } = Visibility;
        public string? DocComment { get; } = DocComment;
        public int BraceDepthAtDeclaration { get; } = BraceDepthAtDeclaration;
        public bool IsContainer { get; } = IsContainer;
        public int CurrentBraceDepth { get; set; } = BraceDepthAtDeclaration;
    }
}

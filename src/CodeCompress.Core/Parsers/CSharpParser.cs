using System.Text;
using System.Text.RegularExpressions;
using CodeCompress.Core.Models;

namespace CodeCompress.Core.Parsers;

public sealed partial class CSharpParser : ILanguageParser
{
    public string LanguageId => "csharp";

    public IReadOnlyList<string> FileExtensions { get; } = [".cs"];

    [GeneratedRegex(@"^\s*namespace\s+([\w.]+)\s*;")]
    private static partial Regex FileScopedNamespacePattern();

    [GeneratedRegex(@"^\s*namespace\s+([\w.]+)\s*$")]
    private static partial Regex BlockScopedNamespacePattern();

    [GeneratedRegex(@"^\s*((?:(?:public|internal|private|protected|static|abstract|sealed|partial|readonly|file|required|new)\s+)*)(?:(class|interface|struct)\s+|(record)\s+(?:(class|struct)\s+)?)([\w]+)\s*(<[^>]+>)?\s*(.*)$")]
    private static partial Regex TypeDeclarationPattern();

    [GeneratedRegex(@"^\s*((?:(?:public|internal|private|protected|static|abstract|sealed|partial|readonly|file|required|new)\s+)*)enum\s+(\w+)\s*(.*)$")]
    private static partial Regex EnumDeclarationPattern();

    [GeneratedRegex(@"^\s*///")]
    private static partial Regex XmlDocCommentPattern();

    public ParseResult Parse(string filePath, ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty)
        {
            return new ParseResult([], []);
        }

        var text = Encoding.UTF8.GetString(content);
        var lines = text.Split('\n');
        var symbols = new List<SymbolInfo>();
        var lineByteOffsets = ComputeLineByteOffsets(content);
        var parentStack = new List<PendingType>();
        var docCommentLines = new List<string>();
        var inBlockComment = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.Trim();
            var lineNumber = i + 1;
            var byteOffset = lineByteOffsets[i];

            if (inBlockComment)
            {
                var endIdx = trimmed.IndexOf("*/", StringComparison.Ordinal);
                if (endIdx >= 0)
                {
                    inBlockComment = false;
                    var afterComment = trimmed[(endIdx + 2)..].Trim();
                    if (!string.IsNullOrEmpty(afterComment))
                    {
                        UpdateBraceDepth(afterComment, lineNumber, byteOffset, parentStack, symbols);
                    }
                }

                continue;
            }

            if (trimmed.StartsWith("/*", StringComparison.Ordinal))
            {
                if (!trimmed.Contains("*/", StringComparison.Ordinal))
                {
                    inBlockComment = true;
                }

                docCommentLines.Clear();
                continue;
            }

            if (XmlDocCommentPattern().IsMatch(line))
            {
                docCommentLines.Add(trimmed);
                continue;
            }

            if (trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                docCommentLines.Clear();
                continue;
            }

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                UpdateBraceDepth(trimmed, lineNumber, byteOffset, parentStack, symbols);
                continue;
            }

            var matched = TryMatchFileScopedNamespace(trimmed, lineNumber, byteOffset, docCommentLines, symbols)
                          || TryMatchBlockScopedNamespace(trimmed, lineNumber, byteOffset, docCommentLines, parentStack)
                          || TryMatchEnum(trimmed, lineNumber, byteOffset, docCommentLines, parentStack)
                          || TryMatchTypeDeclaration(trimmed, lineNumber, byteOffset, docCommentLines, parentStack, symbols);

            if (!matched && !XmlDocCommentPattern().IsMatch(trimmed))
            {
                docCommentLines.Clear();
            }

            if (matched)
            {
                docCommentLines.Clear();
            }

            UpdateBraceDepth(trimmed, lineNumber, byteOffset, parentStack, symbols);
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

    private static bool TryMatchFileScopedNamespace(
        string trimmed, int lineNumber, int byteOffset,
        List<string> docCommentLines, List<SymbolInfo> symbols)
    {
        var match = FileScopedNamespacePattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var docComment = BuildDocComment(docCommentLines);

        symbols.Add(new SymbolInfo(
            Name: match.Groups[1].Value,
            Kind: SymbolKind.Module,
            Signature: trimmed,
            ParentSymbol: null,
            ByteOffset: byteOffset,
            ByteLength: Encoding.UTF8.GetByteCount(trimmed),
            LineStart: lineNumber,
            LineEnd: lineNumber,
            Visibility: Visibility.Public,
            DocComment: docComment));

        return true;
    }

    private static bool TryMatchBlockScopedNamespace(
        string trimmed, int lineNumber, int byteOffset,
        List<string> docCommentLines, List<PendingType> parentStack)
    {
        var match = BlockScopedNamespacePattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var docComment = BuildDocComment(docCommentLines);
        var currentDepth = GetCurrentBraceDepth(parentStack);

        parentStack.Add(new PendingType(
            Name: match.Groups[1].Value,
            Kind: SymbolKind.Module,
            Signature: trimmed,
            ParentName: null,
            ByteOffset: byteOffset,
            LineStart: lineNumber,
            Visibility: Visibility.Public,
            DocComment: docComment,
            BraceDepthAtDeclaration: currentDepth,
            IsNamespace: true));

        return true;
    }

    private static bool TryMatchEnum(
        string trimmed, int lineNumber, int byteOffset,
        List<string> docCommentLines, List<PendingType> parentStack)
    {
        var match = EnumDeclarationPattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var modifiers = match.Groups[1].Value.Trim();
        var name = match.Groups[2].Value;
        var rest = match.Groups[3].Value.Trim();

        var signatureBuilder = new StringBuilder();
        if (!string.IsNullOrEmpty(modifiers))
        {
            signatureBuilder.Append(modifiers).Append(' ');
        }

        signatureBuilder.Append("enum ").Append(name);

        if (rest.Length > 0)
        {
            var baseAndBrace = rest.TrimEnd('{').Trim();
            if (baseAndBrace.Length > 0)
            {
                signatureBuilder.Append(' ').Append(baseAndBrace);
            }
        }

        var signature = signatureBuilder.ToString().TrimEnd();
        var docComment = BuildDocComment(docCommentLines);
        var parentName = GetCurrentParentName(parentStack);
        var isNested = parentName is not null;
        var visibility = DeriveVisibility(modifiers, isNested);
        var currentDepth = GetCurrentBraceDepth(parentStack);

        parentStack.Add(new PendingType(
            Name: name,
            Kind: SymbolKind.Type,
            Signature: signature,
            ParentName: parentName,
            ByteOffset: byteOffset,
            LineStart: lineNumber,
            Visibility: visibility,
            DocComment: docComment,
            BraceDepthAtDeclaration: currentDepth,
            IsNamespace: false));

        return true;
    }

    private static bool TryMatchTypeDeclaration(
        string trimmed, int lineNumber, int byteOffset,
        List<string> docCommentLines, List<PendingType> parentStack,
        List<SymbolInfo> symbols)
    {
        var match = TypeDeclarationPattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var modifiers = match.Groups[1].Value.Trim();
        var typeKeyword = match.Groups[2].Value;
        var recordKeyword = match.Groups[3].Value;
        var recordSubKeyword = match.Groups[4].Value;
        var name = match.Groups[5].Value;
        var genericParams = match.Groups[6].Value;
        var rest = match.Groups[7].Value.Trim();

        SymbolKind kind;
        string keyword;
        if (!string.IsNullOrEmpty(recordKeyword))
        {
            kind = SymbolKind.Class;
            keyword = string.IsNullOrEmpty(recordSubKeyword)
                ? "record"
                : $"record {recordSubKeyword}";
        }
        else if (string.Equals(typeKeyword, "interface", StringComparison.Ordinal))
        {
            kind = SymbolKind.Interface;
            keyword = "interface";
        }
        else
        {
            kind = SymbolKind.Class;
            keyword = typeKeyword;
        }

        var signatureBuilder = new StringBuilder();
        if (!string.IsNullOrEmpty(modifiers))
        {
            signatureBuilder.Append(modifiers).Append(' ');
        }

        signatureBuilder.Append(keyword).Append(' ').Append(name);

        if (!string.IsNullOrEmpty(genericParams))
        {
            signatureBuilder.Append(genericParams);
        }

        if (!string.IsNullOrEmpty(rest))
        {
            var signatureRest = rest;
            var semicolonIdx = FindSignatureEnd(signatureRest);
            if (semicolonIdx >= 0)
            {
                signatureRest = signatureRest[..(semicolonIdx + 1)];
            }
            else
            {
                var braceIdx = FindOpenBraceInCode(signatureRest);
                if (braceIdx >= 0)
                {
                    signatureRest = signatureRest[..braceIdx].Trim();
                }
            }

            if (!string.IsNullOrEmpty(signatureRest))
            {
                // Don't add space before opening paren (record primary constructors)
                if (signatureRest[0] == '(')
                {
                    signatureBuilder.Append(signatureRest);
                }
                else
                {
                    signatureBuilder.Append(' ').Append(signatureRest);
                }
            }
        }

        var signature = signatureBuilder.ToString().TrimEnd();
        var docComment = BuildDocComment(docCommentLines);
        var parentName = GetCurrentParentName(parentStack);
        var isNested = parentName is not null;
        var visibility = DeriveVisibility(modifiers, isNested);
        var currentDepth = GetCurrentBraceDepth(parentStack);

        var isSemicolonTerminated = trimmed.EndsWith(';');

        if (isSemicolonTerminated)
        {
            symbols.Add(new SymbolInfo(
                Name: name,
                Kind: kind,
                Signature: signature,
                ParentSymbol: parentName,
                ByteOffset: byteOffset,
                ByteLength: Encoding.UTF8.GetByteCount(trimmed),
                LineStart: lineNumber,
                LineEnd: lineNumber,
                Visibility: visibility,
                DocComment: docComment));
        }
        else
        {
            parentStack.Add(new PendingType(
                Name: name,
                Kind: kind,
                Signature: signature,
                ParentName: parentName,
                ByteOffset: byteOffset,
                LineStart: lineNumber,
                Visibility: visibility,
                DocComment: docComment,
                BraceDepthAtDeclaration: currentDepth,
                IsNamespace: false));
        }

        return true;
    }

    private static void UpdateBraceDepth(
        string line, int lineNumber, int byteOffset,
        List<PendingType> parentStack, List<SymbolInfo> symbols)
    {
        var (opens, closes) = CountBracesDetailed(line);
        if (opens == 0 && closes == 0)
        {
            return;
        }

        var effectiveDepth = GetCurrentBraceDepth(parentStack);

        effectiveDepth += opens;

        for (var c = 0; c < closes; c++)
        {
            effectiveDepth--;

            for (var i = parentStack.Count - 1; i >= 0; i--)
            {
                if (parentStack[i].BraceDepthAtDeclaration == effectiveDepth)
                {
                    CompletePendingType(parentStack[i], lineNumber, byteOffset, line, symbols);
                    parentStack.RemoveAt(i);
                    break;
                }
            }
        }

        foreach (var pending in parentStack)
        {
            pending.CurrentBraceDepth = effectiveDepth;
        }
    }

    private static (int Opens, int Closes) CountBracesDetailed(string line)
    {
        var opens = 0;
        var closes = 0;
        var state = CharScanState.Normal;
        var pos = 0;

        while (pos < line.Length)
        {
            var ch = line[pos];

            switch (state)
            {
                case CharScanState.Normal:
                    (state, pos) = ProcessNormalChar(line, pos, ch, ref opens, ref closes);
                    break;

                case CharScanState.InSingleLineComment:
                    return (opens, closes);

                case CharScanState.InBlockComment:
                    if (ch == '*' && pos + 1 < line.Length && line[pos + 1] == '/')
                    {
                        state = CharScanState.Normal;
                        pos += 2;
                    }
                    else
                    {
                        pos++;
                    }

                    break;

                case CharScanState.InString:
                    if (ch == '\\')
                    {
                        pos += 2;
                    }
                    else if (ch == '"')
                    {
                        state = CharScanState.Normal;
                        pos++;
                    }
                    else
                    {
                        pos++;
                    }

                    break;

                case CharScanState.InVerbatimString:
                    if (ch == '"')
                    {
                        if (pos + 1 < line.Length && line[pos + 1] == '"')
                        {
                            pos += 2;
                        }
                        else
                        {
                            state = CharScanState.Normal;
                            pos++;
                        }
                    }
                    else
                    {
                        pos++;
                    }

                    break;

                case CharScanState.InCharLiteral:
                    if (ch == '\\')
                    {
                        pos += 2;
                    }
                    else if (ch == '\'')
                    {
                        state = CharScanState.Normal;
                        pos++;
                    }
                    else
                    {
                        pos++;
                    }

                    break;

                default:
                    pos++;
                    break;
            }
        }

        return (opens, closes);
    }

    private static (CharScanState State, int NextPos) ProcessNormalChar(
        string line, int pos, char ch, ref int opens, ref int closes)
    {
        if (ch == '/' && pos + 1 < line.Length)
        {
            if (line[pos + 1] == '/')
            {
                return (CharScanState.InSingleLineComment, pos + 2);
            }

            if (line[pos + 1] == '*')
            {
                return (CharScanState.InBlockComment, pos + 2);
            }
        }

        if (ch == '@' && pos + 1 < line.Length && line[pos + 1] == '"')
        {
            return (CharScanState.InVerbatimString, pos + 2);
        }

        if (ch == '"')
        {
            return (CharScanState.InString, pos + 1);
        }

        if (ch == '\'')
        {
            return (CharScanState.InCharLiteral, pos + 1);
        }

        if (ch == '{')
        {
            opens++;
        }
        else if (ch == '}')
        {
            closes++;
        }

        return (CharScanState.Normal, pos + 1);
    }

    private static int FindOpenBraceInCode(string text)
    {
        var state = CharScanState.Normal;
        var pos = 0;

        while (pos < text.Length)
        {
            var ch = text[pos];

            switch (state)
            {
                case CharScanState.InString:
                    if (ch == '\\')
                    {
                        pos += 2;
                    }
                    else if (ch == '"')
                    {
                        state = CharScanState.Normal;
                        pos++;
                    }
                    else
                    {
                        pos++;
                    }

                    break;

                case CharScanState.InCharLiteral:
                    if (ch == '\\')
                    {
                        pos += 2;
                    }
                    else if (ch == '\'')
                    {
                        state = CharScanState.Normal;
                        pos++;
                    }
                    else
                    {
                        pos++;
                    }

                    break;

                default:
                    if (ch == '"')
                    {
                        state = CharScanState.InString;
                        pos++;
                    }
                    else if (ch == '\'')
                    {
                        state = CharScanState.InCharLiteral;
                        pos++;
                    }
                    else if (ch == '{')
                    {
                        return pos;
                    }
                    else
                    {
                        pos++;
                    }

                    break;
            }
        }

        return -1;
    }

    private static int FindSignatureEnd(string text)
    {
        var parenDepth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '(')
            {
                parenDepth++;
            }
            else if (ch == ')')
            {
                parenDepth--;
            }
            else if (ch == ';' && parenDepth == 0)
            {
                return i;
            }
        }

        return -1;
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

    private static Visibility DeriveVisibility(string modifiers, bool isNested)
    {
        if (string.IsNullOrEmpty(modifiers))
        {
            return isNested ? Visibility.Private : Visibility.Public;
        }

        if (modifiers.Contains("private protected", StringComparison.Ordinal))
        {
            return Visibility.Private;
        }

        if (modifiers.Contains("protected internal", StringComparison.Ordinal))
        {
            return Visibility.Public;
        }

        if (modifiers.Contains("private", StringComparison.Ordinal))
        {
            return Visibility.Private;
        }

        if (modifiers.Contains("protected", StringComparison.Ordinal))
        {
            return Visibility.Private;
        }

        if (modifiers.Contains("file", StringComparison.Ordinal))
        {
            return Visibility.Private;
        }

        return Visibility.Public;
    }

    private static string? BuildDocComment(List<string> docCommentLines)
    {
        if (docCommentLines.Count == 0)
        {
            return null;
        }

        return string.Join("\n", docCommentLines);
    }

    private static string? GetCurrentParentName(List<PendingType> parentStack)
    {
        for (var i = parentStack.Count - 1; i >= 0; i--)
        {
            if (!parentStack[i].IsNamespace)
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

    private enum CharScanState
    {
        Normal,
        InSingleLineComment,
        InBlockComment,
        InString,
        InVerbatimString,
        InCharLiteral
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
        bool IsNamespace)
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
        public bool IsNamespace { get; } = IsNamespace;
        public int CurrentBraceDepth { get; set; } = BraceDepthAtDeclaration;
    }
}

using System.Text;
using System.Text.RegularExpressions;
using CodeCompress.Core.Models;

namespace CodeCompress.Core.Parsers;

public sealed partial class RustParser : ILanguageParser
{
    public string LanguageId => "rust";

    public IReadOnlyList<string> FileExtensions { get; } = [".rs"];

    [GeneratedRegex(@"^\s*(?:pub\s+)?use\s+([\w:]+(?:::\{[^}]+\}|::\*)?)\s*;")]
    private static partial Regex UsePattern();

    [GeneratedRegex(@"^\s*(pub(?:\([\w]+\))?\s+)?mod\s+(\w+)\s*;")]
    private static partial Regex ModDeclPattern();

    [GeneratedRegex(@"^\s*(?:#\[.*\]\s*)*(pub(?:\([\w]+\))?\s+)?struct\s+(\w+)(.*)$")]
    private static partial Regex StructPattern();

    [GeneratedRegex(@"^\s*(?:#\[.*\]\s*)*(pub(?:\([\w]+\))?\s+)?enum\s+(\w+)(.*)$")]
    private static partial Regex EnumPattern();

    [GeneratedRegex(@"^\s*(pub(?:\([\w]+\))?\s+)?trait\s+(\w+)(.*)$")]
    private static partial Regex TraitPattern();

    [GeneratedRegex(@"^\s*impl(?:<[^>]*>)?\s+(?:(\w+)\s+for\s+)?(\w+)(.*)$")]
    private static partial Regex ImplPattern();

    [GeneratedRegex(@"^\s*(pub(?:\([\w]+\))?\s+)?(?:async\s+)?fn\s+(\w+)(.*)$")]
    private static partial Regex FnPattern();

    [GeneratedRegex(@"^\s*(pub(?:\([\w]+\))?\s+)?const\s+(\w+)\s*:(.*)$")]
    private static partial Regex ConstPattern();

    [GeneratedRegex(@"^\s*(pub(?:\([\w]+\))?\s+)?static\s+(\w+)\s*:(.*)$")]
    private static partial Regex StaticPattern();

    [GeneratedRegex(@"^\s*(pub(?:\([\w]+\))?\s+)?type\s+(\w+)(.*)$")]
    private static partial Regex TypeAliasPattern();

    [GeneratedRegex(@"^\s*(?:#\[.*\]\s*)?macro_rules!\s+(\w+)")]
    private static partial Regex MacroRulesPattern();

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
        var docCommentLines = new List<string>();
        var attributeLines = new List<string>();
        var inBlockComment = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.Trim();
            var lineNumber = i + 1;
            var byteOffset = lineByteOffsets[i];

            if (inBlockComment)
            {
                if (trimmed.Contains("*/", StringComparison.Ordinal))
                {
                    inBlockComment = false;
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

            // Doc comments: /// or //!
            if (trimmed.StartsWith("///", StringComparison.Ordinal) || trimmed.StartsWith("//!", StringComparison.Ordinal))
            {
                docCommentLines.Add(trimmed);
                continue;
            }

            if (trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                docCommentLines.Clear();
                UpdateBraceDepth(trimmed, lineNumber, byteOffset, parentStack, symbols);
                continue;
            }

            // Collect attributes
            if (trimmed.StartsWith("#[", StringComparison.Ordinal))
            {
                attributeLines.Add(trimmed);
                UpdateBraceDepth(trimmed, lineNumber, byteOffset, parentStack, symbols);
                continue;
            }

            var matched = TryMatchUse(trimmed, dependencies)
                          || TryMatchModDecl(trimmed, lineNumber, byteOffset, docCommentLines, symbols)
                          || TryMatchStruct(trimmed, lineNumber, byteOffset, docCommentLines, attributeLines, parentStack)
                          || TryMatchEnum(trimmed, lineNumber, byteOffset, docCommentLines, attributeLines, parentStack)
                          || TryMatchTrait(trimmed, lineNumber, byteOffset, docCommentLines, parentStack)
                          || TryMatchImpl(trimmed, lineNumber, byteOffset, parentStack)
                          || TryMatchMacroRules(trimmed, lineNumber, byteOffset, docCommentLines, parentStack)
                          || TryMatchConst(trimmed, lineNumber, byteOffset, docCommentLines, symbols)
                          || TryMatchStatic(trimmed, lineNumber, byteOffset, docCommentLines, symbols)
                          || TryMatchTypeAlias(trimmed, lineNumber, byteOffset, docCommentLines, symbols)
                          || TryMatchFn(trimmed, lineNumber, byteOffset, docCommentLines, parentStack, symbols);

            if (matched || !trimmed.StartsWith("#[", StringComparison.Ordinal))
            {
                docCommentLines.Clear();
                attributeLines.Clear();
            }

            UpdateBraceDepth(trimmed, lineNumber, byteOffset, parentStack, symbols);
        }

        return new ParseResult(symbols, dependencies);
    }

    private static bool TryMatchUse(string trimmed, List<DependencyInfo> dependencies)
    {
        var match = UsePattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        dependencies.Add(new DependencyInfo(RequirePath: match.Groups[1].Value, Alias: null));
        return true;
    }

    private static bool TryMatchModDecl(
        string trimmed, int lineNumber, int byteOffset,
        List<string> docCommentLines, List<SymbolInfo> symbols)
    {
        var match = ModDeclPattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var visibility = DeriveVisibility(match.Groups[1].Value.Trim());
        var name = match.Groups[2].Value;
        var docComment = BuildDocComment(docCommentLines);

        symbols.Add(new SymbolInfo(
            Name: name,
            Kind: SymbolKind.Module,
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

    private static bool TryMatchStruct(
        string trimmed, int lineNumber, int byteOffset,
        List<string> docCommentLines, List<string> attributeLines,
        List<PendingType> parentStack)
    {
        var match = StructPattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var visibility = DeriveVisibility(match.Groups[1].Value.Trim());
        var name = match.Groups[2].Value;

        var signatureBuilder = new StringBuilder();
        foreach (var attr in attributeLines)
        {
            signatureBuilder.Append(attr).Append(' ');
        }

        var sigLine = trimmed.Trim();
        var braceIdx = FindOpenBraceInCode(sigLine);
        var signature = braceIdx >= 0 ? sigLine[..braceIdx].TrimEnd() : sigLine.TrimEnd(';', ' ');
        signature = string.Concat(signatureBuilder.ToString(), signature);

        var docComment = BuildDocComment(docCommentLines);
        var currentDepth = GetCurrentBraceDepth(parentStack);

        if (trimmed.Contains('{', StringComparison.Ordinal))
        {
            parentStack.Add(new PendingType(name, SymbolKind.Class, signature, null, byteOffset, lineNumber, visibility, docComment, currentDepth, true));
        }

        // Tuple structs (end with ;) and unit structs are consumed but not pushed to stack
        return true;
    }

    private static bool TryMatchEnum(
        string trimmed, int lineNumber, int byteOffset,
        List<string> docCommentLines, List<string> attributeLines,
        List<PendingType> parentStack)
    {
        var match = EnumPattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var visibility = DeriveVisibility(match.Groups[1].Value.Trim());
        var name = match.Groups[2].Value;

        var signatureBuilder = new StringBuilder();
        foreach (var attr in attributeLines)
        {
            signatureBuilder.Append(attr).Append(' ');
        }

        var sigLine = trimmed.Trim();
        var braceIdx = FindOpenBraceInCode(sigLine);
        var signature = braceIdx >= 0 ? sigLine[..braceIdx].TrimEnd() : sigLine;
        signature = string.Concat(signatureBuilder.ToString(), signature);

        var docComment = BuildDocComment(docCommentLines);
        var currentDepth = GetCurrentBraceDepth(parentStack);

        parentStack.Add(new PendingType(name, SymbolKind.Enum, signature, null, byteOffset, lineNumber, visibility, docComment, currentDepth, true));
        return true;
    }

    private static bool TryMatchTrait(
        string trimmed, int lineNumber, int byteOffset,
        List<string> docCommentLines, List<PendingType> parentStack)
    {
        var match = TraitPattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var visibility = DeriveVisibility(match.Groups[1].Value.Trim());
        var name = match.Groups[2].Value;

        var sigLine = trimmed.Trim();
        var braceIdx = FindOpenBraceInCode(sigLine);
        var signature = braceIdx >= 0 ? sigLine[..braceIdx].TrimEnd() : sigLine;

        var docComment = BuildDocComment(docCommentLines);
        var currentDepth = GetCurrentBraceDepth(parentStack);

        parentStack.Add(new PendingType(name, SymbolKind.Interface, signature, null, byteOffset, lineNumber, visibility, docComment, currentDepth, true));
        return true;
    }

    private static bool TryMatchImpl(
        string trimmed, int lineNumber, int byteOffset,
        List<PendingType> parentStack)
    {
        var match = ImplPattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        // impl Trait for Type — the type name is the container parent
        var typeName = match.Groups[2].Value;
        var currentDepth = GetCurrentBraceDepth(parentStack);

        // impl blocks are containers — methods inside get parentName = typeName
        // But we don't emit impl as a symbol itself — it's just a grouping construct
        parentStack.Add(new PendingType(typeName, SymbolKind.Class, string.Empty, null, byteOffset, lineNumber, Visibility.Public, null, currentDepth, true));
        return true;
    }

    private static bool TryMatchMacroRules(
        string trimmed, int lineNumber, int byteOffset,
        List<string> docCommentLines, List<PendingType> parentStack)
    {
        var match = MacroRulesPattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var name = match.Groups[1].Value;
        var docComment = BuildDocComment(docCommentLines);
        var currentDepth = GetCurrentBraceDepth(parentStack);

        parentStack.Add(new PendingType(name, SymbolKind.Function, $"macro_rules! {name}", null, byteOffset, lineNumber, Visibility.Public, docComment, currentDepth, false));
        return true;
    }

    private static bool TryMatchFn(
        string trimmed, int lineNumber, int byteOffset,
        List<string> docCommentLines, List<PendingType> parentStack, List<SymbolInfo> symbols)
    {
        var match = FnPattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var visibility = DeriveVisibility(match.Groups[1].Value.Trim());
        var name = match.Groups[2].Value;

        var sigLine = trimmed.Trim();
        var braceIdx = FindOpenBraceInCode(sigLine);
        var signature = braceIdx >= 0 ? sigLine[..braceIdx].TrimEnd() : sigLine.TrimEnd(';', ' ');

        var docComment = BuildDocComment(docCommentLines);
        var parentName = GetCurrentContainerName(parentStack);
        var kind = parentName is not null ? SymbolKind.Method : SymbolKind.Function;

        if (trimmed.EndsWith(';') || !trimmed.Contains('{', StringComparison.Ordinal))
        {
            symbols.Add(new SymbolInfo(name, kind, signature, parentName, byteOffset,
                Encoding.UTF8.GetByteCount(trimmed), lineNumber, lineNumber, visibility, docComment));
        }
        else
        {
            var currentDepth = GetCurrentBraceDepth(parentStack);
            parentStack.Add(new PendingType(name, kind, signature, parentName, byteOffset, lineNumber, visibility, docComment, currentDepth, false));
        }

        return true;
    }

    private static bool TryMatchConst(
        string trimmed, int lineNumber, int byteOffset,
        List<string> docCommentLines, List<SymbolInfo> symbols)
    {
        var match = ConstPattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var visibility = DeriveVisibility(match.Groups[1].Value.Trim());
        var name = match.Groups[2].Value;
        var docComment = BuildDocComment(docCommentLines);

        symbols.Add(new SymbolInfo(name, SymbolKind.Constant, trimmed.Trim().TrimEnd(';'), null, byteOffset,
            Encoding.UTF8.GetByteCount(trimmed), lineNumber, lineNumber, visibility, docComment));
        return true;
    }

    private static bool TryMatchStatic(
        string trimmed, int lineNumber, int byteOffset,
        List<string> docCommentLines, List<SymbolInfo> symbols)
    {
        var match = StaticPattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var visibility = DeriveVisibility(match.Groups[1].Value.Trim());
        var name = match.Groups[2].Value;
        var docComment = BuildDocComment(docCommentLines);

        symbols.Add(new SymbolInfo(name, SymbolKind.Constant, trimmed.Trim().TrimEnd(';'), null, byteOffset,
            Encoding.UTF8.GetByteCount(trimmed), lineNumber, lineNumber, visibility, docComment));
        return true;
    }

    private static bool TryMatchTypeAlias(
        string trimmed, int lineNumber, int byteOffset,
        List<string> docCommentLines, List<SymbolInfo> symbols)
    {
        var match = TypeAliasPattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var rest = match.Groups[3].Value;
        // Only match actual type aliases (with =), not type in impl/trait position
        if (!rest.Contains('=', StringComparison.Ordinal))
        {
            return false;
        }

        var visibility = DeriveVisibility(match.Groups[1].Value.Trim());
        var name = match.Groups[2].Value;
        var docComment = BuildDocComment(docCommentLines);

        symbols.Add(new SymbolInfo(name, SymbolKind.Type, trimmed.Trim().TrimEnd(';'), null, byteOffset,
            Encoding.UTF8.GetByteCount(trimmed), lineNumber, lineNumber, visibility, docComment));
        return true;
    }

    private static Visibility DeriveVisibility(string pubModifier)
    {
        if (string.IsNullOrEmpty(pubModifier))
        {
            return Visibility.Private;
        }

        if (pubModifier.StartsWith("pub(", StringComparison.Ordinal))
        {
            return Visibility.Private; // pub(crate), pub(super) → internal/private
        }

        return pubModifier.StartsWith("pub", StringComparison.Ordinal) ? Visibility.Public : Visibility.Private;
    }

    private static int FindOpenBraceInCode(string text)
    {
        var inString = false;
        var inRawString = false;
        var pos = 0;

        while (pos < text.Length)
        {
            var ch = text[pos];

            if (inString)
            {
                pos += ch == '\\' ? 2 : 1;
                if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (inRawString)
            {
                if (ch == '"')
                {
                    inRawString = false;
                }

                pos++;
                continue;
            }

            switch (ch)
            {
                case '"':
                    inString = true;
                    pos++;
                    break;
                case 'r' when pos + 1 < text.Length && text[pos + 1] == '"':
                    inRawString = true;
                    pos += 2;
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
        var inRawString = false;
        var inChar = false;
        var pos = 0;

        while (pos < line.Length)
        {
            var ch = line[pos];

            if (inString)
            {
                pos += ch == '\\' ? 2 : 1;
                if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (inRawString)
            {
                if (ch == '"')
                {
                    inRawString = false;
                }

                pos++;
                continue;
            }

            if (inChar)
            {
                pos += ch == '\\' ? 2 : 1;
                if (ch == '\'')
                {
                    inChar = false;
                }

                continue;
            }

            switch (ch)
            {
                case '/' when pos + 1 < line.Length && line[pos + 1] == '/':
                    return (opens, closes);
                case '"':
                    inString = true;
                    pos++;
                    break;
                case 'r' when pos + 1 < line.Length && line[pos + 1] == '"':
                    inRawString = true;
                    pos += 2;
                    break;
                case '\'':
                    // Rust: could be char literal or lifetime — heuristic: if followed by \, it's a char
                    if (pos + 1 < line.Length && line[pos + 1] == '\\')
                    {
                        inChar = true;
                    }

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
        // Don't emit impl blocks as symbols — they're just containers
        if (pending.Signature.Length == 0)
        {
            return;
        }

        var byteLength = endByteOffset + Encoding.UTF8.GetByteCount(endLine) - pending.ByteOffset;

        symbols.Add(new SymbolInfo(pending.Name, pending.Kind, pending.Signature, pending.ParentName,
            pending.ByteOffset, byteLength, pending.LineStart, endLineNumber, pending.Visibility, pending.DocComment));
    }

    private static string? BuildDocComment(List<string> docCommentLines)
    {
        return docCommentLines.Count == 0 ? null : string.Join("\n", docCommentLines);
    }

    private static string? GetCurrentContainerName(List<PendingType> parentStack)
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
        return parentStack.Count == 0 ? 0 : parentStack[^1].CurrentBraceDepth;
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
        string Name, SymbolKind Kind, string Signature, string? ParentName,
        int ByteOffset, int LineStart, Visibility Visibility, string? DocComment,
        int BraceDepthAtDeclaration, bool IsContainer)
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

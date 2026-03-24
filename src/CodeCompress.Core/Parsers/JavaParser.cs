using System.Text;
using System.Text.RegularExpressions;
using CodeCompress.Core.Models;

namespace CodeCompress.Core.Parsers;

public sealed partial class JavaParser : ILanguageParser
{
    private static readonly string[] KnownModifiers =
    [
        "public", "private", "protected", "static", "abstract",
        "final", "sealed", "synchronized", "native", "strictfp",
        "transient", "volatile", "default"
    ];

    public string LanguageId => "java";

    public IReadOnlyList<string> FileExtensions { get; } = [".java"];

    [GeneratedRegex(@"^\s*package\s+([\w.]+)\s*;")]
    private static partial Regex PackagePattern();

    [GeneratedRegex(@"^\s*(import\s+(?:static\s+)?([\w.*]+))\s*;")]
    private static partial Regex ImportPattern();

    [GeneratedRegex(@"^\s*((?:(?:public|private|protected|static|abstract|final|sealed|strictfp)\s+)*)(?:(class|interface)\s+|(record)\s+|(enum)\s+|(@interface)\s+)([\w]+)\s*(.*)$")]
    private static partial Regex TypeDeclarationPattern();

    [GeneratedRegex(@"^\s*\*")]
    private static partial Regex JavadocContinuationPattern();

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
        var javadocLines = new List<string>();
        var annotationLines = new List<string>();
        var inBlockComment = false;
        var inJavadoc = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.Trim();
            var lineNumber = i + 1;
            var byteOffset = lineByteOffsets[i];

            // Handle block comments and Javadoc
            if (inBlockComment || inJavadoc)
            {
                if (inJavadoc)
                {
                    javadocLines.Add(trimmed);
                }

                var endIdx = trimmed.IndexOf("*/", StringComparison.Ordinal);
                if (endIdx >= 0)
                {
                    inBlockComment = false;
                    inJavadoc = false;
                    var afterComment = trimmed[(endIdx + 2)..].Trim();
                    if (!string.IsNullOrEmpty(afterComment))
                    {
                        UpdateBraceDepth(afterComment, lineNumber, byteOffset, parentStack, symbols);
                    }
                }

                continue;
            }

            // Javadoc start: /**
            if (trimmed.StartsWith("/**", StringComparison.Ordinal))
            {
                javadocLines.Clear();
                javadocLines.Add(trimmed);
                if (!trimmed.Contains("*/", StringComparison.Ordinal))
                {
                    inJavadoc = true;
                }

                continue;
            }

            // Block comment start: /*
            if (trimmed.StartsWith("/*", StringComparison.Ordinal))
            {
                if (!trimmed.Contains("*/", StringComparison.Ordinal))
                {
                    inBlockComment = true;
                }

                javadocLines.Clear();
                continue;
            }

            // Single-line comment
            if (trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                UpdateBraceDepth(trimmed, lineNumber, byteOffset, parentStack, symbols);
                continue;
            }

            // Collect annotations (preserve across lines until a declaration)
            if (trimmed.StartsWith('@') && !trimmed.StartsWith("@interface", StringComparison.Ordinal))
            {
                annotationLines.Add(trimmed);
                UpdateBraceDepth(trimmed, lineNumber, byteOffset, parentStack, symbols);
                continue;
            }

            var matched = TryMatchPackage(trimmed, dependencies)
                          || TryMatchImport(trimmed, dependencies)
                          || TryMatchTypeDeclaration(trimmed, lineNumber, byteOffset, javadocLines, annotationLines, parentStack)
                          || TryMatchMethod(trimmed, lineNumber, byteOffset, javadocLines, annotationLines, parentStack, symbols)
                          || TryMatchConstant(trimmed, lineNumber, byteOffset, javadocLines, parentStack, symbols);

            if (matched || (!JavadocContinuationPattern().IsMatch(trimmed) && !trimmed.StartsWith('@')))
            {
                javadocLines.Clear();
                annotationLines.Clear();
            }

            UpdateBraceDepth(trimmed, lineNumber, byteOffset, parentStack, symbols);
        }

        return new ParseResult(symbols, dependencies);
    }

    private static bool TryMatchPackage(string trimmed, List<DependencyInfo> dependencies)
    {
        var match = PackagePattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        // Package declarations are tracked as dependencies for module context
        dependencies.Add(new DependencyInfo(RequirePath: match.Groups[1].Value, Alias: null));
        return true;
    }

    private static bool TryMatchImport(string trimmed, List<DependencyInfo> dependencies)
    {
        var match = ImportPattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var importPath = match.Groups[2].Value;
        dependencies.Add(new DependencyInfo(RequirePath: importPath, Alias: null));
        return true;
    }

    private static bool TryMatchTypeDeclaration(
        string trimmed, int lineNumber, int byteOffset,
        List<string> javadocLines, List<string> annotationLines,
        List<PendingType> parentStack)
    {
        var match = TypeDeclarationPattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var modifiers = match.Groups[1].Value.Trim();
        var classOrInterface = match.Groups[2].Value;
        var recordKeyword = match.Groups[3].Value;
        var enumKeyword = match.Groups[4].Value;
        var annotationTypeKeyword = match.Groups[5].Value;
        var name = match.Groups[6].Value;
        var rest = match.Groups[7].Value.Trim();

        SymbolKind kind;
        string keyword;
        if (!string.IsNullOrEmpty(recordKeyword))
        {
            kind = SymbolKind.Record;
            keyword = "record";
        }
        else if (!string.IsNullOrEmpty(enumKeyword))
        {
            kind = SymbolKind.Enum;
            keyword = "enum";
        }
        else if (!string.IsNullOrEmpty(annotationTypeKeyword))
        {
            kind = SymbolKind.Type;
            keyword = "@interface";
        }
        else if (string.Equals(classOrInterface, "interface", StringComparison.Ordinal))
        {
            kind = SymbolKind.Interface;
            keyword = "interface";
        }
        else
        {
            kind = SymbolKind.Class;
            keyword = "class";
        }

        // Build signature
        var signatureBuilder = new StringBuilder();

        // Include annotations in signature
        foreach (var annotation in annotationLines)
        {
            signatureBuilder.Append(annotation).Append(' ');
        }

        if (!string.IsNullOrEmpty(modifiers))
        {
            signatureBuilder.Append(modifiers).Append(' ');
        }

        signatureBuilder.Append(keyword).Append(' ').Append(name);

        // Extract generic parameters
        var (genericParams, restAfterGenerics) = ExtractGenericParameters(rest);
        if (!string.IsNullOrEmpty(genericParams))
        {
            signatureBuilder.Append(genericParams);
        }

        // Add extends/implements/permits clauses (up to '{')
        if (!string.IsNullOrEmpty(restAfterGenerics))
        {
            // For records, include the component list
            if (kind == SymbolKind.Record && restAfterGenerics.StartsWith('('))
            {
                var closeParenIdx = FindMatchingCloseParen(restAfterGenerics, 0);
                if (closeParenIdx >= 0)
                {
                    signatureBuilder.Append(restAfterGenerics[..(closeParenIdx + 1)]);
                    restAfterGenerics = restAfterGenerics[(closeParenIdx + 1)..].TrimStart();
                }
            }

            var braceIdx = FindOpenBraceInCode(restAfterGenerics);
            var clausePart = braceIdx >= 0
                ? restAfterGenerics[..braceIdx].Trim()
                : restAfterGenerics.TrimEnd('{', ' ');

            if (!string.IsNullOrEmpty(clausePart))
            {
                signatureBuilder.Append(' ').Append(clausePart);
            }
        }

        var signature = signatureBuilder.ToString().TrimEnd();
        var docComment = BuildDocComment(javadocLines);
        var parentName = GetCurrentParentName(parentStack);
        var visibility = DeriveVisibility(modifiers);
        var currentDepth = GetCurrentBraceDepth(parentStack);

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
            IsNamespace: false,
            IsContainer: true));

        return true;
    }

    private static bool TryMatchMethod(
        string trimmed, int lineNumber, int byteOffset,
        List<string> javadocLines, List<string> annotationLines,
        List<PendingType> parentStack, List<SymbolInfo> symbols)
    {
        if (!IsInsideTypeBody(parentStack))
        {
            return false;
        }

        // Find first '(' that represents a method parameter list
        var parenPos = FindMethodParenOpen(trimmed);
        if (parenPos < 0)
        {
            return false;
        }

        // Find matching ')'
        var closeParenPos = FindMatchingCloseParen(trimmed, parenPos);
        if (closeParenPos < 0)
        {
            return false;
        }

        var prefix = trimmed[..parenPos].TrimEnd();
        var paramsText = trimmed[parenPos..(closeParenPos + 1)];
        var afterParams = trimmed[(closeParenPos + 1)..].TrimStart();

        // Extract name and modifiers from prefix
        var (name, modifiers) = ExtractMethodNameAndModifiers(prefix);
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        // Build signature
        var signatureBuilder = new StringBuilder();

        foreach (var annotation in annotationLines)
        {
            signatureBuilder.Append(annotation).Append(' ');
        }

        signatureBuilder.Append(prefix).Append(paramsText);

        // Add throws clause if present
        if (afterParams.StartsWith("throws ", StringComparison.Ordinal))
        {
            var throwsEnd = afterParams.IndexOfAny(['{', ';']);
            var throwsClause = throwsEnd >= 0
                ? afterParams[..throwsEnd].TrimEnd()
                : afterParams.TrimEnd('{', ';', ' ');
            signatureBuilder.Append(' ').Append(throwsClause);
        }

        var signature = signatureBuilder.ToString().TrimEnd();
        var docComment = BuildDocComment(javadocLines);
        var parentName = GetCurrentParentName(parentStack);
        var visibility = DeriveVisibility(modifiers);

        var isSingleLine = trimmed.EndsWith(';') || !trimmed.Contains('{', StringComparison.Ordinal);

        if (isSingleLine && trimmed.EndsWith(';'))
        {
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
        else
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
                IsNamespace: false,
                IsContainer: false));
        }

        return true;
    }

    private static bool TryMatchConstant(
        string trimmed, int lineNumber, int byteOffset,
        List<string> javadocLines, List<PendingType> parentStack, List<SymbolInfo> symbols)
    {
        if (!IsInsideTypeBody(parentStack))
        {
            return false;
        }

        // Detect static final constant declarations
        if (!trimmed.Contains("static", StringComparison.Ordinal) ||
            !trimmed.Contains("final", StringComparison.Ordinal))
        {
            return false;
        }

        // Must end with ; and contain =
        if (!trimmed.Contains('=', StringComparison.Ordinal) || !trimmed.EndsWith(';'))
        {
            return false;
        }

        // Must not contain '(' (that would be a method)
        if (trimmed.Contains('(', StringComparison.Ordinal))
        {
            return false;
        }

        var equalsIdx = trimmed.IndexOf('=', StringComparison.Ordinal);
        var beforeEquals = trimmed[..equalsIdx].TrimEnd();
        var parts = beforeEquals.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 3)
        {
            return false;
        }

        var name = parts[^1];
        var modifiers = ExtractModifiers(parts);
        var docComment = BuildDocComment(javadocLines);
        var parentName = GetCurrentParentName(parentStack);
        var visibility = DeriveVisibility(modifiers);

        // Build signature: everything before the '='
        var signature = beforeEquals;

        symbols.Add(new SymbolInfo(
            Name: name,
            Kind: SymbolKind.Constant,
            Signature: signature,
            ParentSymbol: parentName,
            ByteOffset: byteOffset,
            ByteLength: Encoding.UTF8.GetByteCount(trimmed),
            LineStart: lineNumber,
            LineEnd: lineNumber,
            Visibility: visibility,
            DocComment: docComment));

        return true;
    }

    private static (string Name, string Modifiers) ExtractMethodNameAndModifiers(string prefix)
    {
        var parts = prefix.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return (string.Empty, string.Empty);
        }

        var modifiers = ExtractModifiers(parts);

        // Find non-modifier tokens
        var nonModifierStart = 0;
        foreach (var part in parts)
        {
            if (IsModifier(part))
            {
                nonModifierStart++;
            }
            else
            {
                break;
            }
        }

        var remaining = parts[nonModifierStart..];

        if (remaining.Length == 0)
        {
            return (string.Empty, modifiers);
        }

        // Last token is the method/constructor name (may include generic params)
        var lastToken = remaining[^1];

        // Strip generic params from name
        var genericIdx = lastToken.IndexOf('<', StringComparison.Ordinal);
        var name = genericIdx >= 0 ? lastToken[..genericIdx] : lastToken;

        return (name, modifiers);
    }

    private static string ExtractModifiers(string[] parts)
    {
        var modifierList = new List<string>();
        foreach (var part in parts)
        {
            if (IsModifier(part))
            {
                modifierList.Add(part);
            }
            else
            {
                break;
            }
        }

        return string.Join(" ", modifierList);
    }

    private static bool IsModifier(string word)
    {
        return Array.Exists(KnownModifiers, m => string.Equals(m, word, StringComparison.Ordinal));
    }

    private static int FindMethodParenOpen(string line)
    {
        var angleBracketDepth = 0;
        var state = CharScanState.Normal;
        var pos = 0;

        while (pos < line.Length)
        {
            var ch = line[pos];

            if (state != CharScanState.Normal)
            {
                pos = AdvanceNonNormalState(pos, ch, ref state);
                continue;
            }

            switch (ch)
            {
                case '/' when pos + 1 < line.Length && line[pos + 1] == '/':
                    return -1;
                case '"':
                    state = CharScanState.InString;
                    pos++;
                    break;
                case '\'':
                    state = CharScanState.InCharLiteral;
                    pos++;
                    break;
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
                    // Check if preceded by a word char or '>' (method/constructor params)
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
        return char.IsLetterOrDigit(ch) || ch == '>';
    }

    private static int AdvanceNonNormalState(int pos, char ch, ref CharScanState state)
    {
        switch (state)
        {
            case CharScanState.InString:
                if (ch == '\\')
                {
                    return pos + 2;
                }

                if (ch == '"')
                {
                    state = CharScanState.Normal;
                }

                return pos + 1;

            case CharScanState.InCharLiteral:
                if (ch == '\\')
                {
                    return pos + 2;
                }

                if (ch == '\'')
                {
                    state = CharScanState.Normal;
                }

                return pos + 1;

            default:
                return pos + 1;
        }
    }

    private static int FindMatchingCloseParen(string text, int openPos)
    {
        var depth = 0;
        for (var i = openPos; i < text.Length; i++)
        {
            if (text[i] == '(')
            {
                depth++;
            }
            else if (text[i] == ')')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
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

    private static (string? GenericPart, string Remainder) ExtractGenericParameters(string text)
    {
        if (text.Length == 0 || text[0] != '<')
        {
            return (null, text);
        }

        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '<')
            {
                depth++;
            }
            else if (text[i] == '>')
            {
                depth--;
            }

            if (depth == 0)
            {
                return (text[..(i + 1)], text[(i + 1)..].TrimStart());
            }
        }

        return (null, text);
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
                case CharScanState.InCharLiteral:
                    pos = AdvanceNonNormalState(pos, ch, ref state);
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

    private static (int Opens, int Closes) CountBraces(string line)
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
                    if (ch == '/' && pos + 1 < line.Length)
                    {
                        if (line[pos + 1] == '/')
                        {
                            return (opens, closes);
                        }

                        if (line[pos + 1] == '*')
                        {
                            state = CharScanState.InBlockComment;
                            pos += 2;
                            continue;
                        }
                    }

                    if (ch == '"')
                    {
                        state = CharScanState.InString;
                    }
                    else if (ch == '\'')
                    {
                        state = CharScanState.InCharLiteral;
                    }
                    else if (ch == '{')
                    {
                        opens++;
                    }
                    else if (ch == '}')
                    {
                        closes++;
                    }

                    pos++;
                    break;

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
                case CharScanState.InCharLiteral:
                    pos = AdvanceNonNormalState(pos, ch, ref state);
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

    private static Visibility DeriveVisibility(string modifiers)
    {
        if (string.IsNullOrEmpty(modifiers))
        {
            // Java: no modifier = package-private (mapped to Private in our model)
            return Visibility.Private;
        }

        if (modifiers.Contains("private", StringComparison.Ordinal))
        {
            return Visibility.Private;
        }

        if (modifiers.Contains("protected", StringComparison.Ordinal))
        {
            return Visibility.Private;
        }

        if (modifiers.Contains("public", StringComparison.Ordinal))
        {
            return Visibility.Public;
        }

        // Package-private (no access modifier, but has other modifiers like static/final)
        return Visibility.Private;
    }

    private static string? BuildDocComment(List<string> javadocLines)
    {
        if (javadocLines.Count == 0)
        {
            return null;
        }

        return string.Join("\n", javadocLines);
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

    private enum CharScanState
    {
        Normal,
        InBlockComment,
        InString,
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
        bool IsNamespace,
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
        public bool IsNamespace { get; } = IsNamespace;
        public bool IsContainer { get; } = IsContainer;
        public int CurrentBraceDepth { get; set; } = BraceDepthAtDeclaration;
    }
}

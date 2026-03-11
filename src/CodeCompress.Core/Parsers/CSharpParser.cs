using System.Text;
using System.Text.RegularExpressions;
using CodeCompress.Core.Models;

namespace CodeCompress.Core.Parsers;

public sealed partial class CSharpParser : ILanguageParser
{
    private static readonly string[] KnownModifiers =
    [
        "public", "internal", "private", "protected", "static", "abstract",
        "sealed", "partial", "readonly", "file", "required", "new",
        "virtual", "override", "async", "extern", "unsafe", "volatile"
    ];

    public string LanguageId => "csharp";

    public IReadOnlyList<string> FileExtensions { get; } = [".cs"];

    [GeneratedRegex(@"^\s*namespace\s+([\w.]+)\s*;")]
    private static partial Regex FileScopedNamespacePattern();

    [GeneratedRegex(@"^\s*namespace\s+([\w.]+)\s*(?:\{.*)?$")]
    private static partial Regex BlockScopedNamespacePattern();

    [GeneratedRegex(@"^\s*((?:(?:public|internal|private|protected|static|abstract|sealed|partial|readonly|file|required|new)\s+)*)(?:(class|interface|struct)\s+|(record)\s+(?:(class|struct)\s+)?)([\w]+)\s*(.*)$")]
    private static partial Regex TypeDeclarationPattern();

    [GeneratedRegex(@"^\s*((?:(?:public|internal|private|protected|static|abstract|sealed|partial|readonly|file|required|new)\s+)*)enum\s+(\w+)\s*(.*)$")]
    private static partial Regex EnumDeclarationPattern();

    [GeneratedRegex(@"^\s*///")]
    private static partial Regex XmlDocCommentPattern();

    [GeneratedRegex(@"^\s*(global\s+)?using\s+(static\s+)?(?:(\w[\w.]*)\s*=\s*)?(.+?)\s*;\s*$")]
    private static partial Regex UsingStatementPattern();

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

            // Skip attribute lines — preserve doc comments
            if (IsAttributeLine(trimmed))
            {
                UpdateBraceDepth(trimmed, lineNumber, byteOffset, parentStack, symbols);
                continue;
            }

            var matched = TryMatchUsingStatement(trimmed, parentStack, dependencies)
                          || TryMatchFileScopedNamespace(trimmed, lineNumber, byteOffset, docCommentLines, symbols)
                          || TryMatchBlockScopedNamespace(trimmed, lineNumber, byteOffset, docCommentLines, parentStack)
                          || TryMatchEnum(trimmed, lineNumber, byteOffset, docCommentLines, parentStack)
                          || TryMatchTypeDeclaration(trimmed, lineNumber, byteOffset, docCommentLines, parentStack, symbols)
                          || TryMatchProperty(trimmed, lineNumber, byteOffset, docCommentLines, parentStack, symbols)
                          || TryMatchMethod(trimmed, lineNumber, byteOffset, docCommentLines, parentStack, symbols);

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

        return new ParseResult(symbols, dependencies);
    }

    private static bool IsAttributeLine(string trimmed)
    {
        return trimmed.StartsWith('[') && !trimmed.StartsWith("[]", StringComparison.Ordinal);
    }

    private static bool TryMatchUsingStatement(
        string trimmed, List<PendingType> parentStack, List<DependencyInfo> dependencies)
    {
        // Only match at file level (not inside type bodies)
        if (IsInsideTypeBody(parentStack))
        {
            return false;
        }

        var match = UsingStatementPattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var path = match.Groups[4].Value.Trim();

        // Skip `using var` (local variable declaration in top-level statements)
        if (path.StartsWith("var ", StringComparison.Ordinal))
        {
            return false;
        }

        // Skip `using (` (using statement with disposable)
        if (path.StartsWith('('))
        {
            return false;
        }

        var alias = match.Groups[3].Success && match.Groups[3].Value.Length > 0
            ? match.Groups[3].Value
            : null;

        dependencies.Add(new DependencyInfo(RequirePath: path, Alias: alias));

        return true;
    }

    private static bool TryMatchProperty(
        string trimmed, int lineNumber, int byteOffset,
        List<string> docCommentLines, List<PendingType> parentStack, List<SymbolInfo> symbols)
    {
        if (!IsInsideTypeBody(parentStack))
        {
            return false;
        }

        // Check for accessor property: has `{ get`/`{ set`/`{ init` without `)` before `{`
        var bracePos = FindOpenBraceInCode(trimmed);
        if (bracePos >= 0)
        {
            var afterBrace = trimmed[(bracePos + 1)..].TrimStart();
            var isAccessorProperty = afterBrace.StartsWith("get", StringComparison.Ordinal)
                                     || afterBrace.StartsWith("set", StringComparison.Ordinal)
                                     || afterBrace.StartsWith("init", StringComparison.Ordinal);

            if (isAccessorProperty)
            {
                var beforeBrace = trimmed[..bracePos].TrimEnd();
                // If `)` appears right before `{` (with spaces), it's a method body
                if (beforeBrace.EndsWith(')'))
                {
                    return false;
                }

                return ExtractProperty(trimmed, trimmed, lineNumber, byteOffset, docCommentLines, parentStack, symbols);
            }
        }

        // Check for expression-bodied property: has `=>` without `)` before `=>`
        var arrowPos = trimmed.IndexOf("=>", StringComparison.Ordinal);
        if (arrowPos > 0)
        {
            var beforeArrow = trimmed[..arrowPos].TrimEnd();
            // If `)` or `]` right before `=>`, it's a method or indexer body
            if (beforeArrow.EndsWith(')') || beforeArrow.EndsWith(']'))
            {
                return false;
            }

            return ExtractProperty(trimmed, trimmed, lineNumber, byteOffset, docCommentLines, parentStack, symbols);
        }

        return false;
    }

    private static bool ExtractProperty(
        string trimmed, string signatureText, int lineNumber, int byteOffset,
        List<string> docCommentLines, List<PendingType> parentStack, List<SymbolInfo> symbols)
    {
        // Parse: [modifiers] Type Name { ... } or [modifiers] Type Name => ...
        // Find the property name: last word before `{` or `=>`
        var signatureEnd = trimmed.IndexOf('{', StringComparison.Ordinal);
        if (signatureEnd < 0)
        {
            signatureEnd = trimmed.IndexOf("=>", StringComparison.Ordinal);
        }

        if (signatureEnd < 0)
        {
            return false;
        }

        var prefix = trimmed[..signatureEnd].TrimEnd();
        var parts = prefix.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        var name = parts[^1];

        // Extract modifiers
        var modifiers = ExtractModifiers(parts);

        var docComment = BuildDocComment(docCommentLines);
        var parentName = GetCurrentParentName(parentStack);
        var visibility = DeriveVisibility(modifiers, isNested: true);

        symbols.Add(new SymbolInfo(
            Name: name,
            Kind: SymbolKind.Constant,
            Signature: signatureText,
            ParentSymbol: parentName,
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
        List<string> docCommentLines, List<PendingType> parentStack, List<SymbolInfo> symbols)
    {
        if (!IsInsideTypeBody(parentStack))
        {
            return false;
        }

        // Try indexer: `this[`
        if (TryMatchIndexer(trimmed, lineNumber, byteOffset, docCommentLines, parentStack))
        {
            return true;
        }

        // Try finalizer: `~Name(`
        if (TryMatchFinalizer(trimmed, lineNumber, byteOffset, docCommentLines, parentStack))
        {
            return true;
        }

        // Find first `(` at angle-bracket depth 0
        var parenPos = FindMethodParenOpen(trimmed);
        if (parenPos < 0)
        {
            return false;
        }

        // Find matching `)`
        var closeParenPos = FindMatchingCloseParen(trimmed, parenPos);
        if (closeParenPos < 0)
        {
            return false;
        }

        var prefix = trimmed[..parenPos].TrimEnd();
        var paramsText = trimmed[parenPos..(closeParenPos + 1)];
        var afterParams = trimmed[(closeParenPos + 1)..].TrimStart();

        // Build signature: prefix + params + optional where constraints
        var signatureBuilder = new StringBuilder();
        signatureBuilder.Append(prefix).Append(paramsText);

        if (afterParams.StartsWith("where ", StringComparison.Ordinal))
        {
            var constraintEnd = afterParams.IndexOfAny(['{', '=']);
            var constraint = constraintEnd >= 0
                ? afterParams[..constraintEnd].TrimEnd()
                : afterParams.TrimEnd(';', ' ');
            signatureBuilder.Append(' ').Append(constraint);
        }

        var signature = signatureBuilder.ToString().TrimEnd();

        // Extract name and modifiers from prefix
        var (name, modifiers) = ExtractMethodNameAndModifiers(prefix);

        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        var docComment = BuildDocComment(docCommentLines);
        var parentName = GetCurrentParentName(parentStack);
        var visibility = DeriveVisibility(modifiers, isNested: true);

        // Determine if single-line (expression-bodied or semicolon)
        var isSingleLine = trimmed.EndsWith(';')
                           || (afterParams.Contains("=>", StringComparison.Ordinal) && trimmed.EndsWith(';'));

        if (isSingleLine)
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

    private static bool TryMatchIndexer(
        string trimmed, int lineNumber, int byteOffset,
        List<string> docCommentLines, List<PendingType> parentStack)
    {
        var thisIdx = trimmed.IndexOf("this[", StringComparison.Ordinal);
        if (thisIdx < 0)
        {
            return false;
        }

        // Ensure `this[` is a member declaration, not inside code
        var beforeThis = trimmed[..thisIdx].TrimEnd();
        var parts = beforeThis.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        // Build signature up to `]`
        var closeBracket = trimmed.IndexOf(']', thisIdx);
        if (closeBracket < 0)
        {
            return false;
        }

        var signature = trimmed[..(closeBracket + 1)];
        var modifiers = ExtractModifiers(parts);

        var docComment = BuildDocComment(docCommentLines);
        var parentName = GetCurrentParentName(parentStack);
        var visibility = DeriveVisibility(modifiers, isNested: true);

        var currentDepth = GetCurrentBraceDepth(parentStack);
        parentStack.Add(new PendingType(
            Name: "this[]",
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

        return true;
    }

    private static bool TryMatchFinalizer(
        string trimmed, int lineNumber, int byteOffset,
        List<string> docCommentLines, List<PendingType> parentStack)
    {
        if (!trimmed.StartsWith('~'))
        {
            return false;
        }

        var parenPos = trimmed.IndexOf('(', StringComparison.Ordinal);
        if (parenPos < 0)
        {
            return false;
        }

        var name = trimmed[..parenPos].Trim();
        var closeParenPos = trimmed.IndexOf(')', parenPos);
        if (closeParenPos < 0)
        {
            return false;
        }

        var signature = trimmed[..(closeParenPos + 1)];

        var docComment = BuildDocComment(docCommentLines);
        var parentName = GetCurrentParentName(parentStack);

        var currentDepth = GetCurrentBraceDepth(parentStack);
        parentStack.Add(new PendingType(
            Name: name,
            Kind: SymbolKind.Method,
            Signature: signature,
            ParentName: parentName,
            ByteOffset: byteOffset,
            LineStart: lineNumber,
            Visibility: Visibility.Private,
            DocComment: docComment,
            BraceDepthAtDeclaration: currentDepth,
            IsNamespace: false,
            IsContainer: false));

        return true;
    }

    private static (string Name, string Modifiers) ExtractMethodNameAndModifiers(
        string prefix)
    {
        var parts = prefix.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return (string.Empty, string.Empty);
        }

        var modifiers = ExtractModifiers(parts);

        // Find the non-modifier tokens
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

        // Check for operator: `ReturnType operator +`
        var operatorIdx = Array.IndexOf(remaining, "operator");
        if (operatorIdx >= 0 && operatorIdx + 1 < remaining.Length)
        {
            var operatorSymbol = remaining[operatorIdx + 1];
            return ($"operator {operatorSymbol}", modifiers);
        }

        // Last token is the name (may have generic params)
        var lastToken = remaining[^1];

        // Strip generic params from name for display
        var genericIdx = lastToken.IndexOf('<', StringComparison.Ordinal);
        var name = genericIdx >= 0 ? lastToken[..genericIdx] : lastToken;

        // If name contains '.', it's explicit interface implementation
        // Name stays as-is (e.g., "IDisposable.Dispose")

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
        // Find the `(` that represents the method parameter list, not tuple type parens.
        // Strategy: find `(` that follows a word char, `>` (generic), or `]` (indexer)
        // and is at angle-bracket depth 0 and paren depth 0.
        var angleBracketDepth = 0;
        var parenDepth = 0;
        var pos = 0;
        var state = CharScanState.Normal;

        while (pos < line.Length)
        {
            var ch = line[pos];

            if (state != CharScanState.Normal)
            {
                pos = AdvanceNonNormalState(line, pos, ch, ref state);
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
                case '@' when pos + 1 < line.Length && line[pos + 1] == '"':
                    state = CharScanState.InVerbatimString;
                    pos += 2;
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
                case '(' when angleBracketDepth == 0 && parenDepth == 0:
                    // Check if this is a method parameter `(` vs a tuple type `(`
                    // Method params follow: word char, `>`, `~`, or operator symbol
                    // Tuple type `(` follows a space after a modifier keyword
                    if (pos > 0 && IsMethodParenPreceder(line[pos - 1]))
                    {
                        return pos;
                    }

                    // Check if preceded by operator symbol (with possible space)
                    var beforeParen = line[..pos].TrimEnd();
                    if (beforeParen.Length > 0 && IsOperatorChar(beforeParen[^1]))
                    {
                        return pos;
                    }

                    // This is likely a tuple type paren — skip the balanced group
                    parenDepth++;
                    pos++;
                    break;
                case '(' when parenDepth > 0:
                    parenDepth++;
                    pos++;
                    break;
                case ')' when parenDepth > 0:
                    parenDepth--;
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
        return char.IsLetterOrDigit(ch) || ch == '>' || ch == '~' || ch == ']';
    }

    private static bool IsOperatorChar(char ch)
    {
        return ch is '+' or '-' or '*' or '/' or '%' or '&' or '|' or '^'
            or '!' or '<' or '>' or '=' or '~';
    }

    private static int AdvanceNonNormalState(string line, int pos, char ch, ref CharScanState state)
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

            case CharScanState.InVerbatimString:
                if (ch == '"')
                {
                    if (pos + 1 < line.Length && line[pos + 1] == '"')
                    {
                        return pos + 2;
                    }

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
            var ch = text[i];
            if (ch == '(')
            {
                depth++;
            }
            else if (ch == ')')
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
        // Check if we're directly inside a type body (not nested inside a method body)
        // The current depth should be exactly one more than the type's declaration depth
        var currentDepth = GetCurrentBraceDepth(parentStack);

        for (var i = parentStack.Count - 1; i >= 0; i--)
        {
            var entry = parentStack[i];

            // If we encounter a non-container (method/property), we're inside its body
            if (!entry.IsContainer && !entry.IsNamespace)
            {
                return false;
            }

            if (entry.IsContainer)
            {
                // Don't match methods/properties inside enums
                if (entry.Kind == SymbolKind.Type)
                {
                    return false;
                }

                // We're directly inside this type if depth is exactly type's depth + 1
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
        var namespaceName = match.Groups[1].Value;
        var signature = $"namespace {namespaceName}";

        parentStack.Add(new PendingType(
            Name: namespaceName,
            Kind: SymbolKind.Module,
            Signature: signature,
            ParentName: null,
            ByteOffset: byteOffset,
            LineStart: lineNumber,
            Visibility: Visibility.Public,
            DocComment: docComment,
            BraceDepthAtDeclaration: currentDepth,
            IsNamespace: true,
            IsContainer: false));

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
            IsNamespace: false,
            IsContainer: true));

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
        var restRaw = match.Groups[6].Value.Trim();

        // Extract generic parameters using balanced angle-bracket matching
        var (genericParams, rest) = ExtractGenericParameters(restRaw);

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
                IsNamespace: false,
                IsContainer: true));
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
                case CharScanState.InVerbatimString:
                case CharScanState.InCharLiteral:
                    pos = AdvanceNonNormalState(line, pos, ch, ref state);
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
                case CharScanState.InCharLiteral:
                    pos = AdvanceNonNormalState(text, pos, ch, ref state);
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

        return (null, text); // Unbalanced — treat as no generics
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

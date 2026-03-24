using System.Text;
using System.Text.RegularExpressions;
using CodeCompress.Core.Models;

namespace CodeCompress.Core.Parsers;

public sealed partial class GoParser : ILanguageParser
{
    public string LanguageId => "go";

    public IReadOnlyList<string> FileExtensions { get; } = [".go"];

    // package models
    [GeneratedRegex(@"^\s*package\s+(\w+)")]
    private static partial Regex PackagePattern();

    // import "fmt" or import alias "path"
    [GeneratedRegex(@"^\s*import\s+(?:(\w+)\s+)?""([^""]+)""")]
    private static partial Regex SingleImportPattern();

    // Lines inside import ( ... ) block: optional alias + quoted path
    [GeneratedRegex(@"^\s*(?:(\w+)\s+)?""([^""]+)""")]
    private static partial Regex GroupedImportLinePattern();

    // func Name(...) or func (r *Type) Name(...)
    [GeneratedRegex(@"^\s*func\s+(.+)")]
    private static partial Regex FuncPattern();

    // type Name struct/interface/... or type Name[T any] struct/...
    [GeneratedRegex(@"^\s*type\s+(\w+)(\[.*?\])?\s+(struct|interface)\s*(.*)$")]
    private static partial Regex TypeDeclPattern();

    // type Name = Other or type Name Other
    [GeneratedRegex(@"^\s*type\s+(\w+)(\[.*?\])?\s+(.+)$")]
    private static partial Regex TypeAliasPattern();

    // const Name = ... or const Name Type = ...
    [GeneratedRegex(@"^\s*(?:const|var)\s+(\w+)\s+(.*)$")]
    private static partial Regex SingleConstVarPattern();

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
        var inImportBlock = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.Trim();
            var lineNumber = i + 1;
            var byteOffset = lineByteOffsets[i];

            // Block comments
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

            // Import block handling
            if (inImportBlock)
            {
                if (trimmed == ")")
                {
                    inImportBlock = false;
                    continue;
                }

                var importLineMatch = GroupedImportLinePattern().Match(trimmed);
                if (importLineMatch.Success)
                {
                    var alias = importLineMatch.Groups[1].Success && importLineMatch.Groups[1].Value.Length > 0
                        ? importLineMatch.Groups[1].Value
                        : null;
                    dependencies.Add(new DependencyInfo(RequirePath: importLineMatch.Groups[2].Value, Alias: alias));
                }

                continue;
            }

            if (trimmed.StartsWith("import (", StringComparison.Ordinal) || trimmed == "import(")
            {
                inImportBlock = true;
                continue;
            }

            // Single-line doc comment
            if (trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                docCommentLines.Add(trimmed);
                continue;
            }

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                docCommentLines.Clear();
                UpdateBraceDepth(trimmed, lineNumber, byteOffset, parentStack, symbols);
                continue;
            }

            var matched = TryMatchPackage(trimmed, symbols, lineNumber, byteOffset, docCommentLines)
                          || TryMatchSingleImport(trimmed, dependencies)
                          || TryMatchConstVarBlock(trimmed)
                          || TryMatchTypeDeclaration(trimmed, lineNumber, byteOffset, docCommentLines, parentStack)
                          || TryMatchTypeAlias(trimmed, lineNumber, byteOffset, docCommentLines, symbols)
                          || TryMatchSingleConstVar(trimmed, lineNumber, byteOffset, docCommentLines, symbols)
                          || TryMatchFunc(trimmed, lineNumber, byteOffset, docCommentLines, parentStack, symbols);

            if (matched || !trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                docCommentLines.Clear();
            }

            UpdateBraceDepth(trimmed, lineNumber, byteOffset, parentStack, symbols);
        }

        return new ParseResult(symbols, dependencies);
    }

    private static bool TryMatchPackage(
        string trimmed, List<SymbolInfo> symbols, int lineNumber, int byteOffset,
        List<string> docCommentLines)
    {
        var match = PackagePattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var docComment = BuildDocComment(docCommentLines);

        symbols.Add(new SymbolInfo(
            Name: match.Groups[1].Value,
            Kind: SymbolKind.Module,
            Signature: trimmed.Trim(),
            ParentSymbol: null,
            ByteOffset: byteOffset,
            ByteLength: Encoding.UTF8.GetByteCount(trimmed),
            LineStart: lineNumber,
            LineEnd: lineNumber,
            Visibility: Visibility.Public,
            DocComment: docComment));

        return true;
    }

    private static bool TryMatchSingleImport(string trimmed, List<DependencyInfo> dependencies)
    {
        var match = SingleImportPattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var alias = match.Groups[1].Success && match.Groups[1].Value.Length > 0
            ? match.Groups[1].Value
            : null;
        dependencies.Add(new DependencyInfo(RequirePath: match.Groups[2].Value, Alias: alias));
        return true;
    }

    private static bool TryMatchConstVarBlock(string trimmed)
    {
        // Match: const ( or var ( — these use parens not braces, so our brace tracker won't close them.
        // Return true to consume the line; individual const/var lines inside are parsed separately.
        return trimmed is "const (" or "var (" or "const(" or "var(";
    }

    private static bool TryMatchTypeDeclaration(
        string trimmed, int lineNumber, int byteOffset,
        List<string> docCommentLines, List<PendingType> parentStack)
    {
        var match = TypeDeclPattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var name = match.Groups[1].Value;
        var genericParams = match.Groups[2].Value;
        var keyword = match.Groups[3].Value;

        SymbolKind kind = keyword switch
        {
            "interface" => SymbolKind.Interface,
            _ => SymbolKind.Class,
        };

        var signatureBuilder = new StringBuilder("type ").Append(name);
        if (!string.IsNullOrEmpty(genericParams))
        {
            signatureBuilder.Append(genericParams);
        }

        signatureBuilder.Append(' ').Append(keyword);

        var signature = signatureBuilder.ToString();
        var docComment = BuildDocComment(docCommentLines);
        var visibility = DeriveVisibility(name);
        var currentDepth = GetCurrentBraceDepth(parentStack);

        parentStack.Add(new PendingType(
            Name: name,
            Kind: kind,
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
        List<string> docCommentLines, List<SymbolInfo> symbols)
    {
        var match = TypeAliasPattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var name = match.Groups[1].Value;
        var rest = match.Groups[3].Value.Trim();

        // Skip if this was already matched as struct/interface
        if (rest.StartsWith("struct", StringComparison.Ordinal) || rest.StartsWith("interface", StringComparison.Ordinal))
        {
            return false;
        }

        // Skip if rest contains '{' — it's a struct/interface with opening brace on same line
        if (rest.Contains('{', StringComparison.Ordinal))
        {
            return false;
        }

        var docComment = BuildDocComment(docCommentLines);
        var visibility = DeriveVisibility(name);

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

    private static bool TryMatchSingleConstVar(
        string trimmed, int lineNumber, int byteOffset,
        List<string> docCommentLines, List<SymbolInfo> symbols)
    {
        var match = SingleConstVarPattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var name = match.Groups[1].Value;
        var docComment = BuildDocComment(docCommentLines);
        var visibility = DeriveVisibility(name);

        symbols.Add(new SymbolInfo(
            Name: name,
            Kind: SymbolKind.Constant,
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

    private static bool TryMatchFunc(
        string trimmed, int lineNumber, int byteOffset,
        List<string> docCommentLines, List<PendingType> parentStack, List<SymbolInfo> symbols)
    {
        var match = FuncPattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var rest = match.Groups[1].Value;

        // Check for method with receiver: (r *Type) Name(...)
        string? parentName = null;
        if (rest.StartsWith('('))
        {
            var closeReceiverParen = rest.IndexOf(')', StringComparison.Ordinal);
            if (closeReceiverParen < 0)
            {
                return false;
            }

            var receiverText = rest[1..closeReceiverParen].Trim();
            parentName = ExtractReceiverType(receiverText);
            rest = rest[(closeReceiverParen + 1)..].TrimStart();
        }

        // Extract function/method name
        var nameEnd = rest.IndexOfAny(['(', '[']);
        if (nameEnd < 0)
        {
            return false;
        }

        var name = rest[..nameEnd].Trim();
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        // Build signature from the full func line
        var signature = trimmed.Trim();
        // Remove the body part (everything from '{' onward)
        var braceIdx = FindOpenBraceInCode(signature);
        if (braceIdx >= 0)
        {
            signature = signature[..braceIdx].TrimEnd();
        }

        var docComment = BuildDocComment(docCommentLines);
        var visibility = DeriveVisibility(name);
        var kind = parentName is not null ? SymbolKind.Method : SymbolKind.Function;

        // Check if single-line or has body
        if (!trimmed.Contains('{', StringComparison.Ordinal))
        {
            // Interface method or forward declaration
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
                IsContainer: false));
        }

        return true;
    }

    private static string? ExtractReceiverType(string receiverText)
    {
        // Receiver formats: "r Type", "r *Type", "*Type", "Type"
        var parts = receiverText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        var typePart = parts[^1];
        // Strip pointer prefix
        return typePart.TrimStart('*');
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
                if (ch == '`')
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
                case '`':
                    inRawString = true;
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
        var inRune = false;
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
                if (ch == '`')
                {
                    inRawString = false;
                }

                pos++;
                continue;
            }

            if (inRune)
            {
                pos += ch == '\\' ? 2 : 1;
                if (ch == '\'')
                {
                    inRune = false;
                }

                continue;
            }

            switch (ch)
            {
                case '/' when pos + 1 < line.Length && line[pos + 1] == '/':
                    return (opens, closes);
                case '"':
                    inString = true;
                    break;
                case '`':
                    inRawString = true;
                    break;
                case '\'':
                    inRune = true;
                    break;
                case '{':
                    opens++;
                    break;
                case '}':
                    closes++;
                    break;
            }

            pos++;
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

    private static Visibility DeriveVisibility(string name)
    {
        // Go: exported = uppercase first letter, unexported = lowercase
        if (string.IsNullOrEmpty(name))
        {
            return Visibility.Private;
        }

        return char.IsUpper(name[0]) ? Visibility.Public : Visibility.Private;
    }

    private static string? BuildDocComment(List<string> docCommentLines)
    {
        if (docCommentLines.Count == 0)
        {
            return null;
        }

        return string.Join("\n", docCommentLines);
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

    private static int GetCurrentBraceDepth(List<PendingType> parentStack)
    {
        if (parentStack.Count == 0)
        {
            return 0;
        }

        return parentStack[^1].CurrentBraceDepth;
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

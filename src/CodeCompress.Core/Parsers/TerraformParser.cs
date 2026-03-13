using System.Text;
using System.Text.RegularExpressions;
using CodeCompress.Core.Models;

namespace CodeCompress.Core.Parsers;

public sealed partial class TerraformParser : ILanguageParser
{
    public string LanguageId => "terraform";

    public IReadOnlyList<string> FileExtensions { get; } = [".tf", ".tfvars"];

    // Matches two-label HCL blocks (resource and data)
    [GeneratedRegex(@"^(resource|data)\s+""([^""]+)""\s+""([^""]+)""")]
    private static partial Regex TwoLabelBlockPattern();

    // Matches single-label HCL blocks (variable, output, module, provider)
    [GeneratedRegex(@"^(variable|output|module|provider)\s+""([^""]+)""")]
    private static partial Regex SingleLabelBlockPattern();

    // Matches no-label HCL blocks (locals, terraform)
    [GeneratedRegex(@"^(locals|terraform)\s*(\{|$)")]
    private static partial Regex NoLabelBlockPattern();

    // Heredoc start: <<-?MARKER or <<MARKER
    [GeneratedRegex(@"<<-?(\w+)")]
    private static partial Regex HeredocStartPattern();

    // Local assignment: key = value (at start of line, allowing leading whitespace)
    [GeneratedRegex(@"^\s+(\w+)\s*=\s*(.*)$")]
    private static partial Regex LocalAssignmentPattern();

    // Module source: source = "path"
    [GeneratedRegex(@"^\s+source\s*=\s*""([^""]+)""")]
    private static partial Regex ModuleSourcePattern();

    // Variable description: description = "text"
    [GeneratedRegex(@"^\s+description\s*=\s*""([^""]*(?:\\.[^""]*)*)""")]
    private static partial Regex DescriptionPattern();

    // Variable type: type = ...
    [GeneratedRegex(@"^\s+type\s*=\s*(.+)$")]
    private static partial Regex TypePattern();

    // Variable default: default = ...
    [GeneratedRegex(@"^\s+default\s*=\s*(.+)$")]
    private static partial Regex DefaultPattern();

    // .tfvars key = value assignment
    [GeneratedRegex(@"^(\w+)\s*=\s*(.+)$")]
    private static partial Regex TfVarsAssignmentPattern();

    // Line comment patterns
    [GeneratedRegex(@"^\s*#(.*)$")]
    private static partial Regex HashCommentPattern();

    [GeneratedRegex(@"^\s*//(.*)$")]
    private static partial Regex SlashCommentPattern();

    // Block comment start/end
    [GeneratedRegex(@"^\s*/\*")]
    private static partial Regex BlockCommentStartPattern();

    [GeneratedRegex(@"\*/\s*$")]
    private static partial Regex BlockCommentEndPattern();

    public ParseResult Parse(string filePath, ReadOnlySpan<byte> content)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        if (content.IsEmpty)
        {
            return new ParseResult([], []);
        }

        var isTfVars = filePath.EndsWith(".tfvars", StringComparison.OrdinalIgnoreCase);

        var text = Encoding.UTF8.GetString(content);
        var lines = text.Split('\n');
        var lineByteOffsets = ComputeLineByteOffsets(content);

        if (isTfVars)
        {
            return ParseTfVars(lines, lineByteOffsets);
        }

        return ParseTf(lines, lineByteOffsets);
    }

    private static ParseResult ParseTfVars(string[] lines, int[] lineByteOffsets)
    {
        var symbols = new List<SymbolInfo>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.Trim();

            // Skip empty lines and comments
            if (string.IsNullOrWhiteSpace(trimmed)
                || trimmed.StartsWith('#')
                || trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            var match = TfVarsAssignmentPattern().Match(trimmed);
            if (match.Success)
            {
                var key = match.Groups[1].Value;
                var lineNumber = i + 1;
                var byteOffset = lineByteOffsets[i];
                var byteLength = Encoding.UTF8.GetByteCount(line.TrimEnd('\r'));

                symbols.Add(new SymbolInfo(
                    Name: key,
                    Kind: SymbolKind.ConfigKey,
                    Signature: trimmed,
                    ParentSymbol: null,
                    ByteOffset: byteOffset,
                    ByteLength: byteLength,
                    LineStart: lineNumber,
                    LineEnd: lineNumber,
                    Visibility: Visibility.Public,
                    DocComment: null));
            }
        }

        return new ParseResult(symbols, []);
    }

    private static ParseResult ParseTf(string[] lines, int[] lineByteOffsets)
    {
        var symbols = new List<SymbolInfo>();
        var dependencies = new List<DependencyInfo>();

        var inBlockComment = false;
        var inHeredoc = false;
        var heredocMarker = string.Empty;
        var braceDepth = 0;

        // State for current top-level block
        var inBlock = false;
        var blockType = string.Empty;       // "resource", "data", "variable", etc.
        var blockName = string.Empty;       // formatted name
        var blockKind = SymbolKind.Class;
        var blockVisibility = Visibility.Public;
        var blockSignature = string.Empty;
        var blockStartLine = 0;
        var blockByteOffset = 0;
        var blockDocComment = (string?)null;

        // State for enrichment within blocks
        var blockDescription = (string?)null;
        var blockTypeAnnotation = (string?)null;
        var blockDefault = (string?)null;
        var blockSource = (string?)null;

        // State for locals block
        var inLocalsBlock = false;
        var localKeyName = (string?)null;
        var localStartLine = 0;
        var localByteOffset = 0;
        var localSubDepth = 0;

        // State for accumulated doc comments
        var pendingDocLines = new List<string>();
        var lastCommentLine = -1;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.Trim();
            var lineNumber = i + 1;

            // Handle block comment state
            if (inBlockComment)
            {
                if (trimmed.Contains("*/", StringComparison.Ordinal))
                {
                    // Check if the block comment end is on this line
                    var endIdx = line.IndexOf("*/", StringComparison.Ordinal);
                    if (endIdx >= 0)
                    {
                        // Extract comment text up to */
                        var commentText = line[..endIdx].Trim();
                        if (!string.IsNullOrWhiteSpace(commentText))
                        {
                            pendingDocLines.Add(commentText);
                        }

                        lastCommentLine = i;
                        inBlockComment = false;

                        // Check if there's meaningful content after */
                        var afterComment = line[(endIdx + 2)..].Trim();
                        if (!string.IsNullOrWhiteSpace(afterComment))
                        {
                            // Re-process the remainder - but for simplicity, skip
                        }
                    }
                }
                else
                {
                    // Accumulate block comment lines
                    var commentLine = trimmed;
                    if (commentLine.StartsWith('*'))
                    {
                        commentLine = commentLine[1..].TrimStart();
                    }

                    if (!string.IsNullOrWhiteSpace(commentLine))
                    {
                        pendingDocLines.Add(commentLine);
                    }

                    lastCommentLine = i;
                }

                continue;
            }

            // Handle heredoc state
            if (inHeredoc)
            {
                if (trimmed == heredocMarker)
                {
                    inHeredoc = false;
                }

                continue;
            }

            // Check for block comment start
            if (trimmed.StartsWith("/*", StringComparison.Ordinal))
            {
                // Check if it also ends on the same line
                var afterStart = trimmed[2..];
                var endIdx = afterStart.IndexOf("*/", StringComparison.Ordinal);
                if (endIdx >= 0)
                {
                    // Single-line block comment
                    var commentText = afterStart[..endIdx].Trim();
                    if (!string.IsNullOrWhiteSpace(commentText))
                    {
                        pendingDocLines.Add(commentText);
                    }

                    lastCommentLine = i;
                }
                else
                {
                    inBlockComment = true;
                    var commentText = afterStart.Trim();
                    if (!string.IsNullOrWhiteSpace(commentText))
                    {
                        pendingDocLines.Add(commentText);
                    }

                    lastCommentLine = i;
                }

                continue;
            }

            // Check for line comments (# or //)
            if (braceDepth == 0)
            {
                var hashMatch = HashCommentPattern().Match(line);
                if (hashMatch.Success)
                {
                    // Only accumulate if consecutive with previous comment
                    if (pendingDocLines.Count == 0 || lastCommentLine == i - 1)
                    {
                        var commentText = hashMatch.Groups[1].Value.Trim();
                        pendingDocLines.Add(commentText);
                        lastCommentLine = i;
                    }
                    else
                    {
                        pendingDocLines.Clear();
                        var commentText = hashMatch.Groups[1].Value.Trim();
                        pendingDocLines.Add(commentText);
                        lastCommentLine = i;
                    }

                    continue;
                }

                var slashMatch = SlashCommentPattern().Match(line);
                if (slashMatch.Success)
                {
                    if (pendingDocLines.Count == 0 || lastCommentLine == i - 1)
                    {
                        var commentText = slashMatch.Groups[1].Value.Trim();
                        pendingDocLines.Add(commentText);
                        lastCommentLine = i;
                    }
                    else
                    {
                        pendingDocLines.Clear();
                        var commentText = slashMatch.Groups[1].Value.Trim();
                        pendingDocLines.Add(commentText);
                        lastCommentLine = i;
                    }

                    continue;
                }
            }

            // Skip blank lines (but don't clear pending doc comments if they are consecutive)
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            // At depth 0, try to match a top-level block
            if (braceDepth == 0 && !inBlock)
            {
                // Grab doc comment if the comment was on the line(s) immediately before this line
                string? docComment = null;
                if (pendingDocLines.Count > 0 && lastCommentLine == i - 1)
                {
                    docComment = string.Join("\n", pendingDocLines);
                }

                pendingDocLines.Clear();

                // Try two-label block
                var twoLabel = TwoLabelBlockPattern().Match(trimmed);
                if (twoLabel.Success)
                {
                    var type = twoLabel.Groups[1].Value;
                    var label1 = twoLabel.Groups[2].Value;
                    var label2 = twoLabel.Groups[3].Value;

                    blockType = type;
                    if (string.Equals(type, "data", StringComparison.Ordinal))
                    {
                        blockName = $"data.{label1}.{label2}";
                    }
                    else
                    {
                        blockName = $"{label1}.{label2}";
                    }

                    blockKind = SymbolKind.Class;
                    blockVisibility = Visibility.Public;
                    blockSignature = $"{type} \"{label1}\" \"{label2}\"";
                    blockStartLine = lineNumber;
                    blockByteOffset = lineByteOffsets[i];
                    blockDocComment = docComment;
                    blockDescription = null;
                    blockTypeAnnotation = null;
                    blockDefault = null;
                    blockSource = null;
                    inBlock = true;
                    inLocalsBlock = false;

                    // Count braces on this line
                    var delta = CountBracesOnLine(line, ref inHeredoc, ref heredocMarker);
                    braceDepth += delta;

                    if (braceDepth == 0)
                    {
                        // Single-line block (unlikely but handle it)
                        FinalizeBlock(
                            symbols, blockName, blockKind, blockSignature,
                            blockByteOffset, blockStartLine, lineNumber,
                            lineByteOffsets, lines, blockVisibility, blockDocComment,
                            blockType, blockDescription, blockTypeAnnotation,
                            blockDefault, blockSource);
                        inBlock = false;
                    }

                    continue;
                }

                // Try single-label block
                var singleLabel = SingleLabelBlockPattern().Match(trimmed);
                if (singleLabel.Success)
                {
                    var type = singleLabel.Groups[1].Value;
                    var label = singleLabel.Groups[2].Value;

                    blockType = type;
                    blockKind = GetBlockKind(type);
                    blockVisibility = Visibility.Public;
                    blockStartLine = lineNumber;
                    blockByteOffset = lineByteOffsets[i];
                    blockDocComment = docComment;
                    blockDescription = null;
                    blockTypeAnnotation = null;
                    blockDefault = null;
                    blockSource = null;
                    inLocalsBlock = false;

                    if (string.Equals(type, "variable", StringComparison.Ordinal))
                    {
                        blockName = $"var.{label}";
                        blockSignature = $"variable \"{label}\"";
                    }
                    else if (string.Equals(type, "output", StringComparison.Ordinal))
                    {
                        blockName = $"output.{label}";
                        blockSignature = $"output \"{label}\"";
                    }
                    else if (string.Equals(type, "module", StringComparison.Ordinal))
                    {
                        blockName = $"module.{label}";
                        blockSignature = $"module \"{label}\"";
                    }
                    else if (string.Equals(type, "provider", StringComparison.Ordinal))
                    {
                        blockName = $"provider.{label}";
                        blockSignature = $"provider \"{label}\"";
                    }
                    else
                    {
                        blockName = label;
                        blockSignature = $"{type} \"{label}\"";
                    }

                    inBlock = true;

                    var delta = CountBracesOnLine(line, ref inHeredoc, ref heredocMarker);
                    braceDepth += delta;

                    if (braceDepth == 0)
                    {
                        FinalizeBlock(
                            symbols, blockName, blockKind, blockSignature,
                            blockByteOffset, blockStartLine, lineNumber,
                            lineByteOffsets, lines, blockVisibility, blockDocComment,
                            blockType, blockDescription, blockTypeAnnotation,
                            blockDefault, blockSource);
                        inBlock = false;
                    }

                    continue;
                }

                // Try no-label block
                var noLabel = NoLabelBlockPattern().Match(trimmed);
                if (noLabel.Success)
                {
                    var type = noLabel.Groups[1].Value;

                    blockType = type;
                    blockStartLine = lineNumber;
                    blockByteOffset = lineByteOffsets[i];
                    blockDocComment = docComment;
                    blockDescription = null;
                    blockTypeAnnotation = null;
                    blockDefault = null;
                    blockSource = null;

                    if (string.Equals(type, "locals", StringComparison.Ordinal))
                    {
                        inLocalsBlock = true;
                        inBlock = true;
                        blockName = "locals";
                        blockKind = SymbolKind.Constant;
                        blockVisibility = Visibility.Private;
                        blockSignature = "locals";
                        localKeyName = null;
                        localSubDepth = 0;
                    }
                    else if (string.Equals(type, "terraform", StringComparison.Ordinal))
                    {
                        inLocalsBlock = false;
                        inBlock = true;
                        blockName = "terraform";
                        blockKind = SymbolKind.Module;
                        blockVisibility = Visibility.Public;
                        blockSignature = "terraform";
                    }
                    else
                    {
                        inLocalsBlock = false;
                        inBlock = true;
                        blockName = type;
                        blockKind = SymbolKind.Module;
                        blockVisibility = Visibility.Public;
                        blockSignature = type;
                    }

                    var delta = CountBracesOnLine(line, ref inHeredoc, ref heredocMarker);
                    braceDepth += delta;

                    if (braceDepth == 0 && !inLocalsBlock)
                    {
                        FinalizeBlock(
                            symbols, blockName, blockKind, blockSignature,
                            blockByteOffset, blockStartLine, lineNumber,
                            lineByteOffsets, lines, blockVisibility, blockDocComment,
                            blockType, blockDescription, blockTypeAnnotation,
                            blockDefault, blockSource);
                        inBlock = false;
                    }
                    else if (braceDepth == 0 && inLocalsBlock)
                    {
                        // Empty locals block
                        inBlock = false;
                        inLocalsBlock = false;
                    }

                    continue;
                }

                // Not a recognized block, clear pending doc comments
                pendingDocLines.Clear();
                continue;
            }

            // Inside a block — process content lines
            if (inBlock && braceDepth > 0)
            {
                // Handle locals block — detect key = value at depth 1
                if (inLocalsBlock)
                {
                    ProcessLocalsLine(
                        line, trimmed, lineNumber, i, lineByteOffsets, lines,
                        ref localKeyName, ref localStartLine, ref localByteOffset,
                        ref localSubDepth, ref braceDepth, ref inHeredoc, ref heredocMarker,
                        symbols);

                    if (braceDepth == 0)
                    {
                        // Finalize any pending local
                        if (localKeyName is not null)
                        {
                            FinalizeLocalSymbol(
                                symbols, localKeyName, localByteOffset, localStartLine,
                                lineNumber, lineByteOffsets, lines);
                            localKeyName = null;
                        }

                        inBlock = false;
                        inLocalsBlock = false;
                    }

                    continue;
                }

                // Enrichment: look for description, type, default, source within the block at depth 1
                if (braceDepth == 1)
                {
                    var descMatch = DescriptionPattern().Match(line);
                    if (descMatch.Success)
                    {
                        blockDescription = UnescapeString(descMatch.Groups[1].Value);
                    }

                    if (string.Equals(blockType, "variable", StringComparison.Ordinal))
                    {
                        var typeMatch = TypePattern().Match(line);
                        if (typeMatch.Success)
                        {
                            blockTypeAnnotation = typeMatch.Groups[1].Value.Trim();
                        }

                        var defaultMatch = DefaultPattern().Match(line);
                        if (defaultMatch.Success)
                        {
                            blockDefault = defaultMatch.Groups[1].Value.Trim();
                        }
                    }

                    if (string.Equals(blockType, "module", StringComparison.Ordinal))
                    {
                        var sourceMatch = ModuleSourcePattern().Match(line);
                        if (sourceMatch.Success)
                        {
                            blockSource = sourceMatch.Groups[1].Value;
                        }
                    }
                }

                // Count braces
                var delta = CountBracesOnLine(line, ref inHeredoc, ref heredocMarker);
                braceDepth += delta;

                if (braceDepth == 0)
                {
                    // Block closed — finalize
                    // For module blocks, also extract dependency
                    if (string.Equals(blockType, "module", StringComparison.Ordinal) && blockSource is not null)
                    {
                        var moduleName = blockName;
                        if (moduleName.StartsWith("module.", StringComparison.Ordinal))
                        {
                            moduleName = moduleName["module.".Length..];
                        }

                        dependencies.Add(new DependencyInfo(blockSource, moduleName));
                    }

                    FinalizeBlock(
                        symbols, blockName, blockKind, blockSignature,
                        blockByteOffset, blockStartLine, lineNumber,
                        lineByteOffsets, lines, blockVisibility,
                        blockDocComment, blockType, blockDescription,
                        blockTypeAnnotation, blockDefault, blockSource);
                    inBlock = false;
                }
            }
        }

        return new ParseResult(symbols, dependencies);
    }

    private static void ProcessLocalsLine(
        string line,
        string trimmed,
        int lineNumber,
        int lineIndex,
        int[] lineByteOffsets,
        string[] lines,
        ref string? localKeyName,
        ref int localStartLine,
        ref int localByteOffset,
        ref int localSubDepth,
        ref int braceDepth,
        ref bool inHeredoc,
        ref string heredocMarker,
        List<SymbolInfo> symbols)
    {
        // If we're tracking a local with sub-depth (map value), count braces
        if (localKeyName is not null && localSubDepth > 0)
        {
            // Count open and close braces separately to track sub-depth
            var lineOpenBraces = 0;
            var lineCloseBraces = 0;
            CountBracesRaw(line, ref inHeredoc, ref heredocMarker, ref lineOpenBraces, ref lineCloseBraces);

            // Apply close braces one by one
            for (var b = 0; b < lineCloseBraces; b++)
            {
                if (localSubDepth > 0)
                {
                    localSubDepth--;
                    if (localSubDepth == 0)
                    {
                        // Local value is complete
                        FinalizeLocalSymbol(
                            symbols, localKeyName, localByteOffset, localStartLine,
                            lineNumber, lineByteOffsets, lines);
                        localKeyName = null;

                        // Check if this close brace also closes the locals block
                        // The remaining close braces apply to braceDepth
                        var remainingCloses = lineCloseBraces - b - 1;
                        braceDepth += lineOpenBraces - remainingCloses;
                        return;
                    }
                }
                else
                {
                    // This close brace applies to the outer locals block
                    braceDepth--;
                    if (braceDepth == 0)
                    {
                        return;
                    }
                }
            }

            // Apply open braces to sub-depth
            localSubDepth += lineOpenBraces;
            return;
        }

        // Check if this line is a comment inside locals
        if (trimmed.StartsWith('#') || trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            // Still need to check for closing brace on comment lines? No — comments don't have braces
            return;
        }

        // Check for closing brace of the locals block
        if (localKeyName is null)
        {
            // Try to detect a new local assignment
            var assignMatch = LocalAssignmentPattern().Match(line);
            if (assignMatch.Success)
            {
                var key = assignMatch.Groups[1].Value;
                var value = assignMatch.Groups[2].Value.Trim();

                localKeyName = key;
                localStartLine = lineNumber;
                localByteOffset = lineByteOffsets[lineIndex];

                // Check if the value contains an opening brace (map/object value)
                var tempHeredoc = inHeredoc;
                var tempMarker = heredocMarker;
                var openCount = 0;
                var closeCount = 0;
                CountBracesRaw(value, ref tempHeredoc, ref tempMarker, ref openCount, ref closeCount);

                if (openCount > closeCount)
                {
                    // Map value — track sub-depth
                    localSubDepth = openCount - closeCount;
                    inHeredoc = tempHeredoc;
                    heredocMarker = tempMarker;
                    return;
                }

                // Simple value — finalize immediately
                FinalizeLocalSymbol(
                    symbols, localKeyName, localByteOffset, localStartLine,
                    lineNumber, lineByteOffsets, lines);
                localKeyName = null;
                localSubDepth = 0;
                inHeredoc = tempHeredoc;
                heredocMarker = tempMarker;
                return;
            }
        }

        // Count braces for the locals block itself
        var braceDelta = CountBracesOnLine(line, ref inHeredoc, ref heredocMarker);
        braceDepth += braceDelta;
    }

    private static void FinalizeLocalSymbol(
        List<SymbolInfo> symbols,
        string keyName,
        int byteOffset,
        int lineStart,
        int lineEnd,
        int[] lineByteOffsets,
        string[] lines)
    {
        var endLine = lines[lineEnd - 1].TrimEnd('\r');
        var byteLength = lineByteOffsets[lineEnd - 1] + Encoding.UTF8.GetByteCount(endLine) - byteOffset;

        symbols.Add(new SymbolInfo(
            Name: $"local.{keyName}",
            Kind: SymbolKind.Constant,
            Signature: $"local.{keyName}",
            ParentSymbol: null,
            ByteOffset: byteOffset,
            ByteLength: byteLength,
            LineStart: lineStart,
            LineEnd: lineEnd,
            Visibility: Visibility.Private,
            DocComment: null));
    }

    private static void FinalizeBlock(
        List<SymbolInfo> symbols,
        string name,
        SymbolKind kind,
        string signature,
        int byteOffset,
        int lineStart,
        int lineEnd,
        int[] lineByteOffsets,
        string[] lines,
        Visibility visibility,
        string? docComment,
        string blockType,
        string? description,
        string? typeAnnotation,
        string? defaultValue,
        string? source)
    {
        // Build final signature with enrichments
        var finalSignature = signature;

        if (string.Equals(blockType, "variable", StringComparison.Ordinal))
        {
            var enrichments = new List<string>();
            if (typeAnnotation is not null)
            {
                enrichments.Add($"type: {typeAnnotation}");
            }

            if (defaultValue is not null)
            {
                enrichments.Add($"default: {defaultValue}");
            }

            if (enrichments.Count > 0)
            {
                finalSignature = $"{signature} ({string.Join(", ", enrichments)})";
            }
        }
        else if (string.Equals(blockType, "module", StringComparison.Ordinal) && source is not null)
        {
            finalSignature = $"{signature} (source: \"{source}\")";
        }

        // Use description as doc comment if available, overriding preceding comments
        var finalDocComment = description ?? docComment;

        var endLine = lines[lineEnd - 1].TrimEnd('\r');
        var byteLength = lineByteOffsets[lineEnd - 1] + Encoding.UTF8.GetByteCount(endLine) - byteOffset;

        symbols.Add(new SymbolInfo(
            Name: name,
            Kind: kind,
            Signature: finalSignature,
            ParentSymbol: null,
            ByteOffset: byteOffset,
            ByteLength: byteLength,
            LineStart: lineStart,
            LineEnd: lineEnd,
            Visibility: visibility,
            DocComment: finalDocComment));
    }

    private static SymbolKind GetBlockKind(string blockType)
    {
        return blockType switch
        {
            "variable" => SymbolKind.Constant,
            "output" => SymbolKind.Export,
            "module" => SymbolKind.Module,
            "provider" => SymbolKind.Module,
            _ => SymbolKind.Class
        };
    }

    private static int CountBracesOnLine(string line, ref bool inHeredoc, ref string heredocMarker)
    {
        var opens = 0;
        var closes = 0;
        CountBracesRaw(line, ref inHeredoc, ref heredocMarker, ref opens, ref closes);
        return opens - closes;
    }

    private static void CountBracesRaw(
        string line,
        ref bool inHeredoc,
        ref string heredocMarker,
        ref int opens,
        ref int closes)
    {
        var inString = false;
        var i = 0;

        while (i < line.Length)
        {
            var ch = line[i];

            if (inString)
            {
                if (ch == '\\')
                {
                    i += 2;
                    continue;
                }

                if (ch == '"')
                {
                    inString = false;
                }

                i++;
                continue;
            }

            // Check for line comments
            if (ch == '#')
            {
                break;
            }

            if (ch == '/' && i + 1 < line.Length && line[i + 1] == '/')
            {
                break;
            }

            // Check for block comment start
            if (ch == '/' && i + 1 < line.Length && line[i + 1] == '*')
            {
                // Skip until end of block comment on this line
                var endIdx = line.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (endIdx >= 0)
                {
                    i = endIdx + 2;
                    continue;
                }

                // Block comment extends beyond this line — skip rest
                break;
            }

            if (ch == '"')
            {
                inString = true;
                i++;
                continue;
            }

            // Check for heredoc
            if (ch == '<' && i + 1 < line.Length && line[i + 1] == '<')
            {
                var remainder = line[i..];
                var heredocMatch = HeredocStartPattern().Match(remainder);
                if (heredocMatch.Success)
                {
                    heredocMarker = heredocMatch.Groups[1].Value;
                    inHeredoc = true;
                    // No braces to count after heredoc start on this line
                    break;
                }
            }

            if (ch == '{')
            {
                opens++;
            }
            else if (ch == '}')
            {
                closes++;
            }

            i++;
        }
    }

    private static string UnescapeString(string value)
    {
        return value
            .Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\t", "\t", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);
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
}

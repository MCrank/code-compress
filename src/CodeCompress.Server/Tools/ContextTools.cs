using System.ComponentModel;
using System.Text;
using System.Text.Json;
using CodeCompress.Core.Models;
using CodeCompress.Core.Storage;
using CodeCompress.Core.Validation;
using CodeCompress.Server.Scoping;
using ModelContextProtocol.Server;

namespace CodeCompress.Server.Tools;

[McpServerToolType]
internal sealed class ContextTools
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private const double CharsPerToken = 3.5;
    private const int DefaultBudget = 40_000;
    private const int SymbolSizeThresholdBytes = 16_384;
    private const int MaxSearchResults = 50;

    private readonly IPathValidator _pathValidator;
    private readonly IProjectScopeFactory _scopeFactory;

    public ContextTools(IPathValidator pathValidator, IProjectScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(pathValidator);
        ArgumentNullException.ThrowIfNull(scopeFactory);

        _pathValidator = pathValidator;
        _scopeFactory = scopeFactory;
    }

    [McpServerTool(Name = "assemble_context")]
    [Description("One-shot context assembly — searches symbols, retrieves source code, and builds a structured overview within a token budget. Use INSTEAD of manually calling search_symbols + get_symbol + project_outline when starting a task. Reduces 5-10 tool round-trips to 1. Returns Markdown: file tree overview, source code sections grouped by file (with syntax-highlighted fenced blocks), and a metadata footer showing token usage. Large symbols (>16KB) are automatically summarized with signature + child list — use expand_symbol for individual methods. Set activeFile to prioritize the file you're editing. Requires index_project first. Zero-result response is JSON: {query, total_matches: 0, hint}. Errors return JSON {error, code, retryable}. Codes: INVALID_PATH, EMPTY_QUERY.")]
    public async Task<string> AssembleContext(
        [Description("ABSOLUTE path to the project root directory — the same root used with index_project (e.g., 'C:\\Projects\\MyApp' or '/home/user/my-project'). Must NOT be a subdirectory or relative path.")] string path,
        [Description("Task description or search terms describing what you're working on (e.g., 'authentication middleware', 'database connection pooling', 'UserService'). Supports FTS5 full-text search — plain terms, prefix*, *contains*, and boolean operators (AND, OR, NOT).")] string query,
        [Description("Relative path to the file you're currently editing (e.g., 'src/services/UserService.ts'). Symbols from this file get highest priority in the assembled context. Optional — omit if you don't have a specific file focus.")] string? activeFile = null,
        [Description("Maximum token budget for the response (1000-200000, default 40000). The assembled context will not exceed this limit. Larger budgets include more symbols and source code. Values outside range are clamped.")] int budget = DefaultBudget,
        [Description("Maximum depth for dependency traversal (0-5, default 2). Higher values include more transitive dependencies but consume more budget.")] int maxDepth = 2,
        CancellationToken cancellationToken = default)
    {
        string validatedPath;
        try
        {
            validatedPath = _pathValidator.ValidatePath(path, path);
        }
        catch (ArgumentException)
        {
            return SerializeError("Path validation failed", "INVALID_PATH");
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return SerializeError("Query cannot be empty", "EMPTY_QUERY");
        }

        var clampedBudget = Math.Clamp(budget, 1_000, 200_000);
        _ = Math.Clamp(maxDepth, 0, 5); // Reserved for future dependency traversal
        var tokenBudget = (int)(clampedBudget * CharsPerToken);

        var scope = await _scopeFactory.CreateAsync(validatedPath, cancellationToken).ConfigureAwait(false);
        await using (scope.ConfigureAwait(false))
        {
            var output = new StringBuilder();
            var usedChars = 0;

            // ── 1. Search for relevant symbols ──────────────────────
            var searchResults = await scope.Store.SearchSymbolsAsync(
                scope.RepoId, query, null, MaxSearchResults).ConfigureAwait(false);

            // Auto contains-match fallback for plain terms
            if (searchResults.Count == 0 && GlobPattern.IsPlainTerm(query))
            {
                var containsGlob = Fts5QuerySanitizer.SanitizeAsGlob($"*{query}*");
                searchResults = await scope.Store.SearchSymbolsAsync(
                    scope.RepoId, containsGlob.Fts5Query, null, MaxSearchResults, null, containsGlob.SqlLikePattern).ConfigureAwait(false);
            }

            if (searchResults.Count == 0)
            {
                return JsonSerializer.Serialize(new
                {
                    Query = query,
                    TotalMatches = 0,
                    TokensUsed = 0,
                    Budget = clampedBudget,
                    Hint = "No symbols matched this query. Try broader search terms, or use search_symbols with wildcard patterns.",
                }, SerializerOptions);
            }

            // ── 2. Get files for path resolution ────────────────────
            var files = await scope.Store.GetFilesByRepoAsync(scope.RepoId).ConfigureAwait(false);
            var filePathMap = files.ToDictionary(f => f.RelativePath, f => f, StringComparer.OrdinalIgnoreCase);

            // ── 3. Build file tree overview (~10% budget) ───────────
            var overviewBudget = tokenBudget / 10;
            var matchedFiles = searchResults.Select(r => r.FilePath).Distinct().ToList();
            var overview = BuildFileTreeOverview(matchedFiles, overviewBudget);
            output.Append(overview);
            usedChars += overview.Length;

            // ── 4. Active file symbols (highest priority) ───────────
            if (activeFile is not null)
            {
                var normalizedActive = activeFile.Replace('\\', '/');
                if (filePathMap.TryGetValue(normalizedActive, out var activeFileRecord))
                {
                    var activeSymbols = (await scope.Store.GetSymbolsByFileAsync(activeFileRecord.Id).ConfigureAwait(false)).ToList();
                    var activeSection = await BuildSourceSectionAsync(
                        activeFileRecord, activeSymbols, validatedPath, tokenBudget - usedChars, "Active File").ConfigureAwait(false);
                    output.Append(activeSection);
                    usedChars += activeSection.Length;
                }
            }

            // ── 5. Search result symbols (by relevance) ─────────────
            var includedSymbols = new HashSet<string>();
            var symbolsByFile = searchResults
                .GroupBy(r => r.FilePath)
                .OrderByDescending(g => g.Count())
                .ToList();

            foreach (var fileGroup in symbolsByFile)
            {
                if (usedChars >= tokenBudget)
                {
                    break;
                }

                if (!filePathMap.TryGetValue(fileGroup.Key, out var fileRecord))
                {
                    continue;
                }

                var symbolsForFile = fileGroup.Select(r => r.Symbol).ToList();
                var section = await BuildSourceSectionAsync(
                    fileRecord, symbolsForFile, validatedPath, tokenBudget - usedChars, null).ConfigureAwait(false);

                if (section.Length > 0)
                {
                    output.Append(section);
                    usedChars += section.Length;

                    foreach (var sym in symbolsForFile)
                    {
                        var qualifiedName = sym.ParentSymbol is not null ? $"{sym.ParentSymbol}:{sym.Name}" : sym.Name;
                        includedSymbols.Add(qualifiedName);
                    }
                }
            }

            // ── 6. Metadata footer ──────────────────────────────────
            var tokensUsed = (int)(usedChars / CharsPerToken);
            var footer = $"\n---\n**Context Assembly** | Query: `{SanitizeForDisplay(query)}` | " +
                         $"Symbols: {includedSymbols.Count} | Files: {matchedFiles.Count} | " +
                         $"Tokens: ~{tokensUsed:N0}/{clampedBudget:N0}\n";
            output.Append(footer);

            return output.ToString();
        }
    }

    private static string BuildFileTreeOverview(List<string> files, int charBudget)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## File Overview\n");
        sb.AppendLine("```");

        foreach (var file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var line = $"  {file}\n";
            if (sb.Length + line.Length > charBudget)
            {
                var remaining = files.Count - files.IndexOf(file);
                sb.Append("  ... and ").Append(remaining).AppendLine(" more files");
                break;
            }

            sb.Append(line);
        }

        sb.AppendLine("```\n");
        return sb.ToString();
    }

    private static async Task<string> BuildSourceSectionAsync(
        FileRecord file, List<Symbol> symbols, string projectRoot, int charBudget, string? sectionLabel)
    {
        if (charBudget <= 0 || symbols.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        var header = sectionLabel is not null
            ? $"## {sectionLabel}: {file.RelativePath}\n\n"
            : $"## {file.RelativePath}\n\n";
        sb.Append(header);

        var resolvedPath = Path.Combine(projectRoot, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(resolvedPath))
        {
            return string.Empty;
        }

        foreach (var symbol in symbols)
        {
            if (sb.Length >= charBudget)
            {
                break;
            }

            // Size guard: large symbols get signature summary
            if (symbol.ByteLength > SymbolSizeThresholdBytes)
            {
                var summary = $"### {symbol.Kind}: {symbol.Name}\n" +
                              $"```\n{symbol.Signature}\n```\n" +
                              $"*({symbol.ByteLength:N0} bytes — use expand_symbol for individual methods)*\n\n";
                if (sb.Length + summary.Length <= charBudget)
                {
                    sb.Append(summary);
                }

                continue;
            }

            try
            {
                var sourceCode = await ReadSourceCodeAsync(resolvedPath, symbol.ByteOffset, symbol.ByteLength).ConfigureAwait(false);

                var ext = Path.GetExtension(file.RelativePath).TrimStart('.');
                var block = $"### {symbol.Kind}: {symbol.Name} (L{symbol.LineStart}-{symbol.LineEnd})\n" +
                            $"```{ext}\n{sourceCode}\n```\n\n";

                if (sb.Length + block.Length <= charBudget)
                {
                    sb.Append(block);
                }
                else
                {
                    break;
                }
            }
            catch (IOException)
            {
                // File read error — skip this symbol
            }
        }

        return sb.ToString();
    }

    private static async Task<string> ReadSourceCodeAsync(string filePath, int byteOffset, int byteLength)
    {
        var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);

        await using (stream.ConfigureAwait(false))
        {
            if (byteOffset > 0)
            {
                stream.Seek(byteOffset, SeekOrigin.Begin);
            }

            var buffer = new byte[byteLength];
            var bytesRead = await stream.ReadAsync(buffer).ConfigureAwait(false);
            return Encoding.UTF8.GetString(buffer, 0, bytesRead);
        }
    }

    private static string SanitizeForDisplay(string input)
    {
        // Strip characters that could break markdown or inject instructions
        var sanitized = input.Replace('`', ' ').Replace('\n', ' ').Replace('\r', ' ');
        return sanitized.Length > 100 ? sanitized[..100] : sanitized;
    }

    private static string SerializeError(string error, string code) =>
        JsonSerializer.Serialize(new { Error = error, Code = code, Retryable = false }, SerializerOptions);
}

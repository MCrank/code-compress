using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CodeCompress.Core.Models;
using CodeCompress.Core.Storage;
using CodeCompress.Core.Validation;
using CodeCompress.Server.Scoping;
using ModelContextProtocol.Server;

namespace CodeCompress.Server.Tools;

[McpServerToolType]
internal sealed class QueryTools
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private static readonly HashSet<string> ValidGroupByValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "file", "kind", "directory",
    };

    private static readonly HashSet<string> ValidSymbolKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "function", "method", "type", "class", "interface", "export", "constant", "module", "record", "enum",
    };

    private const int SymbolSizeThresholdBytes = 16_384;
    private const string SymbolNotFoundGuidance = "Use search_symbols to find the correct qualified name. If the symbol was recently added or changed, re-run index_project to update the index.";

    private readonly IPathValidator _pathValidator;
    private readonly IProjectScopeFactory _scopeFactory;

    public QueryTools(IPathValidator pathValidator, IProjectScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(pathValidator);
        ArgumentNullException.ThrowIfNull(scopeFactory);

        _pathValidator = pathValidator;
        _scopeFactory = scopeFactory;
    }

    [McpServerTool(Name = "project_outline")]
    [Description("Get a compressed overview of the entire indexed codebase — all symbol signatures grouped by file, kind, or directory in a single response. Far more efficient than reading files individually (saves 90%+ tokens). Use pathFilter to scope to a subdirectory — much faster than retrieving the full outline and filtering client-side. Supports pagination via offset/maxSymbols for large codebases. Requires index_project to have been called first. Returns Markdown: heading hierarchy with symbol signatures (visibility, kind, signature per line). When truncated, includes a footer with the next offset/maxSymbols values to continue pagination. Errors return JSON {error, code, retryable}. Codes: INVALID_PATH, INVALID_GROUP_BY (use 'file', 'kind', or 'directory'), INVALID_PATH_FILTER.")]
    public async Task<string> ProjectOutline(
        [Description("ABSOLUTE path to the project root directory — the same root used with index_project (e.g., 'C:\\Projects\\MyGame' or '/home/user/my-project'). Must NOT be a subdirectory or relative path.")] string path,
        [Description("Include private/local symbols")] bool includePrivate = false,
        [Description("Grouping strategy. Allowed values: 'file' (default — group by source file), 'kind' (group by symbol type), 'directory' (group by folder hierarchy). Other values are rejected.")] string groupBy = "file",
        [Description("Limit directory traversal depth (null for unlimited)")] int? maxDepth = null,
        [Description("Filter outline to files under this relative directory path. Scopes results to only files within the specified directory. Examples: 'src/' (exclude tests), 'src/Core/Models' (specific module). Optional.")] string? pathFilter = null,
        [Description("Maximum number of symbols to return (1-5000, default 500). Values outside this range are clamped. Use with offset to paginate large codebases.")] int maxSymbols = 500,
        [Description("Number of symbols to skip for pagination (default 0). Use with maxSymbols to retrieve subsequent pages.")] int offset = 0,
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

        if (!ValidGroupByValues.Contains(groupBy))
        {
            return SerializeError("Invalid group_by value. Must be one of: file, kind, directory", "INVALID_GROUP_BY");
        }

        string? validatedPathFilter = null;
        if (pathFilter is not null)
        {
            try
            {
                validatedPathFilter = PathValidator.ValidatePathFilter(pathFilter);
            }
            catch (ArgumentException)
            {
                return SerializeError("Invalid path filter", "INVALID_PATH_FILTER");
            }
        }

        var clampedMaxSymbols = Math.Clamp(maxSymbols, 1, 5000);
        var clampedOffset = Math.Max(offset, 0);

        var scope = await _scopeFactory.CreateAsync(validatedPath, cancellationToken).ConfigureAwait(false);
        await using (scope.ConfigureAwait(false))
        {
            var outline = await scope.Store.GetProjectOutlineAsync(
                scope.RepoId,
                includePrivate,
                groupBy,
                maxDepth ?? 0,
                validatedPathFilter,
                clampedOffset,
                clampedMaxSymbols).ConfigureAwait(false);

            return FormatOutline(outline, clampedOffset, clampedMaxSymbols);
        }
    }

    [McpServerTool(Name = "get_module_api")]
    [Description("Get the full public API surface of a single module file — all exported symbols, signatures, and import dependencies in one call. Use instead of reading the file to see only the public interface without implementation details. Requires index_project to have been called first. Returns JSON: {module, symbols: [{name, kind, parent, signature, line, doc_comment}], dependencies: [{requires_path, alias}]}. Errors return JSON {error, code, retryable}. Codes: INVALID_PATH, MODULE_NOT_FOUND (file not in index — verify modulePath and run index_project).")]
    public async Task<string> GetModuleApi(
        [Description("ABSOLUTE path to the project root directory — the same root used with index_project (e.g., 'C:\\Projects\\MyGame' or '/home/user/my-project'). Must NOT be a subdirectory or relative path.")] string path,
        [Description("Relative path from the project root to the module file (e.g., 'src/services/CombatService.luau'). Forward slashes only, NOT an absolute path.")] string modulePath,
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

        var normalizedModulePath = PathValidator.NormalizeRelativePath(modulePath);

        try
        {
            _pathValidator.ValidateRelativePath(normalizedModulePath, validatedPath);
        }
        catch (ArgumentException)
        {
            return SerializeError("Path validation failed", "INVALID_PATH");
        }

        try
        {
            var scope = await _scopeFactory.CreateAsync(validatedPath, cancellationToken).ConfigureAwait(false);
            await using (scope.ConfigureAwait(false))
            {
                var moduleApi = await scope.Store.GetModuleApiAsync(scope.RepoId, normalizedModulePath).ConfigureAwait(false);

                var response = new
                {
                    Module = moduleApi.File.RelativePath,
                    Symbols = moduleApi.Symbols.Select(s => new
                    {
                        s.Name,
                        s.Kind,
                        Parent = s.ParentSymbol,
                        s.Signature,
                        Line = s.LineStart,
                        s.DocComment,
                    }),
                    Dependencies = moduleApi.Dependencies.Select(d => new
                    {
                        d.RequiresPath,
                        d.Alias,
                    }),
                    Hint = "Use get_symbol or expand_symbol with a symbol's name to retrieve its full source code.",
                };

                return JsonSerializer.Serialize(response, SerializerOptions);
            }
        }
        catch (FileNotFoundException)
        {
            return SerializeError("Module not found", "MODULE_NOT_FOUND");
        }
    }

    [McpServerTool(Name = "get_symbol")]
    [Description("Retrieve the full source code of a specific symbol by qualified name — loads only the exact symbol, not the entire file (saves 80%+ tokens vs file reading). For multiple symbols, prefer get_symbols (single round-trip). For a single method in a large class, prefer expand_symbol (saves ~60% more tokens). For large symbols (>16KB), returns a guided summary with child method signatures and instructions to use expand_symbol for individual methods. Use force=true to bypass the size guard. Requires index_project to have been called first. Returns JSON: {name, kind, parent, file, line_start, line_end, signature, source_code}. For large symbols (>16KB): {name, kind, parent, file, line_start, line_end, signature, truncated: true, source_size_bytes, children: [{name, signature, expand_with}], guidance}. Errors return JSON {error, code, retryable}. Codes: INVALID_PATH, SYMBOL_NOT_FOUND (includes 'symbol' field — use search_symbols to find the correct name), FILE_NOT_FOUND.")]
    public async Task<string> GetSymbol(
        [Description("ABSOLUTE path to the project root directory — the same root used with index_project (e.g., 'C:\\Projects\\MyGame' or '/home/user/my-project'). Must NOT be a subdirectory or relative path.")] string path,
        [Description("Symbol name — accepts 'Parent:Child' qualified names (e.g., 'CombatService:ProcessAttack') or unqualified names (e.g., 'ProcessAttack'). Unqualified names are resolved automatically; if ambiguous, returns a candidates list.")] string symbolName,
        [Description("Include 5 lines of context before and after the symbol")] bool includeContext = false,
        [Description("Bypass size guard and return full source code even for large symbols")] bool force = false,
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

        var scope = await _scopeFactory.CreateAsync(validatedPath, cancellationToken).ConfigureAwait(false);
        await using (scope.ConfigureAwait(false))
        {
            var symbol = await scope.Store.GetSymbolByNameAsync(scope.RepoId, symbolName).ConfigureAwait(false);
            if (symbol is null)
            {
                // Fuzzy resolution: try matching by unqualified name
                var candidates = await scope.Store.GetSymbolCandidatesByNameAsync(scope.RepoId, symbolName).ConfigureAwait(false);
                if (candidates.Count == 1)
                {
                    symbol = candidates[0];
                }
                else if (candidates.Count > 1)
                {
                    return JsonSerializer.Serialize(
                        new
                        {
                            Error = "Multiple symbols match this name",
                            Code = "SYMBOL_NOT_FOUND",
                            Retryable = false,
                            Symbol = SanitizeSymbolName(symbolName),
                            Candidates = candidates.Select(c => c.ParentSymbol is not null ? $"{c.ParentSymbol}:{c.Name}" : c.Name),
                        },
                        SerializerOptions);
                }
                else
                {
                    return JsonSerializer.Serialize(
                        new { Error = "Symbol not found", Code = "SYMBOL_NOT_FOUND", Retryable = false, Symbol = SanitizeSymbolName(symbolName), Guidance = SymbolNotFoundGuidance },
                        SerializerOptions);
                }
            }

            var files = await scope.Store.GetFilesByRepoAsync(scope.RepoId).ConfigureAwait(false);
            var file = files.FirstOrDefault(f => f.Id == symbol.FileId);
            if (file is null)
            {
                return SerializeError("File not found for symbol", "FILE_NOT_FOUND");
            }

            string resolvedPath;
            try
            {
                resolvedPath = _pathValidator.ValidatePath(
                    Path.Combine(validatedPath, file.RelativePath), validatedPath);
            }
            catch (ArgumentException)
            {
                return SerializeError("Path validation failed", "INVALID_PATH");
            }

            var sourceCode = includeContext
                ? await ReadSourceCodeWithContextAsync(resolvedPath, symbol.ByteOffset, symbol.ByteLength).ConfigureAwait(false)
                : await ReadSourceCodeAsync(resolvedPath, symbol.ByteOffset, symbol.ByteLength).ConfigureAwait(false);

            if (!force && Encoding.UTF8.GetByteCount(sourceCode) > SymbolSizeThresholdBytes)
            {
                var children = await scope.Store.GetChildSymbolsAsync(scope.RepoId, symbol.Name).ConfigureAwait(false);
                if (children.Count > 0)
                {
                    return FormatGuidedSummary(symbol, file.RelativePath, children);
                }
            }

            var response = new
            {
                symbol.Name,
                symbol.Kind,
                Parent = symbol.ParentSymbol,
                File = file.RelativePath,
                symbol.LineStart,
                symbol.LineEnd,
                symbol.Signature,
                SourceCode = sourceCode,
            };

            return JsonSerializer.Serialize(response, SerializerOptions);
        }
    }

    [McpServerTool(Name = "expand_symbol")]
    [Description("Retrieve only the body of a nested symbol (e.g., a single method) without loading the entire parent class — saves ~60% tokens vs get_symbol on the parent. Use 'Parent:Child' qualified names to extract exactly the method you need. Ideal for reading individual methods in large classes. Requires index_project to have been called first. Returns JSON: {name, kind, parent, file, line_start, line_end, signature, doc_comment, source_code}. Errors return JSON {error, code, retryable}. Codes: INVALID_PATH, SYMBOL_NOT_FOUND (includes 'symbol' field — use search_symbols to find the correct name), FILE_NOT_FOUND.")]
    public async Task<string> ExpandSymbol(
        [Description("ABSOLUTE path to the project root directory — the same root used with index_project (e.g., 'C:\\Projects\\MyGame' or '/home/user/my-project'). Must NOT be a subdirectory or relative path.")] string path,
        [Description("Symbol name — accepts 'Parent:Child' qualified names (e.g., 'PlayerService:GetHealth') or unqualified names (e.g., 'GetHealth'). Unqualified names are resolved automatically; if ambiguous, returns a candidates list.")] string symbolName,
        [Description("Include 3 lines of context before and after the symbol")] bool includeContext = false,
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

        var scope = await _scopeFactory.CreateAsync(validatedPath, cancellationToken).ConfigureAwait(false);
        await using (scope.ConfigureAwait(false))
        {
            var symbol = await scope.Store.GetSymbolByNameAsync(scope.RepoId, symbolName).ConfigureAwait(false);
            if (symbol is null)
            {
                // Fuzzy resolution: try matching by unqualified name
                var candidates = await scope.Store.GetSymbolCandidatesByNameAsync(scope.RepoId, symbolName).ConfigureAwait(false);
                if (candidates.Count == 1)
                {
                    symbol = candidates[0];
                }
                else if (candidates.Count > 1)
                {
                    return JsonSerializer.Serialize(
                        new
                        {
                            Error = "Multiple symbols match this name",
                            Code = "SYMBOL_NOT_FOUND",
                            Retryable = false,
                            Symbol = SanitizeSymbolName(symbolName),
                            Candidates = candidates.Select(c => c.ParentSymbol is not null ? $"{c.ParentSymbol}:{c.Name}" : c.Name),
                        },
                        SerializerOptions);
                }
                else
                {
                    return JsonSerializer.Serialize(
                        new { Error = "Symbol not found", Code = "SYMBOL_NOT_FOUND", Retryable = false, Symbol = SanitizeSymbolName(symbolName), Guidance = SymbolNotFoundGuidance },
                        SerializerOptions);
                }
            }

            var files = await scope.Store.GetFilesByRepoAsync(scope.RepoId).ConfigureAwait(false);
            var file = files.FirstOrDefault(f => f.Id == symbol.FileId);
            if (file is null)
            {
                return SerializeError("File not found for symbol", "FILE_NOT_FOUND");
            }

            string resolvedPath;
            try
            {
                resolvedPath = _pathValidator.ValidatePath(
                    Path.Combine(validatedPath, file.RelativePath), validatedPath);
            }
            catch (ArgumentException)
            {
                return SerializeError("Path validation failed", "INVALID_PATH");
            }

            var sourceCode = includeContext
                ? await ReadSourceCodeWithContextAsync(resolvedPath, symbol.ByteOffset, symbol.ByteLength, contextLines: 3).ConfigureAwait(false)
                : await ReadSourceCodeAsync(resolvedPath, symbol.ByteOffset, symbol.ByteLength).ConfigureAwait(false);

            var response = new
            {
                symbol.Name,
                symbol.Kind,
                Parent = symbol.ParentSymbol,
                File = file.RelativePath,
                symbol.LineStart,
                symbol.LineEnd,
                symbol.Signature,
                symbol.DocComment,
                SourceCode = sourceCode,
            };

            return JsonSerializer.Serialize(response, SerializerOptions);
        }
    }

    [McpServerTool(Name = "get_symbols")]
    [Description("Batch retrieve source code for multiple symbols in one call — significantly more efficient than calling get_symbol repeatedly (single round-trip vs N). Maximum 50 names per call. Requires index_project to have been called first. Returns JSON: {results: [{name, kind, parent, file, line_start, line_end, signature, source_code}], errors: [{symbol, error, code}]}. Large symbols return truncated format with children array (same as get_symbol). Errors return JSON {error, code, retryable}. Codes: INVALID_PATH, EMPTY_SYMBOL_NAMES, SYMBOL_LIMIT_EXCEEDED (max 50). Per-symbol errors in 'errors' array: SYMBOL_NOT_FOUND (includes 'symbol' field).")]
    public async Task<string> GetSymbols(
        [Description("ABSOLUTE path to the project root directory — the same root used with index_project (e.g., 'C:\\Projects\\MyGame' or '/home/user/my-project'). Must NOT be a subdirectory or relative path.")] string path,
        [Description("Array of fully qualified symbol names (same format as get_symbol). Maximum 50 per call.")] string[] symbolNames,
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

        if (symbolNames is null || symbolNames.Length == 0)
        {
            return SerializeError("No symbol names provided", "EMPTY_SYMBOL_NAMES");
        }

        if (symbolNames.Length > 50)
        {
            return SerializeError("Too many symbols requested. Maximum is 50", "SYMBOL_LIMIT_EXCEEDED");
        }

        var scope = await _scopeFactory.CreateAsync(validatedPath, cancellationToken).ConfigureAwait(false);
        await using (scope.ConfigureAwait(false))
        {
            var foundSymbols = await scope.Store.GetSymbolsByNamesAsync(scope.RepoId, symbolNames).ConfigureAwait(false);
            var files = await scope.Store.GetFilesByRepoAsync(scope.RepoId).ConfigureAwait(false);
            var fileMap = files.ToDictionary(f => f.Id);

            // Build set of qualified names that were found
            var foundQualifiedNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var symbol in foundSymbols)
            {
                var qualifiedName = symbol.ParentSymbol is not null
                    ? $"{symbol.ParentSymbol}:{symbol.Name}"
                    : symbol.Name;
                foundQualifiedNames.Add(qualifiedName);
            }

            // Group found symbols by file for efficient reading
            var symbolsByFile = foundSymbols
                .GroupBy(s => s.FileId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var results = new List<object>();

            foreach (var (fileId, fileSymbols) in symbolsByFile)
            {
                if (!fileMap.TryGetValue(fileId, out var file))
                {
                    continue;
                }

                string resolvedPath;
                try
                {
                    resolvedPath = _pathValidator.ValidatePath(
                        Path.Combine(validatedPath, file.RelativePath), validatedPath);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                foreach (var symbol in fileSymbols)
                {
                    var sourceCode = await ReadSourceCodeAsync(resolvedPath, symbol.ByteOffset, symbol.ByteLength).ConfigureAwait(false);

                    if (Encoding.UTF8.GetByteCount(sourceCode) > SymbolSizeThresholdBytes)
                    {
                        var children = await scope.Store.GetChildSymbolsAsync(scope.RepoId, symbol.Name).ConfigureAwait(false);
                        if (children.Count > 0)
                        {
                            var parentName = symbol.ParentSymbol is not null ? $"{symbol.ParentSymbol}:{symbol.Name}" : symbol.Name;
                            results.Add(new
                            {
                                symbol.Name,
                                symbol.Kind,
                                Parent = symbol.ParentSymbol,
                                File = file.RelativePath,
                                symbol.LineStart,
                                symbol.LineEnd,
                                symbol.Signature,
                                Truncated = true,
                                SourceSizeBytes = symbol.ByteLength,
                                Children = children.Select(c => new
                                {
                                    c.Name,
                                    c.Signature,
                                    ExpandWith = $"{parentName}:{c.Name}",
                                }),
                                Guidance = $"This symbol is large ({symbol.ByteLength:N0} bytes). Use expand_symbol to retrieve individual methods.",
                            });
                            continue;
                        }
                    }

                    results.Add(new
                    {
                        symbol.Name,
                        symbol.Kind,
                        Parent = symbol.ParentSymbol,
                        File = file.RelativePath,
                        symbol.LineStart,
                        symbol.LineEnd,
                        symbol.Signature,
                        SourceCode = sourceCode,
                    });
                }
            }

            // Determine which requested names were not found
            var errors = symbolNames
                .Where(name => !foundQualifiedNames.Contains(name))
                .Select(name => new
                {
                    Symbol = SanitizeSymbolName(name),
                    Error = "Symbol not found",
                    Code = "SYMBOL_NOT_FOUND",
                    Retryable = false,
                    Guidance = SymbolNotFoundGuidance,
                })
                .ToList();

            var response = new
            {
                Results = results,
                Errors = errors,
            };

            return JsonSerializer.Serialize(response, SerializerOptions);
        }
    }

    [McpServerTool(Name = "search_symbols")]
    [Description("Search the symbol index using FTS5 full-text search — faster and more precise than grep-style file scanning. Supports prefix*, *suffix, *contains*, and I*Pattern glob matching. Auto-retries with contains-match (*query*) when a plain term returns zero FTS5 results (e.g., searching 'Validator' automatically finds 'PathValidator', 'IPathValidator'). When this fallback triggers, the response includes fallback_used: true. Returns symbol names, kinds, signatures, and locations. Use pathFilter to scope results to a specific directory. Use get_symbol or expand_symbol to retrieve full source code of matched symbols. Requires index_project to have been called first. Returns JSON: {query, total_matches, [fallback_used], results: [{name, kind, parent, file, line, signature, snippet, rank}]}. Chain with get_symbol using the 'name' field (or 'parent:name' for nested symbols). Errors return JSON {error, code, retryable}. Codes: INVALID_PATH, EMPTY_QUERY, QUERY_TOO_BROAD (add a non-wildcard term or pathFilter), INVALID_KIND (see kind param for valid values), INVALID_PATH_FILTER, MIXED_PATTERN (includes 'suggestion' — split into separate queries).")]
    public async Task<string> SearchSymbols(
        [Description("ABSOLUTE path to the project root directory — the same root used with index_project (e.g., 'C:\\Projects\\MyGame' or '/home/user/my-project'). Must NOT be a subdirectory or relative path.")] string path,
        [Description("Search query — supports plain text, FTS5 operators (AND, OR, NOT), and glob patterns (prefix*, *suffix, *contains*)")] string query,
        [Description("Filter by symbol kind (function, method, class, record, enum, type, interface, export, constant, module)")] string? kind = null,
        [Description("Filter results to files under this relative directory path. Scopes results to only files within the specified directory. Examples: 'src/' (exclude tests), 'src/Core/Models' (specific module), 'lib/' (library code only).")] string? pathFilter = null,
        [Description("Maximum results to return (1-100, default 20). Values outside this range are clamped.")] int limit = 20,
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
            return SerializeError("Search query cannot be empty", "EMPTY_QUERY");
        }

        if (GlobPattern.IsWildcardOnly(query) && pathFilter is null)
        {
            return SerializeError("Search query is too broad — provide at least one non-wildcard term", "QUERY_TOO_BROAD");
        }

        if (kind is not null)
        {
            if (!ValidSymbolKinds.Contains(kind))
            {
                return SerializeError("Invalid symbol kind. Must be one of: function, method, type, class, record, interface, export, constant, module", "INVALID_KIND");
            }

            // Normalize to PascalCase to match DB storage (SymbolKind.ToString())
            if (Enum.TryParse<SymbolKind>(kind, ignoreCase: true, out var parsedKind))
            {
                kind = parsedKind.ToString();
            }
        }

        string? validatedPathFilter = null;
        if (pathFilter is not null)
        {
            try
            {
                validatedPathFilter = PathValidator.ValidatePathFilter(pathFilter);
            }
            catch (ArgumentException)
            {
                return SerializeError("Invalid path filter", "INVALID_PATH_FILTER");
            }
        }

        var clampedLimit = Math.Clamp(limit, 1, 100);
        var glob = Fts5QuerySanitizer.SanitizeAsGlob(query);

        if (glob.Strategy == GlobMatchStrategy.MixedStrategy)
        {
            return JsonSerializer.Serialize(
                new
                {
                    Error = glob.ErrorDetail,
                    Code = "MIXED_PATTERN",
                    Retryable = false,
                    Suggestion = "Split into separate queries: run one query for prefix patterns (e.g., 'Claude*') and another for suffix/contains patterns (e.g., '*Service').",
                },
                SerializerOptions);
        }

        // Wildcard-only query with pathFilter: browse all symbols under that path
        if (string.IsNullOrWhiteSpace(glob.Fts5Query) && string.IsNullOrWhiteSpace(glob.SqlLikePattern) && validatedPathFilter is not null)
        {
            glob = GlobPattern.CreateSqlLike(string.Empty, "%");
        }

        if (string.IsNullOrWhiteSpace(glob.Fts5Query) && string.IsNullOrWhiteSpace(glob.SqlLikePattern))
        {
            return SerializeError("Search query cannot be empty", "EMPTY_QUERY");
        }

        var scope = await _scopeFactory.CreateAsync(validatedPath, cancellationToken).ConfigureAwait(false);
        await using (scope.ConfigureAwait(false))
        {
            IReadOnlyList<Core.Models.SymbolSearchResult> results;
            try
            {
                results = await scope.Store.SearchSymbolsAsync(
                    scope.RepoId, glob.Fts5Query, kind, clampedLimit, validatedPathFilter, glob.SqlLikePattern).ConfigureAwait(false);
            }
            catch (System.Data.Common.DbException)
            {
                // FTS5 syntax error — retry with literal phrase
                var literalQuery = $"\"{query.Replace("\"", string.Empty, StringComparison.Ordinal)}\"";
                results = await scope.Store.SearchSymbolsAsync(
                    scope.RepoId, literalQuery, kind, clampedLimit, validatedPathFilter).ConfigureAwait(false);
            }

            // Auto contains-match fallback: if FTS5 returned 0 results and query is a plain term,
            // retry with *query* pattern to find substring matches (e.g., "Validator" → "*Validator*")
            var fallbackUsed = false;
            if (results.Count == 0 && GlobPattern.IsPlainTerm(query))
            {
                var containsGlob = Fts5QuerySanitizer.SanitizeAsGlob($"*{query}*");
                results = await scope.Store.SearchSymbolsAsync(
                    scope.RepoId, containsGlob.Fts5Query, kind, clampedLimit, validatedPathFilter, containsGlob.SqlLikePattern).ConfigureAwait(false);
                fallbackUsed = results.Count > 0;
            }

            var displayQuery = !string.IsNullOrEmpty(glob.Fts5Query) ? glob.Fts5Query : glob.SqlLikePattern ?? query;
            var hint = results.Count > 0
                ? "Use get_symbol with a result's name (or parent:name for nested symbols) to retrieve full source code."
                : (string?)null;

            return SerializeSearchResults(displayQuery, results, hint, fallbackUsed);
        }
    }

    [McpServerTool(Name = "topic_outline")]
    [Description("Search for symbols related to a topic and return results in a structured outline format grouped by file. Combines search_symbols with project_outline presentation — ideal for exploring a concept across the codebase (e.g., 'show me all authentication-related types'). Requires index_project to have been called first. Returns Markdown: heading hierarchy with matching symbol signatures grouped by file. When truncated, includes a footer with remaining match count. Errors return JSON {error, code, retryable}. Codes: INVALID_PATH, EMPTY_QUERY, INVALID_PATH_FILTER.")]
    public async Task<string> TopicOutline(
        [Description("ABSOLUTE path to the project root directory — the same root used with index_project (e.g., 'C:\\Projects\\MyGame' or '/home/user/my-project'). Must NOT be a subdirectory or relative path.")] string path,
        [Description("Topic or keyword to search for (e.g., 'authentication', 'kubernetes', 'database'). Searches symbol names, signatures, and doc comments via FTS5.")] string topic,
        [Description("Filter results to files under this relative directory path (e.g., 'src/Core/Models'). Optional.")] string? pathFilter = null,
        [Description("Maximum number of symbols to return (1-200, default 50). Values outside this range are clamped.")] int maxResults = 50,
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

        if (string.IsNullOrWhiteSpace(topic))
        {
            return SerializeError("Topic query cannot be empty", "EMPTY_QUERY");
        }

        string? validatedPathFilter = null;
        if (pathFilter is not null)
        {
            try
            {
                validatedPathFilter = PathValidator.ValidatePathFilter(pathFilter);
            }
            catch (ArgumentException)
            {
                return SerializeError("Invalid path filter", "INVALID_PATH_FILTER");
            }
        }

        var clampedLimit = Math.Clamp(maxResults, 1, 200);
        var sanitizedQuery = Fts5QuerySanitizer.Sanitize(topic);

        if (string.IsNullOrWhiteSpace(sanitizedQuery))
        {
            return SerializeError("Topic query cannot be empty", "EMPTY_QUERY");
        }

        var scope = await _scopeFactory.CreateAsync(validatedPath, cancellationToken).ConfigureAwait(false);
        await using (scope.ConfigureAwait(false))
        {
            Core.Models.ProjectOutline outline;
            try
            {
                outline = await scope.Store.SearchTopicOutlineAsync(
                    scope.RepoId, sanitizedQuery, clampedLimit, validatedPathFilter).ConfigureAwait(false);
            }
            catch (System.Data.Common.DbException)
            {
                // FTS5 syntax error — retry with literal phrase
                var literalQuery = $"\"{topic.Replace("\"", string.Empty, StringComparison.Ordinal)}\"";
                outline = await scope.Store.SearchTopicOutlineAsync(
                    scope.RepoId, literalQuery, clampedLimit, validatedPathFilter).ConfigureAwait(false);
            }

            return FormatTopicOutline(outline, sanitizedQuery, clampedLimit);
        }
    }

    [McpServerTool(Name = "search_text")]
    [Description("Search raw indexed file contents using FTS5 full-text search — faster than grep for large codebases since content is pre-indexed. Use for string literals, comments, TODOs, or non-symbol patterns that search_symbols wouldn't find. For symbol-specific searches (classes, functions, types), prefer search_symbols which is faster and returns structured metadata with direct chaining to get_symbol. Use pathFilter to scope results. Requires index_project to have been called first. Returns JSON: {query, total_matches, results: [{file_path, snippet, rank}]}. Errors return JSON {error, code, retryable}. Codes: INVALID_PATH, EMPTY_QUERY, INVALID_PATH_FILTER.")]
    public async Task<string> SearchText(
        [Description("ABSOLUTE path to the project root directory — the same root used with index_project (e.g., 'C:\\Projects\\MyGame' or '/home/user/my-project'). Must NOT be a subdirectory or relative path.")] string path,
        [Description("FTS5 search query (supports AND, OR, NOT, quoted phrases, prefix*)")] string query,
        [Description("File pattern filter (e.g., *.luau, src/services/*.lua)")] string? glob = null,
        [Description("Filter results to files under this relative directory path. Scopes results to only files within the specified directory. Examples: 'src/' (exclude tests), 'src/Config' (configuration files only).")] string? pathFilter = null,
        [Description("Maximum results to return (1-100, default 20). Values outside this range are clamped.")] int limit = 20,
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
            return SerializeError("Search query cannot be empty", "EMPTY_QUERY");
        }

        string? validatedPathFilter = null;
        if (pathFilter is not null)
        {
            try
            {
                validatedPathFilter = PathValidator.ValidatePathFilter(pathFilter);
            }
            catch (ArgumentException)
            {
                return SerializeError("Invalid path filter", "INVALID_PATH_FILTER");
            }
        }

        var clampedLimit = Math.Clamp(limit, 1, 100);
        var sanitizedQuery = Fts5QuerySanitizer.Sanitize(query);
        var sanitizedGlob = glob is not null ? Fts5QuerySanitizer.SanitizeGlob(glob) : null;

        if (string.IsNullOrWhiteSpace(sanitizedQuery))
        {
            return SerializeError("Search query cannot be empty", "EMPTY_QUERY");
        }

        // Use null if glob sanitization produced empty string
        if (string.IsNullOrWhiteSpace(sanitizedGlob))
        {
            sanitizedGlob = null;
        }

        var scope = await _scopeFactory.CreateAsync(validatedPath, cancellationToken).ConfigureAwait(false);
        await using (scope.ConfigureAwait(false))
        {
            IReadOnlyList<Core.Models.TextSearchResult> results;
            try
            {
                results = await scope.Store.SearchTextAsync(
                    scope.RepoId, sanitizedQuery, sanitizedGlob, clampedLimit, validatedPathFilter).ConfigureAwait(false);
            }
            catch (System.Data.Common.DbException)
            {
                // FTS5 syntax error — retry with literal phrase
                var literalQuery = $"\"{query.Replace("\"", string.Empty, StringComparison.Ordinal)}\"";
                results = await scope.Store.SearchTextAsync(
                    scope.RepoId, literalQuery, sanitizedGlob, clampedLimit, validatedPathFilter).ConfigureAwait(false);
            }

            var hint = results.Count > 0
                ? "Use search_symbols for structured symbol results, or get_symbol to retrieve source code for a specific symbol."
                : (string?)null;
            var response = new
            {
                Query = sanitizedQuery,
                TotalMatches = results.Count,
                Results = results.Select((r, index) => new
                {
                    r.FilePath,
                    r.Snippet,
                    Rank = index + 1,
                }),
                Hint = hint,
            };

            return JsonSerializer.Serialize(response, SerializerOptions);
        }
    }

    private static string FormatTopicOutline(Core.Models.ProjectOutline outline, string query, int maxResults)
    {
        var sb = new StringBuilder();
        var pageSymbols = CountSymbols(outline.Groups);

        if (outline.IsTruncated)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"# Topic Outline: \"{query}\" (showing {pageSymbols} of {outline.TotalSymbolCount} matches)");
        }
        else
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"# Topic Outline: \"{query}\" ({pageSymbols} matches)");
        }

        foreach (var group in outline.Groups)
        {
            RenderGroup(sb, group, depth: 0);
        }

        if (outline.IsTruncated)
        {
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine(CultureInfo.InvariantCulture, $"**Truncated:** {outline.TotalSymbolCount - pageSymbols} more matches. Refine your topic query or use `pathFilter` to narrow results. Max: `maxResults: {maxResults}`.");
        }

        return sb.ToString();
    }

    private static string FormatOutline(Core.Models.ProjectOutline outline, int offset, int maxSymbols)
    {
        var sb = new StringBuilder();
        var pageSymbols = CountSymbols(outline.Groups);

        if (outline.IsTruncated)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"# Project Outline (showing {pageSymbols} of {outline.TotalSymbolCount} symbols, offset {offset})");
        }
        else
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"# Project Outline ({pageSymbols} symbols)");
        }

        foreach (var group in outline.Groups)
        {
            RenderGroup(sb, group, depth: 0);
        }

        if (outline.IsTruncated)
        {
            var nextOffset = offset + pageSymbols;
            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"---");
            sb.AppendLine(CultureInfo.InvariantCulture, $"**Truncated:** {outline.TotalSymbolCount - nextOffset} symbols remaining. Call again with `offset: {nextOffset}, maxSymbols: {maxSymbols}` to continue.");
        }

        return sb.ToString();
    }

    private static void RenderGroup(StringBuilder sb, Core.Models.OutlineGroup group, int depth)
    {
        var prefix = depth switch
        {
            0 => "## ",
            1 => "### ",
            _ => new string('#', depth + 2) + " ",
        };

        sb.Append(prefix).AppendLine(group.Name);

        foreach (var symbol in group.Symbols)
        {
            var indent = new string(' ', (depth + 1) * 2);
#pragma warning disable CA1308 // Normalize strings to uppercase — lowercase is the intended display format
            sb.Append(indent)
                .Append(symbol.Visibility.ToLowerInvariant())
                .Append(' ')
                .Append(symbol.Kind.ToLowerInvariant())
                .Append(' ')
                .AppendLine(symbol.Signature);
#pragma warning restore CA1308
        }

        foreach (var child in group.Children)
        {
            RenderGroup(sb, child, depth + 1);
        }
    }

    private static int CountSymbols(IReadOnlyList<Core.Models.OutlineGroup> groups)
    {
        var count = 0;
        foreach (var group in groups)
        {
            count += group.Symbols.Count;
            count += CountSymbols(group.Children);
        }

        return count;
    }

    private static async Task<string> ReadSourceCodeAsync(string filePath, int byteOffset, int byteLength)
    {
        var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using (stream.ConfigureAwait(false))
        {
            stream.Seek(byteOffset, SeekOrigin.Begin);
            var buffer = new byte[byteLength];
            var bytesRead = 0;
            while (bytesRead < byteLength)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(bytesRead, byteLength - bytesRead)).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                bytesRead += read;
            }

            return Encoding.UTF8.GetString(buffer, 0, bytesRead);
        }
    }

    private static async Task<string> ReadSourceCodeWithContextAsync(string filePath, int byteOffset, int byteLength, int contextLines = 5)
    {
        var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using (stream.ConfigureAwait(false))
        {
            // Find start position by scanning backward for context lines
            var startOffset = await FindContextStartAsync(stream, byteOffset, contextLines).ConfigureAwait(false);

            // Find end position by scanning forward for context lines
            var endOffset = await FindContextEndAsync(stream, byteOffset + byteLength, contextLines).ConfigureAwait(false);

            // Read the full region
            var length = endOffset - startOffset;
            stream.Seek(startOffset, SeekOrigin.Begin);
            var buffer = new byte[length];
            var bytesRead = 0;
            while (bytesRead < length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(bytesRead, length - bytesRead)).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                bytesRead += read;
            }

            return Encoding.UTF8.GetString(buffer, 0, bytesRead);
        }
    }

    private static async Task<int> FindContextStartAsync(FileStream stream, int symbolStart, int lines)
    {
        if (symbolStart == 0)
        {
            return 0;
        }

        // Read backward from symbolStart to find `lines` newline characters
        var searchStart = Math.Max(0, symbolStart - 4096);
        var searchLength = symbolStart - searchStart;
        stream.Seek(searchStart, SeekOrigin.Begin);
        var buffer = new byte[searchLength];
        _ = await stream.ReadAsync(buffer.AsMemory(0, searchLength)).ConfigureAwait(false);

        var newlineCount = 0;
        for (var i = searchLength - 1; i >= 0; i--)
        {
            if (buffer[i] == (byte)'\n')
            {
                newlineCount++;
                if (newlineCount == lines)
                {
                    return searchStart + i + 1;
                }
            }
        }

        return searchStart;
    }

    private static async Task<int> FindContextEndAsync(FileStream stream, int symbolEnd, int lines)
    {
        stream.Seek(symbolEnd, SeekOrigin.Begin);
        var buffer = new byte[4096];
        var bytesRead = await stream.ReadAsync(buffer).ConfigureAwait(false);

        var newlineCount = 0;
        for (var i = 0; i < bytesRead; i++)
        {
            if (buffer[i] == (byte)'\n')
            {
                newlineCount++;
                if (newlineCount == lines)
                {
                    return symbolEnd + i + 1;
                }
            }
        }

        return symbolEnd + bytesRead;
    }

    private static string SanitizeSymbolName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        // Allow only alphanumeric, colon, underscore, dot, hyphen
        var sb = new StringBuilder(name.Length);
        foreach (var c in name.Where(c => char.IsLetterOrDigit(c) || c is ':' or '_' or '.' or '-'))
        {
            sb.Append(c);
        }

        var result = sb.ToString();
        return result.Length > 256 ? result[..256] : result;
    }

    private static string FormatGuidedSummary(Symbol symbol, string filePath, IReadOnlyList<Symbol> children)
    {
        var parentName = symbol.ParentSymbol is not null ? $"{symbol.ParentSymbol}:{symbol.Name}" : symbol.Name;
        var response = new
        {
            symbol.Name,
            symbol.Kind,
            Parent = symbol.ParentSymbol,
            File = filePath,
            symbol.LineStart,
            symbol.LineEnd,
            symbol.Signature,
            Truncated = true,
            SourceSizeBytes = symbol.ByteLength,
            Children = children.Select(c => new
            {
                c.Name,
                c.Signature,
                ExpandWith = $"{parentName}:{c.Name}",
            }),
            Guidance = $"This symbol is large ({symbol.ByteLength:N0} bytes). Use expand_symbol with '{parentName}:<method_name>' to retrieve individual methods. Use force=true to bypass this guard.",
        };

        return JsonSerializer.Serialize(response, SerializerOptions);
    }

    private static string SerializeSearchResults(
        string displayQuery,
        IReadOnlyList<Core.Models.SymbolSearchResult> results,
        string? hint,
        bool fallbackUsed)
    {
        var resultItems = results.Select((r, index) => new
        {
            r.Symbol.Name,
            r.Symbol.Kind,
            Parent = r.Symbol.ParentSymbol,
            File = r.FilePath,
            Line = r.Symbol.LineStart,
            r.Symbol.Signature,
            Snippet = r.Symbol.DocComment ?? string.Empty,
            Rank = index + 1,
        });

        if (fallbackUsed)
        {
            return JsonSerializer.Serialize(
                new { Query = displayQuery, TotalMatches = results.Count, FallbackUsed = true, Results = resultItems, Hint = hint },
                SerializerOptions);
        }

        return JsonSerializer.Serialize(
            new { Query = displayQuery, TotalMatches = results.Count, Results = resultItems, Hint = hint },
            SerializerOptions);
    }

    private static string SerializeError(string error, string code, string? guidance = null) =>
        guidance is null
            ? JsonSerializer.Serialize(new { Error = error, Code = code, Retryable = false }, SerializerOptions)
            : JsonSerializer.Serialize(new { Error = error, Code = code, Retryable = false, Guidance = guidance }, SerializerOptions);
}

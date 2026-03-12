using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CodeCompress.Core.Models;
using CodeCompress.Core.Storage;
using CodeCompress.Core.Validation;
using CodeCompress.Server.Sanitization;
using CodeCompress.Server.Scoping;
using CodeCompress.Server.Services;
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
        "function", "method", "type", "class", "interface", "export", "constant", "module",
    };

    private readonly IPathValidator _pathValidator;
    private readonly IProjectScopeFactory _scopeFactory;
    private readonly IActivityTracker _activityTracker;

    public QueryTools(IPathValidator pathValidator, IProjectScopeFactory scopeFactory, IActivityTracker activityTracker)
    {
        ArgumentNullException.ThrowIfNull(pathValidator);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(activityTracker);

        _pathValidator = pathValidator;
        _scopeFactory = scopeFactory;
        _activityTracker = activityTracker;
    }

    [McpServerTool(Name = "project_outline")]
    [Description("Get a compressed overview of the indexed codebase showing symbol signatures grouped by file, kind, or directory. Requires index_project to have been called first.")]
    public async Task<string> ProjectOutline(
        [Description("ABSOLUTE path to the project root directory — the same root used with index_project (e.g., 'C:\\Projects\\MyGame' or '/home/user/my-project'). Must NOT be a subdirectory or relative path.")] string path,
        [Description("Include private/local symbols")] bool includePrivate = false,
        [Description("Grouping strategy: file, kind, or directory")] string groupBy = "file",
        [Description("Limit directory traversal depth (null for unlimited)")] int? maxDepth = null,
        [Description("Filter outline to files under this relative directory path (e.g., 'src/Core/Models'). Optional.")] string? pathFilter = null,
        CancellationToken cancellationToken = default)
    {
        _activityTracker.RecordActivity();

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

        var scope = await _scopeFactory.CreateAsync(validatedPath, cancellationToken).ConfigureAwait(false);
        await using (scope.ConfigureAwait(false))
        {
            var outline = await scope.Store.GetProjectOutlineAsync(
                scope.RepoId,
                includePrivate,
                groupBy,
                maxDepth ?? 0,
                validatedPathFilter).ConfigureAwait(false);

            return FormatOutline(outline);
        }
    }

    [McpServerTool(Name = "get_module_api")]
    [Description("Get the full public API surface of a single module file — all exported symbols, signatures, and import dependencies.")]
    public async Task<string> GetModuleApi(
        [Description("ABSOLUTE path to the project root directory — the same root used with index_project (e.g., 'C:\\Projects\\MyGame' or '/home/user/my-project'). Must NOT be a subdirectory or relative path.")] string path,
        [Description("Relative path from the project root to the module file (e.g., 'src/services/CombatService.luau'). Forward slashes only, NOT an absolute path.")] string modulePath,
        CancellationToken cancellationToken = default)
    {
        _activityTracker.RecordActivity();

        string validatedPath;
        try
        {
            validatedPath = _pathValidator.ValidatePath(path, path);
        }
        catch (ArgumentException)
        {
            return SerializeError("Path validation failed", "INVALID_PATH");
        }

        try
        {
            _pathValidator.ValidateRelativePath(modulePath, validatedPath);
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
                var moduleApi = await scope.Store.GetModuleApiAsync(scope.RepoId, modulePath).ConfigureAwait(false);

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
    [Description("Retrieve the full source code of a specific symbol by its qualified name using byte-offset seeking.")]
    public async Task<string> GetSymbol(
        [Description("ABSOLUTE path to the project root directory — the same root used with index_project (e.g., 'C:\\Projects\\MyGame' or '/home/user/my-project'). Must NOT be a subdirectory or relative path.")] string path,
        [Description("Fully qualified symbol name — use 'Parent:Child' for nested symbols (e.g., 'CombatService:ProcessAttack') or just the name for top-level symbols. Use search_symbols to discover names.")] string symbolName,
        [Description("Include 5 lines of context before and after the symbol")] bool includeContext = false,
        CancellationToken cancellationToken = default)
    {
        _activityTracker.RecordActivity();

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
                return JsonSerializer.Serialize(
                    new { Error = "Symbol not found", Code = "SYMBOL_NOT_FOUND", Symbol = SanitizeSymbolName(symbolName) },
                    SerializerOptions);
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

    [McpServerTool(Name = "get_symbols")]
    [Description("Batch retrieve source code for multiple symbols in one call. More efficient than calling get_symbol repeatedly. Maximum 50 names per call.")]
    public async Task<string> GetSymbols(
        [Description("ABSOLUTE path to the project root directory — the same root used with index_project (e.g., 'C:\\Projects\\MyGame' or '/home/user/my-project'). Must NOT be a subdirectory or relative path.")] string path,
        [Description("Array of fully qualified symbol names (same format as get_symbol). Maximum 50 per call.")] string[] symbolNames,
        CancellationToken cancellationToken = default)
    {
        _activityTracker.RecordActivity();

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
    [Description("Search the symbol index using FTS5 full-text search. Supports prefix*, *suffix, *contains*, and I*Pattern glob matching. Optionally filter by file path.")]
    public async Task<string> SearchSymbols(
        [Description("ABSOLUTE path to the project root directory — the same root used with index_project (e.g., 'C:\\Projects\\MyGame' or '/home/user/my-project'). Must NOT be a subdirectory or relative path.")] string path,
        [Description("Search query — supports plain text, FTS5 operators (AND, OR, NOT), and glob patterns (prefix*, *suffix, *contains*)")] string query,
        [Description("Filter by symbol kind (function, method, class, type, interface, export, constant, module)")] string? kind = null,
        [Description("Filter results to files under this relative directory path (e.g., 'src/Core/Models')")] string? pathFilter = null,
        [Description("Maximum results to return (1-100)")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        _activityTracker.RecordActivity();

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
                return SerializeError("Invalid symbol kind. Must be one of: function, method, type, class, interface, export, constant, module", "INVALID_KIND");
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

            var displayQuery = !string.IsNullOrEmpty(glob.Fts5Query) ? glob.Fts5Query : glob.SqlLikePattern ?? query;
            var response = new
            {
                Query = displayQuery,
                TotalMatches = results.Count,
                Results = results.Select((r, index) => new
                {
                    r.Symbol.Name,
                    r.Symbol.Kind,
                    Parent = r.Symbol.ParentSymbol,
                    File = r.FilePath,
                    Line = r.Symbol.LineStart,
                    r.Symbol.Signature,
                    Snippet = r.Symbol.DocComment ?? string.Empty,
                    Rank = index + 1,
                }),
            };

            return JsonSerializer.Serialize(response, SerializerOptions);
        }
    }

    [McpServerTool(Name = "search_text")]
    [Description("Search raw indexed file contents using FTS5 full-text search. Use for string literals, comments, or non-symbol patterns. Supports glob and path filtering.")]
    public async Task<string> SearchText(
        [Description("ABSOLUTE path to the project root directory — the same root used with index_project (e.g., 'C:\\Projects\\MyGame' or '/home/user/my-project'). Must NOT be a subdirectory or relative path.")] string path,
        [Description("FTS5 search query (supports AND, OR, NOT, quoted phrases, prefix*)")] string query,
        [Description("File pattern filter (e.g., *.luau, src/services/*.lua)")] string? glob = null,
        [Description("Filter results to files under this relative directory path (e.g., 'src/Config')")] string? pathFilter = null,
        [Description("Maximum results to return (1-100)")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        _activityTracker.RecordActivity();

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
            };

            return JsonSerializer.Serialize(response, SerializerOptions);
        }
    }

    private static string FormatOutline(Core.Models.ProjectOutline outline)
    {
        var sb = new StringBuilder();
        var totalSymbols = CountSymbols(outline.Groups);
        sb.AppendLine(CultureInfo.InvariantCulture, $"# Project Outline ({totalSymbols} symbols)");

        foreach (var group in outline.Groups)
        {
            RenderGroup(sb, group, depth: 0);
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

    private static string SerializeError(string error, string code) =>
        JsonSerializer.Serialize(new { Error = error, Code = code }, SerializerOptions);
}

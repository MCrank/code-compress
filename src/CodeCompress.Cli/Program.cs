using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CodeCompress.Cli;
using CodeCompress.Core;
using CodeCompress.Core.Contracts;
using CodeCompress.Core.Indexing;
using CodeCompress.Core.Models;
using CodeCompress.Core.Registry;
using CodeCompress.Core.Storage;
using CodeCompress.Core.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// ── DI Setup ────────────────────────────────────────────────

var services = new ServiceCollection();
services.AddCodeCompressCore();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));

using var provider = services.BuildServiceProvider();

var jsonSerializerOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    WriteIndented = true,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
};

// ── Global Options ──────────────────────────────────────────

var jsonOption = new Option<bool>("--json") { Description = "Output as JSON instead of human-readable format" };

// ── Shared Options (reused across commands) ─────────────────

static Option<string> CreatePathOption(bool required = true)
{
    var opt = new Option<string>("--path")
    {
        Description = "Absolute path to the project root directory",
        Required = required,
    };
    return opt;
}

// ── Root Command ────────────────────────────────────────────

var rootCommand = new RootCommand(
    "CodeCompress CLI — Compressed, symbol-level code access. Saves 80-90% tokens vs reading raw files.\n\n" +
    "Recommended Workflow:\n" +
    "  1. index        Build/update the symbol database (run first)\n" +
    "  2. outline      Get a compressed codebase overview\n" +
    "  3. search       Find symbols using FTS5 full-text search\n" +
    "  4. get-symbol   Retrieve exact source code by name\n");

jsonOption.Recursive = true;
rootCommand.Options.Add(jsonOption);

// ── index ───────────────────────────────────────────────────

var indexPathOption = CreatePathOption();
var indexLanguageOption = new Option<string?>("--language") { Description = "Filter to a specific language (e.g., luau, csharp)" };

var indexCommand = new Command("index",
    "Index a project to build a searchable symbol database. Must be run before any query commands. " +
    "Re-running performs an incremental update — only changed files are re-parsed.")
{
    indexPathOption,
    indexLanguageOption,
};

indexCommand.SetAction(async parseResult =>
{
    var path = parseResult.GetValue(indexPathOption)!;
    var language = parseResult.GetValue(indexLanguageOption);
    var json = parseResult.GetValue(jsonOption);

    var scope = await CreateProjectScopeAsync(path, provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        // Progress feedback on stderr only -- stdout stays a single machine-readable document for --json.
        // MCP's index_project can run as a background task an agent polls; the CLI has no equivalent
        // polling loop (it calls IndexEngine in-process), so it surfaces an upfront notice instead.
        await Console.Error.WriteLineAsync(
            "Indexing... (first run can take up to 2 minutes on large codebases; incremental updates are usually <1s)").ConfigureAwait(false);

        var result = await scope.Engine.IndexProjectAsync(
            scope.ProjectRoot,
            language,
            cancellationToken: CancellationToken.None).ConfigureAwait(false);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new IndexProjectResult
                {
                    RepoId = result.RepoId,
                    ProjectRoot = scope.ProjectRoot,
                    FilesIndexed = result.FilesIndexed,
                    FilesUnchanged = result.FilesUnchanged,
                    FilesErrored = result.FilesErrored,
                    TotalFiles = result.TotalFiles,
                    SymbolsFound = result.SymbolsFound,
                    DurationMs = result.DurationMs,
                    ParseErrors = result.ParseFailures?.Select(f => new ParseFailureContract { FilePath = f.FilePath, Reason = f.Reason }).ToList(),
                },
                jsonSerializerOptions));
        }
        else
        {
            Console.WriteLine($"Project root: {scope.ProjectRoot}");
            var errorSuffix = result.FilesErrored > 0 ? $", {result.FilesErrored} failed" : "";
            Console.WriteLine($"Indexed {result.FilesIndexed} files, {result.FilesUnchanged} unchanged{errorSuffix}, {result.SymbolsFound} symbols in {result.DurationMs}ms");

            if (result.ParseFailures is { Count: > 0 })
            {
                Console.WriteLine($"  Parse failures (see ~/.code-compress/ log for details):");
                foreach (var failure in result.ParseFailures)
                {
                    Console.WriteLine($"    - {failure.FilePath}: {failure.Reason}");
                }
            }
        }

        await WriteHintAsync("Run 'codecompress outline --path <path>' to explore the indexed codebase.", json).ConfigureAwait(false);
    }
});

rootCommand.Subcommands.Add(indexCommand);

// ── outline ─────────────────────────────────────────────────

var outlinePathOption = CreatePathOption();
var outlineGroupByOption = new Option<string>("--group-by") { Description = "Grouping strategy. Allowed values: 'file' (default), 'kind', 'directory'. Other values rejected.", DefaultValueFactory = _ => "file" };
var outlineIncludePrivateOption = new Option<bool>("--include-private") { Description = "Include private/local symbols" };
var outlineMaxDepthOption = new Option<int?>("--max-depth") { Description = "Limit directory traversal depth (null for unlimited)" };
var outlinePathFilterOption = new Option<string?>("--path-filter") { Description = "Filter to files under this directory (e.g., 'src/')" };
var outlineMaxSymbolsOption = new Option<int>("--max-symbols") { Description = "Maximum symbols to return (1-5000, default 500). Values outside range are clamped.", DefaultValueFactory = _ => 500 };
var outlineOffsetOption = new Option<int>("--offset") { Description = "Number of symbols to skip for pagination", DefaultValueFactory = _ => 0 };

var outlineCommand = new Command("outline",
    "Show a compressed project outline with symbol signatures. " +
    "Far more efficient than reading files individually. Requires index.")
{
    outlinePathOption,
    outlineGroupByOption,
    outlineIncludePrivateOption,
    outlineMaxDepthOption,
    outlinePathFilterOption,
    outlineMaxSymbolsOption,
    outlineOffsetOption,
};

outlineCommand.SetAction(async parseResult =>
{
    var path = parseResult.GetValue(outlinePathOption)!;
    var groupBy = parseResult.GetValue(outlineGroupByOption)!;
    var includePrivate = parseResult.GetValue(outlineIncludePrivateOption);
    var maxDepth = parseResult.GetValue(outlineMaxDepthOption) ?? 0;
    var pathFilter = parseResult.GetValue(outlinePathFilterOption);
    var maxSymbols = Math.Clamp(parseResult.GetValue(outlineMaxSymbolsOption), 1, 5000);
    var offset = Math.Max(0, parseResult.GetValue(outlineOffsetOption));
    var json = parseResult.GetValue(jsonOption);

    var scope = await CreateProjectScopeAsync(path, provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        var outline = await scope.Store.GetProjectOutlineAsync(
            scope.RepoId, includePrivate, groupBy, maxDepth, pathFilter, offset, maxSymbols).ConfigureAwait(false);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(outline, jsonSerializerOptions));
        }
        else
        {
            foreach (var group in outline.Groups)
            {
                Console.WriteLine($"## {group.Name}");
                foreach (var symbol in group.Symbols)
                {
                    Console.WriteLine($"  {symbol.Kind,-12} {symbol.Visibility,-10} {symbol.Signature}");
                }
            }

            if (outline.IsTruncated)
            {
                Console.WriteLine($"\n(Showing {outline.Groups.Sum(g => g.Symbols.Count)} of {outline.TotalSymbolCount} symbols. Use --offset and --max-symbols to paginate.)");
            }
        }
    }
});

rootCommand.Subcommands.Add(outlineCommand);

// ── get-symbol ──────────────────────────────────────────────

var getSymbolPathOption = CreatePathOption();
var getSymbolNameOption = new Option<string>("--name")
{
    Description = "Symbol name — accepts qualified 'Parent:Child' (e.g., CombatService:ProcessAttack) or unqualified names. Unqualified names are resolved automatically.",
    Required = true,
};

var getSymbolCommand = new Command("get-symbol",
    "Retrieve the full source code of a specific symbol by name. " +
    "Loads only the symbol, not the whole file — saves 80%+ tokens. Requires index.")
{
    getSymbolPathOption,
    getSymbolNameOption,
};

getSymbolCommand.SetAction(async parseResult =>
{
    var path = parseResult.GetValue(getSymbolPathOption)!;
    var name = parseResult.GetValue(getSymbolNameOption)!;
    var json = parseResult.GetValue(jsonOption);

    var scope = await CreateProjectScopeAsync(path, provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        var symbol = await scope.Store.GetSymbolByNameAsync(scope.RepoId, name).ConfigureAwait(false);

        if (symbol is null)
        {
            // Fuzzy resolution: try matching by unqualified name
            var candidates = await scope.Store.GetSymbolCandidatesByNameAsync(scope.RepoId, name).ConfigureAwait(false);
            if (candidates.Count == 1)
            {
                symbol = candidates[0];
            }
            else if (candidates.Count > 1)
            {
                var qualifiedNames = candidates.Select(c => c.ParentSymbol is not null ? $"{c.ParentSymbol}:{c.Name}" : c.Name).ToList();
                if (json)
                {
                    Environment.ExitCode = 1;
                    Console.WriteLine(JsonSerializer.Serialize(
                        new GetSymbolResult { Error = "Multiple symbols match this name", Code = "SYMBOL_NOT_FOUND", Retryable = false, Symbol = SanitizeSymbolName(name), Candidates = qualifiedNames },
                        jsonSerializerOptions));
                }
                else
                {
                    await WriteErrorAsync("Multiple symbols match this name", "SYMBOL_NOT_FOUND", json, jsonSerializerOptions,
                        $"Candidates: {string.Join(", ", qualifiedNames)}").ConfigureAwait(false);
                }

                return;
            }
            else
            {
                await WriteErrorAsync("Symbol not found", "SYMBOL_NOT_FOUND", json, jsonSerializerOptions,
                    "Use 'codecompress search --path <path> --query <name>' to discover symbol names. If the symbol was recently added or changed, re-run 'codecompress index' to update the index.").ConfigureAwait(false);
                return;
            }
        }

        var files = await scope.Store.GetFilesByRepoAsync(scope.RepoId).ConfigureAwait(false);
        var file = files.FirstOrDefault(f => f.Id == symbol.FileId);
        if (file is null)
        {
            await WriteErrorAsync("File not found for symbol", "FILE_NOT_FOUND", json, jsonSerializerOptions).ConfigureAwait(false);
            return;
        }

        var pathValidator = provider.GetRequiredService<IPathValidator>();
        string resolvedPath;
        try
        {
            resolvedPath = pathValidator.ValidatePath(Path.Combine(path, file.RelativePath), path);
        }
        catch (ArgumentException)
        {
            await WriteErrorAsync("Path validation failed", "INVALID_PATH", json, jsonSerializerOptions).ConfigureAwait(false);
            return;
        }

        var sourceCode = await ReadSourceCodeAsync(resolvedPath, symbol.ByteOffset, symbol.ByteLength).ConfigureAwait(false);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new GetSymbolResult
                {
                    Name = symbol.Name,
                    Kind = symbol.Kind,
                    Parent = symbol.ParentSymbol,
                    File = file.RelativePath,
                    LineStart = symbol.LineStart,
                    LineEnd = symbol.LineEnd,
                    Signature = symbol.Signature,
                    SourceCode = sourceCode,
                },
                jsonSerializerOptions));
        }
        else
        {
            Console.WriteLine($"// {symbol.Name} ({symbol.Kind}, {symbol.Visibility})");
            Console.WriteLine($"// {file.RelativePath}:{symbol.LineStart}-{symbol.LineEnd}");
            Console.WriteLine(sourceCode);
        }
    }
});

rootCommand.Subcommands.Add(getSymbolCommand);

// ── search ──────────────────────────────────────────────────

var searchPathOption = CreatePathOption();
var searchQueryOption = new Option<string>("--query")
{
    Description = "Search query (supports FTS5 operators AND/OR/NOT, prefix*, *contains*; multi-word queries like 'user profile' match camelCase/PascalCase symbols)",
    Required = true,
};
var searchKindOption = new Option<string?>("--kind") { Description = "Filter by symbol kind (function, method, class, record, enum, type, interface, export, constant, module)" };
var searchPathFilterOption = new Option<string?>("--path-filter") { Description = "Filter to files under this directory (e.g., 'src/')" };
var searchLimitOption = new Option<int>("--limit") { Description = "Maximum results to return (1-100, default 20). Values outside range are clamped.", DefaultValueFactory = _ => 20 };
var searchFuzzyOption = new Option<bool>("--fuzzy") { Description = "Enable typo-tolerant fuzzy matching (Levenshtein distance ≤ 2). Useful when exact symbol name is unknown or may have a typo." };

var searchCommand = new Command("search",
    "Search the symbol index using FTS5 full-text search. " +
    "Supports camelCase/PascalCase token splitting and optional fuzzy matching. Faster and more precise than grep. Requires index.")
{
    searchPathOption,
    searchQueryOption,
    searchKindOption,
    searchPathFilterOption,
    searchLimitOption,
    searchFuzzyOption,
};

searchCommand.SetAction(async parseResult =>
{
    var path = parseResult.GetValue(searchPathOption)!;
    var query = parseResult.GetValue(searchQueryOption)!;
    var kind = parseResult.GetValue(searchKindOption);
    var pathFilter = parseResult.GetValue(searchPathFilterOption);
    var limit = Math.Clamp(parseResult.GetValue(searchLimitOption), 1, 100);
    var fuzzy = parseResult.GetValue(searchFuzzyOption);
    var json = parseResult.GetValue(jsonOption);

    var scope = await CreateProjectScopeAsync(path, provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        IReadOnlyList<SymbolSearchResult> results;
        try
        {
            results = await scope.Store.SearchSymbolsAsync(scope.RepoId, query, kind, limit, pathFilter, fuzzy: fuzzy).ConfigureAwait(false);
        }
        catch (System.Data.Common.DbException)
        {
            // FTS5 syntax error — retry with literal phrase
            var literalQuery = $"\"{query.Replace("\"", string.Empty, StringComparison.Ordinal)}\"";
            results = await scope.Store.SearchSymbolsAsync(scope.RepoId, literalQuery, kind, limit, pathFilter, fuzzy: fuzzy).ConfigureAwait(false);
        }

        // Auto contains-match fallback for plain terms with zero results
        var fallbackUsed = false;
        if (results.Count == 0 && GlobPattern.IsPlainTerm(query))
        {
            var containsGlob = Fts5QuerySanitizer.SanitizeAsGlob($"*{query}*");
            results = await scope.Store.SearchSymbolsAsync(
                scope.RepoId, containsGlob.Fts5Query, kind, limit, pathFilter, containsGlob.SqlLikePattern, fuzzy).ConfigureAwait(false);
            fallbackUsed = results.Count > 0;
        }

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new SearchSymbolsResult
                {
                    Query = query,
                    TotalMatches = results.Count,
                    FallbackUsed = fallbackUsed ? true : null,
                    Results = results.Select((r, index) => new SymbolSearchItemContract
                    {
                        Name = r.Symbol.Name,
                        Kind = r.Symbol.Kind,
                        Parent = r.Symbol.ParentSymbol,
                        File = r.FilePath,
                        Line = r.Symbol.LineStart,
                        Signature = r.Symbol.Signature,
                        Snippet = r.Symbol.DocComment ?? string.Empty,
                        Rank = index + 1,
                    }).ToList(),
                },
                jsonSerializerOptions));
        }
        else
        {
            if (results.Count == 0)
            {
                Console.WriteLine("No symbols found.");
                return;
            }

            if (fallbackUsed)
            {
                Console.WriteLine($"Found {results.Count} symbol(s) via contains-match (no exact FTS5 match):");
            }
            else
            {
                Console.WriteLine($"Found {results.Count} symbol(s):");
            }

            foreach (var r in results)
            {
                Console.WriteLine($"  {r.Symbol.Kind,-12} {r.Symbol.Name,-30} {r.FilePath}:{r.Symbol.LineStart}");
            }

            await WriteHintAsync("Run 'codecompress get-symbol --path <path> --name <Name>' to retrieve full source code.", json).ConfigureAwait(false);
        }
    }
});

rootCommand.Subcommands.Add(searchCommand);

// ── search-text ─────────────────────────────────────────────

var searchTextPathOption = CreatePathOption();
var searchTextQueryOption = new Option<string>("--query")
{
    Description = "FTS5 search query for raw file contents",
    Required = true,
};
var searchTextGlobOption = new Option<string?>("--glob") { Description = "File pattern filter (e.g., *.cs, src/services/*.lua)" };
var searchTextPathFilterOption = new Option<string?>("--path-filter") { Description = "Filter to files under this directory (e.g., 'src/')" };
var searchTextLimitOption = new Option<int>("--limit") { Description = "Maximum results to return (1-100, default 20). Values outside range are clamped.", DefaultValueFactory = _ => 20 };

var searchTextCommand = new Command("search-text",
    "Search raw file contents using FTS5 full-text search. " +
    "Use for string literals, comments, or non-symbol patterns. Requires index.")
{
    searchTextPathOption,
    searchTextQueryOption,
    searchTextGlobOption,
    searchTextPathFilterOption,
    searchTextLimitOption,
};

searchTextCommand.SetAction(async parseResult =>
{
    var path = parseResult.GetValue(searchTextPathOption)!;
    var query = parseResult.GetValue(searchTextQueryOption)!;
    var glob = parseResult.GetValue(searchTextGlobOption);
    var pathFilter = parseResult.GetValue(searchTextPathFilterOption);
    var limit = Math.Clamp(parseResult.GetValue(searchTextLimitOption), 1, 100);
    var json = parseResult.GetValue(jsonOption);

    var sanitizedQuery = Fts5QuerySanitizer.Sanitize(query);
    var sanitizedGlob = glob is not null ? Fts5QuerySanitizer.SanitizeGlob(glob) : null;

    if (string.IsNullOrWhiteSpace(sanitizedQuery))
    {
        await WriteErrorAsync("Search query is empty after sanitization", "EMPTY_QUERY", json, jsonSerializerOptions).ConfigureAwait(false);
        return;
    }

    if (string.IsNullOrWhiteSpace(sanitizedGlob))
    {
        sanitizedGlob = null;
    }

    var scope = await CreateProjectScopeAsync(path, provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        IReadOnlyList<TextSearchResult> results;
        try
        {
            results = await scope.Store.SearchTextAsync(scope.RepoId, sanitizedQuery, sanitizedGlob, limit, pathFilter).ConfigureAwait(false);
        }
        catch (System.Data.Common.DbException)
        {
            // FTS5 syntax error — retry with query as a quoted literal phrase
            var literalQuery = $"\"{sanitizedQuery.Replace("\"", string.Empty, StringComparison.Ordinal)}\"";
            results = await scope.Store.SearchTextAsync(scope.RepoId, literalQuery, sanitizedGlob, limit, pathFilter).ConfigureAwait(false);
        }

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new SearchTextResult
                {
                    Query = sanitizedQuery,
                    TotalMatches = results.Count,
                    Results = results.Select((r, index) => new TextSearchItemContract
                    {
                        FilePath = r.FilePath,
                        Snippet = r.Snippet,
                        Rank = index + 1,
                    }).ToList(),
                },
                jsonSerializerOptions));
        }
        else
        {
            if (results.Count == 0)
            {
                Console.WriteLine("No matches found.");
                return;
            }

            Console.WriteLine($"Found {results.Count} match(es):");
            foreach (var r in results)
            {
                Console.WriteLine($"  {r.FilePath}");
                Console.WriteLine($"    {r.Snippet.Trim()}");
            }
        }
    }
});

rootCommand.Subcommands.Add(searchTextCommand);

// ── changes ─────────────────────────────────────────────────

var changesPathOption = CreatePathOption();
var changesLabelOption = new Option<string>("--label")
{
    Description = "Snapshot label to compare against",
    Required = true,
};

var changesCommand = new Command("changes",
    "Show what changed since a named snapshot: new, modified, and deleted files. " +
    "Use snapshot to set a baseline first. Requires index.")
{
    changesPathOption,
    changesLabelOption,
};

changesCommand.SetAction(async parseResult =>
{
    var path = parseResult.GetValue(changesPathOption)!;
    var label = parseResult.GetValue(changesLabelOption)!;
    var json = parseResult.GetValue(jsonOption);

    var scope = await CreateProjectScopeAsync(path, provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        var snapshot = await scope.Store.GetSnapshotByLabelAsync(scope.RepoId, label).ConfigureAwait(false);

        if (snapshot is null)
        {
            await WriteErrorAsync("Snapshot not found", "SNAPSHOT_NOT_FOUND", json, jsonSerializerOptions).ConfigureAwait(false);
            return;
        }

        var changedFiles = await scope.Store.GetChangedFilesAsync(scope.RepoId, snapshot.Id).ConfigureAwait(false);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new
                {
                    Label = label,
                    Added = changedFiles.Added.Select(f => f.RelativePath).ToList(),
                    Modified = changedFiles.Modified.Select(f => f.RelativePath).ToList(),
                    Removed = changedFiles.Removed,
                },
                jsonSerializerOptions));
        }
        else
        {
            Console.WriteLine($"Changes since snapshot \"{label}\":");
            Console.WriteLine($"  New files: {changedFiles.Added.Count}");
            Console.WriteLine($"  Modified files: {changedFiles.Modified.Count}");
            Console.WriteLine($"  Deleted files: {changedFiles.Removed.Count}");

            foreach (var file in changedFiles.Added)
            {
                Console.WriteLine($"  + {file.RelativePath}");
            }

            foreach (var file in changedFiles.Modified)
            {
                Console.WriteLine($"  ~ {file.RelativePath}");
            }

            foreach (var filePath in changedFiles.Removed)
            {
                Console.WriteLine($"  - {filePath}");
            }
        }
    }
});

rootCommand.Subcommands.Add(changesCommand);

// ── snapshot ────────────────────────────────────────────────

var snapshotPathOption = CreatePathOption();
var snapshotLabelOption = new Option<string?>("--label") { Description = "Human-readable label (auto-generated if omitted)" };

var snapshotCommand = new Command("snapshot",
    "Create a named snapshot of the current index state. " +
    "Use before making changes, then run 'changes' to see what changed. Requires index.")
{
    snapshotPathOption,
    snapshotLabelOption,
};

snapshotCommand.SetAction(async parseResult =>
{
    var path = parseResult.GetValue(snapshotPathOption)!;
    var label = parseResult.GetValue(snapshotLabelOption)
        ?? DateTimeOffset.UtcNow.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture);
    var json = parseResult.GetValue(jsonOption);

    var scope = await CreateProjectScopeAsync(path, provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        var repo = await scope.Store.GetRepositoryAsync(scope.RepoId).ConfigureAwait(false);

        var snapshotRecord = new IndexSnapshot(
            0, scope.RepoId, label, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), string.Empty);

        var snapshotId = await scope.Store.CreateSnapshotAsync(snapshotRecord).ConfigureAwait(false);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new SnapshotCreateResult
                {
                    SnapshotId = snapshotId,
                    Label = label,
                    FileCount = repo?.FileCount ?? 0,
                    SymbolCount = repo?.SymbolCount ?? 0,
                },
                jsonSerializerOptions));
        }
        else
        {
            Console.WriteLine($"Snapshot created: \"{label}\" (id: {snapshotId})");
        }

        await WriteHintAsync($"After making changes, run 'codecompress changes --path <path> --label {label}' to see diffs.", json).ConfigureAwait(false);
    }
});

rootCommand.Subcommands.Add(snapshotCommand);

// ── file-tree ───────────────────────────────────────────────

var fileTreePathOption = CreatePathOption();
var fileTreeDepthOption = new Option<int>("--depth")
{
    Description = "Maximum directory depth (1-20, default 5). Values outside range are clamped.",
    DefaultValueFactory = _ => 5,
};

var fileTreeCommand = new Command("file-tree",
    "Show an annotated directory tree with file counts. " +
    "Does NOT require index — reads the filesystem directly.")
{
    fileTreePathOption,
    fileTreeDepthOption,
};

fileTreeCommand.SetAction(async parseResult =>
{
    var path = parseResult.GetValue(fileTreePathOption)!;
    var maxDepth = Math.Clamp(parseResult.GetValue(fileTreeDepthOption), 1, 20);
    var json = parseResult.GetValue(jsonOption);

    var pathValidator = provider.GetRequiredService<IPathValidator>();
    var validatedPath = pathValidator.ValidatePath(path, path);

    if (!Directory.Exists(validatedPath))
    {
        await WriteErrorAsync("Directory not found", "DIRECTORY_NOT_FOUND", json, jsonSerializerOptions).ConfigureAwait(false);
        return;
    }

    await PrintDirectoryTreeAsync(validatedPath, validatedPath, maxDepth, 0).ConfigureAwait(false);
});

rootCommand.Subcommands.Add(fileTreeCommand);

// ── deps ────────────────────────────────────────────────────

var depsPathOption = CreatePathOption();
var depsFileOption = new Option<string?>("--file") { Description = "Start from a specific file (relative path)" };
var depsDirectionOption = new Option<string>("--direction") { Description = "Traversal direction. Allowed values: 'dependencies' (outgoing), 'dependents' (incoming), 'both' (default). Other values rejected.", DefaultValueFactory = _ => "both" };
var depsDepthOption = new Option<int>("--depth") { Description = "Maximum traversal depth (1-50, default 3). Values outside range are clamped.", DefaultValueFactory = _ => 3 };
var depsEdgeKindOption = new Option<string?>("--edge-kind") { Description = "Filter edges by kind: imports, calls, implements, inherits, references. Omit for all." };

var depsCommand = new Command("deps",
    "Show the import/require dependency graph. " +
    "Shows which files depend on which others. Requires index.")
{
    depsPathOption,
    depsFileOption,
    depsDirectionOption,
    depsDepthOption,
    depsEdgeKindOption,
};

depsCommand.SetAction(async parseResult =>
{
    var path = parseResult.GetValue(depsPathOption)!;
    var rootFile = parseResult.GetValue(depsFileOption) is { } rf ? PathValidator.NormalizeRelativePath(rf) : null;
    var direction = parseResult.GetValue(depsDirectionOption)!;
    var depth = Math.Clamp(parseResult.GetValue(depsDepthOption), 1, 50);
    var edgeKind = parseResult.GetValue(depsEdgeKindOption);
    var json = parseResult.GetValue(jsonOption);

    var scope = await CreateProjectScopeAsync(path, provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        var graph = await scope.Store.GetDependencyGraphAsync(scope.RepoId, rootFile, direction, depth, edgeKind).ConfigureAwait(false);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(graph, jsonSerializerOptions));
        }
        else
        {
            var kindSuffix = edgeKind is not null ? $" [{edgeKind}]" : string.Empty;
            Console.WriteLine($"Dependency graph ({graph.Nodes.Count} nodes, {graph.Edges.Count} edges{kindSuffix}):");
            foreach (var edge in graph.Edges)
            {
                var edgeLabel = edge.EdgeKind is not null ? $" [{edge.EdgeKind}]" : string.Empty;
                Console.WriteLine($"  {edge.From} → {edge.To}{edgeLabel}" + (edge.Alias is not null ? $" (alias: {edge.Alias})" : ""));
            }
        }
    }
});

rootCommand.Subcommands.Add(depsCommand);

// ── blast-radius ─────────────────────────────────────────────

var blastRadiusPathOption = CreatePathOption();
var blastRadiusFileOption = new Option<string?>("--file") { Description = "Relative path to the file to analyze" };
var blastRadiusSymbolOption = new Option<string?>("--symbol") { Description = "Symbol name to analyze (alternative to --file)" };
var blastRadiusMaxDepthOption = new Option<int>("--max-depth") { Description = "Maximum BFS depth (1-20, default 5). Values outside range are clamped.", DefaultValueFactory = _ => 5 };

var blastRadiusCommand = new Command("blast-radius",
    "Find all files affected if a given file or symbol changes. " +
    "Performs reverse BFS over dependency edges. Requires index.")
{
    blastRadiusPathOption,
    blastRadiusFileOption,
    blastRadiusSymbolOption,
    blastRadiusMaxDepthOption,
};

blastRadiusCommand.SetAction(async parseResult =>
{
    var path = parseResult.GetValue(blastRadiusPathOption)!;
    var filePath = parseResult.GetValue(blastRadiusFileOption) is { } fp ? PathValidator.NormalizeRelativePath(fp) : null;
    var symbolName = parseResult.GetValue(blastRadiusSymbolOption);
    var maxDepth = Math.Clamp(parseResult.GetValue(blastRadiusMaxDepthOption), 1, 20);
    var json = parseResult.GetValue(jsonOption);

    var scope = await CreateProjectScopeAsync(path, provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        var result = await scope.Store.GetBlastRadiusAsync(scope.RepoId, filePath, symbolName, maxDepth).ConfigureAwait(false);

        if (!result.Found)
        {
            await WriteErrorAsync(
                filePath is not null
                    ? "File not found in index — verify the relative path and run index_project"
                    : "Symbol not found in index — use search_symbols to find the correct name",
                "NOT_FOUND", json, jsonSerializerOptions).ConfigureAwait(false);
            return;
        }

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new BlastRadiusToolResult
                {
                    TotalAffected = result.TotalAffected,
                    Depths = result.Depths.Select(d => new BlastRadiusDepthContract { Depth = d.Depth, Files = d.Files }).ToList(),
                },
                jsonSerializerOptions));
        }
        else
        {
            Console.WriteLine($"Blast radius: {result.TotalAffected} file(s) affected");
            foreach (var depth in result.Depths)
            {
                Console.WriteLine($"  Depth {depth.Depth}: {string.Join(", ", depth.Files)}");
            }
        }
    }
});

rootCommand.Subcommands.Add(blastRadiusCommand);

// ── unused-symbols ──────────────────────────────────────────

var unusedPathOption = CreatePathOption();
var unusedLimitOption = new Option<int>("--limit") { Description = "Maximum results to return (1-500, default 100). Values outside range are clamped.", DefaultValueFactory = _ => 100 };

var unusedCommand = new Command("unused-symbols",
    "Find public symbols with no incoming dependency edges (best-effort dead code detection). " +
    "Excludes test files, Main entry point, and HTTP controller actions. Requires index.")
{
    unusedPathOption,
    unusedLimitOption,
};

unusedCommand.SetAction(async parseResult =>
{
    var path = parseResult.GetValue(unusedPathOption)!;
    var limit = Math.Clamp(parseResult.GetValue(unusedLimitOption), 1, 500);
    var json = parseResult.GetValue(jsonOption);

    var scope = await CreateProjectScopeAsync(path, provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        var results = await scope.Store.FindUnusedSymbolsAsync(scope.RepoId, limit).ConfigureAwait(false);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new FindUnusedSymbolsResult
                {
                    Results = results.Select(s => new UnusedSymbolContract { Name = s.Name, Kind = s.Kind, Signature = s.Signature }).ToList(),
                },
                jsonSerializerOptions));
        }
        else
        {
            if (results.Count == 0)
            {
                Console.WriteLine("No potentially unused public symbols found.");
                return;
            }

            Console.WriteLine($"Found {results.Count} potentially unused public symbol(s):");
            foreach (var s in results)
            {
                Console.WriteLine($"  {s.Kind,-12} {s.Name,-30} {s.Signature}");
            }
        }
    }
});

rootCommand.Subcommands.Add(unusedCommand);

// ── invalidate-cache ────────────────────────────────────────

var invalidateCachePathOption = CreatePathOption();

var invalidateCacheCommand = new Command("invalidate-cache",
    "Delete the entire index for a project, forcing a full re-index on the next index command.")
{
    invalidateCachePathOption,
};

invalidateCacheCommand.SetAction(async parseResult =>
{
    var path = parseResult.GetValue(invalidateCachePathOption)!;
    var json = parseResult.GetValue(jsonOption);

    var pathValidator = provider.GetRequiredService<IPathValidator>();
    var validatedPath = pathValidator.ValidatePath(path, path);

    var registryService = provider.GetRequiredService<IRegistryService>();
    await registryService.DeregisterAsync(validatedPath).ConfigureAwait(false);

    if (json)
    {
        Console.WriteLine(JsonSerializer.Serialize(
            new InvalidateCacheResult
            {
                Success = true,
                Message = "Cache invalidated. Next index operation will perform a full reparse.",
            },
            jsonSerializerOptions));
    }
    else
    {
        Console.WriteLine("Index invalidated. Next index command will perform a full reparse.");
    }
});

rootCommand.Subcommands.Add(invalidateCacheCommand);

// ── list ────────────────────────────────────────────────────

var listCommand = new Command("list",
    "List all projects that have been indexed in the global CodeCompress database. " +
    "Shows project name, file count, symbol count, and relative last-indexed time. " +
    "Does NOT require --path — reads from the global ~/.code-compress/index.db.");

listCommand.SetAction(async parseResult =>
{
    var json = parseResult.GetValue(jsonOption);

    var registryService = provider.GetRequiredService<IRegistryService>();
    var repos = await registryService.ListAsync().ConfigureAwait(false);

    if (json)
    {
        Console.WriteLine(JsonSerializer.Serialize(
            new ListReposResult
            {
                Repos = repos.Select(r => new RepoEntryContract
                {
                    ProjectRoot = r.ProjectRoot,
                    DisplayName = r.DisplayName,
                    FileCount = r.FileCount,
                    SymbolCount = r.SymbolCount,
                    LastIndexed = r.LastIndexed,
                    Status = r.LastError is null ? "healthy" : "error",
                }).ToList(),
            },
            jsonSerializerOptions));
        return;
    }

    if (repos.Count == 0)
    {
        Console.WriteLine("No indexed projects found. Run 'codecompress index --path <path>' to index a project.");
        return;
    }

    var now = DateTimeOffset.UtcNow;
    const int nameWidth = 30;
    const int filesWidth = 8;
    const int symbolsWidth = 10;
    const int timeWidth = 16;

    Console.WriteLine(
        $"{"Project",-nameWidth}  {"Files",filesWidth}  {"Symbols",symbolsWidth}  {"Last Indexed",timeWidth}");
    Console.WriteLine(new string('─', nameWidth + filesWidth + symbolsWidth + timeWidth + 6));

    foreach (var repo in repos)
    {
        var relTime = FormatRelativeTime(now - repo.LastIndexed);
        var safeName = new string(repo.DisplayName.Where(c => c >= 0x20 && c != 0x7F && !char.IsControl(c)).ToArray());
        Console.WriteLine(
            $"{safeName,-nameWidth}  {repo.FileCount,filesWidth}  {repo.SymbolCount,symbolsWidth}  {relTime,timeWidth}");
    }
});

rootCommand.Subcommands.Add(listCommand);

// ── get-module-api ──────────────────────────────────────────

var getModuleApiPathOption = CreatePathOption();
var getModuleApiModuleOption = new Option<string>("--module")
{
    Description = "Relative path to the module file (e.g., src/Core/Foo.cs). Forward slashes only.",
    Required = true,
};

var getModuleApiCommand = new Command("get-module-api",
    "Get the full public API surface of a single file — symbols, signatures, and dependencies. Requires index.")
{
    getModuleApiPathOption,
    getModuleApiModuleOption,
};

getModuleApiCommand.SetAction(async parseResult =>
{
    var path = parseResult.GetValue(getModuleApiPathOption)!;
    var modulePath = PathValidator.NormalizeRelativePath(parseResult.GetValue(getModuleApiModuleOption)!);
    var json = parseResult.GetValue(jsonOption);

    var scope = await CreateProjectScopeAsync(path, provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        ModuleApi moduleApi;
        try
        {
            moduleApi = await scope.Store.GetModuleApiAsync(scope.RepoId, modulePath).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            await WriteErrorAsync("Module not found", "MODULE_NOT_FOUND", json, jsonSerializerOptions,
                "Verify the module path and ensure index_project has been run.").ConfigureAwait(false);
            return;
        }

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new GetModuleApiResult
                {
                    Module = moduleApi.File.RelativePath,
                    Symbols = moduleApi.Symbols.Select(s => new ModuleSymbolContract
                    {
                        Name = s.Name,
                        Kind = s.Kind,
                        Parent = s.ParentSymbol,
                        Signature = s.Signature,
                        Line = s.LineStart,
                        DocComment = s.DocComment,
                    }).ToList(),
                    Dependencies = moduleApi.Dependencies.Select(d => new ModuleDependencyContract { RequiresPath = d.RequiresPath, Alias = d.Alias }).ToList(),
                },
                jsonSerializerOptions));
        }
        else
        {
            Console.WriteLine($"## {moduleApi.File.RelativePath}");
            Console.WriteLine();
            foreach (var s in moduleApi.Symbols)
            {
                Console.WriteLine($"  {s.Kind,-12} {s.Visibility,-10} {s.Signature}");
            }

            if (moduleApi.Dependencies.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Dependencies:");
                foreach (var d in moduleApi.Dependencies)
                {
                    Console.WriteLine($"  → {d.RequiresPath}" + (d.Alias is not null ? $" (as {d.Alias})" : ""));
                }
            }
        }
    }
});

rootCommand.Subcommands.Add(getModuleApiCommand);

// ── expand-symbol ───────────────────────────────────────────

var expandSymbolPathOption = CreatePathOption();
var expandSymbolNameOption = new Option<string>("--name")
{
    Description = "Symbol name — accepts qualified 'Parent:Child' (e.g., MyClass:MyMethod) or unqualified names. Unqualified names are resolved automatically.",
    Required = true,
};
var expandSymbolContextOption = new Option<bool>("--context")
{
    Description = "Include 3 lines of context before and after the symbol",
};

var expandSymbolCommand = new Command("expand-symbol",
    "Retrieve only a nested symbol's body without loading the parent class — saves ~60% tokens. Requires index.")
{
    expandSymbolPathOption,
    expandSymbolNameOption,
    expandSymbolContextOption,
};

expandSymbolCommand.SetAction(async parseResult =>
{
    var path = parseResult.GetValue(expandSymbolPathOption)!;
    var name = parseResult.GetValue(expandSymbolNameOption)!;
    _ = parseResult.GetValue(expandSymbolContextOption); // Reserved for future context-with-lines support
    var json = parseResult.GetValue(jsonOption);

    var scope = await CreateProjectScopeAsync(path, provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        var symbol = await scope.Store.GetSymbolByNameAsync(scope.RepoId, name).ConfigureAwait(false);
        if (symbol is null)
        {
            // Step 2: prefix match within parent scope for qualified names
            var separatorIndex = name.IndexOfAny(['.', ':']);
            if (separatorIndex > 0 && separatorIndex < name.Length - 1)
            {
                var parent = name[..separatorIndex];
                var childPrefix = name[(separatorIndex + 1)..];
                var prefixCandidates = await scope.Store.GetSymbolsByParentAndChildPrefixAsync(
                    scope.RepoId, parent, childPrefix).ConfigureAwait(false);

                if (prefixCandidates.Count == 1)
                {
                    symbol = prefixCandidates[0];
                }
                else if (prefixCandidates.Count > 1)
                {
                    var qualifiedNames = prefixCandidates.Select(c => $"{parent}:{c.Name}").ToList();
                    if (json)
                    {
                        Environment.ExitCode = 1;
                        Console.WriteLine(JsonSerializer.Serialize(
                            new ExpandSymbolResult { Error = "Multiple symbols match this prefix", Code = "SYMBOL_NOT_FOUND", Retryable = false, Symbol = SanitizeSymbolName(name), Candidates = qualifiedNames },
                            jsonSerializerOptions));
                    }
                    else
                    {
                        await WriteErrorAsync("Multiple symbols match this prefix", "SYMBOL_NOT_FOUND", json, jsonSerializerOptions,
                            $"Candidates: {string.Join(", ", qualifiedNames)}").ConfigureAwait(false);
                    }

                    return;
                }
            }

            // Step 3: unscoped candidate search by unqualified name
            if (symbol is null)
            {
                var candidates = await scope.Store.GetSymbolCandidatesByNameAsync(scope.RepoId, name).ConfigureAwait(false);
                if (candidates.Count == 1)
                {
                    symbol = candidates[0];
                }
                else if (candidates.Count > 1)
                {
                    var qualifiedNames = candidates.Select(c => c.ParentSymbol is not null ? $"{c.ParentSymbol}:{c.Name}" : c.Name).ToList();
                    if (json)
                    {
                        Environment.ExitCode = 1;
                        Console.WriteLine(JsonSerializer.Serialize(
                            new ExpandSymbolResult { Error = "Multiple symbols match this name", Code = "SYMBOL_NOT_FOUND", Retryable = false, Symbol = SanitizeSymbolName(name), Candidates = qualifiedNames },
                            jsonSerializerOptions));
                    }
                    else
                    {
                        await WriteErrorAsync("Multiple symbols match this name", "SYMBOL_NOT_FOUND", json, jsonSerializerOptions,
                            $"Candidates: {string.Join(", ", qualifiedNames)}").ConfigureAwait(false);
                    }

                    return;
                }
                else
                {
                    await WriteErrorAsync("Symbol not found", "SYMBOL_NOT_FOUND", json, jsonSerializerOptions,
                        "Use 'codecompress search --path <path> --query <name>' to discover symbol names. If the symbol was recently added or changed, re-run 'codecompress index' to update the index.").ConfigureAwait(false);
                    return;
                }
            }
        }

        var files = await scope.Store.GetFilesByRepoAsync(scope.RepoId).ConfigureAwait(false);
        var file = files.FirstOrDefault(f => f.Id == symbol.FileId);
        if (file is null)
        {
            await WriteErrorAsync("File not found for symbol", "FILE_NOT_FOUND", json, jsonSerializerOptions).ConfigureAwait(false);
            return;
        }

        var pathValidator = provider.GetRequiredService<IPathValidator>();
        var resolvedPath = pathValidator.ValidatePath(
            Path.Combine(path, file.RelativePath), path);

        var sourceCode = await ReadSourceCodeAsync(resolvedPath, symbol.ByteOffset, symbol.ByteLength).ConfigureAwait(false);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new ExpandSymbolResult
                {
                    Name = symbol.Name,
                    Kind = symbol.Kind,
                    Parent = symbol.ParentSymbol,
                    File = file.RelativePath,
                    LineStart = symbol.LineStart,
                    LineEnd = symbol.LineEnd,
                    Signature = symbol.Signature,
                    DocComment = symbol.DocComment,
                    SourceCode = sourceCode,
                },
                jsonSerializerOptions));
        }
        else
        {
            Console.WriteLine($"// {symbol.Name} ({symbol.Kind}, {symbol.Visibility})");
            Console.WriteLine($"// {file.RelativePath}:{symbol.LineStart}-{symbol.LineEnd}");
            Console.WriteLine(sourceCode);
        }
    }
});

rootCommand.Subcommands.Add(expandSymbolCommand);

// ── get-hot-path ─────────────────────────────────────────────

var hotPathPathOption = CreatePathOption();
var hotPathNameOption = new Option<string>("--name")
{
    Description = "Symbol name — accepts qualified 'Parent:Child' (e.g., OrderService:ProcessPayment) or unqualified names.",
    Required = true,
};
var hotPathIdentifiersOption = new Option<string>("--identifiers")
{
    Description = "Comma-separated identifiers to search for within the symbol body (whole-word matching, e.g., 'userId,status').",
    Required = true,
};
var hotPathContextLinesOption = new Option<int>("--context-lines")
{
    Description = "Lines of context before and after each match (0-10, default 3). Values outside range are clamped.",
    DefaultValueFactory = _ => 3,
};

var hotPathCommand = new Command("get-hot-path",
    "Return only the lines within a symbol that contain specific identifiers plus surrounding context. " +
    "Use when tracing a variable or condition within a large function — costs ~50-100 tokens vs 500-2000 for the full body. " +
    "Identifiers are matched as whole words only. Overlapping context windows are merged. Requires index.")
{
    hotPathPathOption,
    hotPathNameOption,
    hotPathIdentifiersOption,
    hotPathContextLinesOption,
};

hotPathCommand.SetAction(async parseResult =>
{
    var path = parseResult.GetValue(hotPathPathOption)!;
    var name = parseResult.GetValue(hotPathNameOption)!;
    var identifiersRaw = parseResult.GetValue(hotPathIdentifiersOption)!;
    var contextLines = Math.Clamp(parseResult.GetValue(hotPathContextLinesOption), 0, 10);
    var json = parseResult.GetValue(jsonOption);

    var identifiers = identifiersRaw
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    if (identifiers.Length == 0)
    {
        await WriteErrorAsync("No identifiers provided", "EMPTY_IDENTIFIERS", json, jsonSerializerOptions).ConfigureAwait(false);
        return;
    }

    var scope = await CreateProjectScopeAsync(path, provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        var symbol = await scope.Store.GetSymbolByNameAsync(scope.RepoId, name).ConfigureAwait(false);
        if (symbol is null)
        {
            var candidates = await scope.Store.GetSymbolCandidatesByNameAsync(scope.RepoId, name).ConfigureAwait(false);
            if (candidates.Count == 1)
            {
                symbol = candidates[0];
            }
            else if (candidates.Count > 1)
            {
                var qualifiedNames = candidates.Select(c => c.ParentSymbol is not null ? $"{c.ParentSymbol}:{c.Name}" : c.Name);
                await WriteErrorAsync("Multiple symbols match this name", "SYMBOL_NOT_FOUND", json, jsonSerializerOptions,
                    $"Candidates: {string.Join(", ", qualifiedNames)}").ConfigureAwait(false);
                return;
            }
            else
            {
                await WriteErrorAsync("Symbol not found", "SYMBOL_NOT_FOUND", json, jsonSerializerOptions,
                    "Use 'codecompress search --path <path> --query <name>' to discover symbol names.").ConfigureAwait(false);
                return;
            }
        }

        var files = await scope.Store.GetFilesByRepoAsync(scope.RepoId).ConfigureAwait(false);
        var file = files.FirstOrDefault(f => f.Id == symbol.FileId);
        if (file is null)
        {
            await WriteErrorAsync("File not found for symbol", "FILE_NOT_FOUND", json, jsonSerializerOptions).ConfigureAwait(false);
            return;
        }

        var pathValidator = provider.GetRequiredService<IPathValidator>();
        string resolvedPath;
        try
        {
            resolvedPath = pathValidator.ValidatePath(Path.Combine(path, file.RelativePath), path);
        }
        catch (ArgumentException)
        {
            await WriteErrorAsync("Path validation failed", "INVALID_PATH", json, jsonSerializerOptions).ConfigureAwait(false);
            return;
        }

        // Determine scan range
        int scanLineStart = symbol.BodyLineStart ?? symbol.LineStart;
        int scanLineEnd = symbol.BodyLineEnd ?? symbol.LineEnd;
        var totalLines = Math.Max(0, scanLineEnd - scanLineStart + 1);

        // Read source and split into lines
        var source = await ReadSourceCodeAsync(resolvedPath, symbol.ByteOffset, symbol.ByteLength).ConfigureAwait(false);
        var rawLines = source.Split('\n');

        // Build whole-word patterns
        var patterns = identifiers
            .Select(id => (Id: id, Pattern: new System.Text.RegularExpressions.Regex(
                $@"\b{System.Text.RegularExpressions.Regex.Escape(id)}\b",
                System.Text.RegularExpressions.RegexOptions.None,
                TimeSpan.FromMilliseconds(100))))
            .ToList();

        // Find matches
        var seenMatches = new HashSet<(string, int)>();
        var rawMatches = new List<(string Identifier, int LineNumber)>();
        for (var lineNum = scanLineStart; lineNum <= scanLineEnd; lineNum++)
        {
            var idx = lineNum - symbol.LineStart;
            if (idx < 0 || idx >= rawLines.Length) continue;
            var lineText = rawLines[idx].TrimEnd('\r');
            foreach (var (id, pattern) in patterns)
            {
                if (pattern.IsMatch(lineText) && seenMatches.Add((id, lineNum)))
                    rawMatches.Add((id, lineNum));
            }
        }

        if (json)
        {
            var matchObjects = BuildHotPathMatchObjects(rawMatches, rawLines, symbol, scanLineStart, scanLineEnd, contextLines);
            var returnedLines = matchObjects.Sum(m => m.Context.Count);
            Console.WriteLine(JsonSerializer.Serialize(
                new GetHotPathResult
                {
                    Symbol = symbol.Name,
                    File = file.RelativePath,
                    TotalLines = totalLines,
                    ReturnedLines = returnedLines,
                    Matches = matchObjects.Select(m => new HotPathMatchContract
                    {
                        Identifier = m.Identifier,
                        Line = m.Line,
                        Context = m.Context.Select(c => new HotPathContextLineContract { LineNumber = c.LineNumber, Text = c.Text }).ToList(),
                    }).ToList(),
                },
                jsonSerializerOptions));
        }
        else
        {
            if (rawMatches.Count == 0)
            {
                Console.WriteLine($"No matches found for {identifiers.Length} identifier(s) in '{symbol.Name}'.");
                return;
            }

            Console.WriteLine($"// {symbol.Name} — {rawMatches.Count} match(es) for {identifiers.Length} identifier(s)");
            Console.WriteLine($"// {file.RelativePath}  ({totalLines} total lines in scan range)");
            Console.WriteLine();

            var matchObjects = BuildHotPathMatchObjects(rawMatches, rawLines, symbol, scanLineStart, scanLineEnd, contextLines);
            foreach (var m in matchObjects)
            {
                if (m.Context.Count > 0)
                {
                    foreach (var (lineNumber, text) in m.Context)
                    {
                        var marker = lineNumber == m.Line ? ">" : " ";
                        Console.WriteLine($"  {marker} {lineNumber,6}: {text}");
                    }

                    Console.WriteLine();
                }
                else
                {
                    Console.WriteLine($"  > {m.Line,6}: (see context above — same window as previous match)");
                    Console.WriteLine();
                }
            }

            var uniqueLines = matchObjects.Sum(m => m.Context.Count);
            await WriteHintAsync($"Returned {uniqueLines} unique line(s). Use 'get-symbol' to view the full body.", json).ConfigureAwait(false);
        }
    }
});

rootCommand.Subcommands.Add(hotPathCommand);

// ── get-symbols (batch) ─────────────────────────────────────

var getSymbolsPathOption = CreatePathOption();
var getSymbolsNamesOption = new Option<string>("--names")
{
    Description = "Comma-separated list of qualified symbol names (max 50). Same format as get-symbol.",
    Required = true,
};

var getSymbolsCommand = new Command("get-symbols",
    "Batch retrieve source code for multiple symbols in one call. More efficient than repeated get-symbol. Requires index.")
{
    getSymbolsPathOption,
    getSymbolsNamesOption,
};

getSymbolsCommand.SetAction(async parseResult =>
{
    var path = parseResult.GetValue(getSymbolsPathOption)!;
    var namesRaw = parseResult.GetValue(getSymbolsNamesOption)!;
    var json = parseResult.GetValue(jsonOption);

    var symbolNames = namesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    if (symbolNames.Length == 0)
    {
        await WriteErrorAsync("No symbol names provided", "EMPTY_SYMBOL_NAMES", json, jsonSerializerOptions).ConfigureAwait(false);
        return;
    }

    if (symbolNames.Length > 50)
    {
        await WriteErrorAsync("Too many symbols. Maximum is 50", "SYMBOL_LIMIT_EXCEEDED", json, jsonSerializerOptions).ConfigureAwait(false);
        return;
    }

    var scope = await CreateProjectScopeAsync(path, provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        var foundSymbols = await scope.Store.GetSymbolsByNamesAsync(scope.RepoId, symbolNames).ConfigureAwait(false);
        var files = await scope.Store.GetFilesByRepoAsync(scope.RepoId).ConfigureAwait(false);
        var fileMap = files.ToDictionary(f => f.Id);

        var foundNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in foundSymbols)
        {
            var qn = s.ParentSymbol is not null ? $"{s.ParentSymbol}:{s.Name}" : s.Name;
            foundNames.Add(qn);
        }

        var pathValidator = provider.GetRequiredService<IPathValidator>();
        var results = new List<SymbolItemContract>();

        foreach (var s in foundSymbols)
        {
            if (!fileMap.TryGetValue(s.FileId, out var file))
            {
                continue;
            }

            string resolvedPath;
            try
            {
                resolvedPath = pathValidator.ValidatePath(Path.Combine(path, file.RelativePath), path);
            }
            catch (ArgumentException)
            {
                continue;
            }

            var sourceCode = await ReadSourceCodeAsync(resolvedPath, s.ByteOffset, s.ByteLength).ConfigureAwait(false);
            results.Add(new SymbolItemContract
            {
                Name = s.Name,
                Kind = s.Kind,
                Parent = s.ParentSymbol,
                File = file.RelativePath,
                LineStart = s.LineStart,
                LineEnd = s.LineEnd,
                Signature = s.Signature,
                SourceCode = sourceCode,
            });
        }

        var errors = symbolNames
            .Where(n => !foundNames.Contains(n))
            .Select(n => new SymbolErrorContract { Symbol = SanitizeSymbolName(n), Error = "Symbol not found", Code = "SYMBOL_NOT_FOUND", Retryable = false })
            .ToList();

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new GetSymbolsResult { Results = results, Errors = errors },
                jsonSerializerOptions));
        }
        else
        {
            foreach (var r in results)
            {
                Console.WriteLine(JsonSerializer.Serialize(r, jsonSerializerOptions));
                Console.WriteLine();
            }

            if (errors.Count > 0)
            {
                await Console.Error.WriteLineAsync($"Not found: {string.Join(", ", errors.Select(e => e.Symbol))}").ConfigureAwait(false);
            }
        }
    }
});

rootCommand.Subcommands.Add(getSymbolsCommand);

// ── topic-outline ───────────────────────────────────────────

var topicOutlinePathOption = CreatePathOption();
var topicOutlineTopicOption = new Option<string>("--topic")
{
    Description = "Topic or keyword to search for (e.g., 'authentication', 'database')",
    Required = true,
};
var topicOutlinePathFilterOption = new Option<string?>("--path-filter")
{
    Description = "Filter results to files under this directory (e.g., 'src/Core/')",
};
var topicOutlineMaxResultsOption = new Option<int>("--max-results")
{
    Description = "Maximum symbols to return (1-200, default 50). Values outside range are clamped.",
    DefaultValueFactory = _ => 50,
};

var topicOutlineCommand = new Command("topic-outline",
    "Search for symbols related to a topic and return results in outline format. Requires index.")
{
    topicOutlinePathOption,
    topicOutlineTopicOption,
    topicOutlinePathFilterOption,
    topicOutlineMaxResultsOption,
};

topicOutlineCommand.SetAction(async parseResult =>
{
    var path = parseResult.GetValue(topicOutlinePathOption)!;
    var topic = parseResult.GetValue(topicOutlineTopicOption)!;
    var pathFilter = parseResult.GetValue(topicOutlinePathFilterOption);
    var maxResults = Math.Clamp(parseResult.GetValue(topicOutlineMaxResultsOption), 1, 200);
    var json = parseResult.GetValue(jsonOption);

    var scope = await CreateProjectScopeAsync(path, provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        var outline = await scope.Store.SearchTopicOutlineAsync(
            scope.RepoId, topic, maxResults, pathFilter).ConfigureAwait(false);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(outline, jsonSerializerOptions));
        }
        else
        {
            if (outline.TotalSymbolCount == 0)
            {
                Console.WriteLine($"No symbols found for topic \"{topic}\".");
                return;
            }

            Console.WriteLine($"Found {outline.TotalSymbolCount} symbol(s) for \"{topic}\":");
            foreach (var group in outline.Groups)
            {
                Console.WriteLine($"## {group.Name}");
                foreach (var s in group.Symbols)
                {
                    Console.WriteLine($"  {s.Kind,-12} {s.Visibility,-10} {s.Signature}");
                }
            }
        }
    }
});

rootCommand.Subcommands.Add(topicOutlineCommand);

// ── project-deps ────────────────────────────────────────────

var projectDepsPathOption = CreatePathOption();
var projectDepsFilterOption = new Option<string?>("--filter")
{
    Description = "Filter to projects whose name contains this string (case-insensitive)",
};

var projectDepsCommand = new Command("project-deps",
    "Show inter-project dependency relationships in a .NET solution. Requires index.")
{
    projectDepsPathOption,
    projectDepsFilterOption,
};

projectDepsCommand.SetAction(async parseResult =>
{
    var path = parseResult.GetValue(projectDepsPathOption)!;
    var filter = parseResult.GetValue(projectDepsFilterOption);
    var json = parseResult.GetValue(jsonOption);

    var scope = await CreateProjectScopeAsync(path, provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        var result = await scope.Store.GetProjectDependencyGraphAsync(scope.RepoId, filter).ConfigureAwait(false);

        if (result.Projects.Count == 0)
        {
            await WriteErrorAsync("No project files found in index", "NO_PROJECTS", json, jsonSerializerOptions,
                "Run 'codecompress index' first.").ConfigureAwait(false);
            return;
        }

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, jsonSerializerOptions));
        }
        else
        {
            Console.WriteLine($"Projects ({result.Projects.Count}):");
            foreach (var p in result.Projects)
            {
                Console.WriteLine($"  {p.Name} ({p.RelativePath})");
            }

            if (result.Edges.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Dependencies:");
                foreach (var e in result.Edges)
                {
                    Console.WriteLine($"  {e.FromProject} → {e.ToProject}");
                    if (e.SharedTypes.Count > 0)
                    {
                        Console.WriteLine($"    Shared types: {string.Join(", ", e.SharedTypes.Take(5))}{(e.SharedTypes.Count > 5 ? "..." : "")}");
                    }
                }
            }
        }
    }
});

rootCommand.Subcommands.Add(projectDepsCommand);

// ── find-references ─────────────────────────────────────────

var findRefsPathOption = CreatePathOption();
var findRefsNameOption = new Option<string>("--name")
{
    Description = "Symbol name to search for references (e.g., 'ISymbolStore')",
    Required = true,
};
var findRefsPathFilterOption = new Option<string?>("--path-filter")
{
    Description = "Filter results to files under this directory (e.g., 'src/')",
};
var findRefsLimitOption = new Option<int>("--limit")
{
    Description = "Maximum results to return (1-100, default 20). Values outside range are clamped.",
    DefaultValueFactory = _ => 20,
};

var findRefsCommand = new Command("find-references",
    "Find all locations where a symbol is referenced across the codebase. Faster than grep. Requires index.")
{
    findRefsPathOption,
    findRefsNameOption,
    findRefsPathFilterOption,
    findRefsLimitOption,
};

findRefsCommand.SetAction(async parseResult =>
{
    var path = parseResult.GetValue(findRefsPathOption)!;
    var symbolName = parseResult.GetValue(findRefsNameOption)!;
    var pathFilter = parseResult.GetValue(findRefsPathFilterOption);
    var limit = Math.Clamp(parseResult.GetValue(findRefsLimitOption), 1, 100);
    var json = parseResult.GetValue(jsonOption);

    var pathValidator = provider.GetRequiredService<IPathValidator>();
    var validatedPath = pathValidator.ValidatePath(path, path);

    var scope = await CreateProjectScopeAsync(path, provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        var results = await scope.Store.FindReferencesAsync(
            scope.RepoId, symbolName, validatedPath, limit, pathFilter).ConfigureAwait(false);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new FindReferencesResult
                {
                    Symbol = SanitizeSymbolName(symbolName),
                    TotalMatches = results.Count,
                    Results = results.Select((r, index) => new ReferenceItemContract
                    {
                        File = r.FilePath,
                        Line = r.Line,
                        ContextSnippet = r.ContextSnippet,
                        Rank = index + 1,
                    }).ToList(),
                },
                jsonSerializerOptions));
        }
        else
        {
            if (results.Count == 0)
            {
                Console.WriteLine($"No references found for \"{symbolName}\".");
                return;
            }

            Console.WriteLine($"Found {results.Count} reference(s) for \"{symbolName}\":");
            foreach (var r in results)
            {
                Console.WriteLine($"  {r.FilePath}:{r.Line}");
                Console.WriteLine($"    {r.ContextSnippet.Trim()}");
            }

            await WriteHintAsync("Run 'codecompress get-symbol --path <path> --name <Name>' to view source code of referenced symbols.", json).ConfigureAwait(false);
        }
    }
});

rootCommand.Subcommands.Add(findRefsCommand);

// ── assemble ────────────────────────────────────────────────

var assemblePathOption = CreatePathOption();
var assembleQueryOption = new Option<string>("--query")
{
    Description = "Task description or search terms (e.g., 'authentication middleware')",
    Required = true,
};
var assembleActiveFileOption = new Option<string?>("--active-file")
{
    Description = "Relative path to the file you're editing — gets highest priority",
};
var assembleBudgetOption = new Option<int>("--budget")
{
    Description = "Maximum token budget (1000-200000, default 40000). Values outside range are clamped.",
    DefaultValueFactory = _ => 40000,
};
var assemblePathFilterOption = new Option<string?>("--path-filter") { Description = "Filter to files under this directory (e.g., 'src/')" };

var assembleCommand = new Command("assemble",
    "Assemble relevant code context within a token budget. " +
    "Combines search + source retrieval + file overview in one call. Requires index.")
{
    assemblePathOption,
    assembleQueryOption,
    assembleActiveFileOption,
    assembleBudgetOption,
    assemblePathFilterOption,
};

assembleCommand.SetAction(async parseResult =>
{
    var path = parseResult.GetValue(assemblePathOption)!;
    var query = parseResult.GetValue(assembleQueryOption)!;
    _ = parseResult.GetValue(assembleActiveFileOption); // Reserved for future active-file priority
    var budget = Math.Clamp(parseResult.GetValue(assembleBudgetOption), 1000, 200000);
    var pathFilter = parseResult.GetValue(assemblePathFilterOption);
    var json = parseResult.GetValue(jsonOption);

    var scope = await CreateProjectScopeAsync(path, provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        const double charsPerToken = 3.5;
        var charBudget = (int)(budget * charsPerToken);

        // Tokenize: strips stopwords, joins multi-word queries with OR
        var tokenizedQuery = Fts5QuerySanitizer.TokenizeForSearch(query);
        var searchQuery = string.IsNullOrWhiteSpace(tokenizedQuery)
            ? Fts5QuerySanitizer.Sanitize(query)
            : tokenizedQuery;

        IReadOnlyList<SymbolSearchResult> searchResults;
        try
        {
            searchResults = await scope.Store.SearchSymbolsAsync(scope.RepoId, searchQuery, null, 50, pathFilter).ConfigureAwait(false);
        }
        catch (System.Data.Common.DbException)
        {
            // FTS5 syntax error — retry with literal phrase
            var literalQuery = $"\"{query.Replace("\"", string.Empty, StringComparison.Ordinal)}\"";
            searchResults = await scope.Store.SearchSymbolsAsync(scope.RepoId, literalQuery, null, 50, pathFilter).ConfigureAwait(false);
        }

        // Auto contains-match fallback: try each term as *term* individually
        if (searchResults.Count == 0)
        {
            var terms = searchQuery.Split([" OR "], StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .ToList();

            foreach (var term in terms.Where(GlobPattern.IsPlainTerm))
            {
                var containsGlob = Fts5QuerySanitizer.SanitizeAsGlob($"*{term}*");
                if (containsGlob.SqlLikePattern is null)
                {
                    continue;
                }

                try
                {
                    searchResults = await scope.Store.SearchSymbolsAsync(
                        scope.RepoId, containsGlob.Fts5Query, null, 50, pathFilter, containsGlob.SqlLikePattern).ConfigureAwait(false);
                }
                catch (System.Data.Common.DbException)
                {
                    // Skip this term and try the next one
                    continue;
                }

                if (searchResults.Count > 0)
                {
                    break;
                }
            }
        }

        if (searchResults.Count == 0)
        {
            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { query, total_matches = 0, budget }, jsonSerializerOptions));
            }
            else
            {
                Console.WriteLine("No symbols matched this query.");
            }

            return;
        }

        var files = await scope.Store.GetFilesByRepoAsync(scope.RepoId).ConfigureAwait(false);
        var filePathMap = files.ToDictionary(f => f.RelativePath, f => f, StringComparer.OrdinalIgnoreCase);

        var output = new System.Text.StringBuilder();
        var usedChars = 0;

        // File overview
        var matchedFiles = searchResults.Select(r => r.FilePath).Distinct().OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        output.AppendLine("## File Overview\n```");
        foreach (var file in matchedFiles)
        {
            var line = $"  {file}";
            if (usedChars + line.Length > charBudget / 10)
            {
                break;
            }

            output.AppendLine(line);
            usedChars += line.Length;
        }

        output.AppendLine("```\n");

        // Source code sections
        var symbolsByFile = searchResults.GroupBy(r => r.FilePath).OrderByDescending(g => g.Count());
        foreach (var fileGroup in symbolsByFile)
        {
            if (usedChars >= charBudget)
            {
                break;
            }

            if (!filePathMap.TryGetValue(fileGroup.Key, out var fileRecord))
            {
                continue;
            }

            var resolvedPath = Path.Combine(path, fileRecord.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(resolvedPath))
            {
                continue;
            }

            output.Append("## ").AppendLine(fileRecord.RelativePath).AppendLine();

#pragma warning disable S3267 // Loop has budget check, IO, and break — cannot simplify with LINQ
            foreach (var result in fileGroup)
#pragma warning restore S3267
            {
                if (usedChars >= charBudget)
                {
                    break;
                }

                try
                {
                    using var stream = new FileStream(resolvedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    stream.Seek(result.Symbol.ByteOffset, SeekOrigin.Begin);
                    var buffer = new byte[result.Symbol.ByteLength];
                    var bytesRead = await stream.ReadAsync(buffer).ConfigureAwait(false);
                    var sourceCode = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    var ext = Path.GetExtension(fileRecord.RelativePath).TrimStart('.');
                    var block = $"### {result.Symbol.Kind}: {result.Symbol.Name} (L{result.Symbol.LineStart}-{result.Symbol.LineEnd})\n```{ext}\n{sourceCode}\n```\n\n";

                    if (usedChars + block.Length <= charBudget)
                    {
                        output.Append(block);
                        usedChars += block.Length;
                    }
                }
                catch (IOException)
                {
                    // Skip unreadable files
                }
            }
        }

        var tokensUsed = (int)(usedChars / charsPerToken);
        output.Append("---\n**Context Assembly** | Symbols: ").Append(searchResults.Count)
              .Append(" | Files: ").Append(matchedFiles.Count)
              .Append(" | Tokens: ~").Append(tokensUsed.ToString("N0", System.Globalization.CultureInfo.InvariantCulture))
              .Append('/').Append(budget.ToString("N0", System.Globalization.CultureInfo.InvariantCulture))
              .AppendLine();

        Console.Write(output);
    }
});

rootCommand.Subcommands.Add(assembleCommand);

// ── prompts ─────────────────────────────────────────────────

var promptsNameOption = new Option<string?>("--name")
{
    Description = "Show the full text of a specific prompt. " +
                  "Valid names: explore_codebase, find_impact, review_changes, debug_symbol. " +
                  "Omit to list all prompts.",
};

var promptsCommand = new Command("prompts",
    "List available workflow prompts or show the full text of a specific prompt. " +
    "Prompts guide AI agents through common CodeCompress workflows.")
{
    promptsNameOption,
};

promptsCommand.SetAction(parseResult =>
{
    var name = parseResult.GetValue(promptsNameOption);
    var json = parseResult.GetValue(jsonOption);

    if (name is null)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(CliPrompts.All.Select(p => new { p.Name, p.Description }), jsonSerializerOptions));
        }
        else
        {
            Console.WriteLine("Available prompts (use --name <name> to view full text):\n");
            foreach (var p in CliPrompts.All)
            {
                Console.WriteLine($"  {p.Name,-20}  {p.Description}");
            }
        }

        return;
    }

    var prompt = CliPrompts.All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    if (prompt is null)
    {
        Environment.ExitCode = 1;
        var validNames = string.Join(", ", CliPrompts.All.Select(p => p.Name));
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new { Error = "Prompt not found", Code = "PROMPT_NOT_FOUND", ValidNames = validNames },
                jsonSerializerOptions));
        }
        else
        {
            Console.Error.WriteLine($"Error: Prompt not found. Valid names: {validNames}");
        }

        return;
    }

    if (json)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { prompt.Name, prompt.Description, prompt.Text }, jsonSerializerOptions));
    }
    else
    {
        Console.WriteLine($"# {prompt.Name}");
        Console.WriteLine($"# {prompt.Description}");
        Console.WriteLine();
        Console.WriteLine(prompt.Text);
    }
});

rootCommand.Subcommands.Add(promptsCommand);

// ── agent-instructions ──────────────────────────────────────

var agentInstructionsCommand = new Command("agent-instructions",
    "Output a block of text optimized for pasting into CLAUDE.md, system prompts, or agent configuration files. " +
    "Tells AI agents how to use the CodeCompress CLI for code discovery.");

agentInstructionsCommand.SetAction(_ =>
{
    Console.WriteLine("""
        # CodeCompress CLI — Agent Instructions

        CodeCompress is a code intelligence CLI that provides compressed, symbol-level access
        to indexed codebases. Use it as your PRIMARY tool for code discovery instead of reading
        raw files — it saves 80-90% tokens.

        ## Installation

        ```bash
        dotnet tool install -g CodeCompress
        ```

        ## Access Boundary

        The CLI (and the MCP server) are scoped to a boundary root — the directory from which the
        server was launched, or the value of the `CODECOMPRESS_ROOT` environment variable. All
        `--path` arguments must resolve within that boundary. Out-of-bounds paths are rejected with
        INVALID_PATH. Use `CODECOMPRESS_ALLOWED_ROOTS` (OS path-separator-delimited) to add trusted
        roots for multi-repo workflows.

        ## Workflow

        1. `codecompress index --path <project-root>` — MUST be called first. Builds/updates the
           symbol database. Incremental — only changed files are re-parsed.
        2. `codecompress assemble --path <project-root> --query <term> [--budget <tokens>]` — One-shot:
           search symbols, retrieve source, and include a file overview in a single call. Collapses
           5-10 round-trips into 1. Use as the default starting point for task-specific context.
        3. `codecompress outline --path <project-root>` — Full codebase overview (symbols grouped by
           file). Add `--path-filter src/` to scope to a subdirectory.
        4. `codecompress topic-outline --path <project-root> --topic <term>` — Search for a topic
           and return matching symbols in outline format. Good for thematic exploration.
        5. `codecompress search --path <project-root> --query <term>` — FTS5 symbol search. Faster
           than grep. Auto-retries with contains-match on zero results.
        6. `codecompress search-text --path <project-root> --query <term>` — Search raw file contents
           for string literals, comments, config values, or non-symbol patterns.
        7. `codecompress get-symbol --path <project-root> --name <Name>` — Retrieve exact source code
           by symbol name. Accepts unqualified names (auto-resolved) or Parent:Child format.
        8. `codecompress expand-symbol --path <project-root> --name <Parent:Method>` — Extract a single
           method without loading the parent class (~60% fewer tokens than get-symbol on parent).
        9. `codecompress get-hot-path --path <project-root> --name <Name> --identifiers <id1,id2>` —
           Return only lines in a symbol that contain specific identifiers plus surrounding context.
           10-40x fewer tokens than get-symbol. Add `--context-lines N` (0-10, default 3).
        10. `codecompress get-symbols --path <project-root> --names <N1,N2,N3>` — Batch retrieve up
            to 50 symbols in one call. Far more efficient than repeated get-symbol.
        11. `codecompress get-module-api --path <project-root> --module <rel-path>` — Public API
            surface of a single file — signatures, visibility, and dependencies.
        12. `codecompress find-references --path <project-root> --name <Name>` — All locations where
            a symbol is referenced across the codebase.
        13. `codecompress deps --path <project-root>` — File-level import/dependency relationships.
            Add `--edge-kind imports|calls|implements|inherits|references` to filter by edge type.
        14. `codecompress blast-radius --path <project-root> --file <rel-path>` — Reverse BFS: all
            files that would break if the given file changes. Use `--symbol <name>` for symbol input.
        15. `codecompress project-deps --path <project-root>` — Inter-project dependencies in .NET
            solutions. Shows which projects reference which.
        16. `codecompress unused-symbols --path <project-root>` — Best-effort dead code detection.
            Returns public symbols with no incoming dependency edges.
        17. `codecompress file-tree --path <project-root>` — Annotated directory tree with file and
            line counts. Does NOT require `index` to be run first.
        18. `codecompress snapshot --path <project-root> --label <name>` — Create a named baseline
            of the current index state for change tracking.
        19. `codecompress changes --path <project-root> --label <name>` — Symbol-level diff since a
            named snapshot: new, modified, and deleted symbols.
        20. `codecompress list` — List all projects in the global registry with file/symbol counts
            and last-indexed times. No `--path` required.
        21. `codecompress invalidate-cache --path <project-root>` — Delete all indexed data for a
            project, forcing a full re-parse on the next `index` call.

        ## JSON Output (--json)

        Add `--json` to any command for machine-readable output (snake_case keys, indented).

        Key response shapes (identical field names to the MCP server's structuredContent —
        both surfaces share the same CodeCompress.Core.Contracts record types):
        - index: {repo_id, project_root, files_indexed, files_unchanged, files_errored, total_files,
          symbols_found, duration_ms, parse_errors: [{file_path, reason}] | null}
        - get-symbol / expand-symbol: {name, kind, parent, file, line_start, line_end, signature,
          source_code} (expand-symbol also includes doc_comment)
        - get-symbols: {results: [{name, kind, parent, file, line_start, line_end, signature,
          source_code}], errors: [{symbol, error, code, retryable}]}
        - search: {query, total_matches, fallback_used, results: [{name, kind, parent, file, line,
          signature, snippet, rank}]}
        - search-text: {query, total_matches, results: [{file_path, snippet, rank}]}
        - find-references: {symbol, total_matches, results: [{file, line, context_snippet, rank}]}
        - get-module-api: {module, symbols: [{name, kind, parent, signature, line, doc_comment}],
          dependencies: [{requires_path, alias}]}
        - get-hot-path: {symbol, file, total_lines, returned_lines,
          matches: [{identifier, line, context: [{line_number, text}]}]}
        - blast-radius: {total_affected, depths: [{depth, files}]}
        - unused-symbols: {results: [{name, kind, signature}]}
        - list: {repos: [{project_root, display_name, file_count, symbol_count, last_indexed, status}]}
        - snapshot: {snapshot_id, label, file_count, symbol_count}
        - invalidate-cache: {success, message}

        ## Error Handling

        Errors set exit code 1. With --json, errors output structured JSON to stdout:
        `{error: "message", code: "ERROR_CODE", retryable: false}`

        Error codes: INVALID_PATH, SYMBOL_NOT_FOUND, DIRECTORY_NOT_FOUND, MODULE_NOT_FOUND,
        SNAPSHOT_NOT_FOUND, EMPTY_QUERY, EMPTY_SYMBOL_NAMES, SYMBOL_LIMIT_EXCEEDED, NO_PROJECTS,
        EMPTY_IDENTIFIERS, FILE_NOT_FOUND.

        All current errors are permanent (retryable: false) — fix the input rather than retrying.

        ## Performance Tips

        - Start with `assemble` — it collapses search + retrieval into one call.
        - Use `get-symbols` for batches — single call vs N separate get-symbol calls.
        - Use `expand-symbol` for one method in a large class — ~60% fewer tokens.
        - Use `get-hot-path` to trace a specific variable or condition — 10-40x fewer tokens.
        - Use `search` (not search-text) for finding classes/functions — structured results.
        - Use `outline --path-filter src/` to scope — faster than full outline + client filtering.
        - Symbol names accept unqualified names (e.g., 'MyMethod') — auto-resolved if unique.

        ## Parameter Constraints

        - --limit: 1-100 (default 20), clamped. Applies to: search, search-text, find-references.
        - --max-symbols: 1-5000 (default 500), clamped. Applies to: outline.
        - --max-results: 1-200 (default 50), clamped. Applies to: topic-outline.
        - --depth: 1-50 (default 3), clamped. Applies to: deps.
        - --depth: 1-20 (default 5), clamped. Applies to: file-tree.
        - --group-by: 'file' (default), 'kind', 'directory'. Other values rejected.
        - --direction: 'dependencies', 'dependents', 'both' (default). Other values rejected.
        - --names: max 50 comma-separated. Applies to: get-symbols.
        - --context-lines: 0-10 (default 3), clamped. Applies to: get-hot-path.
        - --identifiers: comma-separated non-empty identifiers, required. Applies to: get-hot-path.

        ## General Tips

        - Run `codecompress <command> --help` for full option details.
        - The index persists at `~/.code-compress/index.db` (global) — shared with the MCP server.
        - PREFER these commands over raw file reading. They are faster, more precise, and dramatically
          reduce token consumption.
        """);
});

rootCommand.Subcommands.Add(agentInstructionsCommand);

// ── Execute ─────────────────────────────────────────────────

return await rootCommand.Parse(args).InvokeAsync().ConfigureAwait(false);

// ── Helpers ─────────────────────────────────────────────────

static async Task WriteHintAsync(string hint, bool isJson)
{
    if (!isJson)
    {
        await Console.Error.WriteLineAsync($"Hint: {hint}").ConfigureAwait(false);
    }
}

static async Task<CliProjectScope> CreateProjectScopeAsync(string path, ServiceProvider serviceProvider)
{
    var pathValidator = serviceProvider.GetRequiredService<IPathValidator>();
    var rootResolver = serviceProvider.GetRequiredService<IProjectRootResolver>();

    // Resolve to nearest git root (or fall back to given path)
    var resolvedRoot = rootResolver.ResolveProjectRoot(path);
    var validatedPath = pathValidator.ValidatePath(resolvedRoot, resolvedRoot);

    var connectionFactory = serviceProvider.GetRequiredService<IConnectionFactory>();
    var connection = await connectionFactory.CreateConnectionAsync(validatedPath).ConfigureAwait(false);

    var store = new SqliteSymbolStore(connection);
    var repoId = IndexEngine.ComputeRepoId(validatedPath);

    var engine = new IndexEngine(
        serviceProvider.GetRequiredService<IFileHasher>(),
        serviceProvider.GetRequiredService<IChangeTracker>(),
        serviceProvider.GetRequiredService<IEnumerable<CodeCompress.Core.Parsers.ILanguageParser>>(),
        store,
        pathValidator,
        serviceProvider.GetRequiredService<IGitIgnoreFilter>(),
        serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<IndexEngine>());

    return new CliProjectScope(connection, store, engine, repoId, validatedPath);
}

static async Task WriteErrorAsync(string error, string code, bool isJson, JsonSerializerOptions jsonOptions, string? guidance = null)
{
    Environment.ExitCode = 1;
    if (isJson)
    {
        var errorObj = guidance is null
            ? new { Error = error, Code = code, Retryable = false }
            : (object)new { Error = error, Code = code, Retryable = false, Guidance = guidance };
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(errorObj, jsonOptions)).ConfigureAwait(false);
    }
    else
    {
        await Console.Error.WriteLineAsync($"Error: {error}").ConfigureAwait(false);
        if (guidance is not null)
        {
            await Console.Error.WriteLineAsync($"  Hint: {guidance}").ConfigureAwait(false);
        }
    }
}

static List<(string Identifier, int Line, List<(int LineNumber, string Text)> Context)> BuildHotPathMatchObjects(
    List<(string Identifier, int LineNumber)> rawMatches,
    string[] rawLines,
    CodeCompress.Core.Models.Symbol symbol,
    int scanLineStart,
    int scanLineEnd,
    int contextLines)
{
    var windowedMatches = rawMatches
        .OrderBy(m => m.LineNumber)
        .ThenBy(m => m.Identifier, StringComparer.Ordinal)
        .Select(m => (
            m.Identifier,
            m.LineNumber,
            WinStart: Math.Max(scanLineStart, m.LineNumber - contextLines),
            WinEnd: Math.Min(scanLineEnd, m.LineNumber + contextLines)))
        .ToList();

    var mergedWindows = new List<(int Start, int End)>();
    foreach (var (_, _, winStart, winEnd) in windowedMatches.OrderBy(m => m.WinStart))
    {
        if (mergedWindows.Count == 0 || winStart > mergedWindows[^1].End)
            mergedWindows.Add((winStart, winEnd));
        else
        {
            var last = mergedWindows[^1];
            mergedWindows[^1] = (last.Start, Math.Max(last.End, winEnd));
        }
    }

    var assignedWindows = new HashSet<int>();
    var result = new List<(string Identifier, int Line, List<(int, string)> Context)>();

    foreach (var (identifier, lineNumber, _, _) in windowedMatches)
    {
        var windowIdx = mergedWindows.FindIndex(w => w.Start <= lineNumber && lineNumber <= w.End);
        List<(int, string)> context;
        if (windowIdx >= 0 && assignedWindows.Add(windowIdx))
        {
            var (mStart, mEnd) = mergedWindows[windowIdx];
            context = [];
            for (var ln = mStart; ln <= mEnd; ln++)
            {
                var idx = ln - symbol.LineStart;
                if (idx >= 0 && idx < rawLines.Length)
                    context.Add((ln, rawLines[idx].TrimEnd('\r')));
            }
        }
        else
        {
            context = [];
        }

        result.Add((identifier, lineNumber, context));
    }

    return result;
}

static string SanitizeSymbolName(string name)
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

static async Task<string> ReadSourceCodeAsync(string filePath, int byteOffset, int byteLength)
{
    var stream = new FileStream(
        filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
        bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
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

static async Task PrintDirectoryTreeAsync(string rootPath, string currentPath, int maxDepth, int currentDepth)
{
    if (currentDepth >= maxDepth)
    {
        return;
    }

    var indent = new string(' ', currentDepth * 2);
    var excludedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", "bin", "obj", ".vs", ".idea", "Packages", "__pycache__",
    };

    try
    {
        foreach (var dir in Directory.GetDirectories(currentPath).Order())
        {
            var dirName = Path.GetFileName(dir);
            if (excludedDirs.Contains(dirName))
            {
                continue;
            }

            var fileCount = Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length;
            Console.WriteLine($"{indent}{dirName}/ ({fileCount} files)");
            await PrintDirectoryTreeAsync(rootPath, dir, maxDepth, currentDepth + 1).ConfigureAwait(false);
        }

        foreach (var file in Directory.GetFiles(currentPath).Order())
        {
            Console.WriteLine($"{indent}{Path.GetFileName(file)}");
        }
    }
    catch (UnauthorizedAccessException)
    {
        // Skip inaccessible directories
    }
}

static string FormatRelativeTime(TimeSpan elapsed)
{
    if (elapsed.TotalSeconds < 60)
    {
        return "just now";
    }

    if (elapsed.TotalMinutes < 60)
    {
        var minutes = (int)elapsed.TotalMinutes;
        return $"{minutes} minute{(minutes == 1 ? "" : "s")} ago";
    }

    if (elapsed.TotalHours < 24)
    {
        var hours = (int)elapsed.TotalHours;
        return $"{hours} hour{(hours == 1 ? "" : "s")} ago";
    }

    if (elapsed.TotalDays < 2)
    {
        return "yesterday";
    }

    var days = (int)elapsed.TotalDays;
    return $"{days} days ago";
}

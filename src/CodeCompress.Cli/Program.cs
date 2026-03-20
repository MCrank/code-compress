using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CodeCompress.Cli;
using CodeCompress.Core;
using CodeCompress.Core.Indexing;
using CodeCompress.Core.Models;
using CodeCompress.Core.Storage;
using CodeCompress.Core.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// ── DI Setup ────────────────────────────────────────────────

var services = new ServiceCollection();
services.AddCodeCompressCore();
services.AddSingleton<IConnectionFactory, SqliteConnectionFactory>();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));

using var provider = services.BuildServiceProvider();

var jsonSerializerOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    WriteIndented = true,
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
        var result = await scope.Engine.IndexProjectAsync(
            scope.ProjectRoot,
            language,
            cancellationToken: CancellationToken.None).ConfigureAwait(false);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new { result.RepoId, ProjectRoot = scope.ProjectRoot, result.FilesIndexed, result.FilesUnchanged, result.FilesErrored, result.TotalFiles, result.SymbolsFound, result.DurationMs },
                jsonSerializerOptions));
        }
        else
        {
            Console.WriteLine($"Project root: {scope.ProjectRoot}");
            var errorSuffix = result.FilesErrored > 0 ? $", {result.FilesErrored} failed" : "";
            Console.WriteLine($"Indexed {result.FilesIndexed} files, {result.FilesUnchanged} unchanged{errorSuffix}, {result.SymbolsFound} symbols in {result.DurationMs}ms");

            if (result.ParseFailures is { Count: > 0 })
            {
                Console.WriteLine($"  Parse failures (see .code-compress/ log for details):");
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
                var qualifiedNames = candidates.Select(c => c.ParentSymbol is not null ? $"{c.ParentSymbol}:{c.Name}" : c.Name);
                await WriteErrorAsync("Multiple symbols match this name", "SYMBOL_NOT_FOUND", json, jsonSerializerOptions,
                    $"Candidates: {string.Join(", ", qualifiedNames)}").ConfigureAwait(false);
                return;
            }
            else
            {
                await WriteErrorAsync("Symbol not found", "SYMBOL_NOT_FOUND", json, jsonSerializerOptions,
                    "Use 'codecompress search --path <path> --query <name>' to discover symbol names. If the symbol was recently added or changed, re-run 'codecompress index' to update the index.").ConfigureAwait(false);
                return;
            }
        }

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(symbol, jsonSerializerOptions));
        }
        else
        {
            Console.WriteLine($"// {symbol.Name} ({symbol.Kind}, {symbol.Visibility})");
            Console.WriteLine($"// Line {symbol.LineStart}-{symbol.LineEnd}");
            Console.WriteLine(symbol.Signature);
        }
    }
});

rootCommand.Subcommands.Add(getSymbolCommand);

// ── search ──────────────────────────────────────────────────

var searchPathOption = CreatePathOption();
var searchQueryOption = new Option<string>("--query")
{
    Description = "FTS5 search query (supports AND, OR, NOT, prefix*, *contains*)",
    Required = true,
};
var searchKindOption = new Option<string?>("--kind") { Description = "Filter by symbol kind (function, method, class, record, enum, type, interface, export, constant, module)" };
var searchPathFilterOption = new Option<string?>("--path-filter") { Description = "Filter to files under this directory (e.g., 'src/')" };
var searchLimitOption = new Option<int>("--limit") { Description = "Maximum results to return (1-100, default 20). Values outside range are clamped.", DefaultValueFactory = _ => 20 };

var searchCommand = new Command("search",
    "Search the symbol index using FTS5 full-text search. " +
    "Faster and more precise than grep. Requires index.")
{
    searchPathOption,
    searchQueryOption,
    searchKindOption,
    searchPathFilterOption,
    searchLimitOption,
};

searchCommand.SetAction(async parseResult =>
{
    var path = parseResult.GetValue(searchPathOption)!;
    var query = parseResult.GetValue(searchQueryOption)!;
    var kind = parseResult.GetValue(searchKindOption);
    var pathFilter = parseResult.GetValue(searchPathFilterOption);
    var limit = Math.Clamp(parseResult.GetValue(searchLimitOption), 1, 100);
    var json = parseResult.GetValue(jsonOption);

    var scope = await CreateProjectScopeAsync(path, provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        var results = await scope.Store.SearchSymbolsAsync(scope.RepoId, query, kind, limit, pathFilter).ConfigureAwait(false);

        // Auto contains-match fallback for plain terms with zero results
        var fallbackUsed = false;
        if (results.Count == 0 && GlobPattern.IsPlainTerm(query))
        {
            var containsGlob = Fts5QuerySanitizer.SanitizeAsGlob($"*{query}*");
            results = await scope.Store.SearchSymbolsAsync(
                scope.RepoId, containsGlob.Fts5Query, kind, limit, pathFilter, containsGlob.SqlLikePattern).ConfigureAwait(false);
            fallbackUsed = results.Count > 0;
        }

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(results, jsonSerializerOptions));
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
            Console.WriteLine(JsonSerializer.Serialize(results, jsonSerializerOptions));
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
        var snapshotRecord = new IndexSnapshot(
            0, scope.RepoId, label, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), string.Empty);

        var snapshotId = await scope.Store.CreateSnapshotAsync(snapshotRecord).ConfigureAwait(false);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new { SnapshotId = snapshotId, Label = label },
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

var depsCommand = new Command("deps",
    "Show the import/require dependency graph. " +
    "Shows which files depend on which others. Requires index.")
{
    depsPathOption,
    depsFileOption,
    depsDirectionOption,
    depsDepthOption,
};

depsCommand.SetAction(async parseResult =>
{
    var path = parseResult.GetValue(depsPathOption)!;
    var rootFile = parseResult.GetValue(depsFileOption) is { } rf ? PathValidator.NormalizeRelativePath(rf) : null;
    var direction = parseResult.GetValue(depsDirectionOption)!;
    var depth = Math.Clamp(parseResult.GetValue(depsDepthOption), 1, 50);
    var json = parseResult.GetValue(jsonOption);

    var scope = await CreateProjectScopeAsync(path, provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        var graph = await scope.Store.GetDependencyGraphAsync(scope.RepoId, rootFile, direction, depth).ConfigureAwait(false);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(graph, jsonSerializerOptions));
        }
        else
        {
            Console.WriteLine($"Dependency graph ({graph.Nodes.Count} nodes, {graph.Edges.Count} edges):");
            foreach (var edge in graph.Edges)
            {
                Console.WriteLine($"  {edge.From} → {edge.To}" + (edge.Alias is not null ? $" (alias: {edge.Alias})" : ""));
            }
        }
    }
});

rootCommand.Subcommands.Add(depsCommand);

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

    var scope = await CreateProjectScopeAsync(path, provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        var files = await scope.Store.GetFilesByRepoAsync(scope.RepoId).ConfigureAwait(false);
        var fileIds = files.Select(f => f.Id).ToList();

        foreach (var fileId in fileIds)
        {
            await scope.Store.DeleteSymbolsByFileAsync(fileId).ConfigureAwait(false);
            await scope.Store.DeleteDependenciesByFileAsync(fileId).ConfigureAwait(false);
            await scope.Store.DeleteFileAsync(fileId).ConfigureAwait(false);
        }

        await scope.Store.DeleteRepositoryAsync(scope.RepoId).ConfigureAwait(false);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new { Status = "invalidated", Path = path },
                jsonSerializerOptions));
        }
        else
        {
            Console.WriteLine("Index invalidated. Next index command will perform a full reparse.");
        }
    }
});

rootCommand.Subcommands.Add(invalidateCacheCommand);

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
                new
                {
                    Module = moduleApi.File.RelativePath,
                    Symbols = moduleApi.Symbols.Select(s => new
                    {
                        s.Name, s.Kind, Parent = s.ParentSymbol, s.Signature, Line = s.LineStart, s.DocComment,
                    }),
                    Dependencies = moduleApi.Dependencies.Select(d => new { d.RequiresPath, d.Alias }),
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
            // Fuzzy resolution: try matching by unqualified name
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
        var resolvedPath = pathValidator.ValidatePath(
            Path.Combine(path, file.RelativePath), path);

        var sourceCode = await ReadSourceCodeAsync(resolvedPath, symbol.ByteOffset, symbol.ByteLength).ConfigureAwait(false);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new
                {
                    symbol.Name, symbol.Kind, Parent = symbol.ParentSymbol,
                    File = file.RelativePath, symbol.LineStart, symbol.LineEnd,
                    symbol.Signature, symbol.DocComment, SourceCode = sourceCode,
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
        var results = new List<object>();

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
            results.Add(new
            {
                s.Name, s.Kind, Parent = s.ParentSymbol,
                File = file.RelativePath, s.LineStart, s.LineEnd,
                s.Signature, SourceCode = sourceCode,
            });
        }

        var errors = symbolNames
            .Where(n => !foundNames.Contains(n))
            .Select(n => new { Symbol = n, Error = "Symbol not found" })
            .ToList();

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new { Results = results, Errors = errors },
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
            Console.WriteLine(JsonSerializer.Serialize(results, jsonSerializerOptions));
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

        ## Workflow

        1. `codecompress index --path <project-root>` — MUST be called first. Builds/updates the
           symbol database. Incremental — only changed files are re-parsed.
        2. `codecompress outline --path <project-root>` — Get a compressed overview of the entire
           codebase (symbols grouped by file). Use --path-filter to scope to a subdirectory.
        3. `codecompress search --path <project-root> --query <term>` — Find specific symbols using
           FTS5 full-text search. Faster than grep.
        4. `codecompress get-symbol --path <project-root> --name <Name>` — Retrieve exact source
           code by symbol name. Accepts unqualified names (auto-resolved) or Parent:Child format.
        5. `codecompress expand-symbol --path <project-root> --name <Parent:Method>` — Get a single
           method without loading the parent class (~60% fewer tokens than get-symbol on parent).
        6. `codecompress get-symbols --path <project-root> --names <N1,N2,N3>` — Batch retrieve
           multiple symbols in one call (max 50). Far more efficient than repeated get-symbol.
        7. `codecompress search-text --path <project-root> --query <term>` — Search raw file contents
           for string literals, comments, or non-symbol patterns.
        8. `codecompress deps --path <project-root>` — Understand import/dependency relationships.
        9. `codecompress file-tree --path <project-root>` — Quick directory structure (no index required).

        ## JSON Output (--json)

        Add `--json` to any command for machine-readable output (snake_case keys, indented).

        Key response shapes:
        - index: {repo_id, project_root, files_indexed, files_unchanged, symbols_found, duration_ms}
        - search: [{name, kind, parent, file, line, signature, snippet, rank}]
        - get-symbol: {id, file_id, name, kind, signature, parent_symbol, line_start, line_end, ...}
        - search-text: [{file_path, snippet, rank}]
        - find-references: [{file_path, line, context_snippet, rank}]

        ## Error Handling

        Errors set exit code 1. With --json, errors output structured JSON to stdout:
        `{error: "message", code: "ERROR_CODE", retryable: false}`

        Error codes: INVALID_PATH, SYMBOL_NOT_FOUND, DIRECTORY_NOT_FOUND, MODULE_NOT_FOUND,
        SNAPSHOT_NOT_FOUND, EMPTY_QUERY, EMPTY_SYMBOL_NAMES, SYMBOL_LIMIT_EXCEEDED, NO_PROJECTS.

        All current errors are permanent (retryable: false) — fix the input rather than retrying.

        ## Performance Tips

        - Use `get-symbols` for batches — single call vs N separate get-symbol calls.
        - Use `expand-symbol` for one method in a large class — ~60% fewer tokens.
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

        ## General Tips

        - Run `codecompress <command> --help` for full option details.
        - The index persists at `<project-root>/.code-compress/index.db` — shared with the MCP server.
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

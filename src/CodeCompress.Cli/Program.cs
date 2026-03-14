using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
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
                new { result.RepoId, result.FilesIndexed, result.FilesSkipped, result.SymbolsFound, result.DurationMs },
                jsonSerializerOptions));
        }
        else
        {
            Console.WriteLine($"Indexed {result.FilesIndexed} files ({result.FilesSkipped} skipped), {result.SymbolsFound} symbols in {result.DurationMs}ms");
        }
    }
});

rootCommand.Subcommands.Add(indexCommand);

// ── outline ─────────────────────────────────────────────────

var outlinePathOption = CreatePathOption();

var outlineCommand = new Command("outline",
    "Show a compressed project outline with symbol signatures. " +
    "Far more efficient than reading files individually. Requires index.")
{
    outlinePathOption,
};

outlineCommand.SetAction(async parseResult =>
{
    var path = parseResult.GetValue(outlinePathOption)!;
    var json = parseResult.GetValue(jsonOption);

    var scope = await CreateProjectScopeAsync(path, provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        var outline = await scope.Store.GetProjectOutlineAsync(scope.RepoId, false, "file", 3).ConfigureAwait(false);

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
        }
    }
});

rootCommand.Subcommands.Add(outlineCommand);

// ── get-symbol ──────────────────────────────────────────────

var getSymbolPathOption = CreatePathOption();
var getSymbolNameOption = new Option<string>("--name")
{
    Description = "Qualified symbol name (e.g., CombatService:ProcessAttack)",
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
            await Console.Error.WriteLineAsync($"Error: Symbol not found: {name}").ConfigureAwait(false);
            await Console.Error.WriteLineAsync("  Hint: Use 'codecompress search --path <path> --query <name>' to discover symbol names.").ConfigureAwait(false);
            Environment.ExitCode = 1;
            return;
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

var searchCommand = new Command("search",
    "Search the symbol index using FTS5 full-text search. " +
    "Faster and more precise than grep. Requires index.")
{
    searchPathOption,
    searchQueryOption,
};

searchCommand.SetAction(async parseResult =>
{
    var path = parseResult.GetValue(searchPathOption)!;
    var query = parseResult.GetValue(searchQueryOption)!;
    var json = parseResult.GetValue(jsonOption);

    var scope = await CreateProjectScopeAsync(path, provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        var results = await scope.Store.SearchSymbolsAsync(scope.RepoId, query, null, 50).ConfigureAwait(false);

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

            Console.WriteLine($"Found {results.Count} symbol(s):");
            foreach (var r in results)
            {
                Console.WriteLine($"  {r.Symbol.Kind,-12} {r.Symbol.Name,-30} {r.FilePath}:{r.Symbol.LineStart}");
            }
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

var searchTextCommand = new Command("search-text",
    "Search raw file contents using FTS5 full-text search. " +
    "Use for string literals, comments, or non-symbol patterns. Requires index.")
{
    searchTextPathOption,
    searchTextQueryOption,
};

searchTextCommand.SetAction(async parseResult =>
{
    var path = parseResult.GetValue(searchTextPathOption)!;
    var query = parseResult.GetValue(searchTextQueryOption)!;
    var json = parseResult.GetValue(jsonOption);

    var scope = await CreateProjectScopeAsync(path, provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        var results = await scope.Store.SearchTextAsync(scope.RepoId, query, null, 50).ConfigureAwait(false);

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
            await Console.Error.WriteLineAsync($"Error: Snapshot not found: {label}").ConfigureAwait(false);
            Environment.ExitCode = 1;
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
    }
});

rootCommand.Subcommands.Add(snapshotCommand);

// ── file-tree ───────────────────────────────────────────────

var fileTreePathOption = CreatePathOption();
var fileTreeDepthOption = new Option<int>("--depth")
{
    Description = "Maximum directory depth (1-20)",
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

    var pathValidator = provider.GetRequiredService<IPathValidator>();
    var validatedPath = pathValidator.ValidatePath(path, path);

    if (!Directory.Exists(validatedPath))
    {
        await Console.Error.WriteLineAsync("Error: Directory not found.").ConfigureAwait(false);
        Environment.ExitCode = 1;
        return;
    }

    await PrintDirectoryTreeAsync(validatedPath, validatedPath, maxDepth, 0).ConfigureAwait(false);
});

rootCommand.Subcommands.Add(fileTreeCommand);

// ── deps ────────────────────────────────────────────────────

var depsPathOption = CreatePathOption();
var depsFileOption = new Option<string?>("--file") { Description = "Start from a specific file (relative path)" };

var depsCommand = new Command("deps",
    "Show the import/require dependency graph. " +
    "Shows which files depend on which others. Requires index.")
{
    depsPathOption,
    depsFileOption,
};

depsCommand.SetAction(async parseResult =>
{
    var path = parseResult.GetValue(depsPathOption)!;
    var rootFile = parseResult.GetValue(depsFileOption);
    var json = parseResult.GetValue(jsonOption);

    var scope = await CreateProjectScopeAsync(path, provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        var graph = await scope.Store.GetDependencyGraphAsync(scope.RepoId, rootFile, "both", 3).ConfigureAwait(false);

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
           codebase (symbols grouped by file).
        3. `codecompress search --path <project-root> --query <term>` — Find specific symbols using
           FTS5 full-text search. Faster than grep.
        4. `codecompress get-symbol --path <project-root> --name <QualifiedName>` — Retrieve exact
           source code by symbol name. Loads only the symbol, not the whole file.
        5. `codecompress search-text --path <project-root> --query <term>` — Search raw file contents
           for string literals, comments, or non-symbol patterns.
        6. `codecompress deps --path <project-root>` — Understand import/dependency relationships.
        7. `codecompress file-tree --path <project-root>` — Quick directory structure overview
           (no index required).

        ## Tips

        - Add `--json` to any command for machine-readable JSON output (snake_case keys).
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

static async Task<CliProjectScope> CreateProjectScopeAsync(string path, ServiceProvider serviceProvider)
{
    var pathValidator = serviceProvider.GetRequiredService<IPathValidator>();
    var validatedPath = pathValidator.ValidatePath(path, path);

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

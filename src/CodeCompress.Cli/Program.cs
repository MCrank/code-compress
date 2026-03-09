using System.Text.Json;
using CodeCompress.Cli;
using CodeCompress.Core;
using CodeCompress.Core.Indexing;
using CodeCompress.Core.Storage;
using CodeCompress.Core.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var services = new ServiceCollection();
services.AddCodeCompressCore();
services.AddSingleton<IConnectionFactory, SqliteConnectionFactory>();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));

using var provider = services.BuildServiceProvider();

if (args.Length == 0)
{
    await PrintUsageAsync().ConfigureAwait(false);
    return 1;
}

var command = args[0];
var serializerOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    WriteIndented = true,
};

try
{
    return await RunCommandAsync(command, args, provider, serializerOptions).ConfigureAwait(false);
}
catch (Exception ex) when (ex is DirectoryNotFoundException or FileNotFoundException)
{
    await Console.Error.WriteLineAsync($"Error: {ex.Message}").ConfigureAwait(false);
    return 1;
}
catch (ArgumentException ex)
{
    await Console.Error.WriteLineAsync($"Error: {ex.Message}").ConfigureAwait(false);
    return 1;
}

static async Task<int> RunCommandAsync(
    string command,
    string[] args,
    ServiceProvider provider,
    JsonSerializerOptions serializerOptions)
{
    switch (command)
    {
        case "index":
            return await IndexCommandAsync(args, provider, serializerOptions).ConfigureAwait(false);
        case "outline":
            return await OutlineCommandAsync(args, provider, serializerOptions).ConfigureAwait(false);
        case "get-symbol":
            return await GetSymbolCommandAsync(args, provider, serializerOptions).ConfigureAwait(false);
        case "search":
            return await SearchCommandAsync(args, provider, serializerOptions).ConfigureAwait(false);
        case "search-text":
            return await SearchTextCommandAsync(args, provider, serializerOptions).ConfigureAwait(false);
        case "changes":
            return await ChangesCommandAsync(args, provider).ConfigureAwait(false);
        case "snapshot":
            return await SnapshotCommandAsync(args, provider, serializerOptions).ConfigureAwait(false);
        case "file-tree":
            return await FileTreeCommandAsync(args, provider).ConfigureAwait(false);
        case "deps":
            return await DepsCommandAsync(args, provider, serializerOptions).ConfigureAwait(false);
        default:
            await Console.Error.WriteLineAsync($"Unknown command: {command}").ConfigureAwait(false);
            await PrintUsageAsync().ConfigureAwait(false);
            return 1;
    }
}

static async Task<int> IndexCommandAsync(string[] args, ServiceProvider provider, JsonSerializerOptions options)
{
    if (args.Length < 2)
    {
        await Console.Error.WriteLineAsync("Usage: codecompress index <path>").ConfigureAwait(false);
        return 1;
    }

    var scope = await CreateProjectScopeAsync(args[1], provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        var result = await scope.Engine.IndexProjectAsync(scope.ProjectRoot, cancellationToken: CancellationToken.None).ConfigureAwait(false);

        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(
            new
            {
                result.RepoId,
                result.FilesIndexed,
                result.FilesSkipped,
                result.SymbolsFound,
                result.DurationMs,
            },
            options)).ConfigureAwait(false);
        return 0;
    }
}

static async Task<int> OutlineCommandAsync(string[] args, ServiceProvider provider, JsonSerializerOptions options)
{
    if (args.Length < 2)
    {
        await Console.Error.WriteLineAsync("Usage: codecompress outline <path>").ConfigureAwait(false);
        return 1;
    }

    var scope = await CreateProjectScopeAsync(args[1], provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        var outline = await scope.Store.GetProjectOutlineAsync(scope.RepoId, false, "file", 3).ConfigureAwait(false);
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(outline, options)).ConfigureAwait(false);
        return 0;
    }
}

static async Task<int> GetSymbolCommandAsync(string[] args, ServiceProvider provider, JsonSerializerOptions options)
{
    if (args.Length < 3)
    {
        await Console.Error.WriteLineAsync("Usage: codecompress get-symbol <path> <name>").ConfigureAwait(false);
        return 1;
    }

    var scope = await CreateProjectScopeAsync(args[1], provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        var symbol = await scope.Store.GetSymbolByNameAsync(scope.RepoId, args[2]).ConfigureAwait(false);

        if (symbol is null)
        {
            await Console.Error.WriteLineAsync($"Symbol not found: {args[2]}").ConfigureAwait(false);
            return 1;
        }

        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(symbol, options)).ConfigureAwait(false);
        return 0;
    }
}

static async Task<int> SearchCommandAsync(string[] args, ServiceProvider provider, JsonSerializerOptions options)
{
    if (args.Length < 3)
    {
        await Console.Error.WriteLineAsync("Usage: codecompress search <path> <query>").ConfigureAwait(false);
        return 1;
    }

    var scope = await CreateProjectScopeAsync(args[1], provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        var results = await scope.Store.SearchSymbolsAsync(scope.RepoId, args[2], null, 50).ConfigureAwait(false);
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(results, options)).ConfigureAwait(false);
        return 0;
    }
}

static async Task<int> SearchTextCommandAsync(string[] args, ServiceProvider provider, JsonSerializerOptions options)
{
    if (args.Length < 3)
    {
        await Console.Error.WriteLineAsync("Usage: codecompress search-text <path> <query>").ConfigureAwait(false);
        return 1;
    }

    var scope = await CreateProjectScopeAsync(args[1], provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        var results = await scope.Store.SearchTextAsync(scope.RepoId, args[2], null, 50).ConfigureAwait(false);
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(results, options)).ConfigureAwait(false);
        return 0;
    }
}

static async Task<int> ChangesCommandAsync(string[] args, ServiceProvider provider)
{
    if (args.Length < 3)
    {
        await Console.Error.WriteLineAsync("Usage: codecompress changes <path> <label>").ConfigureAwait(false);
        return 1;
    }

    var scope = await CreateProjectScopeAsync(args[1], provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        var snapshot = await scope.Store.GetSnapshotByLabelAsync(scope.RepoId, args[2]).ConfigureAwait(false);

        if (snapshot is null)
        {
            await Console.Error.WriteLineAsync($"Snapshot not found: {args[2]}").ConfigureAwait(false);
            return 1;
        }

        var changedFiles = await scope.Store.GetChangedFilesAsync(scope.RepoId, snapshot.Id).ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"Changes since snapshot \"{args[2]}\":").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"  New files: {changedFiles.Added.Count}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"  Modified files: {changedFiles.Modified.Count}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"  Deleted files: {changedFiles.Removed.Count}").ConfigureAwait(false);

        foreach (var file in changedFiles.Added)
        {
            await Console.Out.WriteLineAsync($"  + {file.RelativePath}").ConfigureAwait(false);
        }

        foreach (var file in changedFiles.Modified)
        {
            await Console.Out.WriteLineAsync($"  ~ {file.RelativePath}").ConfigureAwait(false);
        }

        foreach (var path in changedFiles.Removed)
        {
            await Console.Out.WriteLineAsync($"  - {path}").ConfigureAwait(false);
        }

        return 0;
    }
}

static async Task<int> SnapshotCommandAsync(string[] args, ServiceProvider provider, JsonSerializerOptions options)
{
    if (args.Length < 2)
    {
        await Console.Error.WriteLineAsync("Usage: codecompress snapshot <path> [label]").ConfigureAwait(false);
        return 1;
    }

    var label = args.Length >= 3 ? args[2] : DateTimeOffset.UtcNow.ToString("yyyy-MM-dd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);

    var scope = await CreateProjectScopeAsync(args[1], provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        var snapshot = new CodeCompress.Core.Models.IndexSnapshot(
            0,
            scope.RepoId,
            label,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            string.Empty);

        var snapshotId = await scope.Store.CreateSnapshotAsync(snapshot).ConfigureAwait(false);

        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(
            new { SnapshotId = snapshotId, Label = label },
            options)).ConfigureAwait(false);
        return 0;
    }
}

static async Task<int> FileTreeCommandAsync(string[] args, ServiceProvider provider)
{
    if (args.Length < 2)
    {
        await Console.Error.WriteLineAsync("Usage: codecompress file-tree <path>").ConfigureAwait(false);
        return 1;
    }

    var pathValidator = provider.GetRequiredService<IPathValidator>();
    var validatedPath = pathValidator.ValidatePath(args[1], args[1]);

    if (!Directory.Exists(validatedPath))
    {
        await Console.Error.WriteLineAsync("Directory not found").ConfigureAwait(false);
        return 1;
    }

    await PrintDirectoryTreeAsync(validatedPath, validatedPath, 5, 0).ConfigureAwait(false);
    return 0;
}

static async Task<int> DepsCommandAsync(string[] args, ServiceProvider provider, JsonSerializerOptions options)
{
    if (args.Length < 2)
    {
        await Console.Error.WriteLineAsync("Usage: codecompress deps <path> [file]").ConfigureAwait(false);
        return 1;
    }

    var rootFile = args.Length >= 3 ? args[2] : null;

    var scope = await CreateProjectScopeAsync(args[1], provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        var graph = await scope.Store.GetDependencyGraphAsync(scope.RepoId, rootFile, "both", 3).ConfigureAwait(false);
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(graph, options)).ConfigureAwait(false);
        return 0;
    }
}

static async Task<CliProjectScope> CreateProjectScopeAsync(string path, ServiceProvider provider)
{
    var pathValidator = provider.GetRequiredService<IPathValidator>();
    var validatedPath = pathValidator.ValidatePath(path, path);

    var connectionFactory = provider.GetRequiredService<IConnectionFactory>();
    var connection = await connectionFactory.CreateConnectionAsync(validatedPath).ConfigureAwait(false);

    var store = new SqliteSymbolStore(connection);
    var repoId = IndexEngine.ComputeRepoId(validatedPath);

    var engine = new IndexEngine(
        provider.GetRequiredService<IFileHasher>(),
        provider.GetRequiredService<IChangeTracker>(),
        provider.GetRequiredService<IEnumerable<CodeCompress.Core.Parsers.ILanguageParser>>(),
        store,
        pathValidator,
        provider.GetRequiredService<ILoggerFactory>().CreateLogger<IndexEngine>());

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
            await Console.Out.WriteLineAsync($"{indent}{dirName}/ ({fileCount} files)").ConfigureAwait(false);
            await PrintDirectoryTreeAsync(rootPath, dir, maxDepth, currentDepth + 1).ConfigureAwait(false);
        }

        foreach (var file in Directory.GetFiles(currentPath).Order())
        {
            await Console.Out.WriteLineAsync($"{indent}{Path.GetFileName(file)}").ConfigureAwait(false);
        }
    }
    catch (UnauthorizedAccessException)
    {
        // Skip inaccessible directories
    }
}

static async Task PrintUsageAsync()
{
    await Console.Out.WriteLineAsync("""
        CodeCompress CLI — Index and query code symbols

        Usage: codecompress <command> [arguments]

        Commands:
          index <path>                  Index a project directory
          outline <path>                Show project outline
          get-symbol <path> <name>      Retrieve a specific symbol
          search <path> <query>         Search symbols by name
          search-text <path> <query>    Full-text search in files
          changes <path> <label>        Show changes since snapshot
          snapshot <path> [label]       Create a snapshot
          file-tree <path>              Show annotated file tree
          deps <path> [file]            Show dependency graph
        """).ConfigureAwait(false);
}

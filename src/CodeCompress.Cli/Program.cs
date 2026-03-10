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

var serializerOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    WriteIndented = true,
};

try
{
    var cli = CliArgs.Parse(args);

    if (cli.Command.Length == 0)
    {
        await Console.Out.WriteLineAsync(HelpText.FormatGeneralHelp()).ConfigureAwait(false);
        return 1;
    }

    if (cli.HelpRequested)
    {
        await Console.Out.WriteLineAsync(HelpText.FormatCommandHelp(cli.Command)).ConfigureAwait(false);
        return 0;
    }

    if (!HelpText.IsKnownCommand(cli.Command))
    {
        await Console.Error.WriteLineAsync($"Unknown command: {cli.Command}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync(HelpText.FormatGeneralHelp()).ConfigureAwait(false);
        return 1;
    }

    return await RunCommandAsync(cli, provider, serializerOptions).ConfigureAwait(false);
}
catch (CliException ex)
{
    await Console.Error.WriteLineAsync($"Error: {ex.Message}").ConfigureAwait(false);
    return 1;
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
    CliArgs cli,
    ServiceProvider provider,
    JsonSerializerOptions serializerOptions)
{
    var cmd = cli.Command;
    if (string.Equals(cmd, "index", StringComparison.OrdinalIgnoreCase))
    {
        return await IndexCommandAsync(cli, provider, serializerOptions).ConfigureAwait(false);
    }

    if (string.Equals(cmd, "outline", StringComparison.OrdinalIgnoreCase))
    {
        return await OutlineCommandAsync(cli, provider, serializerOptions).ConfigureAwait(false);
    }

    if (string.Equals(cmd, "get-symbol", StringComparison.OrdinalIgnoreCase))
    {
        return await GetSymbolCommandAsync(cli, provider, serializerOptions).ConfigureAwait(false);
    }

    if (string.Equals(cmd, "search-text", StringComparison.OrdinalIgnoreCase))
    {
        return await SearchTextCommandAsync(cli, provider, serializerOptions).ConfigureAwait(false);
    }

    if (string.Equals(cmd, "search", StringComparison.OrdinalIgnoreCase))
    {
        return await SearchCommandAsync(cli, provider, serializerOptions).ConfigureAwait(false);
    }

    if (string.Equals(cmd, "changes", StringComparison.OrdinalIgnoreCase))
    {
        return await ChangesCommandAsync(cli, provider).ConfigureAwait(false);
    }

    if (string.Equals(cmd, "snapshot", StringComparison.OrdinalIgnoreCase))
    {
        return await SnapshotCommandAsync(cli, provider, serializerOptions).ConfigureAwait(false);
    }

    if (string.Equals(cmd, "file-tree", StringComparison.OrdinalIgnoreCase))
    {
        return await FileTreeCommandAsync(cli, provider).ConfigureAwait(false);
    }

    if (string.Equals(cmd, "deps", StringComparison.OrdinalIgnoreCase))
    {
        return await DepsCommandAsync(cli, provider, serializerOptions).ConfigureAwait(false);
    }

    throw new CliException($"Unknown command: {cli.Command}");
}

static async Task<int> IndexCommandAsync(CliArgs cli, ServiceProvider provider, JsonSerializerOptions options)
{
    var path = cli.RequireOption("path");
    var language = cli.GetOption("language");

    var scope = await CreateProjectScopeAsync(path, provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        var result = await scope.Engine.IndexProjectAsync(
            scope.ProjectRoot,
            language,
            cancellationToken: CancellationToken.None).ConfigureAwait(false);

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

static async Task<int> OutlineCommandAsync(CliArgs cli, ServiceProvider provider, JsonSerializerOptions options)
{
    var path = cli.RequireOption("path");

    var scope = await CreateProjectScopeAsync(path, provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        var outline = await scope.Store.GetProjectOutlineAsync(scope.RepoId, false, "file", 3).ConfigureAwait(false);
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(outline, options)).ConfigureAwait(false);
        return 0;
    }
}

static async Task<int> GetSymbolCommandAsync(CliArgs cli, ServiceProvider provider, JsonSerializerOptions options)
{
    var path = cli.RequireOption("path");
    var name = cli.RequireOption("name");

    var scope = await CreateProjectScopeAsync(path, provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        var symbol = await scope.Store.GetSymbolByNameAsync(scope.RepoId, name).ConfigureAwait(false);

        if (symbol is null)
        {
            await Console.Error.WriteLineAsync($"Symbol not found: {name}").ConfigureAwait(false);
            return 1;
        }

        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(symbol, options)).ConfigureAwait(false);
        return 0;
    }
}

static async Task<int> SearchCommandAsync(CliArgs cli, ServiceProvider provider, JsonSerializerOptions options)
{
    var path = cli.RequireOption("path");
    var query = cli.RequireOption("query");

    var scope = await CreateProjectScopeAsync(path, provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        var results = await scope.Store.SearchSymbolsAsync(scope.RepoId, query, null, 50).ConfigureAwait(false);
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(results, options)).ConfigureAwait(false);
        return 0;
    }
}

static async Task<int> SearchTextCommandAsync(CliArgs cli, ServiceProvider provider, JsonSerializerOptions options)
{
    var path = cli.RequireOption("path");
    var query = cli.RequireOption("query");

    var scope = await CreateProjectScopeAsync(path, provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        var results = await scope.Store.SearchTextAsync(scope.RepoId, query, null, 50).ConfigureAwait(false);
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(results, options)).ConfigureAwait(false);
        return 0;
    }
}

static async Task<int> ChangesCommandAsync(CliArgs cli, ServiceProvider provider)
{
    var path = cli.RequireOption("path");
    var label = cli.RequireOption("label");

    var scope = await CreateProjectScopeAsync(path, provider).ConfigureAwait(false);
    await using (scope.ConfigureAwait(false))
    {
        var snapshot = await scope.Store.GetSnapshotByLabelAsync(scope.RepoId, label).ConfigureAwait(false);

        if (snapshot is null)
        {
            await Console.Error.WriteLineAsync($"Snapshot not found: {label}").ConfigureAwait(false);
            return 1;
        }

        var changedFiles = await scope.Store.GetChangedFilesAsync(scope.RepoId, snapshot.Id).ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"Changes since snapshot \"{label}\":").ConfigureAwait(false);
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

        foreach (var filePath in changedFiles.Removed)
        {
            await Console.Out.WriteLineAsync($"  - {filePath}").ConfigureAwait(false);
        }

        return 0;
    }
}

static async Task<int> SnapshotCommandAsync(CliArgs cli, ServiceProvider provider, JsonSerializerOptions options)
{
    var path = cli.RequireOption("path");
    var label = cli.GetOption("label")
        ?? DateTimeOffset.UtcNow.ToString("yyyy-MM-dd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);

    var scope = await CreateProjectScopeAsync(path, provider).ConfigureAwait(false);
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

static async Task<int> FileTreeCommandAsync(CliArgs cli, ServiceProvider provider)
{
    var path = cli.RequireOption("path");
    var depthStr = cli.GetOption("depth");
    var maxDepth = depthStr is not null && int.TryParse(depthStr, out var d) ? Math.Clamp(d, 1, 20) : 5;

    var pathValidator = provider.GetRequiredService<IPathValidator>();
    var validatedPath = pathValidator.ValidatePath(path, path);

    if (!Directory.Exists(validatedPath))
    {
        await Console.Error.WriteLineAsync("Directory not found").ConfigureAwait(false);
        return 1;
    }

    await PrintDirectoryTreeAsync(validatedPath, validatedPath, maxDepth, 0).ConfigureAwait(false);
    return 0;
}

static async Task<int> DepsCommandAsync(CliArgs cli, ServiceProvider provider, JsonSerializerOptions options)
{
    var path = cli.RequireOption("path");
    var rootFile = cli.GetOption("file");

    var scope = await CreateProjectScopeAsync(path, provider).ConfigureAwait(false);
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

using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodeCompress.Core.Validation;
using CodeCompress.Server.Scoping;
using ModelContextProtocol.Server;

namespace CodeCompress.Server.Tools;

[McpServerToolType]
internal sealed partial class IndexingTools
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly IPathValidator _pathValidator;
    private readonly IProjectScopeFactory _scopeFactory;

    public IndexingTools(IPathValidator pathValidator, IProjectScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(pathValidator);
        ArgumentNullException.ThrowIfNull(scopeFactory);

        _pathValidator = pathValidator;
        _scopeFactory = scopeFactory;
    }

    [McpServerTool(Name = "index_project")]
    [Description("Index a codebase to build a searchable symbol database. Must be called before any query tools. Re-running performs an incremental update — only changed files are re-parsed.")]
    public async Task<string> IndexProject(
        [Description("ABSOLUTE path to the project root directory — the same root used with index_project (e.g., 'C:\\Projects\\MyGame' or '/home/user/my-project'). Must NOT be a subdirectory or relative path.")] string path,
        [Description("Filter to a specific language (e.g., 'luau')")] string? language = null,
        [Description("Glob patterns for files to include")] string[]? includePatterns = null,
        [Description("Glob patterns for files to exclude")] string[]? excludePatterns = null,
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

        try
        {
            var scope = await _scopeFactory.CreateAsync(validatedPath, cancellationToken).ConfigureAwait(false);
            await using (scope.ConfigureAwait(false))
            {
                var result = await scope.Engine.IndexProjectAsync(
                    validatedPath,
                    language,
                    includePatterns,
                    excludePatterns,
                    cancellationToken).ConfigureAwait(false);

                return JsonSerializer.Serialize(
                    new
                    {
                        result.RepoId,
                        result.FilesIndexed,
                        result.FilesSkipped,
                        result.SymbolsFound,
                        result.DurationMs,
                    },
                    SerializerOptions);
            }
        }
        catch (DirectoryNotFoundException)
        {
            return SerializeError("Directory not found", "DIRECTORY_NOT_FOUND");
        }
    }

    [McpServerTool(Name = "snapshot_create")]
    [Description("Create a named snapshot of the current index state. Use before making changes, then call changes_since to see a symbol-level diff.")]
    public async Task<string> SnapshotCreate(
        [Description("ABSOLUTE path to the project root directory — the same root used with index_project (e.g., 'C:\\Projects\\MyGame' or '/home/user/my-project'). Must NOT be a subdirectory or relative path.")] string path,
        [Description("Human-readable snapshot label")] string label,
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

        var sanitizedLabel = SanitizeLabel(label);

        var scope = await _scopeFactory.CreateAsync(validatedPath, cancellationToken).ConfigureAwait(false);
        await using (scope.ConfigureAwait(false))
        {
            var repo = await scope.Store.GetRepositoryAsync(scope.RepoId).ConfigureAwait(false);
            var fileCount = repo?.FileCount ?? 0;
            var symbolCount = repo?.SymbolCount ?? 0;

            var snapshot = new Core.Models.IndexSnapshot(
                0,
                scope.RepoId,
                sanitizedLabel,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                string.Empty);

            var snapshotId = await scope.Store.CreateSnapshotAsync(snapshot).ConfigureAwait(false);

            return JsonSerializer.Serialize(
                new
                {
                    SnapshotId = snapshotId,
                    Label = sanitizedLabel,
                    FileCount = fileCount,
                    SymbolCount = symbolCount,
                },
                SerializerOptions);
        }
    }

    [McpServerTool(Name = "invalidate_cache")]
    [Description("Delete the entire index for a project, forcing a full re-index on the next index_project call.")]
    public async Task<string> InvalidateCache(
        [Description("ABSOLUTE path to the project root directory — the same root used with index_project (e.g., 'C:\\Projects\\MyGame' or '/home/user/my-project'). Must NOT be a subdirectory or relative path.")] string path,
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
            var files = await scope.Store.GetFilesByRepoAsync(scope.RepoId).ConfigureAwait(false);
            var fileIds = files.Select(f => f.Id).ToList();

            foreach (var fileId in fileIds)
            {
                await scope.Store.DeleteSymbolsByFileAsync(fileId).ConfigureAwait(false);
                await scope.Store.DeleteDependenciesByFileAsync(fileId).ConfigureAwait(false);
                await scope.Store.DeleteFileAsync(fileId).ConfigureAwait(false);
            }

            await scope.Store.DeleteRepositoryAsync(scope.RepoId).ConfigureAwait(false);

            return JsonSerializer.Serialize(
                new
                {
                    Success = true,
                    Message = "Cache invalidated. Next index operation will perform a full reparse.",
                },
                SerializerOptions);
        }
    }

    internal static string SanitizeLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return string.Empty;
        }

        var sanitized = SafeLabelPattern().Replace(label, string.Empty);

        if (sanitized.Length > 128)
        {
            sanitized = sanitized[..128];
        }

        return sanitized.Trim();
    }

    private static string SerializeError(string error, string code) =>
        JsonSerializer.Serialize(new { Error = error, Code = code }, SerializerOptions);

    [GeneratedRegex(@"[^a-zA-Z0-9 _.\-]")]
    private static partial Regex SafeLabelPattern();
}

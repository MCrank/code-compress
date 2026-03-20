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
    [Description("Index a codebase to build a searchable symbol database — MUST be called before any query tools. Scans source files, extracts symbols (classes, methods, functions, types), and stores them in a SQLite index. Re-running performs an incremental update (only changed files are re-parsed), so it's fast after the initial index. This enables all other CodeCompress tools to provide compressed, symbol-level code access. Returns JSON: {repo_id, project_root, files_indexed, files_unchanged, files_errored, total_files, symbols_found, duration_ms, parse_errors: [{file_path, reason}] or null}. Errors return JSON {error, code, retryable}. Codes: INVALID_PATH (path outside project root — fix the path), DIRECTORY_NOT_FOUND (directory does not exist — verify the path).")]
    public async Task<string> IndexProject(
        [Description("ABSOLUTE path to the project root directory — the same root used with index_project (e.g., 'C:\\Projects\\MyGame' or '/home/user/my-project'). Must NOT be a subdirectory or relative path.")] string path,
        [Description("Filter to a specific language (e.g., 'luau')")] string? language = null,
        [Description("Microsoft glob patterns for files to include (e.g., 'src/**/*.cs', '**/*.py', 'tests/**'). Omit to index all supported file types.")] string[]? includePatterns = null,
        [Description("Microsoft glob patterns for files to exclude (e.g., 'bin/**', 'obj/**', 'node_modules/**', '**/generated/**'). Common build output directories are excluded by default.")] string[]? excludePatterns = null,
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
                    scope.ProjectRoot,
                    language,
                    includePatterns,
                    excludePatterns,
                    cancellationToken).ConfigureAwait(false);

                var response = new
                {
                    result.RepoId,
                    ProjectRoot = scope.ProjectRoot,
                    result.FilesIndexed,
                    result.FilesUnchanged,
                    result.FilesErrored,
                    result.TotalFiles,
                    result.SymbolsFound,
                    result.DurationMs,
                    ParseErrors = result.ParseFailures?.Select(f => new { f.FilePath, f.Reason }),
                };

                return JsonSerializer.Serialize(response, SerializerOptions);
            }
        }
        catch (DirectoryNotFoundException)
        {
            return SerializeError("Directory not found", "DIRECTORY_NOT_FOUND");
        }
    }

    [McpServerTool(Name = "snapshot_create")]
    [Description("Create a named snapshot of the current index state. Use before making code changes, then call changes_since with the snapshot label to see a precise symbol-level diff of what changed. Requires index_project to have been called first. Returns JSON: {snapshot_id, label, file_count, symbol_count}. Errors return JSON {error, code, retryable}. Codes: INVALID_PATH.")]
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
    [Description("Delete ALL indexed data for a project — removes every symbol, dependency, file record, and repository metadata from the database. The next index_project call will perform a full re-index from scratch, which can be slow for large codebases. Only use when the index appears corrupted or out of sync. For normal updates, prefer index_project which performs fast incremental re-indexing of only changed files. Returns JSON: {success: true, message}. Errors return JSON {error, code, retryable}. Codes: INVALID_PATH.")]
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

    private static string SerializeError(string error, string code, string? guidance = null) =>
        guidance is null
            ? JsonSerializer.Serialize(new { Error = error, Code = code, Retryable = false }, SerializerOptions)
            : JsonSerializer.Serialize(new { Error = error, Code = code, Retryable = false, Guidance = guidance }, SerializerOptions);

    [GeneratedRegex(@"[^a-zA-Z0-9 _.\-]")]
    private static partial Regex SafeLabelPattern();
}

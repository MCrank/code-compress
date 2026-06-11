using CodeCompress.Core.Indexing;
using CodeCompress.Core.Models;
using CodeCompress.Core.Parsers;
using CodeCompress.Core.Registry;
using CodeCompress.Core.Storage;
using CodeCompress.Core.Validation;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace CodeCompress.Web.Services;

public sealed class IndexFacade : IIndexFacade
{
    private readonly IRegistryService _registry;
    private readonly IConnectionFactory _connectionFactory;
    private readonly IFileHasher _fileHasher;
    private readonly IChangeTracker _changeTracker;
    private readonly IEnumerable<ILanguageParser> _parsers;
    private readonly IPathValidator _pathValidator;
    private readonly IGitIgnoreFilter _gitIgnoreFilter;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ISymbolStore? _storeOverride;

    public IndexFacade(
        IRegistryService registry,
        IConnectionFactory connectionFactory,
        IFileHasher fileHasher,
        IChangeTracker changeTracker,
        IEnumerable<ILanguageParser> parsers,
        IPathValidator pathValidator,
        IGitIgnoreFilter gitIgnoreFilter,
        ILoggerFactory loggerFactory,
        ISymbolStore? storeOverride = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(fileHasher);
        ArgumentNullException.ThrowIfNull(changeTracker);
        ArgumentNullException.ThrowIfNull(parsers);
        ArgumentNullException.ThrowIfNull(pathValidator);
        ArgumentNullException.ThrowIfNull(gitIgnoreFilter);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _registry = registry;
        _connectionFactory = connectionFactory;
        _fileHasher = fileHasher;
        _changeTracker = changeTracker;
        _parsers = parsers;
        _pathValidator = pathValidator;
        _gitIgnoreFilter = gitIgnoreFilter;
        _loggerFactory = loggerFactory;
        _storeOverride = storeOverride;
    }

    public Task<IReadOnlyList<RepositoryRecord>> GetRepositoriesAsync(CancellationToken ct = default)
        => _registry.ListAsync();

    public async Task<IndexResult> ReIndexAsync(string projectRoot, CancellationToken ct = default)
    {
        var validatedPath = _pathValidator.ValidatePath(projectRoot, projectRoot);
        var connection = await _connectionFactory.CreateConnectionAsync(validatedPath).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var store = new SqliteSymbolStore(connection);
            var engine = new IndexEngine(
                _fileHasher,
                _changeTracker,
                _parsers,
                store,
                _pathValidator,
                _gitIgnoreFilter,
                _loggerFactory.CreateLogger<IndexEngine>());
            return await engine.IndexProjectAsync(validatedPath, cancellationToken: ct).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<SymbolSearchResult>> SearchSymbolsAsync(
        string projectRoot, string query, string? kind, int limit, CancellationToken ct = default)
    {
        var validatedPath = _pathValidator.ValidatePath(projectRoot, projectRoot);
        return await WithStoreAsync(validatedPath, async (store, repoId) =>
            await store.SearchSymbolsAsync(repoId, query, kind, limit).ConfigureAwait(false)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<IndexSnapshot>> GetSnapshotsAsync(string projectRoot, CancellationToken ct = default)
    {
        var validatedPath = _pathValidator.ValidatePath(projectRoot, projectRoot);
        return await WithStoreAsync(validatedPath, async (store, repoId) =>
            await store.GetSnapshotsByRepoAsync(repoId).ConfigureAwait(false)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FileRecord>> GetFilesAsync(string projectRoot, CancellationToken ct = default)
    {
        var validatedPath = _pathValidator.ValidatePath(projectRoot, projectRoot);
        return await WithStoreAsync(validatedPath, async (store, repoId) =>
            await store.GetFilesByRepoAsync(repoId).ConfigureAwait(false)).ConfigureAwait(false);
    }

    public async Task<DependencyGraph> GetDependencyGraphAsync(string projectRoot, CancellationToken ct = default)
    {
        var validatedPath = _pathValidator.ValidatePath(projectRoot, projectRoot);
        return await WithStoreAsync(validatedPath, async (store, repoId) =>
            await store.GetDependencyGraphAsync(repoId, null, "both", 50).ConfigureAwait(false)).ConfigureAwait(false);
    }

    private async Task<TResult> WithStoreAsync<TResult>(
        string projectRoot,
        Func<ISymbolStore, string, Task<TResult>> operation)
    {
        if (_storeOverride is not null)
        {
            var repoId = IndexEngine.ComputeRepoId(projectRoot);
            return await operation(_storeOverride, repoId).ConfigureAwait(false);
        }

        SqliteConnection connection = await _connectionFactory.CreateConnectionAsync(projectRoot).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var store = new SqliteSymbolStore(connection);
            var repoId = IndexEngine.ComputeRepoId(projectRoot);
            return await operation(store, repoId).ConfigureAwait(false);
        }
    }
}

using CodeCompress.Core.Indexing;
using CodeCompress.Core.Parsers;
using CodeCompress.Core.Storage;
using CodeCompress.Core.Validation;
using Microsoft.Extensions.Logging;

namespace CodeCompress.Server.Scoping;

internal sealed class ProjectScopeFactory : IProjectScopeFactory
{
    private readonly IConnectionFactory _connectionFactory;
    private readonly IFileHasher _fileHasher;
    private readonly IChangeTracker _changeTracker;
    private readonly IEnumerable<ILanguageParser> _parsers;
    private readonly IPathValidator _pathValidator;
    private readonly IGitIgnoreFilter _gitIgnoreFilter;
    private readonly IProjectRootResolver _rootResolver;
    private readonly IBoundaryPolicy _boundaryPolicy;
    private readonly ILoggerFactory _loggerFactory;

    public ProjectScopeFactory(
        IConnectionFactory connectionFactory,
        IFileHasher fileHasher,
        IChangeTracker changeTracker,
        IEnumerable<ILanguageParser> parsers,
        IPathValidator pathValidator,
        IGitIgnoreFilter gitIgnoreFilter,
        IProjectRootResolver rootResolver,
        IBoundaryPolicy boundaryPolicy,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(fileHasher);
        ArgumentNullException.ThrowIfNull(changeTracker);
        ArgumentNullException.ThrowIfNull(parsers);
        ArgumentNullException.ThrowIfNull(pathValidator);
        ArgumentNullException.ThrowIfNull(gitIgnoreFilter);
        ArgumentNullException.ThrowIfNull(rootResolver);
        ArgumentNullException.ThrowIfNull(boundaryPolicy);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _connectionFactory = connectionFactory;
        _fileHasher = fileHasher;
        _changeTracker = changeTracker;
        _parsers = parsers;
        _pathValidator = pathValidator;
        _gitIgnoreFilter = gitIgnoreFilter;
        _rootResolver = rootResolver;
        _boundaryPolicy = boundaryPolicy;
        _loggerFactory = loggerFactory;
    }

    public async Task<IProjectScope> CreateAsync(string projectRoot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);

        // Resolve to the nearest git root (or fall back to given path)
        var resolvedRoot = _rootResolver.ResolveProjectRoot(projectRoot);

        // Clamp: git-root resolution must never escape the configured boundary upward.
        // When the resolved root falls outside the boundary, use the requested path, which the
        // caller has already validated to be within bounds.
        var effectiveRoot = _boundaryPolicy.IsWithinBoundary(resolvedRoot) ? resolvedRoot : projectRoot;

        var connection = await _connectionFactory.CreateConnectionAsync(effectiveRoot).ConfigureAwait(false);
        var store = new SqliteSymbolStore(connection);
        var canonicalRoot = _pathValidator.ValidatePath(effectiveRoot, effectiveRoot);
        var repoId = IndexEngine.ComputeRepoId(canonicalRoot);
        var engine = new IndexEngine(
            _fileHasher,
            _changeTracker,
            _parsers,
            store,
            _pathValidator,
            _gitIgnoreFilter,
            _loggerFactory.CreateLogger<IndexEngine>());

        return new ProjectScope(connection, store, engine, repoId, canonicalRoot);
    }
}

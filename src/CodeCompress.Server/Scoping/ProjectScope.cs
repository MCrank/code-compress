using CodeCompress.Core.Indexing;
using CodeCompress.Core.Storage;
using Microsoft.Data.Sqlite;

namespace CodeCompress.Server.Scoping;

internal sealed class ProjectScope : IProjectScope
{
    private readonly SqliteConnection _connection;

    public ProjectScope(
        SqliteConnection connection,
        ISymbolStore store,
        IIndexEngine engine,
        string repoId)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoId);

        _connection = connection;
        Store = store;
        Engine = engine;
        RepoId = repoId;
    }

    public string RepoId { get; }
    public ISymbolStore Store { get; }
    public IIndexEngine Engine { get; }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
    }
}

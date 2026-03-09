using CodeCompress.Core.Indexing;
using CodeCompress.Core.Storage;
using Microsoft.Data.Sqlite;

namespace CodeCompress.Cli;

internal sealed class CliProjectScope : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public CliProjectScope(
        SqliteConnection connection,
        ISymbolStore store,
        IIndexEngine engine,
        string repoId,
        string projectRoot)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);

        _connection = connection;
        Store = store;
        Engine = engine;
        RepoId = repoId;
        ProjectRoot = projectRoot;
    }

    public string RepoId { get; }
    public string ProjectRoot { get; }
    public ISymbolStore Store { get; }
    public IIndexEngine Engine { get; }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
    }
}

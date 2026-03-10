using CodeCompress.Core.Storage;
using Microsoft.Data.Sqlite;

namespace CodeCompress.Core.Tests.Storage;

internal sealed class MigrationsTests
{
    private static async Task<SqliteConnection> CreateInMemoryConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync().ConfigureAwait(false);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys=ON;";
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);

        return connection;
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string tableName)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name;";
        cmd.Parameters.AddWithValue("@name", tableName);
        long count = (long)(await cmd.ExecuteScalarAsync().ConfigureAwait(false))!;
        return count == 1;
    }

    [Test]
    public async Task ApplyAsyncCreatesRepositoriesTable()
    {
        using var connection = await CreateInMemoryConnectionAsync().ConfigureAwait(false);

        await Migrations.ApplyAsync(connection).ConfigureAwait(false);

        bool exists = await TableExistsAsync(connection, "repositories").ConfigureAwait(false);
        await Assert.That(exists).IsTrue();
    }

    [Test]
    public async Task ApplyAsyncCreatesFilesTable()
    {
        using var connection = await CreateInMemoryConnectionAsync().ConfigureAwait(false);

        await Migrations.ApplyAsync(connection).ConfigureAwait(false);

        bool exists = await TableExistsAsync(connection, "files").ConfigureAwait(false);
        await Assert.That(exists).IsTrue();
    }

    [Test]
    public async Task ApplyAsyncCreatesSymbolsTable()
    {
        using var connection = await CreateInMemoryConnectionAsync().ConfigureAwait(false);

        await Migrations.ApplyAsync(connection).ConfigureAwait(false);

        bool exists = await TableExistsAsync(connection, "symbols").ConfigureAwait(false);
        await Assert.That(exists).IsTrue();
    }

    [Test]
    public async Task ApplyAsyncCreatesDependenciesTable()
    {
        using var connection = await CreateInMemoryConnectionAsync().ConfigureAwait(false);

        await Migrations.ApplyAsync(connection).ConfigureAwait(false);

        bool exists = await TableExistsAsync(connection, "dependencies").ConfigureAwait(false);
        await Assert.That(exists).IsTrue();
    }

    [Test]
    public async Task ApplyAsyncCreatesIndexSnapshotsTable()
    {
        using var connection = await CreateInMemoryConnectionAsync().ConfigureAwait(false);

        await Migrations.ApplyAsync(connection).ConfigureAwait(false);

        bool exists = await TableExistsAsync(connection, "index_snapshots").ConfigureAwait(false);
        await Assert.That(exists).IsTrue();
    }

    [Test]
    public async Task ApplyAsyncCreatesAllIndexes()
    {
        using var connection = await CreateInMemoryConnectionAsync().ConfigureAwait(false);

        await Migrations.ApplyAsync(connection).ConfigureAwait(false);

        var expectedIndexes = new[]
        {
            "ix_files_repo_id",
            "ix_files_content_hash",
            "ix_files_repo_path",
            "ix_symbols_file_id",
            "ix_symbols_name",
            "ix_symbols_kind",
            "ix_dependencies_file_id",
            "ix_dependencies_resolved",
            "ix_snapshots_repo_id",
        };

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND name LIKE 'ix_%';";
        using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);

        var actualIndexes = new List<string>();
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            actualIndexes.Add(reader.GetString(0));
        }

        foreach (string expected in expectedIndexes)
        {
            await Assert.That(actualIndexes).Contains(expected);
        }

        await Assert.That(actualIndexes).Count().IsEqualTo(expectedIndexes.Length);
    }

    [Test]
    public async Task ApplyAsyncCreatesSymbolsFtsTable()
    {
        using var connection = await CreateInMemoryConnectionAsync().ConfigureAwait(false);

        await Migrations.ApplyAsync(connection).ConfigureAwait(false);

        bool exists = await TableExistsAsync(connection, "symbols_fts").ConfigureAwait(false);
        await Assert.That(exists).IsTrue();
    }

    [Test]
    public async Task ApplyAsyncCreatesFileContentFtsTable()
    {
        using var connection = await CreateInMemoryConnectionAsync().ConfigureAwait(false);

        await Migrations.ApplyAsync(connection).ConfigureAwait(false);

        bool exists = await TableExistsAsync(connection, "file_content_fts").ConfigureAwait(false);
        await Assert.That(exists).IsTrue();
    }

    [Test]
    public async Task ApplyAsyncIsIdempotent()
    {
        using var connection = await CreateInMemoryConnectionAsync().ConfigureAwait(false);

        await Migrations.ApplyAsync(connection).ConfigureAwait(false);
        await Migrations.ApplyAsync(connection).ConfigureAwait(false);

        bool repositoriesExist = await TableExistsAsync(connection, "repositories").ConfigureAwait(false);
        bool filesExist = await TableExistsAsync(connection, "files").ConfigureAwait(false);
        bool symbolsExist = await TableExistsAsync(connection, "symbols").ConfigureAwait(false);
        bool dependenciesExist = await TableExistsAsync(connection, "dependencies").ConfigureAwait(false);
        bool snapshotsExist = await TableExistsAsync(connection, "index_snapshots").ConfigureAwait(false);

        await Assert.That(repositoriesExist).IsTrue();
        await Assert.That(filesExist).IsTrue();
        await Assert.That(symbolsExist).IsTrue();
        await Assert.That(dependenciesExist).IsTrue();
        await Assert.That(snapshotsExist).IsTrue();
    }

    [Test]
    public async Task ApplyAsyncCreatesAllFiveTables()
    {
        using var connection = await CreateInMemoryConnectionAsync().ConfigureAwait(false);

        await Migrations.ApplyAsync(connection).ConfigureAwait(false);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM sqlite_master
            WHERE type='table'
            AND name IN ('repositories', 'files', 'symbols', 'dependencies', 'index_snapshots');
            """;
        long count = (long)(await cmd.ExecuteScalarAsync().ConfigureAwait(false))!;

        await Assert.That(count).IsEqualTo(5);
    }

    [Test]
    public async Task ApplyAsyncRejectsNullConnection()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => Migrations.ApplyAsync(null!));
    }
}

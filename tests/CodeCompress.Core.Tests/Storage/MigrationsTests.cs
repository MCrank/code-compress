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

    [Test]
    public async Task ApplyAsyncSymbolsFtsUsesPorterTokenizer()
    {
        using var connection = await CreateInMemoryConnectionAsync().ConfigureAwait(false);

        await Migrations.ApplyAsync(connection).ConfigureAwait(false);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name='symbols_fts'";
        var schema = (string)(await cmd.ExecuteScalarAsync().ConfigureAwait(false))!;

        await Assert.That(schema).Contains("porter");
    }

    [Test]
    public async Task ApplyAsyncSymbolsFtsHasNoTriggers()
    {
        using var connection = await CreateInMemoryConnectionAsync().ConfigureAwait(false);

        await Migrations.ApplyAsync(connection).ConfigureAwait(false);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='trigger' AND name LIKE 'symbols_%'";
        var count = (long)(await cmd.ExecuteScalarAsync().ConfigureAwait(false))!;

        await Assert.That(count).IsEqualTo(0);
    }

    [Test]
    public async Task UpgradeFromOldSchemaAppliesPorterTokenizerAndPreservesData()
    {
        using var connection = await CreateInMemoryConnectionAsync().ConfigureAwait(false);

        // Simulate an old database: create the old FTS5 table with content table and triggers
        using (var setupCmd = connection.CreateCommand())
        {
            setupCmd.CommandText = """
                CREATE TABLE IF NOT EXISTS repositories (id TEXT PRIMARY KEY, root_path TEXT NOT NULL, name TEXT NOT NULL, language TEXT NOT NULL, last_indexed INTEGER NOT NULL, file_count INTEGER NOT NULL DEFAULT 0, symbol_count INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE IF NOT EXISTS files (id INTEGER PRIMARY KEY AUTOINCREMENT, repo_id TEXT NOT NULL, relative_path TEXT NOT NULL, content_hash TEXT NOT NULL, byte_length INTEGER NOT NULL, line_count INTEGER NOT NULL, last_modified INTEGER NOT NULL, indexed_at INTEGER NOT NULL);
                CREATE TABLE IF NOT EXISTS symbols (id INTEGER PRIMARY KEY AUTOINCREMENT, file_id INTEGER NOT NULL, name TEXT NOT NULL, kind TEXT NOT NULL, signature TEXT NOT NULL, parent_symbol TEXT, byte_offset INTEGER NOT NULL, byte_length INTEGER NOT NULL, line_start INTEGER NOT NULL, line_end INTEGER NOT NULL, visibility TEXT NOT NULL, doc_comment TEXT, body_line_start INTEGER, body_line_end INTEGER);
                CREATE TABLE IF NOT EXISTS dependencies (id INTEGER PRIMARY KEY AUTOINCREMENT, file_id INTEGER NOT NULL, requires_path TEXT NOT NULL, resolved_file_id INTEGER, alias TEXT, edge_kind TEXT NOT NULL DEFAULT 'imports');
                CREATE TABLE IF NOT EXISTS index_snapshots (id INTEGER PRIMARY KEY AUTOINCREMENT, repo_id TEXT NOT NULL, snapshot_label TEXT NOT NULL, created_at INTEGER NOT NULL, file_hashes TEXT NOT NULL, symbols_json TEXT NOT NULL DEFAULT '');
                CREATE VIRTUAL TABLE IF NOT EXISTS symbols_fts USING fts5(name, parent_symbol, signature, doc_comment, content=symbols, content_rowid=id);
                CREATE VIRTUAL TABLE IF NOT EXISTS file_content_fts USING fts5(relative_path, content);
                CREATE TRIGGER IF NOT EXISTS symbols_ai AFTER INSERT ON symbols BEGIN INSERT INTO symbols_fts(rowid, name, parent_symbol, signature, doc_comment) VALUES (new.id, new.name, new.parent_symbol, new.signature, new.doc_comment); END;
                CREATE TRIGGER IF NOT EXISTS symbols_ad AFTER DELETE ON symbols BEGIN INSERT INTO symbols_fts(symbols_fts, rowid, name, parent_symbol, signature, doc_comment) VALUES ('delete', old.id, old.name, old.parent_symbol, old.signature, old.doc_comment); END;
                CREATE TRIGGER IF NOT EXISTS symbols_au AFTER UPDATE ON symbols BEGIN INSERT INTO symbols_fts(symbols_fts, rowid, name, parent_symbol, signature, doc_comment) VALUES ('delete', old.id, old.name, old.parent_symbol, old.signature, old.doc_comment); INSERT INTO symbols_fts(rowid, name, parent_symbol, signature, doc_comment) VALUES (new.id, new.name, new.parent_symbol, new.signature, new.doc_comment); END;
                INSERT INTO repositories VALUES ('repo1', '/path', 'Test', 'csharp', 1000, 1, 1);
                INSERT INTO files VALUES (1, 'repo1', 'src/test.cs', 'hash', 100, 10, 1000, 1000);
                INSERT INTO symbols VALUES (1, 1, 'getUserProfile', 'Method', 'public UserProfile getUserProfile()', NULL, 0, 50, 1, 5, 'Public', NULL, NULL, NULL);
                """;
            await setupCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        // Run migration — should upgrade old schema
        await Migrations.ApplyAsync(connection).ConfigureAwait(false);

        // Verify: porter tokenizer present in FTS5 schema
        using var schemaCmd = connection.CreateCommand();
        schemaCmd.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name='symbols_fts'";
        var schema = (string)(await schemaCmd.ExecuteScalarAsync().ConfigureAwait(false))!;
        await Assert.That(schema).Contains("porter");

        // Verify: triggers removed
        using var triggerCmd = connection.CreateCommand();
        triggerCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='trigger' AND name LIKE 'symbols_%'";
        var triggerCount = (long)(await triggerCmd.ExecuteScalarAsync().ConfigureAwait(false))!;
        await Assert.That(triggerCount).IsEqualTo(0);

        // Verify: data preserved — FTS5 should find symbol by token search
        using var searchCmd = connection.CreateCommand();
        searchCmd.CommandText = "SELECT COUNT(*) FROM symbols_fts WHERE symbols_fts MATCH 'user'";
        var matchCount = (long)(await searchCmd.ExecuteScalarAsync().ConfigureAwait(false))!;
        await Assert.That(matchCount).IsGreaterThan(0);
    }
}

using Microsoft.Data.Sqlite;

namespace CodeCompress.Core.Storage;

public static class Migrations
{
    private static readonly string[] DdlStatements =
    [
        """
        CREATE TABLE IF NOT EXISTS repositories (
            id TEXT PRIMARY KEY,
            root_path TEXT NOT NULL,
            name TEXT NOT NULL,
            language TEXT NOT NULL,
            last_indexed INTEGER NOT NULL,
            file_count INTEGER NOT NULL DEFAULT 0,
            symbol_count INTEGER NOT NULL DEFAULT 0
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS files (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            repo_id TEXT NOT NULL REFERENCES repositories(id) ON DELETE CASCADE,
            relative_path TEXT NOT NULL,
            content_hash TEXT NOT NULL,
            byte_length INTEGER NOT NULL,
            line_count INTEGER NOT NULL,
            last_modified INTEGER NOT NULL,
            indexed_at INTEGER NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS symbols (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            file_id INTEGER NOT NULL REFERENCES files(id) ON DELETE CASCADE,
            name TEXT NOT NULL,
            kind TEXT NOT NULL,
            signature TEXT NOT NULL,
            parent_symbol TEXT,
            byte_offset INTEGER NOT NULL,
            byte_length INTEGER NOT NULL,
            line_start INTEGER NOT NULL,
            line_end INTEGER NOT NULL,
            visibility TEXT NOT NULL,
            doc_comment TEXT
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS dependencies (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            file_id INTEGER NOT NULL REFERENCES files(id) ON DELETE CASCADE,
            requires_path TEXT NOT NULL,
            resolved_file_id INTEGER REFERENCES files(id) ON DELETE SET NULL,
            alias TEXT
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS index_snapshots (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            repo_id TEXT NOT NULL REFERENCES repositories(id) ON DELETE CASCADE,
            snapshot_label TEXT NOT NULL,
            created_at INTEGER NOT NULL,
            file_hashes TEXT NOT NULL,
            symbols_json TEXT NOT NULL DEFAULT ''
        )
        """,
        "CREATE INDEX IF NOT EXISTS ix_files_repo_id ON files(repo_id)",
        "CREATE INDEX IF NOT EXISTS ix_files_content_hash ON files(content_hash)",
        "CREATE UNIQUE INDEX IF NOT EXISTS ix_files_repo_path ON files(repo_id, relative_path)",
        "CREATE INDEX IF NOT EXISTS ix_symbols_file_id ON symbols(file_id)",
        "CREATE INDEX IF NOT EXISTS ix_symbols_name ON symbols(name)",
        "CREATE INDEX IF NOT EXISTS ix_symbols_kind ON symbols(kind)",
        "CREATE INDEX IF NOT EXISTS ix_dependencies_file_id ON dependencies(file_id)",
        "CREATE INDEX IF NOT EXISTS ix_dependencies_resolved ON dependencies(resolved_file_id)",
        "CREATE INDEX IF NOT EXISTS ix_snapshots_repo_id ON index_snapshots(repo_id)",
        "CREATE VIRTUAL TABLE IF NOT EXISTS symbols_fts USING fts5(name, parent_symbol, signature, doc_comment, content=symbols, content_rowid=id)",
        "CREATE VIRTUAL TABLE IF NOT EXISTS file_content_fts USING fts5(relative_path, content)",
        """
        CREATE TRIGGER IF NOT EXISTS symbols_ai AFTER INSERT ON symbols BEGIN
            INSERT INTO symbols_fts(rowid, name, parent_symbol, signature, doc_comment)
            VALUES (new.id, new.name, new.parent_symbol, new.signature, new.doc_comment);
        END
        """,
        """
        CREATE TRIGGER IF NOT EXISTS symbols_ad AFTER DELETE ON symbols BEGIN
            INSERT INTO symbols_fts(symbols_fts, rowid, name, parent_symbol, signature, doc_comment)
            VALUES ('delete', old.id, old.name, old.parent_symbol, old.signature, old.doc_comment);
        END
        """,
        """
        CREATE TRIGGER IF NOT EXISTS symbols_au AFTER UPDATE ON symbols BEGIN
            INSERT INTO symbols_fts(symbols_fts, rowid, name, parent_symbol, signature, doc_comment)
            VALUES ('delete', old.id, old.name, old.parent_symbol, old.signature, old.doc_comment);
            INSERT INTO symbols_fts(rowid, name, parent_symbol, signature, doc_comment)
            VALUES (new.id, new.name, new.parent_symbol, new.signature, new.doc_comment);
        END
        """,
    ];

    public static async Task ApplyAsync(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var _ = transaction.ConfigureAwait(false);

        foreach (var ddl in DdlStatements)
        {
            using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
#pragma warning disable CA2100 // DDL statements are static literals, not user input
            command.CommandText = ddl;
#pragma warning restore CA2100
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await transaction.CommitAsync().ConfigureAwait(false);

        // Upgrade FTS5 table if it predates the parent_symbol column
        await UpgradeFts5IfNeededAsync(connection).ConfigureAwait(false);
    }

    private static async Task UpgradeFts5IfNeededAsync(SqliteConnection connection)
    {
        // Check if parent_symbol is already a column in symbols_fts by attempting a column query
        using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name='symbols_fts'";
        if (await checkCmd.ExecuteScalarAsync().ConfigureAwait(false) is not string ftsSchema
            || ftsSchema.Contains("parent_symbol", StringComparison.Ordinal))
        {
            return; // Either no FTS table or already upgraded
        }

        // Rebuild: drop old FTS table + triggers, then recreate with parent_symbol
        var upgradeDdl = new[]
        {
            "DROP TRIGGER IF EXISTS symbols_ai",
            "DROP TRIGGER IF EXISTS symbols_ad",
            "DROP TRIGGER IF EXISTS symbols_au",
            "DROP TABLE IF EXISTS symbols_fts",
            "CREATE VIRTUAL TABLE symbols_fts USING fts5(name, parent_symbol, signature, doc_comment, content=symbols, content_rowid=id)",
            """
            CREATE TRIGGER symbols_ai AFTER INSERT ON symbols BEGIN
                INSERT INTO symbols_fts(rowid, name, parent_symbol, signature, doc_comment)
                VALUES (new.id, new.name, new.parent_symbol, new.signature, new.doc_comment);
            END
            """,
            """
            CREATE TRIGGER symbols_ad AFTER DELETE ON symbols BEGIN
                INSERT INTO symbols_fts(symbols_fts, rowid, name, parent_symbol, signature, doc_comment)
                VALUES ('delete', old.id, old.name, old.parent_symbol, old.signature, old.doc_comment);
            END
            """,
            """
            CREATE TRIGGER symbols_au AFTER UPDATE ON symbols BEGIN
                INSERT INTO symbols_fts(symbols_fts, rowid, name, parent_symbol, signature, doc_comment)
                VALUES ('delete', old.id, old.name, old.parent_symbol, old.signature, old.doc_comment);
                INSERT INTO symbols_fts(rowid, name, parent_symbol, signature, doc_comment)
                VALUES (new.id, new.name, new.parent_symbol, new.signature, new.doc_comment);
            END
            """,
            "INSERT INTO symbols_fts(symbols_fts) VALUES('rebuild')",
        };

        var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var tx = transaction.ConfigureAwait(false);

        foreach (var ddl in upgradeDdl)
        {
            using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
#pragma warning disable CA2100 // DDL statements are static literals, not user input
            command.CommandText = ddl;
#pragma warning restore CA2100
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await transaction.CommitAsync().ConfigureAwait(false);
    }
}

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
            doc_comment TEXT,
            body_line_start INTEGER,
            body_line_end INTEGER
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS dependencies (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            file_id INTEGER NOT NULL REFERENCES files(id) ON DELETE CASCADE,
            requires_path TEXT NOT NULL,
            resolved_file_id INTEGER REFERENCES files(id) ON DELETE SET NULL,
            alias TEXT,
            edge_kind TEXT NOT NULL DEFAULT 'imports'
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
        """CREATE VIRTUAL TABLE IF NOT EXISTS symbols_fts USING fts5(name, parent_symbol, signature, doc_comment, tokenize="porter unicode61")""",
        "CREATE VIRTUAL TABLE IF NOT EXISTS file_content_fts USING fts5(relative_path, content)",
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

        // Upgrade FTS5 table if it predates the porter unicode61 tokenizer
        await UpgradeFts5IfNeededAsync(connection).ConfigureAwait(false);

        // Add body line columns to existing databases that predate this migration
        await AddBodyLineColumnsIfNeededAsync(connection).ConfigureAwait(false);

        // Add edge_kind column to existing databases that predate this migration
        await AddEdgeKindColumnIfNeededAsync(connection).ConfigureAwait(false);
    }

    private static async Task AddBodyLineColumnsIfNeededAsync(SqliteConnection connection)
    {
        using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name='symbols'";
        if (await checkCmd.ExecuteScalarAsync().ConfigureAwait(false) is not string symbolsSql
            || symbolsSql.Contains("body_line_start", StringComparison.Ordinal))
        {
            return;
        }

        var alterDdl = new[]
        {
            "ALTER TABLE symbols ADD COLUMN body_line_start INTEGER",
            "ALTER TABLE symbols ADD COLUMN body_line_end INTEGER",
        };

        var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var tx = transaction.ConfigureAwait(false);

        foreach (var ddl in alterDdl)
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

    private static async Task AddEdgeKindColumnIfNeededAsync(SqliteConnection connection)
    {
        using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name='dependencies'";
        if (await checkCmd.ExecuteScalarAsync().ConfigureAwait(false) is not string depsSql
            || depsSql.Contains("edge_kind", StringComparison.Ordinal))
        {
            return;
        }

        var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var tx = transaction.ConfigureAwait(false);

        using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
#pragma warning disable CA2100 // DDL statement is a static literal, not user input
        command.CommandText = "ALTER TABLE dependencies ADD COLUMN edge_kind TEXT NOT NULL DEFAULT 'imports'";
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);

        await transaction.CommitAsync().ConfigureAwait(false);
    }

    private static async Task UpgradeFts5IfNeededAsync(SqliteConnection connection)
    {
        using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name='symbols_fts'";
        if (await checkCmd.ExecuteScalarAsync().ConfigureAwait(false) is not string ftsSchema
            || ftsSchema.Contains("porter", StringComparison.OrdinalIgnoreCase))
        {
            return; // No FTS table or already upgraded to porter tokenizer
        }

        // Old schema: drop triggers and table, recreate with porter tokenizer, repopulate with split names
        var dropDdl = new[]
        {
            "DROP TRIGGER IF EXISTS symbols_ai",
            "DROP TRIGGER IF EXISTS symbols_ad",
            "DROP TRIGGER IF EXISTS symbols_au",
            "DROP TABLE IF EXISTS symbols_fts",
            """CREATE VIRTUAL TABLE symbols_fts USING fts5(name, parent_symbol, signature, doc_comment, tokenize="porter unicode61")""",
        };

        var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var tx = transaction.ConfigureAwait(false);

        foreach (var ddl in dropDdl)
        {
            using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
#pragma warning disable CA2100 // DDL statements are static literals, not user input
            command.CommandText = ddl;
#pragma warning restore CA2100
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await transaction.CommitAsync().ConfigureAwait(false);

        // Repopulate FTS5 with identifier-split names (separate transaction from DDL)
        await RepopulateFts5Async(connection).ConfigureAwait(false);
    }

    internal static async Task RepopulateFts5Async(SqliteConnection connection)
    {
        // Read all symbols into memory first (can't read and write in same transaction)
        var rows = new List<(long Id, string Name, string? ParentSymbol, string Signature, string? DocComment)>();

        using (var selectCmd = connection.CreateCommand())
        {
            selectCmd.CommandText = "SELECT id, name, parent_symbol, signature, doc_comment FROM symbols";
            using var reader = await selectCmd.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                rows.Add((
                    reader.GetInt64(0),
                    reader.GetString(1),
                    await reader.IsDBNullAsync(2).ConfigureAwait(false) ? null : reader.GetString(2),
                    reader.GetString(3),
                    await reader.IsDBNullAsync(4).ConfigureAwait(false) ? null : reader.GetString(4)));
            }
        }

        if (rows.Count == 0)
        {
            return;
        }

        var tx = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var _ = tx.ConfigureAwait(false);

        using var insertCmd = connection.CreateCommand();
        insertCmd.Transaction = (SqliteTransaction)tx;
#pragma warning disable CA2100 // DDL is a static literal, not user input
        insertCmd.CommandText = "INSERT INTO symbols_fts(rowid, name, parent_symbol, signature, doc_comment) VALUES (@rowid, @name, @parentSymbol, @signature, @docComment)";
#pragma warning restore CA2100
        var pRowid = insertCmd.Parameters.Add(new SqliteParameter("@rowid", 0L));
        var pName = insertCmd.Parameters.Add(new SqliteParameter("@name", string.Empty));
        var pParent = insertCmd.Parameters.Add(new SqliteParameter("@parentSymbol", DBNull.Value));
        var pSig = insertCmd.Parameters.Add(new SqliteParameter("@signature", string.Empty));
        var pDoc = insertCmd.Parameters.Add(new SqliteParameter("@docComment", DBNull.Value));

        foreach (var (id, name, parentSymbol, signature, docComment) in rows)
        {
            pRowid.Value = id;
            pName.Value = IdentifierSplitter.Split(name);
            pParent.Value = (object?)parentSymbol ?? DBNull.Value;
            pSig.Value = signature;
            pDoc.Value = (object?)docComment ?? DBNull.Value;
            await insertCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await tx.CommitAsync().ConfigureAwait(false);
    }
}

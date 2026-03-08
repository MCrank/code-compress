using CodeCompress.Core.Models;
using Microsoft.Data.Sqlite;

namespace CodeCompress.Core.Storage;

public sealed class SqliteSymbolStore : ISymbolStore
{
    private readonly SqliteConnection _connection;

    public SqliteSymbolStore(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _connection = connection;
    }

    // ── Repository ──────────────────────────────────────────────────────

    public async Task UpsertRepositoryAsync(Repository repo)
    {
        ArgumentNullException.ThrowIfNull(repo);

        using var command = _connection.CreateCommand();

#pragma warning disable CA2100 // SQL is a static literal, not user input
        command.CommandText =
            """
            INSERT OR REPLACE INTO repositories (id, root_path, name, language, last_indexed, file_count, symbol_count)
            VALUES (@id, @rootPath, @name, @language, @lastIndexed, @fileCount, @symbolCount)
            """;
#pragma warning restore CA2100

        command.Parameters.AddWithValue("@id", repo.Id);
        command.Parameters.AddWithValue("@rootPath", repo.RootPath);
        command.Parameters.AddWithValue("@name", repo.Name);
        command.Parameters.AddWithValue("@language", repo.Language);
        command.Parameters.AddWithValue("@lastIndexed", repo.LastIndexed);
        command.Parameters.AddWithValue("@fileCount", repo.FileCount);
        command.Parameters.AddWithValue("@symbolCount", repo.SymbolCount);

        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task<Repository?> GetRepositoryAsync(string repoId)
    {
        ArgumentNullException.ThrowIfNull(repoId);

        using var command = _connection.CreateCommand();

#pragma warning disable CA2100
        command.CommandText =
            """
            SELECT id, root_path, name, language, last_indexed, file_count, symbol_count
            FROM repositories WHERE id = @id
            """;
#pragma warning restore CA2100

        command.Parameters.AddWithValue("@id", repoId);

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

        if (await reader.ReadAsync().ConfigureAwait(false))
        {
            return new Repository(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt64(4),
                reader.GetInt32(5),
                reader.GetInt32(6));
        }

        return null;
    }

    public async Task DeleteRepositoryAsync(string repoId)
    {
        ArgumentNullException.ThrowIfNull(repoId);

        using var command = _connection.CreateCommand();

#pragma warning disable CA2100
        command.CommandText = "DELETE FROM repositories WHERE id = @id";
#pragma warning restore CA2100

        command.Parameters.AddWithValue("@id", repoId);

        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    // ── Files ───────────────────────────────────────────────────────────

    public async Task InsertFilesAsync(IReadOnlyList<FileRecord> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        if (files.Count == 0)
        {
            return;
        }

        var transaction = await _connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var _ = transaction.ConfigureAwait(false);

        try
        {
            using var command = _connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;

#pragma warning disable CA2100
            command.CommandText =
                """
                INSERT INTO files (repo_id, relative_path, content_hash, byte_length, line_count, last_modified, indexed_at)
                VALUES (@repoId, @relativePath, @contentHash, @byteLength, @lineCount, @lastModified, @indexedAt)
                """;
#pragma warning restore CA2100

            var pRepoId = command.Parameters.Add(new SqliteParameter("@repoId", ""));
            var pRelativePath = command.Parameters.Add(new SqliteParameter("@relativePath", ""));
            var pContentHash = command.Parameters.Add(new SqliteParameter("@contentHash", ""));
            var pByteLength = command.Parameters.Add(new SqliteParameter("@byteLength", 0L));
            var pLineCount = command.Parameters.Add(new SqliteParameter("@lineCount", 0));
            var pLastModified = command.Parameters.Add(new SqliteParameter("@lastModified", 0L));
            var pIndexedAt = command.Parameters.Add(new SqliteParameter("@indexedAt", 0L));

            foreach (var file in files)
            {
                pRepoId.Value = file.RepoId;
                pRelativePath.Value = file.RelativePath;
                pContentHash.Value = file.ContentHash;
                pByteLength.Value = file.ByteLength;
                pLineCount.Value = file.LineCount;
                pLastModified.Value = file.LastModified;
                pIndexedAt.Value = file.IndexedAt;

                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            await transaction.CommitAsync().ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<IReadOnlyList<FileRecord>> GetFilesByRepoAsync(string repoId)
    {
        ArgumentNullException.ThrowIfNull(repoId);

        using var command = _connection.CreateCommand();

#pragma warning disable CA2100
        command.CommandText =
            """
            SELECT id, repo_id, relative_path, content_hash, byte_length, line_count, last_modified, indexed_at
            FROM files WHERE repo_id = @repoId ORDER BY relative_path
            """;
#pragma warning restore CA2100

        command.Parameters.AddWithValue("@repoId", repoId);

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var results = new List<FileRecord>();

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            results.Add(new FileRecord(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt64(4),
                reader.GetInt32(5),
                reader.GetInt64(6),
                reader.GetInt64(7)));
        }

        return results;
    }

    public async Task<FileRecord?> GetFileByPathAsync(string repoId, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(repoId);
        ArgumentNullException.ThrowIfNull(relativePath);

        using var command = _connection.CreateCommand();

#pragma warning disable CA2100
        command.CommandText =
            """
            SELECT id, repo_id, relative_path, content_hash, byte_length, line_count, last_modified, indexed_at
            FROM files WHERE repo_id = @repoId AND relative_path = @relativePath
            """;
#pragma warning restore CA2100

        command.Parameters.AddWithValue("@repoId", repoId);
        command.Parameters.AddWithValue("@relativePath", relativePath);

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

        if (await reader.ReadAsync().ConfigureAwait(false))
        {
            return new FileRecord(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt64(4),
                reader.GetInt32(5),
                reader.GetInt64(6),
                reader.GetInt64(7));
        }

        return null;
    }

    public async Task UpdateFileAsync(FileRecord file)
    {
        ArgumentNullException.ThrowIfNull(file);

        using var command = _connection.CreateCommand();

#pragma warning disable CA2100
        command.CommandText =
            """
            UPDATE files
            SET repo_id = @repoId, relative_path = @relativePath, content_hash = @contentHash,
                byte_length = @byteLength, line_count = @lineCount, last_modified = @lastModified, indexed_at = @indexedAt
            WHERE id = @id
            """;
#pragma warning restore CA2100

        command.Parameters.AddWithValue("@id", file.Id);
        command.Parameters.AddWithValue("@repoId", file.RepoId);
        command.Parameters.AddWithValue("@relativePath", file.RelativePath);
        command.Parameters.AddWithValue("@contentHash", file.ContentHash);
        command.Parameters.AddWithValue("@byteLength", file.ByteLength);
        command.Parameters.AddWithValue("@lineCount", file.LineCount);
        command.Parameters.AddWithValue("@lastModified", file.LastModified);
        command.Parameters.AddWithValue("@indexedAt", file.IndexedAt);

        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task DeleteFileAsync(long fileId)
    {
        using var command = _connection.CreateCommand();

#pragma warning disable CA2100
        command.CommandText = "DELETE FROM files WHERE id = @id";
#pragma warning restore CA2100

        command.Parameters.AddWithValue("@id", fileId);

        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    // ── Symbols ─────────────────────────────────────────────────────────

    public async Task InsertSymbolsAsync(IReadOnlyList<Symbol> symbols)
    {
        ArgumentNullException.ThrowIfNull(symbols);

        if (symbols.Count == 0)
        {
            return;
        }

        var transaction = await _connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var _ = transaction.ConfigureAwait(false);

        try
        {
            using var command = _connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;

#pragma warning disable CA2100
            command.CommandText =
                """
                INSERT INTO symbols (file_id, name, kind, signature, parent_symbol, byte_offset, byte_length, line_start, line_end, visibility, doc_comment)
                VALUES (@fileId, @name, @kind, @signature, @parentSymbol, @byteOffset, @byteLength, @lineStart, @lineEnd, @visibility, @docComment)
                """;
#pragma warning restore CA2100

            var pFileId = command.Parameters.Add(new SqliteParameter("@fileId", 0L));
            var pName = command.Parameters.Add(new SqliteParameter("@name", ""));
            var pKind = command.Parameters.Add(new SqliteParameter("@kind", ""));
            var pSignature = command.Parameters.Add(new SqliteParameter("@signature", ""));
            var pParentSymbol = command.Parameters.Add(new SqliteParameter("@parentSymbol", ""));
            var pByteOffset = command.Parameters.Add(new SqliteParameter("@byteOffset", 0));
            var pByteLength = command.Parameters.Add(new SqliteParameter("@byteLength", 0));
            var pLineStart = command.Parameters.Add(new SqliteParameter("@lineStart", 0));
            var pLineEnd = command.Parameters.Add(new SqliteParameter("@lineEnd", 0));
            var pVisibility = command.Parameters.Add(new SqliteParameter("@visibility", ""));
            var pDocComment = command.Parameters.Add(new SqliteParameter("@docComment", ""));

            foreach (var symbol in symbols)
            {
                pFileId.Value = symbol.FileId;
                pName.Value = symbol.Name;
                pKind.Value = symbol.Kind;
                pSignature.Value = symbol.Signature;
                pParentSymbol.Value = (object?)symbol.ParentSymbol ?? DBNull.Value;
                pByteOffset.Value = symbol.ByteOffset;
                pByteLength.Value = symbol.ByteLength;
                pLineStart.Value = symbol.LineStart;
                pLineEnd.Value = symbol.LineEnd;
                pVisibility.Value = symbol.Visibility;
                pDocComment.Value = (object?)symbol.DocComment ?? DBNull.Value;

                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            await transaction.CommitAsync().ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<IReadOnlyList<Symbol>> GetSymbolsByFileAsync(long fileId)
    {
        using var command = _connection.CreateCommand();

#pragma warning disable CA2100
        command.CommandText =
            """
            SELECT id, file_id, name, kind, signature, parent_symbol, byte_offset, byte_length, line_start, line_end, visibility, doc_comment
            FROM symbols WHERE file_id = @fileId ORDER BY line_start
            """;
#pragma warning restore CA2100

        command.Parameters.AddWithValue("@fileId", fileId);

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var results = new List<Symbol>();

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            results.Add(new Symbol(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                await reader.IsDBNullAsync(5).ConfigureAwait(false) ? null : reader.GetString(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetInt32(8),
                reader.GetInt32(9),
                reader.GetString(10),
                await reader.IsDBNullAsync(11).ConfigureAwait(false) ? null : reader.GetString(11)));
        }

        return results;
    }

    public async Task DeleteSymbolsByFileAsync(long fileId)
    {
        using var command = _connection.CreateCommand();

#pragma warning disable CA2100
        command.CommandText = "DELETE FROM symbols WHERE file_id = @fileId";
#pragma warning restore CA2100

        command.Parameters.AddWithValue("@fileId", fileId);

        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    // ── Dependencies ────────────────────────────────────────────────────

    public async Task InsertDependenciesAsync(IReadOnlyList<Dependency> deps)
    {
        ArgumentNullException.ThrowIfNull(deps);

        if (deps.Count == 0)
        {
            return;
        }

        var transaction = await _connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var _ = transaction.ConfigureAwait(false);

        try
        {
            using var command = _connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;

#pragma warning disable CA2100
            command.CommandText =
                """
                INSERT INTO dependencies (file_id, requires_path, resolved_file_id, alias)
                VALUES (@fileId, @requiresPath, @resolvedFileId, @alias)
                """;
#pragma warning restore CA2100

            var pFileId = command.Parameters.Add(new SqliteParameter("@fileId", 0L));
            var pRequiresPath = command.Parameters.Add(new SqliteParameter("@requiresPath", ""));
            var pResolvedFileId = command.Parameters.Add(new SqliteParameter("@resolvedFileId", 0L));
            var pAlias = command.Parameters.Add(new SqliteParameter("@alias", ""));

            foreach (var dep in deps)
            {
                pFileId.Value = dep.FileId;
                pRequiresPath.Value = dep.RequiresPath;
                pResolvedFileId.Value = dep.ResolvedFileId.HasValue ? dep.ResolvedFileId.Value : DBNull.Value;
                pAlias.Value = (object?)dep.Alias ?? DBNull.Value;

                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            await transaction.CommitAsync().ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<IReadOnlyList<Dependency>> GetDependenciesByFileAsync(long fileId)
    {
        using var command = _connection.CreateCommand();

#pragma warning disable CA2100
        command.CommandText =
            """
            SELECT id, file_id, requires_path, resolved_file_id, alias
            FROM dependencies WHERE file_id = @fileId
            """;
#pragma warning restore CA2100

        command.Parameters.AddWithValue("@fileId", fileId);

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var results = new List<Dependency>();

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            results.Add(new Dependency(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetString(2),
                await reader.IsDBNullAsync(3).ConfigureAwait(false) ? null : reader.GetInt64(3),
                await reader.IsDBNullAsync(4).ConfigureAwait(false) ? null : reader.GetString(4)));
        }

        return results;
    }

    public async Task DeleteDependenciesByFileAsync(long fileId)
    {
        using var command = _connection.CreateCommand();

#pragma warning disable CA2100
        command.CommandText = "DELETE FROM dependencies WHERE file_id = @fileId";
#pragma warning restore CA2100

        command.Parameters.AddWithValue("@fileId", fileId);

        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    // ── Snapshots ───────────────────────────────────────────────────────

    public async Task<long> CreateSnapshotAsync(IndexSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        using var command = _connection.CreateCommand();

#pragma warning disable CA2100
        command.CommandText =
            """
            INSERT INTO index_snapshots (repo_id, snapshot_label, created_at, file_hashes)
            VALUES (@repoId, @snapshotLabel, @createdAt, @fileHashes)
            RETURNING id
            """;
#pragma warning restore CA2100

        command.Parameters.AddWithValue("@repoId", snapshot.RepoId);
        command.Parameters.AddWithValue("@snapshotLabel", snapshot.SnapshotLabel);
        command.Parameters.AddWithValue("@createdAt", snapshot.CreatedAt);
        command.Parameters.AddWithValue("@fileHashes", snapshot.FileHashes);

        var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
        return (long)result!;
    }

    public async Task<IndexSnapshot?> GetSnapshotAsync(long snapshotId)
    {
        using var command = _connection.CreateCommand();

#pragma warning disable CA2100
        command.CommandText =
            """
            SELECT id, repo_id, snapshot_label, created_at, file_hashes
            FROM index_snapshots WHERE id = @id
            """;
#pragma warning restore CA2100

        command.Parameters.AddWithValue("@id", snapshotId);

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

        if (await reader.ReadAsync().ConfigureAwait(false))
        {
            return new IndexSnapshot(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3),
                reader.GetString(4));
        }

        return null;
    }

    public async Task<IReadOnlyList<IndexSnapshot>> GetSnapshotsByRepoAsync(string repoId)
    {
        ArgumentNullException.ThrowIfNull(repoId);

        using var command = _connection.CreateCommand();

#pragma warning disable CA2100
        command.CommandText =
            """
            SELECT id, repo_id, snapshot_label, created_at, file_hashes
            FROM index_snapshots WHERE repo_id = @repoId ORDER BY created_at DESC
            """;
#pragma warning restore CA2100

        command.Parameters.AddWithValue("@repoId", repoId);

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var results = new List<IndexSnapshot>();

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            results.Add(new IndexSnapshot(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3),
                reader.GetString(4)));
        }

        return results;
    }
}

using System.Globalization;
using System.Text;
using System.Text.Json;
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
            INSERT INTO repositories (id, root_path, name, language, last_indexed, file_count, symbol_count)
            VALUES (@id, @rootPath, @name, @language, @lastIndexed, @fileCount, @symbolCount)
            ON CONFLICT(id) DO UPDATE SET
                root_path = excluded.root_path,
                name = excluded.name,
                language = excluded.language,
                last_indexed = excluded.last_indexed,
                file_count = excluded.file_count,
                symbol_count = excluded.symbol_count
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

    // ── File Content FTS ─────────────────────────────────────────────────

    public async Task UpsertFileContentAsync(string relativePath, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(content);

        // Delete any existing entry first, then insert fresh
        await DeleteFileContentAsync(relativePath).ConfigureAwait(false);

        using var command = _connection.CreateCommand();

#pragma warning disable CA2100
        command.CommandText =
            """
            INSERT INTO file_content_fts (relative_path, content)
            VALUES (@relativePath, @content)
            """;
#pragma warning restore CA2100

        command.Parameters.AddWithValue("@relativePath", relativePath);
        command.Parameters.AddWithValue("@content", content);

        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task DeleteFileContentAsync(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        using var command = _connection.CreateCommand();

#pragma warning disable CA2100
        command.CommandText =
            """
            DELETE FROM file_content_fts WHERE relative_path = @relativePath
            """;
#pragma warning restore CA2100

        command.Parameters.AddWithValue("@relativePath", relativePath);

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

        var fileHashes = snapshot.FileHashes;
        var symbolsJson = snapshot.SymbolsJson;

        // Auto-populate file hashes and symbol data if not provided
        if (string.IsNullOrEmpty(fileHashes))
        {
            var files = await GetFilesByRepoAsync(snapshot.RepoId).ConfigureAwait(false);
            var hashDict = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in files)
            {
                hashDict[file.RelativePath] = file.ContentHash;
            }

            fileHashes = JsonSerializer.Serialize(hashDict);
        }

        if (string.IsNullOrEmpty(symbolsJson))
        {
            var files = await GetFilesByRepoAsync(snapshot.RepoId).ConfigureAwait(false);
            var symbolDict = new Dictionary<string, List<SymbolSummary>>(StringComparer.Ordinal);
            foreach (var file in files)
            {
                var symbols = await GetSymbolsByFileAsync(file.Id).ConfigureAwait(false);
                if (symbols.Count > 0)
                {
                    symbolDict[file.RelativePath] = symbols
                        .Select(s => new SymbolSummary(s.Name, s.Kind, s.Signature))
                        .ToList();
                }
            }

            symbolsJson = JsonSerializer.Serialize(symbolDict);
        }

        using var command = _connection.CreateCommand();

#pragma warning disable CA2100
        command.CommandText =
            """
            INSERT INTO index_snapshots (repo_id, snapshot_label, created_at, file_hashes, symbols_json)
            VALUES (@repoId, @snapshotLabel, @createdAt, @fileHashes, @symbolsJson)
            RETURNING id
            """;
#pragma warning restore CA2100

        command.Parameters.AddWithValue("@repoId", snapshot.RepoId);
        command.Parameters.AddWithValue("@snapshotLabel", snapshot.SnapshotLabel);
        command.Parameters.AddWithValue("@createdAt", snapshot.CreatedAt);
        command.Parameters.AddWithValue("@fileHashes", fileHashes);
        command.Parameters.AddWithValue("@symbolsJson", symbolsJson);

        var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
        return (long)result!;
    }

    public async Task<IndexSnapshot?> GetSnapshotAsync(long snapshotId)
    {
        using var command = _connection.CreateCommand();

#pragma warning disable CA2100
        command.CommandText =
            """
            SELECT id, repo_id, snapshot_label, created_at, file_hashes, symbols_json
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
                reader.GetString(4),
                reader.GetString(5));
        }

        return null;
    }

    public async Task<IndexSnapshot?> GetSnapshotByLabelAsync(string repoId, string snapshotLabel)
    {
        ArgumentNullException.ThrowIfNull(repoId);
        ArgumentNullException.ThrowIfNull(snapshotLabel);

        using var command = _connection.CreateCommand();

#pragma warning disable CA2100
        command.CommandText =
            """
            SELECT id, repo_id, snapshot_label, created_at, file_hashes, symbols_json
            FROM index_snapshots WHERE repo_id = @repoId AND snapshot_label = @label
            ORDER BY created_at DESC LIMIT 1
            """;
#pragma warning restore CA2100

        command.Parameters.AddWithValue("@repoId", repoId);
        command.Parameters.AddWithValue("@label", snapshotLabel);

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

        if (await reader.ReadAsync().ConfigureAwait(false))
        {
            return new IndexSnapshot(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3),
                reader.GetString(4),
                reader.GetString(5));
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
            SELECT id, repo_id, snapshot_label, created_at, file_hashes, symbols_json
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
                reader.GetString(4),
                reader.GetString(5)));
        }

        return results;
    }

    // ── Search ──────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<SymbolSearchResult>> SearchSymbolsAsync(string repoId, string query, string? kind, int limit)
    {
        ArgumentNullException.ThrowIfNull(repoId);
        ArgumentNullException.ThrowIfNull(query);

        var sanitized = Fts5Sanitizer.Sanitize(query);
        if (sanitized.Length == 0)
        {
            return [];
        }

        var clampedLimit = Math.Min(Math.Max(limit, 1), 100);

        using var command = _connection.CreateCommand();

        var sql = new StringBuilder();
        sql.Append(
            """
            SELECT s.id, s.file_id, s.name, s.kind, s.signature, s.parent_symbol,
                   s.byte_offset, s.byte_length, s.line_start, s.line_end, s.visibility, s.doc_comment,
                   f.relative_path, bm25(symbols_fts) AS rank
            FROM symbols_fts
            JOIN symbols s ON s.id = symbols_fts.rowid
            JOIN files f ON f.id = s.file_id
            WHERE symbols_fts MATCH @query AND f.repo_id = @repoId
            """);

        if (kind is not null)
        {
            sql.Append(" AND s.kind = @kind");
        }

        sql.Append(" ORDER BY rank LIMIT @limit");

#pragma warning disable CA2100 // SQL is built from static literals and parameterized placeholders only
        command.CommandText = sql.ToString();
#pragma warning restore CA2100

        command.Parameters.AddWithValue("@query", sanitized);
        command.Parameters.AddWithValue("@repoId", repoId);
        command.Parameters.AddWithValue("@limit", clampedLimit);

        if (kind is not null)
        {
            command.Parameters.AddWithValue("@kind", kind);
        }

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var results = new List<SymbolSearchResult>();

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            var symbol = new Symbol(
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
                await reader.IsDBNullAsync(11).ConfigureAwait(false) ? null : reader.GetString(11));

            results.Add(new SymbolSearchResult(symbol, reader.GetString(12), reader.GetDouble(13)));
        }

        return results;
    }

    public async Task<IReadOnlyList<TextSearchResult>> SearchTextAsync(string repoId, string query, string? glob, int limit)
    {
        ArgumentNullException.ThrowIfNull(repoId);
        ArgumentNullException.ThrowIfNull(query);

        var sanitized = Fts5Sanitizer.Sanitize(query);
        if (sanitized.Length == 0)
        {
            return [];
        }

        var clampedLimit = Math.Min(Math.Max(limit, 1), 100);

        using var command = _connection.CreateCommand();

        var sql = new StringBuilder();
        sql.Append(
            """
            SELECT fts.relative_path, snippet(file_content_fts, 1, '<b>', '</b>', '...', 32) AS snippet,
                   bm25(file_content_fts) AS rank
            FROM file_content_fts AS fts
            WHERE file_content_fts MATCH @query
              AND fts.relative_path IN (SELECT relative_path FROM files WHERE repo_id = @repoId)
            """);

        if (glob is not null)
        {
            sql.Append(" AND fts.relative_path GLOB @glob");
        }

        sql.Append(" ORDER BY rank LIMIT @limit");

#pragma warning disable CA2100 // SQL is built from static literals and parameterized placeholders only
        command.CommandText = sql.ToString();
#pragma warning restore CA2100

        command.Parameters.AddWithValue("@query", sanitized);
        command.Parameters.AddWithValue("@repoId", repoId);
        command.Parameters.AddWithValue("@limit", clampedLimit);

        if (glob is not null)
        {
            command.Parameters.AddWithValue("@glob", glob);
        }

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var results = new List<TextSearchResult>();

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            results.Add(new TextSearchResult(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetDouble(2)));
        }

        return results;
    }

    // ── Lookups ─────────────────────────────────────────────────────────

    public async Task<Symbol?> GetSymbolByNameAsync(string repoId, string symbolName)
    {
        ArgumentNullException.ThrowIfNull(repoId);
        ArgumentNullException.ThrowIfNull(symbolName);

        using var command = _connection.CreateCommand();

        // Check for qualified name (e.g. "Module.Func" or "Module:Method")
        var separatorIndex = symbolName.IndexOfAny(['.', ':']);

        if (separatorIndex > 0 && separatorIndex < symbolName.Length - 1)
        {
            var parent = symbolName[..separatorIndex];
            var child = symbolName[(separatorIndex + 1)..];

#pragma warning disable CA2100
            command.CommandText =
                """
                SELECT s.id, s.file_id, s.name, s.kind, s.signature, s.parent_symbol,
                       s.byte_offset, s.byte_length, s.line_start, s.line_end, s.visibility, s.doc_comment
                FROM symbols s
                JOIN files f ON f.id = s.file_id
                WHERE s.parent_symbol = @parent AND s.name = @child AND f.repo_id = @repoId
                LIMIT 1
                """;
#pragma warning restore CA2100

            command.Parameters.AddWithValue("@parent", parent);
            command.Parameters.AddWithValue("@child", child);
        }
        else
        {
#pragma warning disable CA2100
            command.CommandText =
                """
                SELECT s.id, s.file_id, s.name, s.kind, s.signature, s.parent_symbol,
                       s.byte_offset, s.byte_length, s.line_start, s.line_end, s.visibility, s.doc_comment
                FROM symbols s
                JOIN files f ON f.id = s.file_id
                WHERE s.name = @name AND f.repo_id = @repoId
                LIMIT 1
                """;
#pragma warning restore CA2100

            command.Parameters.AddWithValue("@name", symbolName);
        }

        command.Parameters.AddWithValue("@repoId", repoId);

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

        if (await reader.ReadAsync().ConfigureAwait(false))
        {
            return new Symbol(
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
                await reader.IsDBNullAsync(11).ConfigureAwait(false) ? null : reader.GetString(11));
        }

        return null;
    }

    public async Task<IReadOnlyList<Symbol>> GetSymbolsByNamesAsync(string repoId, IReadOnlyList<string> symbolNames)
    {
        ArgumentNullException.ThrowIfNull(repoId);
        ArgumentNullException.ThrowIfNull(symbolNames);

        if (symbolNames.Count == 0)
        {
            return [];
        }

        using var command = _connection.CreateCommand();

        // Split names into simple (unqualified) and qualified (Parent:Child / Parent.Child)
        var conditions = new StringBuilder();
        var paramIndex = 0;

        for (var i = 0; i < symbolNames.Count; i++)
        {
            if (i > 0)
            {
                conditions.Append(" OR ");
            }

            var separatorIndex = symbolNames[i].IndexOfAny(['.', ':']);

            if (separatorIndex > 0 && separatorIndex < symbolNames[i].Length - 1)
            {
                var parentParam = $"@parent{paramIndex}";
                var childParam = $"@child{paramIndex}";
                conditions.Append(CultureInfo.InvariantCulture, $"(s.parent_symbol = {parentParam} AND s.name = {childParam})");
                command.Parameters.AddWithValue(parentParam, symbolNames[i][..separatorIndex]);
                command.Parameters.AddWithValue(childParam, symbolNames[i][(separatorIndex + 1)..]);
            }
            else
            {
                var nameParam = $"@p{paramIndex}";
                conditions.Append(CultureInfo.InvariantCulture, $"s.name = {nameParam}");
                command.Parameters.AddWithValue(nameParam, symbolNames[i]);
            }

            paramIndex++;
        }

#pragma warning disable CA2100 // SQL is built from static literals and parameterized placeholder names only
        command.CommandText =
            $"""
             SELECT s.id, s.file_id, s.name, s.kind, s.signature, s.parent_symbol,
                    s.byte_offset, s.byte_length, s.line_start, s.line_end, s.visibility, s.doc_comment
             FROM symbols s
             JOIN files f ON f.id = s.file_id
             WHERE ({conditions}) AND f.repo_id = @repoId
             """;
#pragma warning restore CA2100

        command.Parameters.AddWithValue("@repoId", repoId);

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

    // ── Aggregation ─────────────────────────────────────────────────────

    public async Task<ProjectOutline> GetProjectOutlineAsync(string repoId, bool includePrivate, string groupBy, int maxDepth, string? pathFilter = null)
    {
        ArgumentNullException.ThrowIfNull(repoId);
        ArgumentNullException.ThrowIfNull(groupBy);

        var clampedDepth = Math.Min(Math.Max(maxDepth, 1), 10);

        using var command = _connection.CreateCommand();

        var sql = new StringBuilder();
        sql.Append(
            """
            SELECT s.id, s.file_id, s.name, s.kind, s.signature, s.parent_symbol,
                   s.byte_offset, s.byte_length, s.line_start, s.line_end, s.visibility, s.doc_comment,
                   f.relative_path
            FROM symbols s
            JOIN files f ON f.id = s.file_id
            WHERE f.repo_id = @repoId
            """);

        if (!includePrivate)
        {
            sql.Append(" AND s.visibility != 'Private'");
        }

        if (pathFilter is not null)
        {
            sql.Append(" AND f.relative_path LIKE @pathPrefix || '%'");
        }

        sql.Append(" ORDER BY f.relative_path, s.line_start");

#pragma warning disable CA2100 // SQL is built from static literals and parameterized placeholders only
        command.CommandText = sql.ToString();
#pragma warning restore CA2100

        command.Parameters.AddWithValue("@repoId", repoId);

        if (pathFilter is not null)
        {
            var prefix = pathFilter.EndsWith('/') ? pathFilter : pathFilter + "/";
            command.Parameters.AddWithValue("@pathPrefix", prefix);
        }

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

        var symbolsByKey = new Dictionary<string, List<Symbol>>(StringComparer.Ordinal);

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            var symbol = new Symbol(
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
                await reader.IsDBNullAsync(11).ConfigureAwait(false) ? null : reader.GetString(11));

            var key = string.Equals(groupBy, "kind", StringComparison.OrdinalIgnoreCase)
                ? symbol.Kind
                : reader.GetString(12);

            if (!symbolsByKey.TryGetValue(key, out var list))
            {
                list = [];
                symbolsByKey[key] = list;
            }

            list.Add(symbol);
        }

        var groups = new List<OutlineGroup>();

        foreach (var (key, symbols) in symbolsByKey.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
        {
            if (clampedDepth >= 2)
            {
                // Build nested groups: top-level symbols and children grouped by parent
                var topLevel = new List<Symbol>();
                var childrenByParent = new Dictionary<string, List<Symbol>>(StringComparer.Ordinal);

                foreach (var sym in symbols)
                {
                    if (sym.ParentSymbol is null)
                    {
                        topLevel.Add(sym);
                    }
                    else
                    {
                        if (!childrenByParent.TryGetValue(sym.ParentSymbol, out var children))
                        {
                            children = [];
                            childrenByParent[sym.ParentSymbol] = children;
                        }

                        children.Add(sym);
                    }
                }

                var childGroups = new List<OutlineGroup>();
                foreach (var (parentName, children) in childrenByParent.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
                {
                    childGroups.Add(new OutlineGroup(parentName, children, []));
                }

                groups.Add(new OutlineGroup(key, topLevel, childGroups));
            }
            else
            {
                groups.Add(new OutlineGroup(key, symbols, []));
            }
        }

        return new ProjectOutline(repoId, groups);
    }

    public async Task<ModuleApi> GetModuleApiAsync(string repoId, string filePath)
    {
        ArgumentNullException.ThrowIfNull(repoId);
        ArgumentNullException.ThrowIfNull(filePath);

        var file = await GetFileByPathAsync(repoId, filePath).ConfigureAwait(false);

        if (file is null)
        {
            throw new ArgumentException($"File not found: {filePath}", nameof(filePath));
        }

        var symbols = await GetSymbolsByFileAsync(file.Id).ConfigureAwait(false);
        var dependencies = await GetDependenciesByFileAsync(file.Id).ConfigureAwait(false);

        return new ModuleApi(file, symbols, dependencies);
    }

    public async Task<DependencyGraph> GetDependencyGraphAsync(string repoId, string? rootFile, string direction, int depth)
    {
        ArgumentNullException.ThrowIfNull(repoId);
        ArgumentNullException.ThrowIfNull(direction);

        var clampedDepth = Math.Min(Math.Max(depth, 1), 50);

        // Load all files for this repo into a lookup
        var allFiles = await GetFilesByRepoAsync(repoId).ConfigureAwait(false);
        var fileById = new Dictionary<long, FileRecord>();
        var fileByPath = new Dictionary<string, FileRecord>(StringComparer.Ordinal);

        foreach (var f in allFiles)
        {
            fileById[f.Id] = f;
            fileByPath[f.RelativePath] = f;
        }

        var nodes = new HashSet<string>(StringComparer.Ordinal);
        var edges = new List<DependencyEdge>();
        var visited = new HashSet<long>();

        // Determine starting file IDs
        var frontier = new List<long>();

        if (rootFile is not null)
        {
            if (fileByPath.TryGetValue(rootFile, out var root))
            {
                frontier.Add(root.Id);
            }
            else
            {
                return new DependencyGraph([], []);
            }
        }
        else
        {
            frontier.AddRange(allFiles.Select(f => f.Id));
        }

        var isDependencies = string.Equals(direction, "dependencies", StringComparison.OrdinalIgnoreCase);

        for (int level = 0; level < clampedDepth && frontier.Count > 0; level++)
        {
            var nextFrontier = new List<long>();

            foreach (var fileId in frontier)
            {
                if (!visited.Add(fileId))
                {
                    continue;
                }

                if (!fileById.TryGetValue(fileId, out var currentFile))
                {
                    continue;
                }

                nodes.Add(currentFile.RelativePath);

                if (isDependencies)
                {
                    var deps = await GetDependenciesByFileAsync(fileId).ConfigureAwait(false);

                    foreach (var dep in deps)
                    {
                        var toPath = dep.RequiresPath;

                        if (dep.ResolvedFileId.HasValue && fileById.TryGetValue(dep.ResolvedFileId.Value, out var resolved))
                        {
                            toPath = resolved.RelativePath;
                            nodes.Add(toPath);
                            nextFrontier.Add(dep.ResolvedFileId.Value);
                        }
                        else
                        {
                            nodes.Add(toPath);
                        }

                        edges.Add(new DependencyEdge(currentFile.RelativePath, toPath, dep.Alias));
                    }
                }
                else
                {
                    // Dependents: find files that depend on the current file
                    using var command = _connection.CreateCommand();

#pragma warning disable CA2100
                    command.CommandText =
                        """
                        SELECT d.id, d.file_id, d.requires_path, d.resolved_file_id, d.alias
                        FROM dependencies d
                        WHERE d.resolved_file_id = @fileId
                        """;
#pragma warning restore CA2100

                    command.Parameters.AddWithValue("@fileId", fileId);

                    using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

                    while (await reader.ReadAsync().ConfigureAwait(false))
                    {
                        var depFileId = reader.GetInt64(1);
                        var alias = await reader.IsDBNullAsync(4).ConfigureAwait(false) ? null : reader.GetString(4);

                        if (fileById.TryGetValue(depFileId, out var dependentFile))
                        {
                            nodes.Add(dependentFile.RelativePath);
                            edges.Add(new DependencyEdge(dependentFile.RelativePath, currentFile.RelativePath, alias));
                            nextFrontier.Add(depFileId);
                        }
                    }
                }
            }

            frontier = nextFrontier;
        }

        return new DependencyGraph(
            nodes.OrderBy(n => n, StringComparer.Ordinal).ToList(),
            edges);
    }

    public async Task<ChangedFilesResult> GetChangedFilesAsync(string repoId, long snapshotId)
    {
        ArgumentNullException.ThrowIfNull(repoId);

        var snapshot = await GetSnapshotAsync(snapshotId).ConfigureAwait(false);

        if (snapshot is null)
        {
            return new ChangedFilesResult([], [], []);
        }

        var snapshotHashes = JsonSerializer.Deserialize<Dictionary<string, string>>(snapshot.FileHashes)
            ?? new Dictionary<string, string>();

        var currentFiles = await GetFilesByRepoAsync(repoId).ConfigureAwait(false);
        var currentByPath = new Dictionary<string, FileRecord>(StringComparer.Ordinal);

        foreach (var file in currentFiles)
        {
            currentByPath[file.RelativePath] = file;
        }

        var added = new List<FileRecord>();
        var modified = new List<FileRecord>();
        var removed = new List<string>();

        // Files in current but not in snapshot = added; in both but hash differs = modified
        foreach (var file in currentFiles)
        {
            if (!snapshotHashes.TryGetValue(file.RelativePath, out var oldHash))
            {
                added.Add(file);
            }
            else if (!string.Equals(oldHash, file.ContentHash, StringComparison.Ordinal))
            {
                modified.Add(file);
            }
        }

        // Files in snapshot but not in current = removed
        removed.AddRange(snapshotHashes.Keys.Where(path => !currentByPath.ContainsKey(path)));

        return new ChangedFilesResult(added, modified, removed);
    }
}

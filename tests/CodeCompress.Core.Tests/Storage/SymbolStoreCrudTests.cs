using CodeCompress.Core.Models;
using CodeCompress.Core.Storage;
using Microsoft.Data.Sqlite;

namespace CodeCompress.Core.Tests.Storage;

internal sealed class SymbolStoreCrudTests
{
    private static async Task<SqliteConnection> CreateTestConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync().ConfigureAwait(false);

        using var fkCmd = connection.CreateCommand();
        fkCmd.CommandText = "PRAGMA foreign_keys=ON;";
        await fkCmd.ExecuteNonQueryAsync().ConfigureAwait(false);

        await Migrations.ApplyAsync(connection).ConfigureAwait(false);
        return connection;
    }

    private static Repository CreateTestRepo(string id = "test-repo-id") =>
        new(id, "/test/path", "TestProject", "luau", DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 0, 0);

    private static FileRecord CreateTestFile(string repoId, string path = "src/main.luau", long id = 0) =>
        new(id, repoId, path, "abc123hash", 1024, 50, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    private static Symbol CreateTestSymbol(long fileId, string name = "TestFunction", int lineStart = 1, int lineEnd = 10) =>
        new(0, fileId, name, "function", $"function {name}()", null, 0, 100, lineStart, lineEnd, "public", "A test function");

    private static Dependency CreateTestDependency(long fileId, string requiresPath = "modules/utils", long? resolvedFileId = null, string? alias = null) =>
        new(0, fileId, requiresPath, resolvedFileId, alias);

    // ── Repository Tests ────────────────────────────────────────────────

    [Test]
    public async Task UpsertRepositoryAsyncInsertsNewRepository()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(connection);
        var repo = CreateTestRepo();

        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);

        var result = await store.GetRepositoryAsync(repo.Id).ConfigureAwait(false);
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Id).IsEqualTo(repo.Id);
        await Assert.That(result.RootPath).IsEqualTo(repo.RootPath);
        await Assert.That(result.Name).IsEqualTo(repo.Name);
        await Assert.That(result.Language).IsEqualTo(repo.Language);
        await Assert.That(result.LastIndexed).IsEqualTo(repo.LastIndexed);
        await Assert.That(result.FileCount).IsEqualTo(repo.FileCount);
        await Assert.That(result.SymbolCount).IsEqualTo(repo.SymbolCount);
    }

    [Test]
    public async Task UpsertRepositoryAsyncUpdatesExistingRepository()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(connection);
        var repo = CreateTestRepo();

        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);

        var updated = new Repository(repo.Id, "/updated/path", "UpdatedProject", "csharp", 999999L, 10, 42);
        await store.UpsertRepositoryAsync(updated).ConfigureAwait(false);

        var result = await store.GetRepositoryAsync(repo.Id).ConfigureAwait(false);
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.RootPath).IsEqualTo("/updated/path");
        await Assert.That(result.Name).IsEqualTo("UpdatedProject");
        await Assert.That(result.Language).IsEqualTo("csharp");
        await Assert.That(result.FileCount).IsEqualTo(10);
        await Assert.That(result.SymbolCount).IsEqualTo(42);
    }

    [Test]
    public async Task GetRepositoryAsyncReturnsNullForMissingId()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(connection);

        var result = await store.GetRepositoryAsync("nonexistent").ConfigureAwait(false);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task DeleteRepositoryAsyncCascadesToChildRecords()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(connection);
        var repo = CreateTestRepo();

        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);
        await store.InsertFilesAsync([CreateTestFile(repo.Id)]).ConfigureAwait(false);

        var files = await store.GetFilesByRepoAsync(repo.Id).ConfigureAwait(false);
        long fileId = files[0].Id;

        await store.InsertSymbolsAsync([CreateTestSymbol(fileId)]).ConfigureAwait(false);

        // Delete the repository - should cascade to files and symbols
        await store.DeleteRepositoryAsync(repo.Id).ConfigureAwait(false);

        var deletedRepo = await store.GetRepositoryAsync(repo.Id).ConfigureAwait(false);
        var remainingFiles = await store.GetFilesByRepoAsync(repo.Id).ConfigureAwait(false);
        var remainingSymbols = await store.GetSymbolsByFileAsync(fileId).ConfigureAwait(false);

        await Assert.That(deletedRepo).IsNull();
        await Assert.That(remainingFiles).Count().IsEqualTo(0);
        await Assert.That(remainingSymbols).Count().IsEqualTo(0);
    }

    // ── File Tests ──────────────────────────────────────────────────────

    [Test]
    public async Task InsertFilesAsyncInsertsMultipleFiles()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(connection);
        var repo = CreateTestRepo();
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);

        var files = new List<FileRecord>
        {
            CreateTestFile(repo.Id, "src/a.luau"),
            CreateTestFile(repo.Id, "src/b.luau"),
            CreateTestFile(repo.Id, "src/c.luau"),
        };

        await store.InsertFilesAsync(files).ConfigureAwait(false);

        var result = await store.GetFilesByRepoAsync(repo.Id).ConfigureAwait(false);
        await Assert.That(result).Count().IsEqualTo(3);
    }

    [Test]
    public async Task GetFileByPathAsyncReturnsCorrectFile()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(connection);
        var repo = CreateTestRepo();
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);

        await store.InsertFilesAsync([CreateTestFile(repo.Id, "src/main.luau")]).ConfigureAwait(false);

        var result = await store.GetFileByPathAsync(repo.Id, "src/main.luau").ConfigureAwait(false);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.RelativePath).IsEqualTo("src/main.luau");
        await Assert.That(result.RepoId).IsEqualTo(repo.Id);
        await Assert.That(result.ContentHash).IsEqualTo("abc123hash");
    }

    [Test]
    public async Task GetFileByPathAsyncReturnsNullForMissingPath()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(connection);
        var repo = CreateTestRepo();
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);

        var result = await store.GetFileByPathAsync(repo.Id, "nonexistent.luau").ConfigureAwait(false);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task UpdateFileAsyncUpdatesFields()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(connection);
        var repo = CreateTestRepo();
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);

        await store.InsertFilesAsync([CreateTestFile(repo.Id, "src/main.luau")]).ConfigureAwait(false);

        var files = await store.GetFilesByRepoAsync(repo.Id).ConfigureAwait(false);
        var original = files[0];

        var updated = new FileRecord(original.Id, original.RepoId, original.RelativePath, "newhash999", 2048, 100, original.LastModified, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await store.UpdateFileAsync(updated).ConfigureAwait(false);

        var result = await store.GetFileByPathAsync(repo.Id, "src/main.luau").ConfigureAwait(false);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.ContentHash).IsEqualTo("newhash999");
        await Assert.That(result.ByteLength).IsEqualTo(2048L);
        await Assert.That(result.LineCount).IsEqualTo(100);
    }

    [Test]
    public async Task DeleteFileAsyncRemovesFile()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(connection);
        var repo = CreateTestRepo();
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);

        await store.InsertFilesAsync([CreateTestFile(repo.Id, "src/main.luau")]).ConfigureAwait(false);

        var files = await store.GetFilesByRepoAsync(repo.Id).ConfigureAwait(false);
        long fileId = files[0].Id;

        await store.DeleteFileAsync(fileId).ConfigureAwait(false);

        var remaining = await store.GetFilesByRepoAsync(repo.Id).ConfigureAwait(false);
        await Assert.That(remaining).Count().IsEqualTo(0);
    }

    // ── Symbol Tests ────────────────────────────────────────────────────

    [Test]
    public async Task InsertSymbolsAsyncInsertsMultipleSymbols()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(connection);
        var repo = CreateTestRepo();
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);
        await store.InsertFilesAsync([CreateTestFile(repo.Id)]).ConfigureAwait(false);

        var files = await store.GetFilesByRepoAsync(repo.Id).ConfigureAwait(false);
        long fileId = files[0].Id;

        var symbols = new List<Symbol>
        {
            CreateTestSymbol(fileId, "FuncA", 1, 10),
            CreateTestSymbol(fileId, "FuncB", 12, 20),
            CreateTestSymbol(fileId, "FuncC", 22, 30),
        };

        await store.InsertSymbolsAsync(symbols).ConfigureAwait(false);

        var result = await store.GetSymbolsByFileAsync(fileId).ConfigureAwait(false);
        await Assert.That(result).Count().IsEqualTo(3);
    }

    [Test]
    public async Task GetSymbolsByFileAsyncReturnsOrderedByLineStart()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(connection);
        var repo = CreateTestRepo();
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);
        await store.InsertFilesAsync([CreateTestFile(repo.Id)]).ConfigureAwait(false);

        var files = await store.GetFilesByRepoAsync(repo.Id).ConfigureAwait(false);
        long fileId = files[0].Id;

        // Insert in reverse order
        var symbols = new List<Symbol>
        {
            CreateTestSymbol(fileId, "Third", 30, 40),
            CreateTestSymbol(fileId, "First", 1, 10),
            CreateTestSymbol(fileId, "Second", 15, 25),
        };

        await store.InsertSymbolsAsync(symbols).ConfigureAwait(false);

        var result = await store.GetSymbolsByFileAsync(fileId).ConfigureAwait(false);
        await Assert.That(result[0].Name).IsEqualTo("First");
        await Assert.That(result[1].Name).IsEqualTo("Second");
        await Assert.That(result[2].Name).IsEqualTo("Third");
    }

    [Test]
    public async Task DeleteSymbolsByFileAsyncRemovesAllSymbols()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(connection);
        var repo = CreateTestRepo();
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);
        await store.InsertFilesAsync([CreateTestFile(repo.Id)]).ConfigureAwait(false);

        var files = await store.GetFilesByRepoAsync(repo.Id).ConfigureAwait(false);
        long fileId = files[0].Id;

        await store.InsertSymbolsAsync([CreateTestSymbol(fileId, "A"), CreateTestSymbol(fileId, "B", 11, 20)]).ConfigureAwait(false);

        await store.DeleteSymbolsByFileAsync(fileId).ConfigureAwait(false);

        var result = await store.GetSymbolsByFileAsync(fileId).ConfigureAwait(false);
        await Assert.That(result).Count().IsEqualTo(0);
    }

    // ── Dependency Tests ────────────────────────────────────────────────

    [Test]
    public async Task InsertDependenciesAsyncInsertsMultipleDependencies()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(connection);
        var repo = CreateTestRepo();
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);
        await store.InsertFilesAsync([CreateTestFile(repo.Id)]).ConfigureAwait(false);

        var files = await store.GetFilesByRepoAsync(repo.Id).ConfigureAwait(false);
        long fileId = files[0].Id;

        var deps = new List<Dependency>
        {
            CreateTestDependency(fileId, "modules/utils"),
            CreateTestDependency(fileId, "modules/config", null, "Config"),
        };

        await store.InsertDependenciesAsync(deps).ConfigureAwait(false);

        var result = await store.GetDependenciesByFileAsync(fileId).ConfigureAwait(false);
        await Assert.That(result).Count().IsEqualTo(2);
    }

    [Test]
    public async Task GetDependenciesByFileAsyncReturnsCorrectDependencies()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(connection);
        var repo = CreateTestRepo();
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);
        await store.InsertFilesAsync([CreateTestFile(repo.Id)]).ConfigureAwait(false);

        var files = await store.GetFilesByRepoAsync(repo.Id).ConfigureAwait(false);
        long fileId = files[0].Id;

        await store.InsertDependenciesAsync([CreateTestDependency(fileId, "modules/utils", null, "Utils")]).ConfigureAwait(false);

        var result = await store.GetDependenciesByFileAsync(fileId).ConfigureAwait(false);
        await Assert.That(result).Count().IsEqualTo(1);
        await Assert.That(result[0].RequiresPath).IsEqualTo("modules/utils");
        await Assert.That(result[0].Alias).IsEqualTo("Utils");
        await Assert.That(result[0].ResolvedFileId).IsNull();
    }

    [Test]
    public async Task DeleteDependenciesByFileAsyncRemovesAllDependencies()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(connection);
        var repo = CreateTestRepo();
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);
        await store.InsertFilesAsync([CreateTestFile(repo.Id)]).ConfigureAwait(false);

        var files = await store.GetFilesByRepoAsync(repo.Id).ConfigureAwait(false);
        long fileId = files[0].Id;

        await store.InsertDependenciesAsync(
        [
            CreateTestDependency(fileId, "a"),
            CreateTestDependency(fileId, "b"),
        ]).ConfigureAwait(false);

        await store.DeleteDependenciesByFileAsync(fileId).ConfigureAwait(false);

        var result = await store.GetDependenciesByFileAsync(fileId).ConfigureAwait(false);
        await Assert.That(result).Count().IsEqualTo(0);
    }

    // ── Snapshot Tests ──────────────────────────────────────────────────

    [Test]
    public async Task CreateSnapshotAsyncReturnsAutoIncrementedId()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(connection);
        var repo = CreateTestRepo();
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var snapshot1 = new IndexSnapshot(0, repo.Id, "v1", now, "{}");
        var snapshot2 = new IndexSnapshot(0, repo.Id, "v2", now + 1, "{}");

        long id1 = await store.CreateSnapshotAsync(snapshot1).ConfigureAwait(false);
        long id2 = await store.CreateSnapshotAsync(snapshot2).ConfigureAwait(false);

        await Assert.That(id1).IsGreaterThan(0L);
        await Assert.That(id2).IsGreaterThan(id1);
    }

    [Test]
    public async Task GetSnapshotAsyncReturnsCorrectSnapshot()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(connection);
        var repo = CreateTestRepo();
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var snapshot = new IndexSnapshot(0, repo.Id, "my-snapshot", now, "{\"hash\":\"abc\"}");
        long id = await store.CreateSnapshotAsync(snapshot).ConfigureAwait(false);

        var result = await store.GetSnapshotAsync(id).ConfigureAwait(false);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Id).IsEqualTo(id);
        await Assert.That(result.RepoId).IsEqualTo(repo.Id);
        await Assert.That(result.SnapshotLabel).IsEqualTo("my-snapshot");
        await Assert.That(result.CreatedAt).IsEqualTo(now);
        await Assert.That(result.FileHashes).IsEqualTo("{\"hash\":\"abc\"}");
    }

    [Test]
    public async Task GetSnapshotAsyncReturnsNullForMissingId()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(connection);

        var result = await store.GetSnapshotAsync(99999L).ConfigureAwait(false);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetSnapshotsByRepoAsyncReturnsOrderedByCreatedAtDesc()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(connection);
        var repo = CreateTestRepo();
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await store.CreateSnapshotAsync(new IndexSnapshot(0, repo.Id, "oldest", now - 100, "{}")).ConfigureAwait(false);
        await store.CreateSnapshotAsync(new IndexSnapshot(0, repo.Id, "newest", now, "{}")).ConfigureAwait(false);
        await store.CreateSnapshotAsync(new IndexSnapshot(0, repo.Id, "middle", now - 50, "{}")).ConfigureAwait(false);

        var result = await store.GetSnapshotsByRepoAsync(repo.Id).ConfigureAwait(false);

        await Assert.That(result).Count().IsEqualTo(3);
        await Assert.That(result[0].SnapshotLabel).IsEqualTo("newest");
        await Assert.That(result[1].SnapshotLabel).IsEqualTo("middle");
        await Assert.That(result[2].SnapshotLabel).IsEqualTo("oldest");
    }

    // ── FTS5 Test ───────────────────────────────────────────────────────

    [Test]
    public async Task Fts5TriggersKeepSymbolsFtsInSync()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(connection);
        var repo = CreateTestRepo();
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);
        await store.InsertFilesAsync([CreateTestFile(repo.Id)]).ConfigureAwait(false);

        var files = await store.GetFilesByRepoAsync(repo.Id).ConfigureAwait(false);
        long fileId = files[0].Id;

        // Insert a symbol and verify it appears in FTS
        await store.InsertSymbolsAsync([CreateTestSymbol(fileId, "UniqueSearchableName")]).ConfigureAwait(false);

        using var searchCmd = connection.CreateCommand();
        searchCmd.CommandText = "SELECT COUNT(*) FROM symbols_fts WHERE symbols_fts MATCH @query";
        searchCmd.Parameters.AddWithValue("@query", "UniqueSearchableName");
        long ftsCount = (long)(await searchCmd.ExecuteScalarAsync().ConfigureAwait(false))!;
        await Assert.That(ftsCount).IsEqualTo(1L);

        // Delete the symbol and verify FTS is cleaned up
        await store.DeleteSymbolsByFileAsync(fileId).ConfigureAwait(false);

        using var searchCmd2 = connection.CreateCommand();
        searchCmd2.CommandText = "SELECT COUNT(*) FROM symbols_fts WHERE symbols_fts MATCH @query";
        searchCmd2.Parameters.AddWithValue("@query", "UniqueSearchableName");
        long ftsCountAfter = (long)(await searchCmd2.ExecuteScalarAsync().ConfigureAwait(false))!;
        await Assert.That(ftsCountAfter).IsEqualTo(0L);
    }

    // ── Batch Rollback Test ─────────────────────────────────────────────

    [Test]
    public async Task InsertFilesAsyncRollsBackOnFailure()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(connection);
        var repo = CreateTestRepo();
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);

        // Insert one file successfully
        await store.InsertFilesAsync([CreateTestFile(repo.Id, "src/existing.luau")]).ConfigureAwait(false);

        // Try to batch-insert two files where the second has the same (repo_id, relative_path)
        // as the first in this batch — violating the unique constraint
        var duplicateBatch = new List<FileRecord>
        {
            CreateTestFile(repo.Id, "src/new.luau"),
            CreateTestFile(repo.Id, "src/existing.luau"), // duplicate of already-inserted file
        };

        bool threw = false;
        try
        {
            await store.InsertFilesAsync(duplicateBatch).ConfigureAwait(false);
        }
        catch (SqliteException)
        {
            threw = true;
        }

        await Assert.That(threw).IsTrue();

        // The original file should still exist, but the batch should have been rolled back
        var remaining = await store.GetFilesByRepoAsync(repo.Id).ConfigureAwait(false);
        await Assert.That(remaining).Count().IsEqualTo(1);
        await Assert.That(remaining[0].RelativePath).IsEqualTo("src/existing.luau");
    }
}

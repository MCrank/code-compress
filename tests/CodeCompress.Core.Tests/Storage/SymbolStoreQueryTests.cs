using CodeCompress.Core.Models;
using CodeCompress.Core.Storage;
using Microsoft.Data.Sqlite;

namespace CodeCompress.Core.Tests.Storage;

internal sealed class SymbolStoreQueryTests
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

    private static async Task<(SqliteSymbolStore Store, long FileId)> SeedTestDataAsync(SqliteConnection connection)
    {
        var store = new SqliteSymbolStore(connection);
        var repo = new Repository("repo1", "/test/path", "TestProject", "luau", 1000, 0, 0);
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);

        var file = new FileRecord(0, "repo1", "src/main.luau", "hash1", 1024, 50, 1000, 1000);
        await store.InsertFilesAsync([file]).ConfigureAwait(false);

        var insertedFile = await store.GetFileByPathAsync("repo1", "src/main.luau").ConfigureAwait(false);

        var symbols = new List<Symbol>
        {
            new(0, insertedFile!.Id, "Initialize", "Function", "function Initialize()", null, 0, 100, 1, 10, "Public", "Initializes the module"),
            new(0, insertedFile.Id, "Helper", "Function", "local function Helper()", null, 100, 50, 11, 15, "Private", null),
            new(0, insertedFile.Id, "MyClass", "Class", "class MyClass", null, 150, 200, 16, 40, "Public", "A class"),
            new(0, insertedFile.Id, "DoWork", "Method", "function MyClass:DoWork()", "MyClass", 200, 80, 20, 30, "Public", "Does work"),
        };
        await store.InsertSymbolsAsync(symbols).ConfigureAwait(false);

        return (store, insertedFile.Id);
    }

    // ── SearchSymbolsAsync Tests ─────────────────────────────────────────

    [Test]
    public async Task SearchSymbolsAsyncReturnsMatchingResults()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var results = await store.SearchSymbolsAsync("repo1", "Initialize", null, 10).ConfigureAwait(false);

        await Assert.That(results.Count).IsGreaterThan(0);
        await Assert.That(results[0].Symbol.Name).IsEqualTo("Initialize");
    }

    [Test]
    public async Task SearchSymbolsAsyncFiltersByKind()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var results = await store.SearchSymbolsAsync("repo1", "Initialize", "Method", 10).ConfigureAwait(false);

        await Assert.That(results).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SearchSymbolsAsyncRespectsLimit()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var results = await store.SearchSymbolsAsync("repo1", "function", null, 2).ConfigureAwait(false);

        await Assert.That(results.Count).IsLessThanOrEqualTo(2);
    }

    [Test]
    public async Task SearchSymbolsAsyncReturnsEmptyForEmptyQuery()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var results = await store.SearchSymbolsAsync("repo1", "", null, 10).ConfigureAwait(false);

        await Assert.That(results).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SearchSymbolsAsyncReturnsFilePathInResult()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var results = await store.SearchSymbolsAsync("repo1", "Initialize", null, 10).ConfigureAwait(false);

        await Assert.That(results.Count).IsGreaterThan(0);
        await Assert.That(results[0].FilePath).IsEqualTo("src/main.luau");
    }

    [Test]
    public async Task SearchSymbolsAsyncReturnsEmptyForWrongRepo()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var results = await store.SearchSymbolsAsync("nonexistent", "Initialize", null, 10).ConfigureAwait(false);

        await Assert.That(results).Count().IsEqualTo(0);
    }

    // ── GetSymbolByNameAsync Tests ───────────────────────────────────────

    [Test]
    public async Task GetSymbolByNameAsyncReturnsExactMatch()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var result = await store.GetSymbolByNameAsync("repo1", "Initialize").ConfigureAwait(false);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Name).IsEqualTo("Initialize");
    }

    [Test]
    public async Task GetSymbolByNameAsyncReturnsNullForMissing()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var result = await store.GetSymbolByNameAsync("repo1", "NonExistent").ConfigureAwait(false);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetSymbolByNameAsyncHandlesDotQualifiedName()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var result = await store.GetSymbolByNameAsync("repo1", "MyClass.DoWork").ConfigureAwait(false);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Name).IsEqualTo("DoWork");
        await Assert.That(result.ParentSymbol).IsEqualTo("MyClass");
    }

    [Test]
    public async Task GetSymbolByNameAsyncHandlesColonQualifiedName()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var result = await store.GetSymbolByNameAsync("repo1", "MyClass:DoWork").ConfigureAwait(false);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Name).IsEqualTo("DoWork");
        await Assert.That(result.ParentSymbol).IsEqualTo("MyClass");
    }

    [Test]
    public async Task GetSymbolByNameAsyncReturnsNullForWrongRepo()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var result = await store.GetSymbolByNameAsync("nonexistent", "Initialize").ConfigureAwait(false);

        await Assert.That(result).IsNull();
    }

    // ── GetSymbolsByNamesAsync Tests ─────────────────────────────────────

    [Test]
    public async Task GetSymbolsByNamesAsyncReturnsBatchResults()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var results = await store.GetSymbolsByNamesAsync("repo1", ["Initialize", "DoWork"]).ConfigureAwait(false);

        await Assert.That(results).Count().IsEqualTo(2);
    }

    [Test]
    public async Task GetSymbolsByNamesAsyncHandlesMissingNames()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var results = await store.GetSymbolsByNamesAsync("repo1", ["Initialize", "NonExistent"]).ConfigureAwait(false);

        await Assert.That(results).Count().IsEqualTo(1);
    }

    [Test]
    public async Task GetSymbolsByNamesAsyncReturnsEmptyForEmptyInput()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var results = await store.GetSymbolsByNamesAsync("repo1", []).ConfigureAwait(false);

        await Assert.That(results).Count().IsEqualTo(0);
    }

    [Test]
    public async Task GetSymbolsByNamesAsyncReturnsEmptyForWrongRepo()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var results = await store.GetSymbolsByNamesAsync("nonexistent", ["Initialize"]).ConfigureAwait(false);

        await Assert.That(results).Count().IsEqualTo(0);
    }

    [Test]
    public async Task GetSymbolsByNamesAsyncHandlesQualifiedNames()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var results = await store.GetSymbolsByNamesAsync("repo1", ["MyClass:DoWork", "Initialize"]).ConfigureAwait(false);

        await Assert.That(results).Count().IsEqualTo(2);
        var names = results.Select(s => s.Name).ToList();
        await Assert.That(names).Contains("DoWork");
        await Assert.That(names).Contains("Initialize");
    }

    [Test]
    public async Task GetSymbolsByNamesAsyncHandlesDotQualifiedNames()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var results = await store.GetSymbolsByNamesAsync("repo1", ["MyClass.DoWork"]).ConfigureAwait(false);

        await Assert.That(results).Count().IsEqualTo(1);
        await Assert.That(results[0].Name).IsEqualTo("DoWork");
        await Assert.That(results[0].ParentSymbol).IsEqualTo("MyClass");
    }

    // ── GetProjectOutlineAsync PathFilter Tests ─────────────────────────

    private static string OsPath(string forwardSlashPath) =>
        forwardSlashPath.Replace('/', Path.DirectorySeparatorChar);

    private static async Task<SqliteSymbolStore> SeedMultiFileDataAsync(SqliteConnection connection)
    {
        var store = new SqliteSymbolStore(connection);
        var repo = new Repository("repo1", "/test/path", "TestProject", "luau", 1000, 0, 0);
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);

        var file1 = new FileRecord(0, "repo1", OsPath("src/services/Combat.luau"), "hash1", 512, 20, 1000, 1000);
        var file2 = new FileRecord(0, "repo1", OsPath("src/utils/Math.luau"), "hash2", 256, 10, 1000, 1000);
        var file3 = new FileRecord(0, "repo1", OsPath("src/Core/Models/Foo.luau"), "hash3", 128, 5, 1000, 1000);
        var file4 = new FileRecord(0, "repo1", OsPath("src/Core/Services/Bar.luau"), "hash4", 128, 5, 1000, 1000);
        var file5 = new FileRecord(0, "repo1", OsPath("src/servicesExtra/Bonus.luau"), "hash5", 128, 5, 1000, 1000);
        await store.InsertFilesAsync([file1, file2, file3, file4, file5]).ConfigureAwait(false);

        var f1 = await store.GetFileByPathAsync("repo1", OsPath("src/services/Combat.luau")).ConfigureAwait(false);
        var f2 = await store.GetFileByPathAsync("repo1", OsPath("src/utils/Math.luau")).ConfigureAwait(false);
        var f3 = await store.GetFileByPathAsync("repo1", OsPath("src/Core/Models/Foo.luau")).ConfigureAwait(false);
        var f4 = await store.GetFileByPathAsync("repo1", OsPath("src/Core/Services/Bar.luau")).ConfigureAwait(false);
        var f5 = await store.GetFileByPathAsync("repo1", OsPath("src/servicesExtra/Bonus.luau")).ConfigureAwait(false);

        await store.InsertSymbolsAsync([
            new Symbol(0, f1!.Id, "Attack", "Function", "function Attack()", null, 0, 50, 1, 5, "Public", null),
            new Symbol(0, f2!.Id, "Add", "Function", "function Add(a, b)", null, 0, 30, 1, 3, "Public", null),
            new Symbol(0, f3!.Id, "FooModel", "Class", "class FooModel", null, 0, 40, 1, 4, "Public", null),
            new Symbol(0, f4!.Id, "BarService", "Class", "class BarService", null, 0, 40, 1, 4, "Public", null),
            new Symbol(0, f5!.Id, "BonusFunc", "Function", "function BonusFunc()", null, 0, 30, 1, 3, "Private", null),
        ]).ConfigureAwait(false);

        return store;
    }

    [Test]
    public async Task GetProjectOutlineAsyncFiltersByPathPrefix()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = await SeedMultiFileDataAsync(connection).ConfigureAwait(false);

        var outline = await store.GetProjectOutlineAsync("repo1", true, "file", 1, "src/services").ConfigureAwait(false);

        var allSymbols = outline.Groups.SelectMany(g => g.Symbols).ToList();
        await Assert.That(allSymbols).Count().IsEqualTo(1);
        await Assert.That(allSymbols[0].Name).IsEqualTo("Attack");
    }

    [Test]
    public async Task GetProjectOutlineAsyncPathFilterWithTrailingSlash()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = await SeedMultiFileDataAsync(connection).ConfigureAwait(false);

        var outline = await store.GetProjectOutlineAsync("repo1", true, "file", 1, "src/services/").ConfigureAwait(false);

        var allSymbols = outline.Groups.SelectMany(g => g.Symbols).ToList();
        await Assert.That(allSymbols).Count().IsEqualTo(1);
        await Assert.That(allSymbols[0].Name).IsEqualTo("Attack");
    }

    [Test]
    public async Task GetProjectOutlineAsyncPathFilterNestedPath()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = await SeedMultiFileDataAsync(connection).ConfigureAwait(false);

        var outline = await store.GetProjectOutlineAsync("repo1", true, "file", 1, "src/Core/Models").ConfigureAwait(false);

        var allSymbols = outline.Groups.SelectMany(g => g.Symbols).ToList();
        await Assert.That(allSymbols).Count().IsEqualTo(1);
        await Assert.That(allSymbols[0].Name).IsEqualTo("FooModel");
    }

    [Test]
    public async Task GetProjectOutlineAsyncPathFilterNoMatchReturnsEmptyGroups()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = await SeedMultiFileDataAsync(connection).ConfigureAwait(false);

        var outline = await store.GetProjectOutlineAsync("repo1", true, "file", 1, "nonexistent").ConfigureAwait(false);

        await Assert.That(outline.Groups).Count().IsEqualTo(0);
    }

    [Test]
    public async Task GetProjectOutlineAsyncPathFilterNullReturnsAllSymbols()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = await SeedMultiFileDataAsync(connection).ConfigureAwait(false);

        var outline = await store.GetProjectOutlineAsync("repo1", true, "file", 1, null).ConfigureAwait(false);

        var allSymbols = outline.Groups.SelectMany(g => g.Symbols).ToList();
        await Assert.That(allSymbols).Count().IsEqualTo(5);
    }

    [Test]
    public async Task GetProjectOutlineAsyncPathFilterCombinesWithIncludePrivate()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = await SeedMultiFileDataAsync(connection).ConfigureAwait(false);

        var outline = await store.GetProjectOutlineAsync("repo1", false, "file", 1, "src/servicesExtra").ConfigureAwait(false);

        var allSymbols = outline.Groups.SelectMany(g => g.Symbols).ToList();
        await Assert.That(allSymbols).Count().IsEqualTo(0);
    }

    [Test]
    public async Task GetProjectOutlineAsyncPathFilterDoesNotMatchPartialDirectoryName()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = await SeedMultiFileDataAsync(connection).ConfigureAwait(false);

        var outline = await store.GetProjectOutlineAsync("repo1", true, "file", 1, "src/services").ConfigureAwait(false);

        var allFiles = outline.Groups.Select(g => g.Name).ToList();
        await Assert.That(allFiles).DoesNotContain(OsPath("src/servicesExtra/Bonus.luau"));
        var allSymbols = outline.Groups.SelectMany(g => g.Symbols).ToList();
        await Assert.That(allSymbols).Count().IsEqualTo(1);
        await Assert.That(allSymbols[0].Name).IsEqualTo("Attack");
    }

    // ── GetProjectOutlineAsync Tests ─────────────────────────────────────

    [Test]
    public async Task GetProjectOutlineAsyncGroupsByFile()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var outline = await store.GetProjectOutlineAsync("repo1", true, "file", 1).ConfigureAwait(false);

        await Assert.That(outline.RepoId).IsEqualTo("repo1");
        await Assert.That(outline.Groups.Count).IsGreaterThan(0);
        await Assert.That(outline.Groups[0].Name).IsEqualTo("src/main.luau");
    }

    [Test]
    public async Task GetProjectOutlineAsyncGroupsByKind()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var outline = await store.GetProjectOutlineAsync("repo1", true, "kind", 1).ConfigureAwait(false);

        await Assert.That(outline.Groups.Count).IsGreaterThan(0);
        var groupNames = outline.Groups.Select(g => g.Name).ToList();
        await Assert.That(groupNames).Contains("Function");
    }

    [Test]
    public async Task GetProjectOutlineAsyncExcludesPrivateWhenFlagged()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var outline = await store.GetProjectOutlineAsync("repo1", false, "file", 1).ConfigureAwait(false);

        var allSymbols = outline.Groups.SelectMany(g => g.Symbols).ToList();
        var hasPrivate = allSymbols.Any(s => s.Visibility == "Private");
        await Assert.That(hasPrivate).IsFalse();
    }

    [Test]
    public async Task GetProjectOutlineAsyncIncludesPrivateWhenFlagged()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var outline = await store.GetProjectOutlineAsync("repo1", true, "file", 1).ConfigureAwait(false);

        var allSymbols = outline.Groups.SelectMany(g => g.Symbols).ToList();
        var hasPrivate = allSymbols.Any(s => s.Visibility == "Private");
        await Assert.That(hasPrivate).IsTrue();
    }

    [Test]
    public async Task GetProjectOutlineAsyncNestsChildrenAtDepthTwo()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var outline = await store.GetProjectOutlineAsync("repo1", true, "file", 2).ConfigureAwait(false);

        var group = outline.Groups[0];
        await Assert.That(group.Children.Count).IsGreaterThan(0);
        var childGroup = group.Children.First(c => c.Name == "MyClass");
        await Assert.That(childGroup.Symbols.Count).IsGreaterThan(0);
        await Assert.That(childGroup.Symbols[0].Name).IsEqualTo("DoWork");
    }

    [Test]
    public async Task GetProjectOutlineAsyncClampsMaxDepth()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var outline = await store.GetProjectOutlineAsync("repo1", true, "file", 999).ConfigureAwait(false);

        await Assert.That(outline).IsNotNull();
        await Assert.That(outline.Groups.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task GetProjectOutlineAsyncReturnsEmptyGroupsForMissingRepo()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var outline = await store.GetProjectOutlineAsync("nonexistent", true, "file", 1).ConfigureAwait(false);

        await Assert.That(outline.RepoId).IsEqualTo("nonexistent");
        await Assert.That(outline.Groups).Count().IsEqualTo(0);
    }

    // ── GetProjectOutlineAsync Pagination Tests ────────────────────────

    [Test]
    public async Task GetProjectOutlineAsyncReturnsTotalSymbolCount()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var outline = await store.GetProjectOutlineAsync("repo1", true, "file", 1).ConfigureAwait(false);

        await Assert.That(outline.TotalSymbolCount).IsEqualTo(4);
    }

    [Test]
    public async Task GetProjectOutlineAsyncWithLimitReturnsTruncatedResults()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var outline = await store.GetProjectOutlineAsync("repo1", true, "file", 1, limit: 2).ConfigureAwait(false);

        var allSymbols = outline.Groups.SelectMany(g => g.Symbols).ToList();
        await Assert.That(allSymbols).Count().IsEqualTo(2);
        await Assert.That(outline.TotalSymbolCount).IsEqualTo(4);
        await Assert.That(outline.IsTruncated).IsTrue();
    }

    [Test]
    public async Task GetProjectOutlineAsyncWithOffsetSkipsSymbols()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var outline = await store.GetProjectOutlineAsync("repo1", true, "file", 1, offset: 2, limit: 10).ConfigureAwait(false);

        var allSymbols = outline.Groups.SelectMany(g => g.Symbols).ToList();
        await Assert.That(allSymbols).Count().IsEqualTo(2);
        await Assert.That(outline.IsTruncated).IsFalse();
    }

    [Test]
    public async Task GetProjectOutlineAsyncZeroLimitReturnsAllSymbols()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var outline = await store.GetProjectOutlineAsync("repo1", true, "file", 1, limit: 0).ConfigureAwait(false);

        var allSymbols = outline.Groups.SelectMany(g => g.Symbols).ToList();
        await Assert.That(allSymbols).Count().IsEqualTo(4);
        await Assert.That(outline.IsTruncated).IsFalse();
        await Assert.That(outline.TotalSymbolCount).IsEqualTo(4);
    }

    [Test]
    public async Task GetProjectOutlineAsyncLimitExceedsTotalReturnsNotTruncated()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var outline = await store.GetProjectOutlineAsync("repo1", true, "file", 1, limit: 100).ConfigureAwait(false);

        var allSymbols = outline.Groups.SelectMany(g => g.Symbols).ToList();
        await Assert.That(allSymbols).Count().IsEqualTo(4);
        await Assert.That(outline.IsTruncated).IsFalse();
    }

    [Test]
    public async Task GetProjectOutlineAsyncPaginationCombinesWithPathFilter()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = await SeedMultiFileDataAsync(connection).ConfigureAwait(false);

        var outline = await store.GetProjectOutlineAsync("repo1", true, "file", 1, "src/services", limit: 1).ConfigureAwait(false);

        var allSymbols = outline.Groups.SelectMany(g => g.Symbols).ToList();
        await Assert.That(allSymbols).Count().IsEqualTo(1);
        await Assert.That(outline.TotalSymbolCount).IsEqualTo(1);
        await Assert.That(outline.IsTruncated).IsFalse();
    }

    [Test]
    public async Task GetProjectOutlineAsyncPaginationCombinesWithIncludePrivate()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        // Without private: 3 public symbols (Initialize, MyClass, DoWork)
        var outline = await store.GetProjectOutlineAsync("repo1", false, "file", 1, limit: 2).ConfigureAwait(false);

        await Assert.That(outline.TotalSymbolCount).IsEqualTo(3);
        await Assert.That(outline.IsTruncated).IsTrue();
    }

    // ── GetModuleApiAsync Tests ──────────────────────────────────────────

    [Test]
    public async Task GetModuleApiAsyncReturnsSymbolsAndDependencies()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, fileId) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var dep = new Dependency(0, fileId, "game/ReplicatedStorage/Utils", null, "Utils");
        await store.InsertDependenciesAsync([dep]).ConfigureAwait(false);

        var api = await store.GetModuleApiAsync("repo1", "src/main.luau").ConfigureAwait(false);

        await Assert.That(api.File.RelativePath).IsEqualTo("src/main.luau");
        await Assert.That(api.Symbols.Count).IsGreaterThan(0);
        await Assert.That(api.Dependencies).Count().IsEqualTo(1);
    }

    [Test]
    public async Task GetModuleApiAsyncReturnsAllSymbolsForFile()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var api = await store.GetModuleApiAsync("repo1", "src/main.luau").ConfigureAwait(false);

        await Assert.That(api.Symbols).Count().IsEqualTo(4);
    }

    [Test]
    public async Task GetModuleApiAsyncThrowsForMissingFile()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.GetModuleApiAsync("repo1", "nonexistent.luau"));
    }

    // ── GetDependencyGraphAsync Tests ────────────────────────────────────

    [Test]
    public async Task GetDependencyGraphAsyncReturnsDependencies()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(connection);
        var repo = new Repository("repo1", "/test/path", "TestProject", "luau", 1000, 0, 0);
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);

        var file1 = new FileRecord(0, "repo1", "src/main.luau", "h1", 100, 10, 1000, 1000);
        var file2 = new FileRecord(0, "repo1", "src/utils.luau", "h2", 200, 20, 1000, 1000);
        await store.InsertFilesAsync([file1, file2]).ConfigureAwait(false);

        var f1 = await store.GetFileByPathAsync("repo1", "src/main.luau").ConfigureAwait(false);
        var f2 = await store.GetFileByPathAsync("repo1", "src/utils.luau").ConfigureAwait(false);

        var dep = new Dependency(0, f1!.Id, "src/utils.luau", f2!.Id, "Utils");
        await store.InsertDependenciesAsync([dep]).ConfigureAwait(false);

        var graph = await store.GetDependencyGraphAsync("repo1", "src/main.luau", "dependencies", 3).ConfigureAwait(false);

        await Assert.That(graph.Nodes.Count).IsGreaterThan(0);
        await Assert.That(graph.Edges.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task GetDependencyGraphAsyncReturnsDependents()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(connection);
        var repo = new Repository("repo1", "/test/path", "TestProject", "luau", 1000, 0, 0);
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);

        var file1 = new FileRecord(0, "repo1", "src/main.luau", "h1", 100, 10, 1000, 1000);
        var file2 = new FileRecord(0, "repo1", "src/utils.luau", "h2", 200, 20, 1000, 1000);
        await store.InsertFilesAsync([file1, file2]).ConfigureAwait(false);

        var f1 = await store.GetFileByPathAsync("repo1", "src/main.luau").ConfigureAwait(false);
        var f2 = await store.GetFileByPathAsync("repo1", "src/utils.luau").ConfigureAwait(false);

        var dep = new Dependency(0, f1!.Id, "src/utils.luau", f2!.Id, "Utils");
        await store.InsertDependenciesAsync([dep]).ConfigureAwait(false);

        var graph = await store.GetDependencyGraphAsync("repo1", "src/utils.luau", "dependents", 3).ConfigureAwait(false);

        await Assert.That(graph.Edges.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task GetDependencyGraphAsyncCapsDepthAt10()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(connection);
        var repo = new Repository("repo1", "/test/path", "TestProject", "luau", 1000, 0, 0);
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);

        var graph = await store.GetDependencyGraphAsync("repo1", null, "dependencies", 999).ConfigureAwait(false);

        await Assert.That(graph).IsNotNull();
    }

    [Test]
    public async Task GetDependencyGraphAsyncReturnsEmptyForMissingRootFile()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(connection);
        var repo = new Repository("repo1", "/test/path", "TestProject", "luau", 1000, 0, 0);
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);

        var graph = await store.GetDependencyGraphAsync("repo1", "nonexistent.luau", "dependencies", 3).ConfigureAwait(false);

        await Assert.That(graph.Nodes).Count().IsEqualTo(0);
        await Assert.That(graph.Edges).Count().IsEqualTo(0);
    }

    [Test]
    public async Task GetDependencyGraphAsyncReturnsNodesInSortedOrder()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(connection);
        var repo = new Repository("repo1", "/test/path", "TestProject", "luau", 1000, 0, 0);
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);

        var file1 = new FileRecord(0, "repo1", "src/zebra.luau", "h1", 100, 10, 1000, 1000);
        var file2 = new FileRecord(0, "repo1", "src/alpha.luau", "h2", 200, 20, 1000, 1000);
        await store.InsertFilesAsync([file1, file2]).ConfigureAwait(false);

        var f1 = await store.GetFileByPathAsync("repo1", "src/zebra.luau").ConfigureAwait(false);
        var f2 = await store.GetFileByPathAsync("repo1", "src/alpha.luau").ConfigureAwait(false);

        var dep = new Dependency(0, f1!.Id, "src/alpha.luau", f2!.Id, null);
        await store.InsertDependenciesAsync([dep]).ConfigureAwait(false);

        var graph = await store.GetDependencyGraphAsync("repo1", "src/zebra.luau", "dependencies", 3).ConfigureAwait(false);

        await Assert.That(graph.Nodes.Count).IsEqualTo(2);
        await Assert.That(graph.Nodes[0]).IsEqualTo("src/alpha.luau");
        await Assert.That(graph.Nodes[1]).IsEqualTo("src/zebra.luau");
    }

    [Test]
    public async Task GetDependencyGraphAsyncIncludesEdgeAlias()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(connection);
        var repo = new Repository("repo1", "/test/path", "TestProject", "luau", 1000, 0, 0);
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);

        var file1 = new FileRecord(0, "repo1", "src/main.luau", "h1", 100, 10, 1000, 1000);
        var file2 = new FileRecord(0, "repo1", "src/utils.luau", "h2", 200, 20, 1000, 1000);
        await store.InsertFilesAsync([file1, file2]).ConfigureAwait(false);

        var f1 = await store.GetFileByPathAsync("repo1", "src/main.luau").ConfigureAwait(false);
        var f2 = await store.GetFileByPathAsync("repo1", "src/utils.luau").ConfigureAwait(false);

        var dep = new Dependency(0, f1!.Id, "src/utils.luau", f2!.Id, "Utils");
        await store.InsertDependenciesAsync([dep]).ConfigureAwait(false);

        var graph = await store.GetDependencyGraphAsync("repo1", "src/main.luau", "dependencies", 1).ConfigureAwait(false);

        await Assert.That(graph.Edges.Count).IsEqualTo(1);
        await Assert.That(graph.Edges[0].From).IsEqualTo("src/main.luau");
        await Assert.That(graph.Edges[0].To).IsEqualTo("src/utils.luau");
        await Assert.That(graph.Edges[0].Alias).IsEqualTo("Utils");
    }

    // ── GetChangedFilesAsync Tests ───────────────────────────────────────

    [Test]
    public async Task GetChangedFilesAsyncIdentifiesAddedModifiedRemoved()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(connection);
        var repo = new Repository("repo1", "/test/path", "TestProject", "luau", 1000, 0, 0);
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);

        // Create initial files
        await store.InsertFilesAsync([
            new FileRecord(0, "repo1", "src/kept.luau", "hash_old", 100, 10, 1000, 1000),
            new FileRecord(0, "repo1", "src/modified.luau", "hash_before", 200, 20, 1000, 1000),
            new FileRecord(0, "repo1", "src/removed.luau", "hash_rem", 300, 30, 1000, 1000),
        ]).ConfigureAwait(false);

        // Create snapshot with current state
        var snapshotHashes = System.Text.Json.JsonSerializer.Serialize(
            new Dictionary<string, string>
            {
                ["src/kept.luau"] = "hash_old",
                ["src/modified.luau"] = "hash_before",
                ["src/removed.luau"] = "hash_rem",
            });
        long snapshotId = await store.CreateSnapshotAsync(
            new IndexSnapshot(0, "repo1", "v1", 1000, snapshotHashes)).ConfigureAwait(false);

        // Simulate changes: modify one file's hash, remove one file, add a new one
        var modFile = await store.GetFileByPathAsync("repo1", "src/modified.luau").ConfigureAwait(false);
        await store.UpdateFileAsync(modFile! with { ContentHash = "hash_after" }).ConfigureAwait(false);

        var remFile = await store.GetFileByPathAsync("repo1", "src/removed.luau").ConfigureAwait(false);
        await store.DeleteFileAsync(remFile!.Id).ConfigureAwait(false);

        await store.InsertFilesAsync([new FileRecord(0, "repo1", "src/new.luau", "hash_new", 400, 40, 1000, 1000)]).ConfigureAwait(false);

        // Detect changes
        var changes = await store.GetChangedFilesAsync("repo1", snapshotId).ConfigureAwait(false);
        await Assert.That(changes.Added).Count().IsEqualTo(1);
        await Assert.That(changes.Modified).Count().IsEqualTo(1);
        await Assert.That(changes.Removed).Count().IsEqualTo(1);
        await Assert.That(changes.Added[0].RelativePath).IsEqualTo("src/new.luau");
        await Assert.That(changes.Modified[0].RelativePath).IsEqualTo("src/modified.luau");
        await Assert.That(changes.Removed[0]).IsEqualTo("src/removed.luau");
    }

    [Test]
    public async Task GetChangedFilesAsyncReturnsEmptyForMissingSnapshot()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(connection);

        var changes = await store.GetChangedFilesAsync("repo1", 9999).ConfigureAwait(false);

        await Assert.That(changes.Added).Count().IsEqualTo(0);
        await Assert.That(changes.Modified).Count().IsEqualTo(0);
        await Assert.That(changes.Removed).Count().IsEqualTo(0);
    }

    // ── File Content FTS Tests ───────────────────────────────────────────

    [Test]
    public async Task UpsertFileContentMakesContentSearchable()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        await store.UpsertFileContentAsync("src/main.luau", "local function Initialize()\n  print('hello world')\nend").ConfigureAwait(false);

        var results = await store.SearchTextAsync("repo1", "hello", null, 10).ConfigureAwait(false);

        await Assert.That(results.Count).IsGreaterThan(0);
        await Assert.That(results[0].FilePath).IsEqualTo("src/main.luau");
    }

    [Test]
    public async Task UpsertFileContentReplacesExistingContent()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        await store.UpsertFileContentAsync("src/main.luau", "old content alpha").ConfigureAwait(false);
        await store.UpsertFileContentAsync("src/main.luau", "new content beta").ConfigureAwait(false);

        var oldResults = await store.SearchTextAsync("repo1", "alpha", null, 10).ConfigureAwait(false);
        var newResults = await store.SearchTextAsync("repo1", "beta", null, 10).ConfigureAwait(false);

        await Assert.That(oldResults).Count().IsEqualTo(0);
        await Assert.That(newResults.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task DeleteFileContentRemovesFromSearch()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        await store.UpsertFileContentAsync("src/main.luau", "searchable content gamma").ConfigureAwait(false);
        await store.DeleteFileContentAsync("src/main.luau").ConfigureAwait(false);

        var results = await store.SearchTextAsync("repo1", "gamma", null, 10).ConfigureAwait(false);

        await Assert.That(results).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SearchTextAsyncReturnsEmptyForUnindexedContent()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var results = await store.SearchTextAsync("repo1", "nonexistent", null, 10).ConfigureAwait(false);

        await Assert.That(results).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SearchTextAsyncFiltersbyRepoViaFileJoin()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        await store.UpsertFileContentAsync("src/main.luau", "unique token zeta").ConfigureAwait(false);

        var results = await store.SearchTextAsync("nonexistent-repo", "zeta", null, 10).ConfigureAwait(false);

        await Assert.That(results).Count().IsEqualTo(0);
    }

    // ── SearchTopicOutlineAsync Tests ────────────────────────────────────

    [Test]
    public async Task SearchTopicOutlineAsyncReturnsMatchingSymbolsGroupedByFile()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var outline = await store.SearchTopicOutlineAsync("repo1", "Initialize", 50).ConfigureAwait(false);

        await Assert.That(outline.Groups.Count).IsGreaterThan(0);
        var allSymbols = outline.Groups.SelectMany(g => g.Symbols).ToList();
        await Assert.That(allSymbols.Count).IsGreaterThan(0);
        await Assert.That(allSymbols[0].Name).IsEqualTo("Initialize");
    }

    [Test]
    public async Task SearchTopicOutlineAsyncGroupsByFilePath()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = await SeedMultiFileDataAsync(connection).ConfigureAwait(false);

        var outline = await store.SearchTopicOutlineAsync("repo1", "function", 50).ConfigureAwait(false);

        await Assert.That(outline.Groups.Count).IsGreaterThan(1);
        var groupNames = outline.Groups.Select(g => g.Name).ToList();
        foreach (var name in groupNames)
        {
            await Assert.That(name).Contains(".luau");
        }
    }

    [Test]
    public async Task SearchTopicOutlineAsyncRespectsLimit()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var outline = await store.SearchTopicOutlineAsync("repo1", "function", 1).ConfigureAwait(false);

        var allSymbols = outline.Groups.SelectMany(g => g.Symbols).ToList();
        await Assert.That(allSymbols.Count).IsLessThanOrEqualTo(1);
    }

    [Test]
    public async Task SearchTopicOutlineAsyncReturnsTruncatedWhenLimitExceeded()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var outline = await store.SearchTopicOutlineAsync("repo1", "function", 1).ConfigureAwait(false);

        await Assert.That(outline.IsTruncated).IsTrue();
        await Assert.That(outline.TotalSymbolCount).IsGreaterThan(1);
    }

    [Test]
    public async Task SearchTopicOutlineAsyncReturnsEmptyForNoMatches()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var outline = await store.SearchTopicOutlineAsync("repo1", "nonexistentxyz", 50).ConfigureAwait(false);

        await Assert.That(outline.Groups).Count().IsEqualTo(0);
        await Assert.That(outline.TotalSymbolCount).IsEqualTo(0);
        await Assert.That(outline.IsTruncated).IsFalse();
    }

    [Test]
    public async Task SearchTopicOutlineAsyncReturnsEmptyForEmptyQuery()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var outline = await store.SearchTopicOutlineAsync("repo1", "", 50).ConfigureAwait(false);

        await Assert.That(outline.Groups).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SearchTopicOutlineAsyncFiltersbyPathFilter()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = await SeedMultiFileDataAsync(connection).ConfigureAwait(false);

        var outline = await store.SearchTopicOutlineAsync("repo1", "function", 50, "src/services").ConfigureAwait(false);

        var allSymbols = outline.Groups.SelectMany(g => g.Symbols).ToList();
        await Assert.That(allSymbols.Count).IsGreaterThan(0);
        foreach (var group in outline.Groups)
        {
            await Assert.That(group.Name).StartsWith(OsPath("src/services/"));
        }
    }

    [Test]
    public async Task SearchTopicOutlineAsyncMatchesDocComments()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var outline = await store.SearchTopicOutlineAsync("repo1", "Initializes", 50).ConfigureAwait(false);

        var allSymbols = outline.Groups.SelectMany(g => g.Symbols).ToList();
        await Assert.That(allSymbols.Count).IsGreaterThan(0);
        await Assert.That(allSymbols[0].Name).IsEqualTo("Initialize");
    }

    [Test]
    public async Task SearchTopicOutlineAsyncReturnsEmptyForWrongRepo()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        var outline = await store.SearchTopicOutlineAsync("nonexistent", "Initialize", 50).ConfigureAwait(false);

        await Assert.That(outline.Groups).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SearchTopicOutlineAsyncOrdersSymbolsByLineWithinFile()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var (store, _) = await SeedTestDataAsync(connection).ConfigureAwait(false);

        // "function" matches multiple symbols in the same file via FTS5
        var outline = await store.SearchTopicOutlineAsync("repo1", "function", 50).ConfigureAwait(false);

        foreach (var group in outline.Groups)
        {
            for (var i = 1; i < group.Symbols.Count; i++)
            {
                await Assert.That(group.Symbols[i].LineStart).IsGreaterThanOrEqualTo(group.Symbols[i - 1].LineStart);
            }
        }
    }

    // ── Kind-based ranking tests ─────────────────────────────────────────

    [Test]
    public async Task SearchSymbolsSqlLikeRanksClassAboveConfigKey()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(connection);

        var repo = new Repository("repo1", "/test", "Test", "csharp", 1000, 0, 0);
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);

        var file = new FileRecord(0, "repo1", "src/test.cs", "hash1", 1024, 10, 1000, 1000);
        await store.InsertFilesAsync([file]).ConfigureAwait(false);
        var insertedFile = await store.GetFileByPathAsync("repo1", "src/test.cs").ConfigureAwait(false);

        var symbols = new List<Symbol>
        {
            new(0, insertedFile!.Id, "ClaudeConfig", "ConfigKey", "ClaudeConfig: \"value\"", null, 0, 50, 1, 1, "Public", null),
            new(0, insertedFile.Id, "ClaudeService", "Class", "class ClaudeService", null, 100, 200, 5, 20, "Public", null),
        };
        await store.InsertSymbolsAsync(symbols).ConfigureAwait(false);

        // SQL LIKE path (nameLikePattern, empty FTS5 query)
        var results = await store.SearchSymbolsAsync("repo1", "", null, 10, null, "%Claude%").ConfigureAwait(false);

        await Assert.That(results).Count().IsEqualTo(2);
        await Assert.That(results[0].Symbol.Name).IsEqualTo("ClaudeService"); // Class = tier 1
        await Assert.That(results[1].Symbol.Name).IsEqualTo("ClaudeConfig");  // ConfigKey = tier 3
    }

    [Test]
    public async Task SearchSymbolsSqlLikeRanksStructuralAboveMemberAboveOther()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(connection);

        var repo = new Repository("repo1", "/test", "Test", "csharp", 1000, 0, 0);
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);

        var file = new FileRecord(0, "repo1", "src/test.cs", "hash1", 1024, 10, 1000, 1000);
        await store.InsertFilesAsync([file]).ConfigureAwait(false);
        var insertedFile = await store.GetFileByPathAsync("repo1", "src/test.cs").ConfigureAwait(false);

        var symbols = new List<Symbol>
        {
            new(0, insertedFile!.Id, "CLAUDE_KEY", "Constant", "const CLAUDE_KEY", null, 0, 30, 1, 1, "Public", null),
            new(0, insertedFile.Id, "HandleClaude", "Method", "void HandleClaude()", null, 50, 80, 5, 10, "Public", null),
            new(0, insertedFile.Id, "IClaudeClient", "Interface", "interface IClaudeClient", null, 150, 100, 15, 25, "Public", null),
            new(0, insertedFile.Id, "ClaudeRecord", "Record", "record ClaudeRecord", null, 300, 50, 30, 35, "Public", null),
        };
        await store.InsertSymbolsAsync(symbols).ConfigureAwait(false);

        var results = await store.SearchSymbolsAsync("repo1", "", null, 10, null, "%Claude%").ConfigureAwait(false);

        await Assert.That(results).Count().IsEqualTo(4);
        // Tier 1 (Structural): ClaudeRecord, IClaudeClient (alphabetical within tier)
        await Assert.That(results[0].Symbol.Kind).IsEqualTo("Record");
        await Assert.That(results[1].Symbol.Kind).IsEqualTo("Interface");
        // Tier 2 (Member): HandleClaude
        await Assert.That(results[2].Symbol.Kind).IsEqualTo("Method");
        // Tier 3 (Other): CLAUDE_KEY
        await Assert.That(results[3].Symbol.Kind).IsEqualTo("Constant");
    }

    [Test]
    public async Task SearchSymbolsSameTierOrdersAlphabetically()
    {
        using var connection = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(connection);

        var repo = new Repository("repo1", "/test", "Test", "csharp", 1000, 0, 0);
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);

        var file = new FileRecord(0, "repo1", "src/test.cs", "hash1", 1024, 10, 1000, 1000);
        await store.InsertFilesAsync([file]).ConfigureAwait(false);
        var insertedFile = await store.GetFileByPathAsync("repo1", "src/test.cs").ConfigureAwait(false);

        // Insert in reverse alphabetical order to prove sorting works
        var symbols = new List<Symbol>
        {
            new(0, insertedFile!.Id, "ZService", "Class", "class ZService", null, 0, 50, 1, 5, "Public", null),
            new(0, insertedFile.Id, "AService", "Class", "class AService", null, 100, 50, 10, 15, "Public", null),
            new(0, insertedFile.Id, "MService", "Class", "class MService", null, 200, 50, 20, 25, "Public", null),
        };
        await store.InsertSymbolsAsync(symbols).ConfigureAwait(false);

        var results = await store.SearchSymbolsAsync("repo1", "", null, 10, null, "%Service%").ConfigureAwait(false);

        await Assert.That(results).Count().IsEqualTo(3);
        await Assert.That(results[0].Symbol.Name).IsEqualTo("AService");
        await Assert.That(results[1].Symbol.Name).IsEqualTo("MService");
        await Assert.That(results[2].Symbol.Name).IsEqualTo("ZService");
    }
}

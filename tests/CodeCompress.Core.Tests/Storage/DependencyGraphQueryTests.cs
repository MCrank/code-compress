using CodeCompress.Core.Models;
using CodeCompress.Core.Storage;
using Microsoft.Data.Sqlite;

namespace CodeCompress.Core.Tests.Storage;

internal sealed class DependencyGraphQueryTests
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

    private static Repository MakeRepo() =>
        new("repo1", "/root", "Test", "csharp", 0, 0, 0);

    private static FileRecord MakeFile(string repoId, string path, long id = 0) =>
        new(id, repoId, path, "hash", 100, 10, 0, 0);

    private static Dependency MakeDep(long fileId, string requiresPath, long? resolvedFileId, string edgeKind = "imports") =>
        new(0, fileId, requiresPath, resolvedFileId, null, edgeKind);

    private static Symbol MakeSymbol(long fileId, string name, string kind = "method", string visibility = "public") =>
        new(0, fileId, name, kind, $"{visibility} {kind} {name}()", null, 0, 10, 1, 5, visibility, null, null, null);

    // ── Dependency edge_kind roundtrip ───────────────────────────────

    [Test]
    public async Task InsertAndReadDependencyPreservesEdgeKind()
    {
        using var conn = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(conn);
        var repo = MakeRepo();
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);
        await store.InsertFilesAsync([MakeFile(repo.Id, "A.cs")]).ConfigureAwait(false);
        var files = await store.GetFilesByRepoAsync(repo.Id).ConfigureAwait(false);
        var fileId = files[0].Id;

        await store.InsertDependenciesAsync([
            MakeDep(fileId, "Base", null, "inherits"),
            MakeDep(fileId, "IFoo", null, "implements"),
            MakeDep(fileId, "System.Linq", null, "imports"),
        ]).ConfigureAwait(false);

        var deps = await store.GetDependenciesByFileAsync(fileId).ConfigureAwait(false);

        await Assert.That(deps).Count().IsEqualTo(3);
        var inherits = deps.First(d => d.RequiresPath == "Base");
        var implements = deps.First(d => d.RequiresPath == "IFoo");
        var imports = deps.First(d => d.RequiresPath == "System.Linq");
        await Assert.That(inherits.EdgeKind).IsEqualTo("inherits");
        await Assert.That(implements.EdgeKind).IsEqualTo("implements");
        await Assert.That(imports.EdgeKind).IsEqualTo("imports");
    }

    [Test]
    public async Task InsertDependencyDefaultEdgeKindIsImports()
    {
        using var conn = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(conn);
        var repo = MakeRepo();
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);
        await store.InsertFilesAsync([MakeFile(repo.Id, "A.cs")]).ConfigureAwait(false);
        var files = await store.GetFilesByRepoAsync(repo.Id).ConfigureAwait(false);
        var fileId = files[0].Id;

        // Insert without specifying EdgeKind — should default to "imports"
        await store.InsertDependenciesAsync([new Dependency(0, fileId, "Foo", null, null)]).ConfigureAwait(false);

        var deps = await store.GetDependenciesByFileAsync(fileId).ConfigureAwait(false);
        await Assert.That(deps[0].EdgeKind).IsEqualTo("imports");
    }

    // ── GetDependencyGraphAsync edgeKind filter ──────────────────────

    [Test]
    public async Task GetDependencyGraphFiltersByEdgeKind()
    {
        using var conn = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(conn);
        var repo = MakeRepo();
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);
        await store.InsertFilesAsync([
            MakeFile(repo.Id, "A.cs"),
            MakeFile(repo.Id, "B.cs"),
            MakeFile(repo.Id, "C.cs"),
        ]).ConfigureAwait(false);
        var files = await store.GetFilesByRepoAsync(repo.Id).ConfigureAwait(false);
        var a = files.First(f => f.RelativePath == "A.cs");
        var b = files.First(f => f.RelativePath == "B.cs");
        var c = files.First(f => f.RelativePath == "C.cs");

        await store.InsertDependenciesAsync([
            MakeDep(a.Id, "B", b.Id, "imports"),
            MakeDep(a.Id, "C", c.Id, "inherits"),
        ]).ConfigureAwait(false);

        var importsOnly = await store.GetDependencyGraphAsync(
            repo.Id, "A.cs", "dependencies", 5, "imports").ConfigureAwait(false);
        var inheritsOnly = await store.GetDependencyGraphAsync(
            repo.Id, "A.cs", "dependencies", 5, "inherits").ConfigureAwait(false);
        var allEdges = await store.GetDependencyGraphAsync(
            repo.Id, "A.cs", "dependencies", 5).ConfigureAwait(false);

        await Assert.That(importsOnly.Edges).Count().IsEqualTo(1);
        await Assert.That(importsOnly.Edges[0].To).IsEqualTo("B.cs");
        await Assert.That(inheritsOnly.Edges).Count().IsEqualTo(1);
        await Assert.That(inheritsOnly.Edges[0].To).IsEqualTo("C.cs");
        await Assert.That(allEdges.Edges).Count().IsEqualTo(2);
    }

    [Test]
    public async Task GetDependencyGraphEdgesIncludeEdgeKind()
    {
        using var conn = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(conn);
        var repo = MakeRepo();
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);
        await store.InsertFilesAsync([
            MakeFile(repo.Id, "A.cs"),
            MakeFile(repo.Id, "B.cs"),
        ]).ConfigureAwait(false);
        var files = await store.GetFilesByRepoAsync(repo.Id).ConfigureAwait(false);
        var a = files.First(f => f.RelativePath == "A.cs");
        var b = files.First(f => f.RelativePath == "B.cs");

        await store.InsertDependenciesAsync([
            MakeDep(a.Id, "B", b.Id, "inherits"),
        ]).ConfigureAwait(false);

        var graph = await store.GetDependencyGraphAsync(repo.Id, "A.cs", "dependencies", 5).ConfigureAwait(false);

        await Assert.That(graph.Edges).Count().IsEqualTo(1);
        await Assert.That(graph.Edges[0].EdgeKind).IsEqualTo("inherits");
    }

    // ── blast_radius ─────────────────────────────────────────────────

    [Test]
    public async Task BlastRadiusNoIncomingEdgesReturnsEmpty()
    {
        using var conn = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(conn);
        var repo = MakeRepo();
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);
        await store.InsertFilesAsync([MakeFile(repo.Id, "Isolated.cs")]).ConfigureAwait(false);

        var result = await store.GetBlastRadiusAsync(repo.Id, "Isolated.cs", null, 5).ConfigureAwait(false);

        await Assert.That(result.TotalAffected).IsEqualTo(0);
        await Assert.That(result.Depths).Count().IsEqualTo(0);
    }

    [Test]
    public async Task BlastRadiusDepth1ReturnsDirectImporters()
    {
        using var conn = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(conn);
        var repo = MakeRepo();
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);
        await store.InsertFilesAsync([
            MakeFile(repo.Id, "Core.cs"),
            MakeFile(repo.Id, "ServiceA.cs"),
            MakeFile(repo.Id, "ServiceB.cs"),
        ]).ConfigureAwait(false);
        var files = await store.GetFilesByRepoAsync(repo.Id).ConfigureAwait(false);
        var core = files.First(f => f.RelativePath == "Core.cs");
        var svcA = files.First(f => f.RelativePath == "ServiceA.cs");
        var svcB = files.First(f => f.RelativePath == "ServiceB.cs");

        await store.InsertDependenciesAsync([
            MakeDep(svcA.Id, "Core", core.Id),
            MakeDep(svcB.Id, "Core", core.Id),
        ]).ConfigureAwait(false);

        var result = await store.GetBlastRadiusAsync(repo.Id, "Core.cs", null, 5).ConfigureAwait(false);

        await Assert.That(result.TotalAffected).IsEqualTo(2);
        await Assert.That(result.Depths).Count().IsEqualTo(1);
        await Assert.That(result.Depths[0].Depth).IsEqualTo(1);
        await Assert.That(result.Depths[0].Files).Count().IsEqualTo(2);
    }

    [Test]
    public async Task BlastRadiusTransitiveDependentsGroupedByDepth()
    {
        using var conn = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(conn);
        var repo = MakeRepo();
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);
        // A <- B <- C (chain: A is imported by B, B is imported by C)
        await store.InsertFilesAsync([
            MakeFile(repo.Id, "A.cs"),
            MakeFile(repo.Id, "B.cs"),
            MakeFile(repo.Id, "C.cs"),
        ]).ConfigureAwait(false);
        var files = await store.GetFilesByRepoAsync(repo.Id).ConfigureAwait(false);
        var a = files.First(f => f.RelativePath == "A.cs");
        var b = files.First(f => f.RelativePath == "B.cs");
        var c = files.First(f => f.RelativePath == "C.cs");

        await store.InsertDependenciesAsync([
            MakeDep(b.Id, "A", a.Id),
            MakeDep(c.Id, "B", b.Id),
        ]).ConfigureAwait(false);

        var result = await store.GetBlastRadiusAsync(repo.Id, "A.cs", null, 5).ConfigureAwait(false);

        await Assert.That(result.TotalAffected).IsEqualTo(2);
        await Assert.That(result.Depths).Count().IsEqualTo(2);
        var depth1Files = result.Depths.First(d => d.Depth == 1).Files;
        var depth2Files = result.Depths.First(d => d.Depth == 2).Files;
        await Assert.That(depth1Files).Contains("B.cs");
        await Assert.That(depth2Files).Contains("C.cs");
    }

    [Test]
    public async Task BlastRadiusMaxDepthLimitsTraversal()
    {
        using var conn = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(conn);
        var repo = MakeRepo();
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);
        // A <- B <- C chain, but maxDepth=1 should stop at B
        await store.InsertFilesAsync([
            MakeFile(repo.Id, "A.cs"),
            MakeFile(repo.Id, "B.cs"),
            MakeFile(repo.Id, "C.cs"),
        ]).ConfigureAwait(false);
        var files = await store.GetFilesByRepoAsync(repo.Id).ConfigureAwait(false);
        var a = files.First(f => f.RelativePath == "A.cs");
        var b = files.First(f => f.RelativePath == "B.cs");
        var c = files.First(f => f.RelativePath == "C.cs");

        await store.InsertDependenciesAsync([
            MakeDep(b.Id, "A", a.Id),
            MakeDep(c.Id, "B", b.Id),
        ]).ConfigureAwait(false);

        var result = await store.GetBlastRadiusAsync(repo.Id, "A.cs", null, 1).ConfigureAwait(false);

        await Assert.That(result.TotalAffected).IsEqualTo(1);
        await Assert.That(result.Depths).Count().IsEqualTo(1);
        await Assert.That(result.Depths[0].Files).Contains("B.cs");
    }

    [Test]
    public async Task BlastRadiusFileNotInIndexReturnsEmpty()
    {
        using var conn = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(conn);
        var repo = MakeRepo();
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);

        var result = await store.GetBlastRadiusAsync(repo.Id, "nonexistent.cs", null, 5).ConfigureAwait(false);

        await Assert.That(result.TotalAffected).IsEqualTo(0);
        await Assert.That(result.Depths).Count().IsEqualTo(0);
    }

    [Test]
    public async Task BlastRadiusRootNotIncludedInResults()
    {
        using var conn = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(conn);
        var repo = MakeRepo();
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);
        await store.InsertFilesAsync([
            MakeFile(repo.Id, "Core.cs"),
            MakeFile(repo.Id, "Service.cs"),
        ]).ConfigureAwait(false);
        var files = await store.GetFilesByRepoAsync(repo.Id).ConfigureAwait(false);
        var core = files.First(f => f.RelativePath == "Core.cs");
        var svc = files.First(f => f.RelativePath == "Service.cs");

        await store.InsertDependenciesAsync([MakeDep(svc.Id, "Core", core.Id)]).ConfigureAwait(false);

        var result = await store.GetBlastRadiusAsync(repo.Id, "Core.cs", null, 5).ConfigureAwait(false);

        foreach (var depth in result.Depths)
        {
            await Assert.That(depth.Files).DoesNotContain("Core.cs");
        }
    }

    // ── find_unused_symbols ──────────────────────────────────────────

    [Test]
    public async Task FindUnusedSymbolsReturnsPublicSymbolWithNoIncomingEdges()
    {
        using var conn = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(conn);
        var repo = MakeRepo();
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);
        await store.InsertFilesAsync([MakeFile(repo.Id, "Helpers.cs")]).ConfigureAwait(false);
        var files = await store.GetFilesByRepoAsync(repo.Id).ConfigureAwait(false);
        var fileId = files[0].Id;

        await store.InsertSymbolsAsync([
            MakeSymbol(fileId, "LegacyHelper", "method", "public"),
        ]).ConfigureAwait(false);

        var result = await store.FindUnusedSymbolsAsync(repo.Id, 100).ConfigureAwait(false);

        await Assert.That(result.Any(s => s.Name == "LegacyHelper")).IsTrue();
    }

    [Test]
    public async Task FindUnusedSymbolsExcludesPrivateSymbols()
    {
        using var conn = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(conn);
        var repo = MakeRepo();
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);
        await store.InsertFilesAsync([MakeFile(repo.Id, "Helpers.cs")]).ConfigureAwait(false);
        var files = await store.GetFilesByRepoAsync(repo.Id).ConfigureAwait(false);
        var fileId = files[0].Id;

        await store.InsertSymbolsAsync([
            MakeSymbol(fileId, "PrivateHelper", "method", "private"),
        ]).ConfigureAwait(false);

        var result = await store.FindUnusedSymbolsAsync(repo.Id, 100).ConfigureAwait(false);

        await Assert.That(result.Any(s => s.Name == "PrivateHelper")).IsFalse();
    }

    [Test]
    public async Task FindUnusedSymbolsExcludesSymbolsInTestFiles()
    {
        using var conn = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(conn);
        var repo = MakeRepo();
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);
        await store.InsertFilesAsync([MakeFile(repo.Id, "tests/FooTests.cs")]).ConfigureAwait(false);
        var files = await store.GetFilesByRepoAsync(repo.Id).ConfigureAwait(false);
        var fileId = files[0].Id;

        await store.InsertSymbolsAsync([
            MakeSymbol(fileId, "TestMethod", "method", "public"),
        ]).ConfigureAwait(false);

        var result = await store.FindUnusedSymbolsAsync(repo.Id, 100).ConfigureAwait(false);

        await Assert.That(result.Any(s => s.Name == "TestMethod")).IsFalse();
    }

    [Test]
    public async Task FindUnusedSymbolsExcludesMainEntryPoint()
    {
        using var conn = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(conn);
        var repo = MakeRepo();
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);
        await store.InsertFilesAsync([MakeFile(repo.Id, "Program.cs")]).ConfigureAwait(false);
        var files = await store.GetFilesByRepoAsync(repo.Id).ConfigureAwait(false);
        var fileId = files[0].Id;

        await store.InsertSymbolsAsync([
            new Symbol(0, fileId, "Main", "method", "public static void Main()", null, 0, 10, 1, 5, "public", null, null, null),
        ]).ConfigureAwait(false);

        var result = await store.FindUnusedSymbolsAsync(repo.Id, 100).ConfigureAwait(false);

        await Assert.That(result.Any(s => s.Name == "Main")).IsFalse();
    }

    [Test]
    public async Task FindUnusedSymbolsLimitIsRespected()
    {
        using var conn = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(conn);
        var repo = MakeRepo();
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);
        await store.InsertFilesAsync([MakeFile(repo.Id, "Helpers.cs")]).ConfigureAwait(false);
        var files = await store.GetFilesByRepoAsync(repo.Id).ConfigureAwait(false);
        var fileId = files[0].Id;

        var symbols = Enumerable.Range(1, 10)
            .Select(i => MakeSymbol(fileId, $"Helper{i}", "method", "public"))
            .ToList();
        await store.InsertSymbolsAsync(symbols).ConfigureAwait(false);

        var result = await store.FindUnusedSymbolsAsync(repo.Id, 3).ConfigureAwait(false);

        await Assert.That(result).Count().IsEqualTo(3);
    }

    // ── direction = "both" ──────────────────────────────────────────

    [Test]
    public async Task GetDependencyGraphDirectionBothReturnsBidirectionalEdges()
    {
        using var conn = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(conn);
        var repo = MakeRepo();
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);
        // A -> B -> C: querying B with direction=both should return edges to A and C
        await store.InsertFilesAsync([
            MakeFile(repo.Id, "A.cs"),
            MakeFile(repo.Id, "B.cs"),
            MakeFile(repo.Id, "C.cs"),
        ]).ConfigureAwait(false);
        var files = await store.GetFilesByRepoAsync(repo.Id).ConfigureAwait(false);
        var a = files.First(f => f.RelativePath == "A.cs");
        var b = files.First(f => f.RelativePath == "B.cs");
        var c = files.First(f => f.RelativePath == "C.cs");

        await store.InsertDependenciesAsync([
            MakeDep(b.Id, "A", a.Id),   // B depends on A
            MakeDep(c.Id, "B", b.Id),   // C depends on B
        ]).ConfigureAwait(false);

        var graph = await store.GetDependencyGraphAsync(repo.Id, "B.cs", "both", 5).ConfigureAwait(false);

        // Should include edges in both directions
        var outgoing = graph.Edges.Where(e => e.From == "B.cs").ToList();
        var incoming = graph.Edges.Where(e => e.To == "B.cs").ToList();
        await Assert.That(outgoing).Count().IsGreaterThan(0);
        await Assert.That(incoming).Count().IsGreaterThan(0);
    }

    // ── Cross-repo isolation ─────────────────────────────────────────

    [Test]
    public async Task BlastRadiusDoesNotLeakAcrossRepos()
    {
        using var conn = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(conn);
        var repo1 = new Repository("repo1", "/root1", "Repo1", "csharp", 0, 0, 0);
        var repo2 = new Repository("repo2", "/root2", "Repo2", "csharp", 0, 0, 0);
        await store.UpsertRepositoryAsync(repo1).ConfigureAwait(false);
        await store.UpsertRepositoryAsync(repo2).ConfigureAwait(false);

        await store.InsertFilesAsync([MakeFile(repo1.Id, "Core.cs"), MakeFile(repo1.Id, "Service.cs")]).ConfigureAwait(false);
        await store.InsertFilesAsync([MakeFile(repo2.Id, "Core.cs"), MakeFile(repo2.Id, "OtherService.cs")]).ConfigureAwait(false);

        var repo1Files = await store.GetFilesByRepoAsync(repo1.Id).ConfigureAwait(false);
        var repo2Files = await store.GetFilesByRepoAsync(repo2.Id).ConfigureAwait(false);
        var core1 = repo1Files.First(f => f.RelativePath == "Core.cs");
        var svc1 = repo1Files.First(f => f.RelativePath == "Service.cs");
        var core2 = repo2Files.First(f => f.RelativePath == "Core.cs");
        var otherSvc2 = repo2Files.First(f => f.RelativePath == "OtherService.cs");

        await store.InsertDependenciesAsync([MakeDep(svc1.Id, "Core", core1.Id)]).ConfigureAwait(false);
        await store.InsertDependenciesAsync([MakeDep(otherSvc2.Id, "Core", core2.Id)]).ConfigureAwait(false);

        var result = await store.GetBlastRadiusAsync(repo1.Id, "Core.cs", null, 5).ConfigureAwait(false);

        // Only repo1's Service.cs should be affected — OtherService.cs from repo2 must not appear
        var allAffectedFiles = result.Depths.SelectMany(d => d.Files).ToList();
        await Assert.That(allAffectedFiles).Contains("Service.cs");
        await Assert.That(allAffectedFiles).DoesNotContain("OtherService.cs");
    }

    [Test]
    public async Task GetDependencyGraphDependentsDoesNotLeakAcrossRepos()
    {
        using var conn = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(conn);
        var repo1 = new Repository("repo1", "/root1", "Repo1", "csharp", 0, 0, 0);
        var repo2 = new Repository("repo2", "/root2", "Repo2", "csharp", 0, 0, 0);
        await store.UpsertRepositoryAsync(repo1).ConfigureAwait(false);
        await store.UpsertRepositoryAsync(repo2).ConfigureAwait(false);

        await store.InsertFilesAsync([MakeFile(repo1.Id, "Lib.cs"), MakeFile(repo1.Id, "Consumer.cs")]).ConfigureAwait(false);
        await store.InsertFilesAsync([MakeFile(repo2.Id, "Lib.cs"), MakeFile(repo2.Id, "OtherConsumer.cs")]).ConfigureAwait(false);

        var repo1Files = await store.GetFilesByRepoAsync(repo1.Id).ConfigureAwait(false);
        var repo2Files = await store.GetFilesByRepoAsync(repo2.Id).ConfigureAwait(false);
        var lib1 = repo1Files.First(f => f.RelativePath == "Lib.cs");
        var consumer1 = repo1Files.First(f => f.RelativePath == "Consumer.cs");
        var lib2 = repo2Files.First(f => f.RelativePath == "Lib.cs");
        var otherConsumer2 = repo2Files.First(f => f.RelativePath == "OtherConsumer.cs");

        await store.InsertDependenciesAsync([MakeDep(consumer1.Id, "Lib", lib1.Id)]).ConfigureAwait(false);
        await store.InsertDependenciesAsync([MakeDep(otherConsumer2.Id, "Lib", lib2.Id)]).ConfigureAwait(false);

        var graph = await store.GetDependencyGraphAsync(repo1.Id, "Lib.cs", "dependents", 5).ConfigureAwait(false);

        var dependentFiles = graph.Nodes.Where(n => n != "Lib.cs").ToList();
        await Assert.That(dependentFiles).Contains("Consumer.cs");
        await Assert.That(graph.Nodes).DoesNotContain("OtherConsumer.cs");
    }

    // ── Schema migration (edge_kind column) ──────────────────────────

    [Test]
    public async Task MigrationAddsEdgeKindColumnToExistingDatabase()
    {
        using var conn = await CreateTestConnectionAsync().ConfigureAwait(false);

        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name='dependencies'";
        var sql = (string?)await checkCmd.ExecuteScalarAsync().ConfigureAwait(false);

        await Assert.That(sql).IsNotNull();
        await Assert.That(sql!).Contains("edge_kind");
    }

    [Test]
    public async Task ExistingDependencyRowsDefaultToImportsEdgeKind()
    {
        using var conn = await CreateTestConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(conn);
        var repo = MakeRepo();
        await store.UpsertRepositoryAsync(repo).ConfigureAwait(false);
        await store.InsertFilesAsync([MakeFile(repo.Id, "A.cs")]).ConfigureAwait(false);
        var files = await store.GetFilesByRepoAsync(repo.Id).ConfigureAwait(false);
        var fileId = files[0].Id;

        // Insert row without specifying edge_kind (uses default)
        await store.InsertDependenciesAsync([new Dependency(0, fileId, "B", null, null)]).ConfigureAwait(false);

        var deps = await store.GetDependenciesByFileAsync(fileId).ConfigureAwait(false);
        await Assert.That(deps[0].EdgeKind).IsEqualTo("imports");
    }
}

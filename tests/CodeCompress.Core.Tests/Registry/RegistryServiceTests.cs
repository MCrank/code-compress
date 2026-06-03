using CodeCompress.Core.Models;
using CodeCompress.Core.Registry;
using CodeCompress.Core.Storage;
using CodeCompress.Core.Validation;
using Microsoft.Data.Sqlite;
using NSubstitute;

namespace CodeCompress.Core.Tests.Registry;

internal sealed class RegistryServiceTests
{
    // Default boundary used by the legacy tests — encloses both /home/user/projectA and projectB.
    private static readonly string DefaultBoundaryRoot = OperatingSystem.IsWindows()
        ? @"C:\home\user"
        : "/home/user";

    private string _dbName = string.Empty;

    private RegistryService CreateService(string? boundaryRoot = null) =>
        new(CreateMockFactory(), new BoundaryPolicy(boundaryRoot ?? DefaultBoundaryRoot));

    [Before(Test)]
    public void SetUp()
    {
        _dbName = $"registry-test-{Guid.NewGuid():N}";
    }

    private async Task<SqliteConnection> OpenSeedConnectionAsync()
    {
        var connection = new SqliteConnection($"Data Source={_dbName};Mode=Memory;Cache=Shared");
        await connection.OpenAsync().ConfigureAwait(false);
        await Migrations.ApplyAsync(connection).ConfigureAwait(false);
        return connection;
    }

    private async Task<SqliteConnection> OpenServiceConnectionAsync()
    {
        var connection = new SqliteConnection($"Data Source={_dbName};Mode=Memory;Cache=Shared");
        await connection.OpenAsync().ConfigureAwait(false);
        return connection;
    }

    private IConnectionFactory CreateMockFactory()
    {
        var factory = Substitute.For<IConnectionFactory>();
        factory.CreateConnectionAsync(Arg.Any<string>()).Returns(_ => OpenServiceConnectionAsync());
        return factory;
    }

    private static Repository BuildRepo(string id, string root, int files = 3, int symbols = 42) =>
        new(id, root, Path.GetFileName(root), "csharp",
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(), files, symbols);

    // ── ListAsync ────────────────────────────────────────────────────────

    [Test]
    public async Task ListAsyncReturnsEmptyArrayWhenNoReposIndexed()
    {
        using var seed = await OpenSeedConnectionAsync().ConfigureAwait(false);
        var service = CreateService();

        var result = await service.ListAsync().ConfigureAwait(false);

        await Assert.That(result).Count().IsEqualTo(0);
    }

    [Test]
    public async Task ListAsyncReturnsBothReposWhenTwoIndexed()
    {
        using var seed = await OpenSeedConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(seed);
        await store.UpsertRepositoryAsync(BuildRepo("id-1", "/home/user/projectA")).ConfigureAwait(false);
        await store.UpsertRepositoryAsync(BuildRepo("id-2", "/home/user/projectB")).ConfigureAwait(false);

        var service = CreateService();
        var result = await service.ListAsync().ConfigureAwait(false);

        await Assert.That(result).Count().IsEqualTo(2);
    }

    [Test]
    public async Task ListAsyncMapsDisplayNameFromDirectoryName()
    {
        using var seed = await OpenSeedConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(seed);
        await store.UpsertRepositoryAsync(BuildRepo("id-1", "/home/user/my-project")).ConfigureAwait(false);

        var service = CreateService();
        var result = await service.ListAsync().ConfigureAwait(false);

        await Assert.That(result[0].DisplayName).IsEqualTo("my-project");
    }

    [Test]
    public async Task ListAsyncMapsFileAndSymbolCounts()
    {
        using var seed = await OpenSeedConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(seed);
        await store.UpsertRepositoryAsync(BuildRepo("id-1", "/home/user/projectA", files: 7, symbols: 99)).ConfigureAwait(false);

        var service = CreateService();
        var result = await service.ListAsync().ConfigureAwait(false);

        await Assert.That(result[0].FileCount).IsEqualTo(7);
        await Assert.That(result[0].SymbolCount).IsEqualTo(99);
    }

    [Test]
    public async Task ListAsyncStatusIsHealthyWhenNoLastError()
    {
        using var seed = await OpenSeedConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(seed);
        await store.UpsertRepositoryAsync(BuildRepo("id-1", "/home/user/projectA")).ConfigureAwait(false);

        var service = CreateService();
        var result = await service.ListAsync().ConfigureAwait(false);

        await Assert.That(result[0].LastError).IsNull();
    }

    // ── UpdateStatsAsync ─────────────────────────────────────────────────

    [Test]
    public async Task UpdateStatsAsyncUpdatesFileAndSymbolCounts()
    {
        using var seed = await OpenSeedConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(seed);
        var repoId = SqliteConnectionFactory.ComputeRepoHash(Path.GetFullPath("/home/user/projectA"));
        await store.UpsertRepositoryAsync(BuildRepo(repoId, "/home/user/projectA", files: 1, symbols: 1)).ConfigureAwait(false);

        var service = CreateService();
        await service.UpdateStatsAsync("/home/user/projectA", 10, 50, DateTimeOffset.UtcNow).ConfigureAwait(false);

        var updated = await store.GetRepositoryAsync(repoId).ConfigureAwait(false);
        await Assert.That(updated!.FileCount).IsEqualTo(10);
        await Assert.That(updated.SymbolCount).IsEqualTo(50);
    }

    // ── DeregisterAsync ──────────────────────────────────────────────────

    [Test]
    public async Task DeregisterAsyncRemovesRepoFromDatabase()
    {
        using var seed = await OpenSeedConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(seed);
        var repoId = SqliteConnectionFactory.ComputeRepoHash(Path.GetFullPath("/home/user/projectA"));
        await store.UpsertRepositoryAsync(BuildRepo(repoId, "/home/user/projectA")).ConfigureAwait(false);

        var service = CreateService();
        await service.DeregisterAsync("/home/user/projectA").ConfigureAwait(false);

        var result = await store.GetRepositoryAsync(repoId).ConfigureAwait(false);
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task DeregisterAsyncLeavesOtherReposIntact()
    {
        using var seed = await OpenSeedConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(seed);
        var idA = SqliteConnectionFactory.ComputeRepoHash(Path.GetFullPath("/home/user/projectA"));
        var idB = SqliteConnectionFactory.ComputeRepoHash(Path.GetFullPath("/home/user/projectB"));
        await store.UpsertRepositoryAsync(BuildRepo(idA, "/home/user/projectA")).ConfigureAwait(false);
        await store.UpsertRepositoryAsync(BuildRepo(idB, "/home/user/projectB")).ConfigureAwait(false);

        var service = CreateService();
        await service.DeregisterAsync("/home/user/projectA").ConfigureAwait(false);

        var remaining = await store.GetAllRepositoriesAsync().ConfigureAwait(false);
        await Assert.That(remaining).Count().IsEqualTo(1);
        await Assert.That(remaining[0].Id).IsEqualTo(idB);
    }

    // ── Boundary filtering ───────────────────────────────────────

    [Test]
    public async Task ListAsyncExcludesReposOutsideBoundary()
    {
        using var seed = await OpenSeedConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(seed);
        await store.UpsertRepositoryAsync(BuildRepo("id-in", "/home/user/projectA")).ConfigureAwait(false);
        await store.UpsertRepositoryAsync(BuildRepo("id-out", "/home/other/secret")).ConfigureAwait(false);

        // Boundary is narrowed to projectA's parent — the sibling under /home/other is out of bounds.
        var service = CreateService("/home/user");
        var result = await service.ListAsync().ConfigureAwait(false);

        await Assert.That(result).Count().IsEqualTo(1);
        await Assert.That(result[0].DisplayName).IsEqualTo("projectA");
    }

    [Test]
    public async Task ListAsyncIncludesSiblingReposUnderBoundaryRoot()
    {
        using var seed = await OpenSeedConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(seed);
        await store.UpsertRepositoryAsync(BuildRepo("id-1", "/home/user/projectA")).ConfigureAwait(false);
        await store.UpsertRepositoryAsync(BuildRepo("id-2", "/home/user/projectB")).ConfigureAwait(false);

        var service = CreateService("/home/user");
        var result = await service.ListAsync().ConfigureAwait(false);

        await Assert.That(result).Count().IsEqualTo(2);
    }

    [Test]
    public async Task UpdateStatsAsyncIsNoOpForRepoOutsideBoundary()
    {
        using var seed = await OpenSeedConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(seed);
        var repoId = SqliteConnectionFactory.ComputeRepoHash(Path.GetFullPath("/home/other/secret"));
        await store.UpsertRepositoryAsync(BuildRepo(repoId, "/home/other/secret", files: 1, symbols: 1)).ConfigureAwait(false);

        var service = CreateService("/home/user");
        await service.UpdateStatsAsync("/home/other/secret", 99, 99, DateTimeOffset.UtcNow).ConfigureAwait(false);

        var unchanged = await store.GetRepositoryAsync(repoId).ConfigureAwait(false);
        await Assert.That(unchanged!.FileCount).IsEqualTo(1);
        await Assert.That(unchanged.SymbolCount).IsEqualTo(1);
    }

    [Test]
    public async Task DeregisterAsyncIsNoOpForRepoOutsideBoundary()
    {
        using var seed = await OpenSeedConnectionAsync().ConfigureAwait(false);
        var store = new SqliteSymbolStore(seed);
        var repoId = SqliteConnectionFactory.ComputeRepoHash(Path.GetFullPath("/home/other/secret"));
        await store.UpsertRepositoryAsync(BuildRepo(repoId, "/home/other/secret")).ConfigureAwait(false);

        var service = CreateService("/home/user");
        await service.DeregisterAsync("/home/other/secret").ConfigureAwait(false);

        var stillPresent = await store.GetRepositoryAsync(repoId).ConfigureAwait(false);
        await Assert.That(stillPresent).IsNotNull();
    }
}

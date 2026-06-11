using CodeCompress.Core.Indexing;
using CodeCompress.Core.Models;
using CodeCompress.Core.Registry;
using CodeCompress.Core.Storage;
using CodeCompress.Core.Validation;
using CodeCompress.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeCompress.Web.Tests;

internal sealed class IndexFacadeTests
{
    [Test]
    public async Task GetRepositoriesAsync_ReturnsListFromRegistry()
    {
        var registry = Substitute.For<IRegistryService>();
        var expected = new List<RepositoryRecord>
        {
            new("abc123", "C:/proj", "MyProject", 10, 100, DateTimeOffset.UtcNow, null),
        };
        registry.ListAsync().Returns(expected);

        var facade = CreateFacade(registry: registry);
        var result = await facade.GetRepositoriesAsync();

        await Assert.That(result).IsEquivalentTo(expected);
    }

    [Test]
    public async Task SearchSymbolsAsync_InvalidPath_ThrowsArgumentException()
    {
        var pathValidator = Substitute.For<IPathValidator>();
        pathValidator
            .ValidatePath(Arg.Any<string>(), Arg.Any<string>())
            .Throws(new ArgumentException("Path outside root"));

        var facade = CreateFacade(pathValidator: pathValidator);

        await Assert.ThrowsAsync<ArgumentException>(
            () => facade.SearchSymbolsAsync("../evil", "query", null, 20));
    }

    [Test]
    public async Task GetSnapshotsAsync_ReturnsSnapshotsForProject()
    {
        var registry = Substitute.For<IRegistryService>();
        registry.ListAsync().Returns([]);

        var store = Substitute.For<ISymbolStore>();
        var snapshots = new List<IndexSnapshot>
        {
            new(1L, "repo1", "before-refactor", DateTimeOffset.UtcNow.ToUnixTimeSeconds(), "{}"),
        };
        store.GetSnapshotsByRepoAsync(Arg.Any<string>()).Returns(snapshots);

        var facade = CreateFacadeWithStore(registry, store);
        var result = await facade.GetSnapshotsAsync("C:/proj");

        await Assert.That(result).IsEquivalentTo(snapshots);
    }

    private static IndexFacade CreateFacade(
        IRegistryService? registry = null,
        IPathValidator? pathValidator = null)
    {
        registry ??= Substitute.For<IRegistryService>();
        if (pathValidator is null)
        {
            pathValidator = Substitute.For<IPathValidator>();
            pathValidator
                .ValidatePath(Arg.Any<string>(), Arg.Any<string>())
                .Returns(x => (string)x[0]);
        }

        return new IndexFacade(
            registry,
            Substitute.For<IConnectionFactory>(),
            Substitute.For<IFileHasher>(),
            Substitute.For<IChangeTracker>(),
            [],
            pathValidator,
            Substitute.For<IGitIgnoreFilter>(),
            NullLoggerFactory.Instance);
    }

    private static IndexFacade CreateFacadeWithStore(
        IRegistryService registry,
        ISymbolStore store)
    {
        var pathValidator = Substitute.For<IPathValidator>();
        pathValidator
            .ValidatePath(Arg.Any<string>(), Arg.Any<string>())
            .Returns(x => (string)x[0]);

        return new IndexFacade(
            registry,
            Substitute.For<IConnectionFactory>(),
            Substitute.For<IFileHasher>(),
            Substitute.For<IChangeTracker>(),
            [],
            pathValidator,
            Substitute.For<IGitIgnoreFilter>(),
            NullLoggerFactory.Instance,
            store);
    }
}

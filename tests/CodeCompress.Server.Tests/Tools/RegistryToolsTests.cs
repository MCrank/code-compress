using CodeCompress.Core.Registry;
using CodeCompress.Server.Tools;
using NSubstitute;

namespace CodeCompress.Server.Tests.Tools;

internal sealed class RegistryToolsTests
{
    private IRegistryService _registryService = null!;
    private RegistryTools _tools = null!;

    [Before(Test)]
    public void SetUp()
    {
        _registryService = Substitute.For<IRegistryService>();
        _tools = new RegistryTools(_registryService);
    }

    [Test]
    public async Task ListReposReturnsEmptyArrayWhenNoReposIndexed()
    {
        _registryService.ListAsync().Returns([]);

        var result = await _tools.ListRepos().ConfigureAwait(false);

        await Assert.That(result.Repos).Count().IsEqualTo(0);
    }

    [Test]
    public async Task ListReposReturnsAllIndexedRepos()
    {
        var repos = new List<RepositoryRecord>
        {
            new("id1", "/home/user/projectA", "projectA", 10, 50, DateTimeOffset.UtcNow, null),
            new("id2", "/home/user/projectB", "projectB", 5, 20, DateTimeOffset.UtcNow, null),
        };
        _registryService.ListAsync().Returns(repos);

        var result = await _tools.ListRepos().ConfigureAwait(false);

        await Assert.That(result.Repos).Count().IsEqualTo(2);
    }

    [Test]
    public async Task ListReposIncludesRequiredFields()
    {
        var lastIndexed = new DateTimeOffset(2026, 4, 30, 12, 0, 0, TimeSpan.Zero);
        var repos = new List<RepositoryRecord>
        {
            new("id1", "/home/user/my-project", "my-project", 7, 99, lastIndexed, null),
        };
        _registryService.ListAsync().Returns(repos);

        var result = await _tools.ListRepos().ConfigureAwait(false);

        var item = result.Repos[0];
        await Assert.That(item.ProjectRoot).IsEqualTo("/home/user/my-project");
        await Assert.That(item.DisplayName).IsEqualTo("my-project");
        await Assert.That(item.FileCount).IsEqualTo(7);
        await Assert.That(item.SymbolCount).IsEqualTo(99);
        await Assert.That(item.Status).IsEqualTo("healthy");
    }

    [Test]
    public async Task ListReposStatusIsErrorWhenLastErrorPresent()
    {
        var repos = new List<RepositoryRecord>
        {
            new("id1", "/home/user/broken", "broken", 0, 0, DateTimeOffset.UtcNow, "Index corruption detected"),
        };
        _registryService.ListAsync().Returns(repos);

        var result = await _tools.ListRepos().ConfigureAwait(false);

        await Assert.That(result.Repos[0].Status).IsEqualTo("error");
    }
}

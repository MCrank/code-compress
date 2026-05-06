using Bunit;
using CodeCompress.Core.Registry;
using CodeCompress.Web.Components.Pages;
using CodeCompress.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace CodeCompress.Web.Tests;

internal sealed class DashboardTests : BunitContext
{
    [Test]
    public async Task Dashboard_ShowsRepositoryNamesFromFacade()
    {
        var facade = Substitute.For<IIndexFacade>();
        facade.GetRepositoriesAsync(Arg.Any<CancellationToken>())
            .Returns([new RepositoryRecord("abc123", "C:/project", "MyApp", 50, 500, DateTimeOffset.UtcNow, null)]);
        Services.AddSingleton(facade);

        var cut = Render<Dashboard>();
        await cut.WaitForStateAsync(() => cut.FindAll("[data-testid='repo-name']").Count > 0, TimeSpan.FromSeconds(3));

        await Assert.That(cut.Find("[data-testid='repo-name']").TextContent).IsEqualTo("MyApp");
    }

    [Test]
    public async Task Dashboard_ShowsEmptyStateWhenNoRepositories()
    {
        var facade = Substitute.For<IIndexFacade>();
        facade.GetRepositoriesAsync(Arg.Any<CancellationToken>()).Returns([]);
        Services.AddSingleton(facade);

        var cut = Render<Dashboard>();
        await cut.WaitForStateAsync(() => cut.FindAll("[data-testid='empty-state']").Count > 0, TimeSpan.FromSeconds(3));

        await Assert.That(cut.FindAll("[data-testid='empty-state']").Count).IsGreaterThan(0);
    }

    [Test]
    public async Task Dashboard_ShowsFileAndSymbolCounts()
    {
        var facade = Substitute.For<IIndexFacade>();
        facade.GetRepositoriesAsync(Arg.Any<CancellationToken>())
            .Returns([new RepositoryRecord("abc123", "C:/project", "MyApp", 42, 789, DateTimeOffset.UtcNow, null)]);
        Services.AddSingleton(facade);

        var cut = Render<Dashboard>();
        await cut.WaitForStateAsync(() => cut.FindAll("[data-testid='file-count']").Count > 0, TimeSpan.FromSeconds(3));

        await Assert.That(cut.Find("[data-testid='file-count']").TextContent).Contains("42");
        await Assert.That(cut.Find("[data-testid='symbol-count']").TextContent).Contains("789");
    }
}

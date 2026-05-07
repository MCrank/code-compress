using BlazorBlueprint.Components;
using Blazor.Diagrams.Components;
using Bunit;
using CodeCompress.Core.Models;
using CodeCompress.Core.Registry;
using CodeCompress.Web.Components.Pages;
using CodeCompress.Web.Services;
using CodeCompress.Web.Services.Graph;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace CodeCompress.Web.Tests;

internal sealed class GraphPageTests : BunitContext
{
    [Before(Test)]
    public void Setup()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddBlazorBlueprintComponents();
        ComponentFactories.AddStub<DiagramCanvas>("<div class=\"diagram-canvas-stub\"></div>");
    }

    [Test]
    public async Task Graph_InvalidRepoId_ShowsError()
    {
        var facade = Substitute.For<IIndexFacade>();
        facade.GetRepositoriesAsync(Arg.Any<CancellationToken>()).Returns([]);
        Services.AddSingleton(facade);
        Services.AddSingleton(new GraphDataService(facade));

        var cut = Render<Graph>(p => p.Add(x => x.RepoId, "nonexistent-hash"));
        await cut.WaitForStateAsync(() =>
            cut.FindAll("[data-testid='graph-error']").Count > 0,
            TimeSpan.FromSeconds(3));

        await Assert.That(cut.FindAll("[data-testid='graph-error']").Count).IsGreaterThan(0);
    }

    [Test]
    public async Task Graph_ValidRepoId_ShowsCanvasContainer()
    {
        var repo = new RepositoryRecord("abc123", "C:/project", "MyApp", 10, 100, DateTimeOffset.UtcNow, null);
        var facade = Substitute.For<IIndexFacade>();
        facade.GetRepositoriesAsync(Arg.Any<CancellationToken>()).Returns([repo]);
        facade.GetDependencyGraphAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DependencyGraph([], []));
        Services.AddSingleton(facade);
        Services.AddSingleton(new GraphDataService(facade));

        var cut = Render<Graph>(p => p.Add(x => x.RepoId, "abc123"));
        await cut.WaitForStateAsync(() =>
            cut.FindAll("[data-testid='graph-canvas']").Count > 0,
            TimeSpan.FromSeconds(3));

        await Assert.That(cut.FindAll("[data-testid='graph-canvas']").Count).IsGreaterThan(0);
    }

    [Test]
    public async Task Graph_ValidRepo_ShowsToolbar()
    {
        var repo = new RepositoryRecord("abc123", "C:/project", "MyApp", 5, 50, DateTimeOffset.UtcNow, null);
        var facade = Substitute.For<IIndexFacade>();
        facade.GetRepositoriesAsync(Arg.Any<CancellationToken>()).Returns([repo]);
        facade.GetDependencyGraphAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DependencyGraph([], []));
        Services.AddSingleton(facade);
        Services.AddSingleton(new GraphDataService(facade));

        var cut = Render<Graph>(p => p.Add(x => x.RepoId, "abc123"));
        await cut.WaitForStateAsync(() =>
            cut.FindAll("[data-testid='graph-toolbar']").Count > 0,
            TimeSpan.FromSeconds(3));

        await Assert.That(cut.FindAll("[data-testid='graph-toolbar']").Count).IsGreaterThan(0);
    }

    [Test]
    public async Task Graph_ValidRepo_ShowsEdgeLegend()
    {
        var repo = new RepositoryRecord("abc123", "C:/project", "MyApp", 5, 50, DateTimeOffset.UtcNow, null);
        var facade = Substitute.For<IIndexFacade>();
        facade.GetRepositoriesAsync(Arg.Any<CancellationToken>()).Returns([repo]);
        facade.GetDependencyGraphAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DependencyGraph([], []));
        Services.AddSingleton(facade);
        Services.AddSingleton(new GraphDataService(facade));

        var cut = Render<Graph>(p => p.Add(x => x.RepoId, "abc123"));
        await cut.WaitForStateAsync(() =>
            cut.FindAll("[data-testid='graph-legend']").Count > 0,
            TimeSpan.FromSeconds(3));

        await Assert.That(cut.FindAll("[data-testid='graph-legend']").Count).IsGreaterThan(0);
    }
}

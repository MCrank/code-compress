using CodeCompress.Core.Models;
using CodeCompress.Web.Services;
using CodeCompress.Web.Services.Graph;
using NSubstitute;

namespace CodeCompress.Web.Tests;

internal sealed class GraphDataServiceTests
{
    [Test]
    public async Task GetGraphAsync_MapsNodesToViewModels()
    {
        var facade = Substitute.For<IIndexFacade>();
        facade.GetDependencyGraphAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DependencyGraph(
                ["src/Foo.cs", "src/Bar.ts"],
                []));
        var svc = new GraphDataService(facade);

        var (nodes, _) = await svc.GetGraphAsync("C:/project");

        await Assert.That(nodes).Count().IsEqualTo(2);
        await Assert.That(nodes[0].RelativePath).IsEqualTo("src/Foo.cs");
        await Assert.That(nodes[0].Extension).IsEqualTo(".cs");
        await Assert.That(nodes[0].Directory).IsEqualTo("src");
        await Assert.That(nodes[1].RelativePath).IsEqualTo("src/Bar.ts");
        await Assert.That(nodes[1].Extension).IsEqualTo(".ts");
    }

    [Test]
    public async Task GetGraphAsync_MapsEdgesToViewModels()
    {
        var facade = Substitute.For<IIndexFacade>();
        facade.GetDependencyGraphAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DependencyGraph(
                ["A.cs", "B.cs"],
                [new DependencyEdge("B.cs", "A.cs", null, "imports")]));
        var svc = new GraphDataService(facade);

        var (_, edges) = await svc.GetGraphAsync("C:/project");

        await Assert.That(edges).Count().IsEqualTo(1);
        await Assert.That(edges[0].From).IsEqualTo("B.cs");
        await Assert.That(edges[0].To).IsEqualTo("A.cs");
        await Assert.That(edges[0].EdgeKind).IsEqualTo("imports");
    }

    [Test]
    public async Task GetGraphAsync_NullEdgeKind_DefaultsToImports()
    {
        var facade = Substitute.For<IIndexFacade>();
        facade.GetDependencyGraphAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DependencyGraph(
                ["A.cs", "B.cs"],
                [new DependencyEdge("B.cs", "A.cs", null, null)]));
        var svc = new GraphDataService(facade);

        var (_, edges) = await svc.GetGraphAsync("C:/project");

        await Assert.That(edges[0].EdgeKind).IsEqualTo("imports");
    }

    [Test]
    public async Task GetGraphAsync_EmptyGraph_ReturnsEmpty()
    {
        var facade = Substitute.For<IIndexFacade>();
        facade.GetDependencyGraphAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DependencyGraph([], []));
        var svc = new GraphDataService(facade);

        var (nodes, edges) = await svc.GetGraphAsync("C:/project");

        await Assert.That(nodes).Count().IsEqualTo(0);
        await Assert.That(edges).Count().IsEqualTo(0);
    }

    [Test]
    [Arguments(".cs", "#178600")]
    [Arguments(".ts", "#3178c6")]
    [Arguments(".js", "#f7df1e")]
    [Arguments(".py", "#3572a5")]
    [Arguments(".go", "#00add8")]
    [Arguments(".rs", "#dea584")]
    [Arguments(".java", "#b07219")]
    [Arguments(".tf", "#7b42bc")]
    [Arguments(".razor", "#512bd4")]
    [Arguments(".unknown", "#6b7280")]
    public async Task GetLanguageColor_ReturnsExpectedColor(string extension, string expected)
    {
        var color = GraphDataService.GetLanguageColor("file" + extension);
        await Assert.That(color).IsEqualTo(expected);
    }

    [Test]
    [Arguments(".cs", "code-2")]
    [Arguments(".razor", "layout")]
    [Arguments(".json", "braces")]
    [Arguments(".yaml", "file-text")]
    [Arguments(".yml", "file-text")]
    [Arguments(".xyz", "file")]
    public async Task GetLanguageIcon_ReturnsExpectedIcon(string extension, string expected)
    {
        var icon = GraphDataService.GetLanguageIcon("file" + extension);
        await Assert.That(icon).IsEqualTo(expected);
    }

    [Test]
    public async Task Constructor_NullFacade_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
        {
            _ = new GraphDataService(null!);
            return Task.CompletedTask;
        });
    }
}

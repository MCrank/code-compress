using CodeCompress.Web.Services.Graph;

namespace CodeCompress.Web.Tests;

internal sealed class DiagramBuilderTests
{
    [Test]
    public async Task Build_EmptyNodes_CreatesDiagramWithNoNodes()
    {
        var diagram = DiagramBuilder.Build([], []);

        await Assert.That(diagram.Nodes.Count).IsEqualTo(0);
        await Assert.That(diagram.Links.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Build_ThreeNodes_CreatesDiagramWithThreeNodes()
    {
        var nodes = new List<FileNodeViewModel>
        {
            new("src/A.cs", ".cs", "src"),
            new("src/B.cs", ".cs", "src"),
            new("lib/C.ts", ".ts", "lib"),
        };

        var diagram = DiagramBuilder.Build(nodes, []);

        await Assert.That(diagram.Nodes.Count).IsEqualTo(3);
    }

    [Test]
    public async Task Build_ValidEdge_CreatesLink()
    {
        var nodes = new List<FileNodeViewModel>
        {
            new("src/A.cs", ".cs", "src"),
            new("src/B.cs", ".cs", "src"),
        };
        var edges = new List<EdgeViewModel>
        {
            new("src/B.cs", "src/A.cs", "imports"),
        };

        var diagram = DiagramBuilder.Build(nodes, edges);

        await Assert.That(diagram.Links.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Build_EdgeWithUnknownNode_SkipsLink()
    {
        var nodes = new List<FileNodeViewModel>
        {
            new("src/A.cs", ".cs", "src"),
        };
        var edges = new List<EdgeViewModel>
        {
            new("src/B.cs", "src/A.cs", "imports"),
        };

        var diagram = DiagramBuilder.Build(nodes, edges);

        await Assert.That(diagram.Links.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Build_UnderGroupThreshold_CreatesNoGroups()
    {
        var nodes = Enumerable.Range(0, 5)
            .Select(i => new FileNodeViewModel($"dir{i}/file.cs", ".cs", $"dir{i}"))
            .ToList();

        var diagram = DiagramBuilder.Build(nodes, []);

        await Assert.That(diagram.Groups.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Build_OverGroupThreshold_CreatesGroups()
    {
        var nodes = Enumerable.Range(0, 110)
            .Select(i => new FileNodeViewModel($"dir{i % 5}/file{i}.cs", ".cs", $"dir{i % 5}"))
            .ToList();

        var diagram = DiagramBuilder.Build(nodes, []);

        await Assert.That(diagram.Groups.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task Build_MultipleEdgeKinds_AllLinksCreated()
    {
        var nodes = new List<FileNodeViewModel>
        {
            new("A.cs", ".cs", ""),
            new("B.cs", ".cs", ""),
            new("C.cs", ".cs", ""),
        };
        var edges = new List<EdgeViewModel>
        {
            new("B.cs", "A.cs", "imports"),
            new("C.cs", "A.cs", "calls"),
        };

        var diagram = DiagramBuilder.Build(nodes, edges);

        await Assert.That(diagram.Links.Count).IsEqualTo(2);
    }
}

using Blazor.Diagrams;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Options;
using CodeCompress.Web.Components.Graph;

namespace CodeCompress.Web.Services.Graph;

public static class DiagramBuilder
{
    private const int GroupThreshold = 100;
    private const int GridCols = 8;
    private const int NodeWidth = 220;
    private const int NodeHeight = 80;

    public static BlazorDiagram Build(
        IReadOnlyList<FileNodeViewModel> nodes,
        IReadOnlyList<EdgeViewModel> edges)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);

        var options = new BlazorDiagramOptions
        {
            AllowMultiSelection = true,
            AllowPanning = true,
            Zoom = { Minimum = 0.1, Maximum = 4.0 },
            Links = { RequireTarget = false },
        };

        var diagram = new BlazorDiagram(options);
        diagram.RegisterComponent<FileNodeModel, FileNodeComponent>();

        var nodeMap = new Dictionary<string, FileNodeModel>(StringComparer.Ordinal);

        diagram.SuspendRefresh = true;

        for (var i = 0; i < nodes.Count; i++)
        {
            var vm = nodes[i];
            var col = i % GridCols;
            var row = i / GridCols;
            var model = new FileNodeModel(vm, new Point(col * NodeWidth + 20, row * NodeHeight + 20));
            nodeMap[vm.RelativePath] = model;
            diagram.Nodes.Add(model);
        }

        foreach (var edge in edges)
        {
            if (nodeMap.TryGetValue(edge.From, out var from) &&
                nodeMap.TryGetValue(edge.To, out var to))
            {
                diagram.Links.Add(new LinkModel(from, to));
            }
        }

        if (nodes.Count > GroupThreshold)
        {
            var byDir = nodeMap.Values
                .GroupBy(n => n.ViewModel.Directory, StringComparer.Ordinal)
                .Where(g => g.Count() > 1);

            foreach (var dirGroup in byDir)
            {
                diagram.Groups.Group([.. dirGroup]);
            }
        }

        diagram.SuspendRefresh = false;
        diagram.Refresh();

        return diagram;
    }
}

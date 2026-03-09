namespace CodeCompress.Core.Models;

public sealed record DependencyGraph(
    IReadOnlyList<string> Nodes,
    IReadOnlyList<DependencyEdge> Edges);

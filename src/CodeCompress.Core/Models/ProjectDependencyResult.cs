namespace CodeCompress.Core.Models;

public sealed record ProjectNode(
    string Name,
    string RelativePath);

public sealed record ProjectDependencyEdge(
    string FromProject,
    string ToProject,
    IReadOnlyList<string> SharedTypes);

public sealed record ProjectDependencyResult(
    IReadOnlyList<ProjectNode> Projects,
    IReadOnlyList<ProjectDependencyEdge> Edges);

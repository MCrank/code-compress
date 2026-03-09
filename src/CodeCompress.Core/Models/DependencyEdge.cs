namespace CodeCompress.Core.Models;

public sealed record DependencyEdge(
    string From,
    string To,
    string? Alias);

namespace CodeCompress.Core.Models;

public sealed record DependencyInfo(
    string RequirePath,
    string? Alias);

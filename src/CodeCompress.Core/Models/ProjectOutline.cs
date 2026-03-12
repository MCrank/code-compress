namespace CodeCompress.Core.Models;

public sealed record ProjectOutline(
    string RepoId,
    IReadOnlyList<OutlineGroup> Groups,
    int TotalSymbolCount,
    bool IsTruncated);

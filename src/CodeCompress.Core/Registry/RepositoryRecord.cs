namespace CodeCompress.Core.Registry;

public sealed record RepositoryRecord(
    string RepoId,
    string ProjectRoot,
    string DisplayName,
    int FileCount,
    int SymbolCount,
    DateTimeOffset LastIndexed,
    string? LastError);

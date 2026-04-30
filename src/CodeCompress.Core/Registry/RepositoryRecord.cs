namespace CodeCompress.Core.Registry;

public sealed record RepositoryRecord(
    string ProjectRoot,
    string DisplayName,
    int FileCount,
    int SymbolCount,
    DateTimeOffset LastIndexed,
    string? LastError);

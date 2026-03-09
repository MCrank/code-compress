namespace CodeCompress.Core.Indexing;

public sealed record IndexResult(
    string RepoId,
    int FilesIndexed,
    int FilesSkipped,
    int FilesDeleted,
    int SymbolsFound,
    long DurationMs);

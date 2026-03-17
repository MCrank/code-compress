namespace CodeCompress.Core.Indexing;

public sealed record IndexResult(
    string RepoId,
    int FilesIndexed,
    int FilesUnchanged,
    int FilesDeleted,
    int SymbolsFound,
    long DurationMs)
{
    public int TotalFiles => FilesIndexed + FilesUnchanged + FilesDeleted;
}

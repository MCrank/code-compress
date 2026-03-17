namespace CodeCompress.Core.Indexing;

public sealed record ParseFailure(string FilePath, string Reason);

public sealed record IndexResult(
    string RepoId,
    int FilesIndexed,
    int FilesUnchanged,
    int FilesDeleted,
    int SymbolsFound,
    long DurationMs,
    IReadOnlyList<ParseFailure>? ParseFailures = null)
{
    public int TotalFiles => FilesIndexed + FilesUnchanged + FilesDeleted;
    public int FilesErrored => ParseFailures?.Count ?? 0;
}

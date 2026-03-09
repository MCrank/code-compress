namespace CodeCompress.Core.Models;

public sealed record ChangedFilesResult(
    IReadOnlyList<FileRecord> Added,
    IReadOnlyList<FileRecord> Modified,
    IReadOnlyList<string> Removed);

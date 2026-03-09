namespace CodeCompress.Core.Models;

public sealed record FileRecord(
    long Id,
    string RepoId,
    string RelativePath,
    string ContentHash,
    long ByteLength,
    int LineCount,
    long LastModified,
    long IndexedAt);

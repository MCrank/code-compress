namespace CodeCompress.Core.Models;

public sealed record IndexSnapshot(
    long Id,
    string RepoId,
    string SnapshotLabel,
    long CreatedAt,
    string FileHashes);

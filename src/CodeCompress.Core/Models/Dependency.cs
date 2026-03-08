namespace CodeCompress.Core.Models;

public sealed record Dependency(
    long Id,
    long FileId,
    string RequiresPath,
    long? ResolvedFileId,
    string? Alias);

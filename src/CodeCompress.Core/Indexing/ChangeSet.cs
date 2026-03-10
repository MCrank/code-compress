namespace CodeCompress.Core.Indexing;

public sealed record ChangeSet(
    IReadOnlyList<string> NewFiles,
    IReadOnlyList<string> ModifiedFiles,
    IReadOnlyList<string> DeletedFiles,
    IReadOnlyList<string> UnchangedFiles)
{
    public bool HasChanges => NewFiles.Count > 0 || ModifiedFiles.Count > 0 || DeletedFiles.Count > 0;
}

namespace CodeCompress.Core.Indexing;

public interface IChangeTracker
{
    public ChangeSet DetectChanges(
        Dictionary<string, string> currentHashes,
        Dictionary<string, string> storedHashes);
}

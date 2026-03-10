namespace CodeCompress.Core.Indexing;

public sealed class ChangeTracker : IChangeTracker
{
    public ChangeSet DetectChanges(
        Dictionary<string, string> currentHashes,
        Dictionary<string, string> storedHashes)
    {
        ArgumentNullException.ThrowIfNull(currentHashes);
        ArgumentNullException.ThrowIfNull(storedHashes);

        var storedKeys = new HashSet<string>(storedHashes.Keys, StringComparer.OrdinalIgnoreCase);
        var storedLookup = new Dictionary<string, string>(storedHashes, StringComparer.OrdinalIgnoreCase);

        var newFiles = new List<string>();
        var modifiedFiles = new List<string>();
        var unchangedFiles = new List<string>();

        foreach (var (path, hash) in currentHashes)
        {
            if (!storedLookup.TryGetValue(path, out var storedHash))
            {
                newFiles.Add(path);
            }
            else if (!string.Equals(hash, storedHash, StringComparison.Ordinal))
            {
                modifiedFiles.Add(path);
            }
            else
            {
                unchangedFiles.Add(path);
            }

            storedKeys.Remove(path);
        }

        var deletedFiles = new List<string>(storedKeys);

        return new ChangeSet(newFiles, modifiedFiles, deletedFiles, unchangedFiles);
    }
}

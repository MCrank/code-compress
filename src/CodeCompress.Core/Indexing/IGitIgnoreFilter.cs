namespace CodeCompress.Core.Indexing;

/// <summary>
/// Filters file paths by querying git for .gitignore rules.
/// Returns an empty set on graceful fallback (non-git directory, git not installed, timeout).
/// </summary>
public interface IGitIgnoreFilter
{
    /// <summary>
    /// Returns the subset of <paramref name="relativePaths"/> that git would ignore.
    /// </summary>
    public Task<HashSet<string>> GetIgnoredPathsAsync(
        string projectRoot,
        IReadOnlyList<string> relativePaths,
        CancellationToken cancellationToken = default);
}

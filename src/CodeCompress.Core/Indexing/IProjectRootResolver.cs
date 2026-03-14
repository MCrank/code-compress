namespace CodeCompress.Core.Indexing;

/// <summary>
/// Resolves the project root directory by walking up the directory tree
/// to find the nearest .git directory. Falls back to the given path if
/// no .git directory is found.
/// </summary>
public interface IProjectRootResolver
{
    /// <summary>
    /// Resolves the project root for the given path.
    /// Walks up the directory tree looking for a .git directory.
    /// Returns the directory containing .git, or the original path if not found.
    /// </summary>
    public string ResolveProjectRoot(string path);
}

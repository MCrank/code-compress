namespace CodeCompress.Core.Indexing;

/// <summary>
/// Resolves the project root by walking up the directory tree to find the nearest
/// .git directory. Falls back to the given path if no .git directory is found.
/// </summary>
public sealed class ProjectRootResolver : IProjectRootResolver
{
    internal const string GitDirectoryName = ".git";

    public string ResolveProjectRoot(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        var current = new DirectoryInfo(fullPath);

        while (current is not null)
        {
            var gitDir = Path.Combine(current.FullName, GitDirectoryName);
            if (Directory.Exists(gitDir))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        // No .git found — fall back to the original path
        return fullPath;
    }
}

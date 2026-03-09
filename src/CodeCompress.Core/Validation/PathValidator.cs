namespace CodeCompress.Core.Validation;

/// <summary>
/// Prevents path traversal attacks (OWASP A01) by canonicalizing paths
/// and verifying they resolve within a declared project root.
/// </summary>
public static class PathValidator
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    /// <summary>
    /// Validates that an absolute path resolves within the project root.
    /// Returns the canonicalized safe path if valid.
    /// </summary>
    public static string ValidatePath(string inputPath, string projectRoot)
    {
        ValidateInputNotEmpty(inputPath, nameof(inputPath));
        ValidateInputNotEmpty(projectRoot, nameof(projectRoot));
        RejectNullBytes(inputPath, nameof(inputPath));
        RejectNullBytes(projectRoot, nameof(projectRoot));

        string canonicalPath;
        string canonicalRoot;

        try
        {
            canonicalPath = Path.GetFullPath(inputPath);
            canonicalRoot = Path.GetFullPath(projectRoot);
        }
        catch (Exception ex) when (ex is PathTooLongException or NotSupportedException)
        {
            throw new ArgumentException("The provided path is invalid.", ex);
        }

        // Reject UNC paths unless root is also UNC
        if (IsUncPath(canonicalPath) && !IsUncPath(canonicalRoot))
        {
            throw new ArgumentException("UNC paths are not permitted.");
        }

        // Allow exact root match
        if (string.Equals(canonicalPath, canonicalRoot, PathComparison))
        {
            return canonicalPath;
        }

        // Normalize root to include trailing separator to prevent prefix false positives
        var rootWithSeparator = canonicalRoot.EndsWith(Path.DirectorySeparatorChar)
            ? canonicalRoot
            : canonicalRoot + Path.DirectorySeparatorChar;

        if (!canonicalPath.StartsWith(rootWithSeparator, PathComparison))
        {
            throw new ArgumentException("Path resolves outside the project root.");
        }

        // If the path exists and is a symlink, resolve and re-validate the target
        ValidateSymlinkTarget(canonicalPath, rootWithSeparator);

        return canonicalPath;
    }

    /// <summary>
    /// Resolves a relative path against the project root, then validates it.
    /// Returns the canonicalized absolute path.
    /// </summary>
    public static string ValidateRelativePath(string relativePath, string projectRoot)
    {
        ValidateInputNotEmpty(relativePath, nameof(relativePath));
        ValidateInputNotEmpty(projectRoot, nameof(projectRoot));

        var absolutePath = Path.GetFullPath(relativePath, projectRoot);
        return ValidatePath(absolutePath, projectRoot);
    }

    /// <summary>
    /// Non-throwing variant that returns true if the candidate resolves within root.
    /// </summary>
    public static bool IsWithinRoot(string candidatePath, string projectRoot)
    {
        try
        {
            ValidatePath(candidatePath, projectRoot);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (PathTooLongException)
        {
            return false;
        }
    }

    private static void ValidateInputNotEmpty(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be null, empty, or whitespace.", paramName);
        }
    }

    private static void RejectNullBytes(string value, string paramName)
    {
        if (value.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("Path contains invalid null byte character.", paramName);
        }
    }

    private static bool IsUncPath(string path)
    {
        return path.StartsWith(@"\\", StringComparison.Ordinal)
            || path.StartsWith("//", StringComparison.Ordinal);
    }

    private static void ValidateSymlinkTarget(string canonicalPath, string rootWithSeparator)
    {
        if (!File.Exists(canonicalPath) && !Directory.Exists(canonicalPath))
        {
            return;
        }

        var fileInfo = new FileInfo(canonicalPath);

        if (fileInfo.LinkTarget is null)
        {
            return;
        }

        var resolvedTarget = File.ResolveLinkTarget(canonicalPath, returnFinalTarget: true);

        if (resolvedTarget is null)
        {
            return;
        }

        var resolvedPath = Path.GetFullPath(resolvedTarget.FullName);

        if (!resolvedPath.StartsWith(rootWithSeparator, PathComparison)
            && !string.Equals(resolvedPath, rootWithSeparator.TrimEnd(Path.DirectorySeparatorChar), PathComparison))
        {
            throw new ArgumentException("Symbolic link resolves outside the project root.");
        }
    }
}

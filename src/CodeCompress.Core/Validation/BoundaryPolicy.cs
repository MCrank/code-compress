namespace CodeCompress.Core.Validation;

/// <summary>
/// Default <see cref="IBoundaryPolicy"/> implementation. Resolves the boundary root from the
/// <c>CODECOMPRESS_ROOT</c> environment variable (falling back to the process working directory)
/// and an optional <c>CODECOMPRESS_ALLOWED_ROOTS</c> allowlist, captured once at construction.
/// </summary>
public sealed class BoundaryPolicy : IBoundaryPolicy
{
    internal const string RootEnvVar = "CODECOMPRESS_ROOT";
    internal const string AllowedRootsEnvVar = "CODECOMPRESS_ALLOWED_ROOTS";

    public string BoundaryRoot { get; }

    public IReadOnlyList<string> AllowedRoots { get; }

    public BoundaryPolicy(string boundaryRoot, IReadOnlyList<string>? allowedRoots = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(boundaryRoot);

        BoundaryRoot = Path.GetFullPath(boundaryRoot);
        AllowedRoots = CanonicalizeRoots(allowedRoots);
    }

    /// <summary>
    /// Builds a policy from the live environment: <c>CODECOMPRESS_ROOT</c> (or the current working
    /// directory when unset) and the <c>CODECOMPRESS_ALLOWED_ROOTS</c> allowlist.
    /// </summary>
    public static BoundaryPolicy FromEnvironment() =>
        Create(
            Environment.GetEnvironmentVariable(RootEnvVar),
            Environment.GetEnvironmentVariable(AllowedRootsEnvVar),
            Directory.GetCurrentDirectory());

    /// <summary>
    /// Pure factory used by <see cref="FromEnvironment"/> and tests: resolves the boundary root and
    /// allowlist from explicit inputs without reading process state.
    /// </summary>
    internal static BoundaryPolicy Create(string? rootEnv, string? allowedRootsEnv, string currentDirectory)
    {
        var root = string.IsNullOrWhiteSpace(rootEnv) ? currentDirectory : rootEnv;
        return new BoundaryPolicy(root, ParseAllowedRoots(allowedRootsEnv));
    }

    public bool IsWithinBoundary(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return PathValidator.IsWithinRoot(path, BoundaryRoot)
            || AllowedRoots.Any(root => PathValidator.IsWithinRoot(path, root));
    }

    public string EnsureWithinBoundary(string path)
    {
        if (!IsWithinBoundary(path))
        {
            throw new BoundaryViolationException();
        }

        return Path.GetFullPath(path);
    }

    private static List<string> ParseAllowedRoots(string? allowedRootsEnv)
    {
        if (string.IsNullOrWhiteSpace(allowedRootsEnv))
        {
            return [];
        }

        var parts = allowedRootsEnv.Split(
            Path.PathSeparator,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return CanonicalizeRoots(parts);
    }

    private static List<string> CanonicalizeRoots(IReadOnlyList<string>? roots)
    {
        if (roots is null || roots.Count == 0)
        {
            return [];
        }

        var canonical = new List<string>(roots.Count);
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            try
            {
                var full = Path.GetFullPath(root);

                // Reject filesystem-root entries (e.g. "/", "C:\") — granting the entire volume as an
                // allowlist root would defeat the boundary. Skip rather than fail startup.
                if (!IsFilesystemRoot(full))
                {
                    canonical.Add(full);
                }
            }
            catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
            {
                // Skip malformed allowlist entries rather than failing startup.
            }
        }

        return canonical;
    }

    private static bool IsFilesystemRoot(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath);
        return !string.IsNullOrEmpty(root)
            && string.Equals(
                fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}

namespace CodeCompress.Core.Validation;

/// <summary>
/// Defines the access boundary that clamps the MCP server to the directory it was launched from
/// (and its descendants), plus any explicitly allowlisted roots. Used to prevent an agent from
/// scanning, enumerating, or exfiltrating repositories outside its working directory.
/// The boundary is resolved once at startup and is immutable for the process lifetime.
/// </summary>
public interface IBoundaryPolicy
{
    /// <summary>The canonicalized root directory the server is permitted to operate at or below.</summary>
    public string BoundaryRoot { get; }

    /// <summary>Additional canonicalized roots that are explicitly permitted beyond the boundary root.</summary>
    public IReadOnlyList<string> AllowedRoots { get; }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="path"/> resolves at or below the boundary root
    /// or any allowlisted root; otherwise <c>false</c>. Never throws.
    /// </summary>
    public bool IsWithinBoundary(string path);

    /// <summary>
    /// Returns the canonicalized form of <paramref name="path"/> when it is within the boundary;
    /// throws <see cref="BoundaryViolationException"/> otherwise.
    /// </summary>
    public string EnsureWithinBoundary(string path);
}

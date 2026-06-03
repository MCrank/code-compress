namespace CodeCompress.Core.Validation;

public sealed class PathValidatorService : IPathValidator
{
    private readonly IBoundaryPolicy? _boundaryPolicy;

    /// <summary>
    /// Creates an unrestricted validator that enforces only path-traversal safety (no access boundary).
    /// Intentionally <c>internal</c> — production code must use the boundary-aware constructor (selected by DI).
    /// This overload exists only for first-party tests that index temporary directories.
    /// </summary>
    internal PathValidatorService()
    {
    }

    /// <summary>
    /// Creates a boundary-aware validator. In addition to the path-traversal check, every validated
    /// path must resolve within the configured boundary; otherwise a <see cref="BoundaryViolationException"/>
    /// is thrown. This is the single choke point that clamps all MCP tools to the launch directory.
    /// </summary>
    public PathValidatorService(IBoundaryPolicy boundaryPolicy)
    {
        ArgumentNullException.ThrowIfNull(boundaryPolicy);
        _boundaryPolicy = boundaryPolicy;
    }

    public string ValidatePath(string inputPath, string projectRoot)
    {
        var canonical = PathValidator.ValidatePath(inputPath, projectRoot);
        EnforceBoundary(canonical);
        return canonical;
    }

    public string ValidateRelativePath(string relativePath, string projectRoot)
    {
        var canonical = PathValidator.ValidateRelativePath(relativePath, projectRoot);
        EnforceBoundary(canonical);
        return canonical;
    }

    public bool IsWithinRoot(string candidatePath, string projectRoot)
    {
        if (!PathValidator.IsWithinRoot(candidatePath, projectRoot))
        {
            return false;
        }

        return _boundaryPolicy is null || _boundaryPolicy.IsWithinBoundary(candidatePath);
    }

    private void EnforceBoundary(string canonicalPath)
    {
        if (_boundaryPolicy is not null && !_boundaryPolicy.IsWithinBoundary(canonicalPath))
        {
            throw new BoundaryViolationException();
        }
    }
}

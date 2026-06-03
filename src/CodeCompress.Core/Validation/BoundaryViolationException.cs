namespace CodeCompress.Core.Validation;

/// <summary>
/// Thrown when a requested path resolves outside the server's configured access boundary
/// (the launch working directory / <c>CODECOMPRESS_ROOT</c> and any explicit allowlist roots).
/// Derives from <see cref="ArgumentException"/> so existing path-validation catch blocks
/// treat boundary violations uniformly as an invalid path, without leaking why the path was rejected.
/// </summary>
public sealed class BoundaryViolationException : ArgumentException
{
    public BoundaryViolationException()
        : base("Path resolves outside the permitted boundary.")
    {
    }

    public BoundaryViolationException(string message)
        : base(message)
    {
    }

    public BoundaryViolationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

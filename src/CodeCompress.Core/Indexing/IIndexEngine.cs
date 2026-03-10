namespace CodeCompress.Core.Indexing;

public interface IIndexEngine
{
    public Task<IndexResult> IndexProjectAsync(
        string projectRoot,
        string? language = null,
        string[]? includePatterns = null,
        string[]? excludePatterns = null,
        CancellationToken cancellationToken = default);
}

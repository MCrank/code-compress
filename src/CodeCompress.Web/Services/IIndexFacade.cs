using CodeCompress.Core.Indexing;
using CodeCompress.Core.Models;
using CodeCompress.Core.Registry;

namespace CodeCompress.Web.Services;

public interface IIndexFacade
{
    public Task<IReadOnlyList<RepositoryRecord>> GetRepositoriesAsync(CancellationToken ct = default);
    public Task<IndexResult> ReIndexAsync(string projectRoot, CancellationToken ct = default);
    public Task<IReadOnlyList<SymbolSearchResult>> SearchSymbolsAsync(string projectRoot, string query, string? kind, int limit, CancellationToken ct = default);
    public Task<IReadOnlyList<IndexSnapshot>> GetSnapshotsAsync(string projectRoot, CancellationToken ct = default);
    public Task<IReadOnlyList<FileRecord>> GetFilesAsync(string projectRoot, CancellationToken ct = default);
}

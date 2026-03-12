using CodeCompress.Core.Models;

namespace CodeCompress.Core.Storage;

public interface ISymbolStore
{
    // Repository
    public Task UpsertRepositoryAsync(Repository repo);
    public Task<Repository?> GetRepositoryAsync(string repoId);
    public Task DeleteRepositoryAsync(string repoId);

    // Files
    public Task InsertFilesAsync(IReadOnlyList<FileRecord> files);
    public Task<IReadOnlyList<FileRecord>> GetFilesByRepoAsync(string repoId);
    public Task<FileRecord?> GetFileByPathAsync(string repoId, string relativePath);
    public Task UpdateFileAsync(FileRecord file);
    public Task DeleteFileAsync(long fileId);

    // Symbols
    public Task InsertSymbolsAsync(IReadOnlyList<Symbol> symbols);
    public Task<IReadOnlyList<Symbol>> GetSymbolsByFileAsync(long fileId);
    public Task DeleteSymbolsByFileAsync(long fileId);

    // Dependencies
    public Task InsertDependenciesAsync(IReadOnlyList<Dependency> deps);
    public Task<IReadOnlyList<Dependency>> GetDependenciesByFileAsync(long fileId);
    public Task DeleteDependenciesByFileAsync(long fileId);

    // Snapshots
    public Task<long> CreateSnapshotAsync(IndexSnapshot snapshot);
    public Task<IndexSnapshot?> GetSnapshotAsync(long snapshotId);
    public Task<IndexSnapshot?> GetSnapshotByLabelAsync(string repoId, string snapshotLabel);
    public Task<IReadOnlyList<IndexSnapshot>> GetSnapshotsByRepoAsync(string repoId);

    // File Content FTS
    public Task UpsertFileContentAsync(string relativePath, string content);
    public Task DeleteFileContentAsync(string relativePath);

    // Search
    public Task<IReadOnlyList<SymbolSearchResult>> SearchSymbolsAsync(string repoId, string query, string? kind, int limit, string? pathFilter = null, string? nameLikePattern = null);
    public Task<IReadOnlyList<TextSearchResult>> SearchTextAsync(string repoId, string query, string? glob, int limit, string? pathFilter = null);
    public Task<IReadOnlyList<ReferenceResult>> FindReferencesAsync(string repoId, string symbolName, string projectRoot, int limit, string? pathFilter = null);

    // Lookups
    public Task<Symbol?> GetSymbolByNameAsync(string repoId, string symbolName);
    public Task<IReadOnlyList<Symbol>> GetSymbolsByNamesAsync(string repoId, IReadOnlyList<string> symbolNames);

    // Aggregation
    public Task<ProjectOutline> GetProjectOutlineAsync(string repoId, bool includePrivate, string groupBy, int maxDepth, string? pathFilter = null, int offset = 0, int limit = 0);
    public Task<ModuleApi> GetModuleApiAsync(string repoId, string filePath);
    public Task<DependencyGraph> GetDependencyGraphAsync(string repoId, string? rootFile, string direction, int depth);
    public Task<ChangedFilesResult> GetChangedFilesAsync(string repoId, long snapshotId);
}

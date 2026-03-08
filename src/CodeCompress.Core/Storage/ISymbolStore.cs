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
    public Task<IReadOnlyList<IndexSnapshot>> GetSnapshotsByRepoAsync(string repoId);
}

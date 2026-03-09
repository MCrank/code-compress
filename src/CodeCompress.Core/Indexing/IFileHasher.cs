namespace CodeCompress.Core.Indexing;

public interface IFileHasher
{
    public Task<string> HashFileAsync(string filePath, CancellationToken cancellationToken = default);
    public Task<Dictionary<string, string>> HashFilesAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default);
}

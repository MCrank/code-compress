using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace CodeCompress.Core.Indexing;

public sealed class FileHasher : IFileHasher
{
    private const int BufferSize = 8192;

    public async Task<string> HashFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: BufferSize,
                useAsync: true);

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
            {
                hash.AppendData(buffer, 0, bytesRead);
            }

            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public async Task<Dictionary<string, string>> HashFilesAsync(
        IEnumerable<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        var results = new ConcurrentDictionary<string, string>();

        await Parallel.ForEachAsync(
            filePaths,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = cancellationToken,
            },
            async (path, ct) =>
            {
                var hash = await HashFileAsync(path, ct).ConfigureAwait(false);
                results[path] = hash;
            }).ConfigureAwait(false);

        return new Dictionary<string, string>(results);
    }
}

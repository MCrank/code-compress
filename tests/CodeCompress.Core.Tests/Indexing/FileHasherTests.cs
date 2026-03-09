using System.Security.Cryptography;
using CodeCompress.Core.Indexing;

namespace CodeCompress.Core.Tests.Indexing;

internal sealed class FileHasherTests
{
    private string _tempDir = null!;
    private FileHasher _hasher = null!;

    [Before(Test)]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FileHasherTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _hasher = new FileHasher();
    }

    [After(Test)]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private string CreateTempFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Test]
    public async Task HashFileAsyncReturnsExpectedSha256ForKnownContent()
    {
        var path = CreateTempFile("known.txt", "hello world");

        var hash = await _hasher.HashFileAsync(path).ConfigureAwait(false);

        // SHA-256 of "hello world" (UTF-8, no BOM)
        await Assert.That(hash).IsEqualTo("b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9");
    }

    [Test]
    public async Task HashFileAsyncReturnsSameHashForSameContent()
    {
        var path1 = CreateTempFile("file1.txt", "identical content");
        var path2 = CreateTempFile("file2.txt", "identical content");

        var hash1 = await _hasher.HashFileAsync(path1).ConfigureAwait(false);
        var hash2 = await _hasher.HashFileAsync(path2).ConfigureAwait(false);

        await Assert.That(hash1).IsEqualTo(hash2);
    }

    [Test]
    public async Task HashFileAsyncReturnsDifferentHashForDifferentContent()
    {
        var path1 = CreateTempFile("a.txt", "content A");
        var path2 = CreateTempFile("b.txt", "content B");

        var hash1 = await _hasher.HashFileAsync(path1).ConfigureAwait(false);
        var hash2 = await _hasher.HashFileAsync(path2).ConfigureAwait(false);

        await Assert.That(hash1).IsNotEqualTo(hash2);
    }

    [Test]
    public async Task HashFilesAsyncReturnsAllResultsCorrectly()
    {
        var paths = new List<string>
        {
            CreateTempFile("p1.txt", "alpha"),
            CreateTempFile("p2.txt", "beta"),
            CreateTempFile("p3.txt", "gamma"),
        };

        var results = await _hasher.HashFilesAsync(paths).ConfigureAwait(false);

        await Assert.That(results).Count().IsEqualTo(3);
        foreach (var path in paths)
        {
            var expected = await _hasher.HashFileAsync(path).ConfigureAwait(false);
            await Assert.That(results[path]).IsEqualTo(expected);
        }
    }

    [Test]
    public async Task HashFileAsyncThrowsFileNotFoundExceptionForMissingFile()
    {
        var missingPath = Path.Combine(_tempDir, "does_not_exist.txt");

        await Assert.That(async () => await _hasher.HashFileAsync(missingPath).ConfigureAwait(false))
            .Throws<FileNotFoundException>();
    }

    [Test]
    public async Task HashFileAsyncReturnsCorrectHashForEmptyFile()
    {
        var path = CreateTempFile("empty.txt", string.Empty);

        var hash = await _hasher.HashFileAsync(path).ConfigureAwait(false);

        // SHA-256 of empty input
        await Assert.That(hash).IsEqualTo("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    }

    [Test]
    public async Task HashFileAsyncCompletesForLargeFile()
    {
        var path = Path.Combine(_tempDir, "large.bin");
        var data = new byte[2 * 1024 * 1024]; // 2 MB
        RandomNumberGenerator.Fill(data);
        await File.WriteAllBytesAsync(path, data).ConfigureAwait(false);

        var hash = await _hasher.HashFileAsync(path).ConfigureAwait(false);

        await Assert.That(hash).Length().IsEqualTo(64); // SHA-256 hex = 64 chars
    }

    [Test]
    public async Task HashFileAsyncThrowsWhenCancelled()
    {
        var path = CreateTempFile("cancel.txt", "some content");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(false);

        await Assert.That(async () => await _hasher.HashFileAsync(path, cts.Token).ConfigureAwait(false))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task HashFilesAsyncThrowsWhenCancelledMidBatch()
    {
        var paths = new List<string>();
        for (var i = 0; i < 50; i++)
        {
            paths.Add(CreateTempFile($"batch_{i}.txt", $"content_{i}_{new string('x', 1000)}"));
        }

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(false);

        await Assert.That(async () => await _hasher.HashFilesAsync(paths, cts.Token).ConfigureAwait(false))
            .Throws<OperationCanceledException>();
    }
}

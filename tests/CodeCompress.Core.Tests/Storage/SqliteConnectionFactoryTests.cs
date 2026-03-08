using CodeCompress.Core.Storage;

namespace CodeCompress.Core.Tests.Storage;

internal sealed class SqliteConnectionFactoryTests
{
    [Test]
    public async Task ComputeRepoHashIsDeterministic()
    {
        var hash1 = SqliteConnectionFactory.ComputeRepoHash("/home/user/project");
        var hash2 = SqliteConnectionFactory.ComputeRepoHash("/home/user/project");

        await Assert.That(hash1).IsEqualTo(hash2);
    }

    [Test]
    public async Task ComputeRepoHashNormalizesSlashes()
    {
        var hashForward = SqliteConnectionFactory.ComputeRepoHash("C:/foo/bar");
        var hashBackward = SqliteConnectionFactory.ComputeRepoHash(@"C:\foo\bar");

        await Assert.That(hashForward).IsEqualTo(hashBackward);
    }

    [Test]
    public async Task ComputeRepoHashRemovesTrailingSlash()
    {
        var hashWithout = SqliteConnectionFactory.ComputeRepoHash("/home/user/project");
        var hashWithTrailing = SqliteConnectionFactory.ComputeRepoHash("/home/user/project/");

        await Assert.That(hashWithout).IsEqualTo(hashWithTrailing);
    }

    [Test]
    public async Task ComputeRepoHashProduces64CharHexString()
    {
        var hash = SqliteConnectionFactory.ComputeRepoHash("/some/path");

        await Assert.That(hash.Length).IsEqualTo(64);
        await Assert.That(hash).Matches("^[0-9a-f]{64}$");
    }

    [Test]
    public async Task ComputeRepoHashDifferentPathsProduceDifferentHashes()
    {
        var hash1 = SqliteConnectionFactory.ComputeRepoHash("/home/user/projectA");
        var hash2 = SqliteConnectionFactory.ComputeRepoHash("/home/user/projectB");

        await Assert.That(hash1).IsNotEqualTo(hash2);
    }

    [Test]
    public async Task CreateConnectionAsyncRejectsNullPath()
    {
        var factory = new SqliteConnectionFactory();

        await Assert.ThrowsAsync<ArgumentException>(
            () => factory.CreateConnectionAsync(null!));
    }

    [Test]
    public async Task CreateConnectionAsyncRejectsEmptyPath()
    {
        var factory = new SqliteConnectionFactory();

        await Assert.ThrowsAsync<ArgumentException>(
            () => factory.CreateConnectionAsync(""));
    }

    [Test]
    public async Task CreateConnectionAsyncRejectsWhitespacePath()
    {
        var factory = new SqliteConnectionFactory();

        await Assert.ThrowsAsync<ArgumentException>(
            () => factory.CreateConnectionAsync("   "));
    }

    [Test]
    public async Task CreateConnectionAsyncRejectsPathTraversal()
    {
        var factory = new SqliteConnectionFactory();

        await Assert.ThrowsAsync<ArgumentException>(
            () => factory.CreateConnectionAsync("/valid/../../etc/passwd"));
    }
}

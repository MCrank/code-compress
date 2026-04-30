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
    public async Task GlobalCodeCompressDirIsUnderUserProfile()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".code-compress");

        await Assert.That(SqliteConnectionFactory.GlobalCodeCompressDir).IsEqualTo(expected);
    }

    [Test]
    public async Task CreateConnectionAsyncCreatesGlobalDb()
    {
        var factory = new SqliteConnectionFactory();
        var connection = await factory.CreateConnectionAsync("/any/project/root").ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var expectedDbPath = Path.Combine(SqliteConnectionFactory.GlobalCodeCompressDir, "index.db");
            await Assert.That(connection.DataSource).IsEqualTo(expectedDbPath);
        }
    }
}

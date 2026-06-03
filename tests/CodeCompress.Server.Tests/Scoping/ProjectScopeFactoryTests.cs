using CodeCompress.Core.Indexing;
using CodeCompress.Core.Parsers;
using CodeCompress.Core.Storage;
using CodeCompress.Core.Validation;
using CodeCompress.Server.Scoping;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace CodeCompress.Server.Tests.Scoping;

internal sealed class ProjectScopeFactoryTests
{
    private static readonly string BoundaryRoot = OperatingSystem.IsWindows()
        ? @"C:\work\repoA"
        : "/work/repoA";

    private static readonly string SubDirectory = OperatingSystem.IsWindows()
        ? @"C:\work\repoA\src"
        : "/work/repoA/src";

    private static readonly string EscapingGitRoot = OperatingSystem.IsWindows()
        ? @"C:\work"
        : "/work";

    private string _dbName = string.Empty;

    [Before(Test)]
    public void SetUp()
    {
        _dbName = $"scope-factory-test-{Guid.NewGuid():N}";
    }

    private async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection($"Data Source={_dbName};Mode=Memory;Cache=Shared");
        await connection.OpenAsync().ConfigureAwait(false);
        await Migrations.ApplyAsync(connection).ConfigureAwait(false);
        return connection;
    }

    private ProjectScopeFactory CreateFactory(IProjectRootResolver resolver, IBoundaryPolicy boundaryPolicy)
    {
        var connectionFactory = Substitute.For<IConnectionFactory>();
        connectionFactory.CreateConnectionAsync(Arg.Any<string>()).Returns(_ => OpenConnectionAsync());

        return new ProjectScopeFactory(
            connectionFactory,
            Substitute.For<IFileHasher>(),
            Substitute.For<IChangeTracker>(),
            Array.Empty<ILanguageParser>(),
            new PathValidatorService(boundaryPolicy),
            Substitute.For<IGitIgnoreFilter>(),
            resolver,
            boundaryPolicy,
            NullLoggerFactory.Instance);
    }

    [Test]
    public async Task CreateAsyncUsesResolvedGitRootWhenWithinBoundary()
    {
        var resolver = Substitute.For<IProjectRootResolver>();
        resolver.ResolveProjectRoot(Arg.Any<string>()).Returns(BoundaryRoot);
        var factory = CreateFactory(resolver, new BoundaryPolicy(BoundaryRoot));

        var scope = await factory.CreateAsync(BoundaryRoot).ConfigureAwait(false);
        await using (scope.ConfigureAwait(false))
        {
            await Assert.That(scope.ProjectRoot).IsEqualTo(Path.GetFullPath(BoundaryRoot));
        }
    }

    [Test]
    public async Task CreateAsyncClampsToRequestedPathWhenGitRootEscapesBoundary()
    {
        // Boundary is a subdirectory; git-root resolution walks UP past it. The effective root
        // must be clamped to the requested (in-bounds) path, never the escaping git root.
        var resolver = Substitute.For<IProjectRootResolver>();
        resolver.ResolveProjectRoot(Arg.Any<string>()).Returns(EscapingGitRoot);
        var factory = CreateFactory(resolver, new BoundaryPolicy(SubDirectory));

        var scope = await factory.CreateAsync(SubDirectory).ConfigureAwait(false);
        await using (scope.ConfigureAwait(false))
        {
            await Assert.That(scope.ProjectRoot).IsEqualTo(Path.GetFullPath(SubDirectory));
            await Assert.That(scope.ProjectRoot).IsNotEqualTo(Path.GetFullPath(EscapingGitRoot));
        }
    }
}

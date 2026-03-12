using System.Text.Json;
using CodeCompress.Core.Indexing;
using CodeCompress.Core.Models;
using CodeCompress.Core.Storage;
using CodeCompress.Core.Validation;
using CodeCompress.Server.Scoping;
using CodeCompress.Server.Services;
using CodeCompress.Server.Tools;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeCompress.Server.Tests.Tools;

internal sealed class ProjectDependenciesToolTests
{
    private IPathValidator _pathValidator = null!;
    private IProjectScopeFactory _scopeFactory = null!;
    private IProjectScope _scope = null!;
    private IIndexEngine _engine = null!;
    private ISymbolStore _store = null!;
    private IActivityTracker _activityTracker = null!;
    private DependencyTools _tools = null!;

    [Before(Test)]
    public void SetUp()
    {
        _pathValidator = Substitute.For<IPathValidator>();
        _scopeFactory = Substitute.For<IProjectScopeFactory>();
        _scope = Substitute.For<IProjectScope>();
        _engine = Substitute.For<IIndexEngine>();
        _store = Substitute.For<ISymbolStore>();
        _activityTracker = Substitute.For<IActivityTracker>();

        _scope.Engine.Returns(_engine);
        _scope.Store.Returns(_store);
        _scope.RepoId.Returns("test-repo-id");
        _scopeFactory.CreateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_scope);
        _pathValidator.ValidatePath(Arg.Any<string>(), Arg.Any<string>()).Returns(callInfo => callInfo.ArgAt<string>(0));

        _tools = new DependencyTools(_pathValidator, _scopeFactory, _activityTracker);
    }

    // ── 1. BasicProjectGraphShowsReferences ─────────────────────────

    [Test]
    public async Task BasicProjectGraphShowsReferences()
    {
        var result = new ProjectDependencyResult(
            [
                new ProjectNode("Core", "src/Core/Core.csproj"),
                new ProjectNode("Server", "src/Server/Server.csproj"),
            ],
            [
                new ProjectDependencyEdge("Server", "Core", ["Interface: IService", "Class: ServiceBase"]),
            ]);

        _store.GetProjectDependencyGraphAsync("test-repo-id", null).Returns(result);

        var output = await _tools.ProjectDependencies("/valid/path").ConfigureAwait(false);

        await Assert.That(output).Contains("Project dependencies:");
        await Assert.That(output).Contains("[Core]");
        await Assert.That(output).Contains("[Server]");
        await Assert.That(output).Contains("references -> Core");
        await Assert.That(output).Contains("via Core: Interface: IService, Class: ServiceBase");
    }

    // ── 2. ProjectFilterShowsFilteredResults ────────────────────────

    [Test]
    public async Task ProjectFilterShowsFilteredResults()
    {
        var result = new ProjectDependencyResult(
            [new ProjectNode("Server", "src/Server/Server.csproj")],
            [new ProjectDependencyEdge("Server", "Core", [])]);

        _store.GetProjectDependencyGraphAsync("test-repo-id", "Server").Returns(result);

        var output = await _tools.ProjectDependencies("/valid/path", projectFilter: "Server").ConfigureAwait(false);

        await Assert.That(output).Contains("filter: \"Server\"");
        await Assert.That(output).Contains("[Server]");
    }

    // ── 3. NoProjectsFoundReturnsError ──────────────────────────────

    [Test]
    public async Task NoProjectsFoundReturnsError()
    {
        var result = new ProjectDependencyResult([], []);

        _store.GetProjectDependencyGraphAsync("test-repo-id", null).Returns(result);

        var output = await _tools.ProjectDependencies("/valid/path").ConfigureAwait(false);

        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo("NO_PROJECTS");
    }

    // ── 4. PathValidationRejectsTraversal ───────────────────────────

    [Test]
    public async Task PathValidationRejectsTraversal()
    {
        _pathValidator.ValidatePath(Arg.Any<string>(), Arg.Any<string>())
            .Throws(new ArgumentException("Path traversal detected"));

        var output = await _tools.ProjectDependencies("/../../../etc/passwd").ConfigureAwait(false);

        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo("INVALID_PATH");
    }

    // ── 5. ProjectWithNoReferencesShowsNone ─────────────────────────

    [Test]
    public async Task ProjectWithNoReferencesShowsNone()
    {
        var result = new ProjectDependencyResult(
            [new ProjectNode("Standalone", "src/Standalone/Standalone.csproj")],
            []);

        _store.GetProjectDependencyGraphAsync("test-repo-id", null).Returns(result);

        var output = await _tools.ProjectDependencies("/valid/path").ConfigureAwait(false);

        await Assert.That(output).Contains("[Standalone]");
        await Assert.That(output).Contains("references -> (none)");
        await Assert.That(output).Contains("referenced by -> (none)");
    }

    // ── 6. SummaryShowsTotalCounts ──────────────────────────────────

    [Test]
    public async Task SummaryShowsTotalCounts()
    {
        var result = new ProjectDependencyResult(
            [
                new ProjectNode("A", "src/A/A.csproj"),
                new ProjectNode("B", "src/B/B.csproj"),
                new ProjectNode("C", "src/C/C.csproj"),
            ],
            [
                new ProjectDependencyEdge("B", "A", []),
                new ProjectDependencyEdge("C", "A", []),
            ]);

        _store.GetProjectDependencyGraphAsync("test-repo-id", null).Returns(result);

        var output = await _tools.ProjectDependencies("/valid/path").ConfigureAwait(false);

        await Assert.That(output).Contains("Total: 3 projects, 2 project references");
    }

    // ── 7. ReferencedByShowsIncomingEdges ───────────────────────────

    [Test]
    public async Task ReferencedByShowsIncomingEdges()
    {
        var result = new ProjectDependencyResult(
            [
                new ProjectNode("Core", "src/Core/Core.csproj"),
                new ProjectNode("Server", "src/Server/Server.csproj"),
                new ProjectNode("Cli", "src/Cli/Cli.csproj"),
            ],
            [
                new ProjectDependencyEdge("Server", "Core", []),
                new ProjectDependencyEdge("Cli", "Core", []),
            ]);

        _store.GetProjectDependencyGraphAsync("test-repo-id", null).Returns(result);

        var output = await _tools.ProjectDependencies("/valid/path").ConfigureAwait(false);

        await Assert.That(output).Contains("referenced by -> Cli, Server");
    }

    // ── 8. RecordsActivity ──────────────────────────────────────────

    [Test]
    public async Task RecordsActivity()
    {
        var result = new ProjectDependencyResult(
            [new ProjectNode("A", "src/A/A.csproj")],
            []);

        _store.GetProjectDependencyGraphAsync("test-repo-id", null).Returns(result);

        await _tools.ProjectDependencies("/valid/path").ConfigureAwait(false);

        _activityTracker.Received(1).RecordActivity();
    }

    // ── 9. MultipleReferencesFromSameProjectSorted ──────────────────

    [Test]
    public async Task MultipleReferencesFromSameProjectSorted()
    {
        var result = new ProjectDependencyResult(
            [
                new ProjectNode("App", "src/App/App.csproj"),
                new ProjectNode("Core", "src/Core/Core.csproj"),
                new ProjectNode("Infra", "src/Infra/Infra.csproj"),
            ],
            [
                new ProjectDependencyEdge("App", "Infra", []),
                new ProjectDependencyEdge("App", "Core", []),
            ]);

        _store.GetProjectDependencyGraphAsync("test-repo-id", null).Returns(result);

        var output = await _tools.ProjectDependencies("/valid/path").ConfigureAwait(false);

        await Assert.That(output).Contains("references -> Core, Infra");
    }
}

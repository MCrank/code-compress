using System.Text.Json;
using CodeCompress.Core.Indexing;
using CodeCompress.Core.Models;
using CodeCompress.Core.Storage;
using CodeCompress.Core.Validation;
using CodeCompress.Server.Scoping;
using CodeCompress.Server.Tools;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeCompress.Server.Tests.Tools;

internal sealed class DependencyToolsTests
{
    private IPathValidator _pathValidator = null!;
    private IProjectScopeFactory _scopeFactory = null!;
    private IProjectScope _scope = null!;
    private IIndexEngine _engine = null!;
    private ISymbolStore _store = null!;
    private DependencyTools _tools = null!;

    [Before(Test)]
    public void SetUp()
    {
        _pathValidator = Substitute.For<IPathValidator>();
        _scopeFactory = Substitute.For<IProjectScopeFactory>();
        _scope = Substitute.For<IProjectScope>();
        _engine = Substitute.For<IIndexEngine>();
        _store = Substitute.For<ISymbolStore>();
        _scope.Engine.Returns(_engine);
        _scope.Store.Returns(_store);
        _scope.RepoId.Returns("test-repo-id");
        _scopeFactory.CreateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_scope);
        _pathValidator.ValidatePath(Arg.Any<string>(), Arg.Any<string>()).Returns(callInfo => callInfo.ArgAt<string>(0));
        _pathValidator.ValidateRelativePath(Arg.Any<string>(), Arg.Any<string>()).Returns(callInfo => Path.Combine(callInfo.ArgAt<string>(1), callInfo.ArgAt<string>(0)));

        _tools = new DependencyTools(_pathValidator, _scopeFactory);
    }

    // ── 1. SingleFileDependenciesReturnsOutgoingEdges ───────────────

    [Test]
    public async Task SingleFileDependenciesReturnsOutgoingEdges()
    {
        var graph = new DependencyGraph(
            ["CombatService.luau", "GameTypes.luau", "WeaponConfig.luau"],
            [
                new DependencyEdge("CombatService.luau", "GameTypes.luau", null),
                new DependencyEdge("CombatService.luau", "WeaponConfig.luau", null),
            ]);

        _store.GetDependencyGraphAsync("test-repo-id", "CombatService.luau", "dependencies", Arg.Any<int>())
            .Returns(graph);

        var result = await _tools.DependencyGraph("/valid/path", rootFile: "CombatService.luau", direction: "dependencies").ConfigureAwait(false);

        await Assert.That(result).Contains("requires -> GameTypes.luau, WeaponConfig.luau");
    }

    // ── 2. SingleFileDependentsReturnsIncomingEdges ─────────────────

    [Test]
    public async Task SingleFileDependentsReturnsIncomingEdges()
    {
        var graph = new DependencyGraph(
            ["GameTypes.luau", "CombatService.luau", "AIService.luau"],
            [
                new DependencyEdge("CombatService.luau", "GameTypes.luau", null),
                new DependencyEdge("AIService.luau", "GameTypes.luau", null),
            ]);

        _store.GetDependencyGraphAsync("test-repo-id", "GameTypes.luau", "dependents", Arg.Any<int>())
            .Returns(graph);

        var result = await _tools.DependencyGraph("/valid/path", rootFile: "GameTypes.luau", direction: "dependents").ConfigureAwait(false);

        await Assert.That(result).Contains("required by ->");
        await Assert.That(result).Contains("CombatService.luau");
        await Assert.That(result).Contains("AIService.luau");
    }

    // ── 3. SingleFileBothReturnsBothDirections ─────────────────────

    [Test]
    public async Task SingleFileBothReturnsBothDirections()
    {
        var graph = new DependencyGraph(
            ["CombatService.luau", "GameTypes.luau", "AgentService.luau"],
            [
                new DependencyEdge("CombatService.luau", "GameTypes.luau", null),
                new DependencyEdge("AgentService.luau", "CombatService.luau", null),
            ]);

        _store.GetDependencyGraphAsync("test-repo-id", "CombatService.luau", "both", Arg.Any<int>())
            .Returns(graph);

        var result = await _tools.DependencyGraph("/valid/path", rootFile: "CombatService.luau", direction: "both").ConfigureAwait(false);

        await Assert.That(result).Contains("requires ->");
        await Assert.That(result).Contains("required by ->");
    }

    // ── 4. DepthLimitingStopsAtSpecifiedDepth ──────────────────────

    [Test]
    public async Task DepthLimitingStopsAtSpecifiedDepth()
    {
        var graph = new DependencyGraph(
            ["A.luau", "B.luau"],
            [new DependencyEdge("A.luau", "B.luau", null)]);

        _store.GetDependencyGraphAsync("test-repo-id", "A.luau", "both", 1)
            .Returns(graph);

        var result = await _tools.DependencyGraph("/valid/path", rootFile: "A.luau", direction: "both", depth: 1).ConfigureAwait(false);

        await _store.Received(1).GetDependencyGraphAsync("test-repo-id", "A.luau", "both", 1).ConfigureAwait(false);
        await Assert.That(result).Contains("(depth: 1");
    }

    // ── 5. FullProjectGraphNoRootFileReturnsAllEdges ───────────────

    [Test]
    public async Task FullProjectGraphNoRootFileReturnsAllEdges()
    {
        var graph = new DependencyGraph(
            ["File1.luau", "File2.luau", "File3.luau"],
            [
                new DependencyEdge("File1.luau", "File2.luau", null),
                new DependencyEdge("File2.luau", "File3.luau", null),
            ]);

        _store.GetDependencyGraphAsync("test-repo-id", null, "both", Arg.Any<int>())
            .Returns(graph);

        var result = await _tools.DependencyGraph("/valid/path", rootFile: null, direction: "both").ConfigureAwait(false);

        await Assert.That(result).Contains("full project");
        await Assert.That(result).Contains("Total:");
        await Assert.That(result).Contains("3 files");
        await Assert.That(result).Contains("2 dependency edges");
    }

    // ── 6. FileWithNoDependenciesReturnsEmptyEdges ─────────────────

    [Test]
    public async Task FileWithNoDependenciesReturnsEmptyEdges()
    {
        var graph = new DependencyGraph(
            ["Isolated.luau"],
            []);

        _store.GetDependencyGraphAsync("test-repo-id", "Isolated.luau", "both", Arg.Any<int>())
            .Returns(graph);

        var result = await _tools.DependencyGraph("/valid/path", rootFile: "Isolated.luau", direction: "both").ConfigureAwait(false);

        await Assert.That(result).Contains("(none)");
    }

    // ── 7. NonExistentFileReturnsError ─────────────────────────────

    [Test]
    public async Task NonExistentFileReturnsError()
    {
        var graph = new DependencyGraph([], []);

        _store.GetDependencyGraphAsync("test-repo-id", "missing.luau", "both", Arg.Any<int>())
            .Returns(graph);

        var result = await _tools.DependencyGraph("/valid/path", rootFile: "missing.luau", direction: "both").ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo("FILE_NOT_FOUND");
    }

    // ── 8. CircularDependenciesNoInfiniteLoop ──────────────────────

    [Test]
    public async Task CircularDependenciesNoInfiniteLoop()
    {
        var graph = new DependencyGraph(
            ["A.luau", "B.luau"],
            [
                new DependencyEdge("A.luau", "B.luau", null),
                new DependencyEdge("B.luau", "A.luau", null),
            ]);

        _store.GetDependencyGraphAsync("test-repo-id", "A.luau", "both", Arg.Any<int>())
            .Returns(graph);

        var result = await _tools.DependencyGraph("/valid/path", rootFile: "A.luau", direction: "both").ConfigureAwait(false);

        await Assert.That(result).Contains("A.luau");
        await Assert.That(result).Contains("B.luau");
    }

    // ── 9. InvalidDirectionReturnsError ────────────────────────────

    [Test]
    public async Task InvalidDirectionReturnsError()
    {
        var result = await _tools.DependencyGraph("/valid/path", rootFile: "file.luau", direction: "invalid").ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo("INVALID_DIRECTION");
    }

    // ── 10. PathValidationRejectsTraversal ─────────────────────────

    [Test]
    public async Task PathValidationRejectsTraversal()
    {
        _pathValidator.ValidatePath(Arg.Any<string>(), Arg.Any<string>())
            .Throws(new ArgumentException("Path traversal detected"));

        var result = await _tools.DependencyGraph("/../../../etc/passwd").ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("error").GetString()).IsEqualTo("Path validation failed");
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo("INVALID_PATH");
    }

    // ── 11. DepthClampingExcessiveDepthClamped ─────────────────────

    [Test]
    public async Task DepthClampingExcessiveDepthClamped()
    {
        var graph = new DependencyGraph(["A.luau"], []);

        _store.GetDependencyGraphAsync("test-repo-id", "A.luau", "both", 50)
            .Returns(graph);

        await _tools.DependencyGraph("/valid/path", rootFile: "A.luau", direction: "both", depth: 999).ConfigureAwait(false);

        await _store.Received(1).GetDependencyGraphAsync(
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string>(), 50).ConfigureAwait(false);
    }

    // ── 12. MultipleRootsInGraphCorrectTraversal ───────────────────

    [Test]
    public async Task MultipleRootsInGraphCorrectTraversal()
    {
        var graph = new DependencyGraph(
            ["Main.luau", "Lib.luau", "Utils.luau", "Config.luau"],
            [
                new DependencyEdge("Main.luau", "Lib.luau", null),
                new DependencyEdge("Main.luau", "Utils.luau", null),
                new DependencyEdge("Lib.luau", "Config.luau", null),
                new DependencyEdge("Utils.luau", "Config.luau", null),
            ]);

        _store.GetDependencyGraphAsync("test-repo-id", "Main.luau", "both", Arg.Any<int>())
            .Returns(graph);

        var result = await _tools.DependencyGraph("/valid/path", rootFile: "Main.luau", direction: "both").ConfigureAwait(false);

        await Assert.That(result).Contains("Main.luau");
        await Assert.That(result).Contains("Lib.luau");
        await Assert.That(result).Contains("Utils.luau");
        await Assert.That(result).Contains("Config.luau");
    }

    // ── 13. TransitiveDependenciesFullChain ────────────────────────

    [Test]
    public async Task TransitiveDependenciesFullChain()
    {
        var graph = new DependencyGraph(
            ["A.luau", "B.luau", "C.luau", "D.luau"],
            [
                new DependencyEdge("A.luau", "B.luau", null),
                new DependencyEdge("B.luau", "C.luau", null),
                new DependencyEdge("C.luau", "D.luau", null),
            ]);

        _store.GetDependencyGraphAsync("test-repo-id", "A.luau", "dependencies", Arg.Any<int>())
            .Returns(graph);

        var result = await _tools.DependencyGraph("/valid/path", rootFile: "A.luau", direction: "dependencies").ConfigureAwait(false);

        await Assert.That(result).Contains("A.luau");
        await Assert.That(result).Contains("B.luau");
        await Assert.That(result).Contains("C.luau");
        await Assert.That(result).Contains("D.luau");
    }

    // ── BlastRadius ──────────────────────────────────────────────────

    [Test]
    public async Task BlastRadiusFoundReturnsDepthsAndTotalAffected()
    {
        var blastResult = new BlastRadiusResult(
            true,
            3,
            [
                new BlastRadiusDepth(1, ["A.luau", "B.luau"]),
                new BlastRadiusDepth(2, ["C.luau"]),
            ]);

        _store.GetBlastRadiusAsync("test-repo-id", "Root.luau", null, Arg.Any<int>())
            .Returns(blastResult);

        var result = await _tools.BlastRadius("/valid/path", filePath: "Root.luau").ConfigureAwait(false);

        await Assert.That(result.TotalAffected).IsEqualTo(3);
        await Assert.That(result.Depths).Count().IsEqualTo(2);
        await Assert.That(result.Depths![0].Depth).IsEqualTo(1);
        await Assert.That(result.Depths![0].Files).Count().IsEqualTo(2);
        await Assert.That(result.Depths![1].Depth).IsEqualTo(2);
    }

    [Test]
    public async Task BlastRadiusBySymbolNameReturnsDepths()
    {
        var blastResult = new BlastRadiusResult(true, 1, [new BlastRadiusDepth(1, ["Caller.luau"])]);

        _store.GetBlastRadiusAsync("test-repo-id", null, "ProcessPayment", Arg.Any<int>())
            .Returns(blastResult);

        var result = await _tools.BlastRadius("/valid/path", symbolName: "ProcessPayment").ConfigureAwait(false);

        await Assert.That(result.TotalAffected).IsEqualTo(1);
    }

    [Test]
    public async Task BlastRadiusNotFoundReturnsError()
    {
        var blastResult = new BlastRadiusResult(false, 0, []);

        _store.GetBlastRadiusAsync("test-repo-id", "Missing.luau", null, Arg.Any<int>())
            .Returns(blastResult);

        var result = await _tools.BlastRadius("/valid/path", filePath: "Missing.luau").ConfigureAwait(false);

        await Assert.That(result.Error).IsNotNull();
        await Assert.That(result.Code).IsEqualTo("NOT_FOUND");
    }

    [Test]
    public async Task BlastRadiusNeitherFilePathNorSymbolNameReturnsError()
    {
        var result = await _tools.BlastRadius("/valid/path").ConfigureAwait(false);

        await Assert.That(result.Code).IsEqualTo("INVALID_INPUT");
    }

    [Test]
    public async Task BlastRadiusInvalidPathReturnsError()
    {
        _pathValidator.ValidatePath(Arg.Any<string>(), Arg.Any<string>())
            .Throws(new ArgumentException("Path traversal detected"));

        var result = await _tools.BlastRadius("/../../../etc/passwd", filePath: "A.luau").ConfigureAwait(false);

        await Assert.That(result.Error).IsEqualTo("Path validation failed");
        await Assert.That(result.Code).IsEqualTo("INVALID_PATH");
    }

    [Test]
    public async Task BlastRadiusMaxDepthClampedToUpperBound()
    {
        var blastResult = new BlastRadiusResult(true, 0, []);
        _store.GetBlastRadiusAsync("test-repo-id", "A.luau", null, 20).Returns(blastResult);

        await _tools.BlastRadius("/valid/path", filePath: "A.luau", maxDepth: 999).ConfigureAwait(false);

        await _store.Received(1).GetBlastRadiusAsync("test-repo-id", "A.luau", null, 20).ConfigureAwait(false);
    }

    // ── FindUnusedSymbols ────────────────────────────────────────────

    [Test]
    public async Task FindUnusedSymbolsReturnsResultsWrappedInObject()
    {
        var symbols = new List<SymbolSummary>
        {
            new("UnusedHelper", "Function", "function UnusedHelper()"),
            new("DeadClass", "Class", "class DeadClass"),
        };
        _store.FindUnusedSymbolsAsync("test-repo-id", Arg.Any<int>()).Returns(symbols);

        var result = await _tools.FindUnusedSymbols("/valid/path").ConfigureAwait(false);

        await Assert.That(result.Results).Count().IsEqualTo(2);
        await Assert.That(result.Results![0].Name).IsEqualTo("UnusedHelper");
        await Assert.That(result.Results![0].Kind).IsEqualTo("Function");
        await Assert.That(result.Results![1].Name).IsEqualTo("DeadClass");
    }

    [Test]
    public async Task FindUnusedSymbolsEmptyReturnsEmptyResults()
    {
        _store.FindUnusedSymbolsAsync("test-repo-id", Arg.Any<int>()).Returns(new List<SymbolSummary>());

        var result = await _tools.FindUnusedSymbols("/valid/path").ConfigureAwait(false);

        await Assert.That(result.Results).Count().IsEqualTo(0);
    }

    [Test]
    public async Task FindUnusedSymbolsInvalidPathReturnsError()
    {
        _pathValidator.ValidatePath(Arg.Any<string>(), Arg.Any<string>())
            .Throws(new ArgumentException("Path traversal detected"));

        var result = await _tools.FindUnusedSymbols("/../../../etc/passwd").ConfigureAwait(false);

        await Assert.That(result.Error).IsEqualTo("Path validation failed");
        await Assert.That(result.Code).IsEqualTo("INVALID_PATH");
    }

    [Test]
    public async Task FindUnusedSymbolsLimitClampedToUpperBound()
    {
        _store.FindUnusedSymbolsAsync("test-repo-id", 500).Returns(new List<SymbolSummary>());

        await _tools.FindUnusedSymbols("/valid/path", limit: 9999).ConfigureAwait(false);

        await _store.Received(1).FindUnusedSymbolsAsync("test-repo-id", 500).ConfigureAwait(false);
    }
}

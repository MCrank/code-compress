using System.Text.Json;
using CodeCompress.Core.Models;
using CodeCompress.Core.Storage;
using CodeCompress.Core.Validation;
using CodeCompress.Server.Scoping;
using CodeCompress.Server.Tools;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeCompress.Server.Tests.Tools;

internal sealed class ContextToolsTests
{
    private IPathValidator _pathValidator = null!;
    private IProjectScopeFactory _scopeFactory = null!;
    private IProjectScope _scope = null!;
    private ISymbolStore _store = null!;
    private ContextTools _tools = null!;

    [Before(Test)]
    public void SetUp()
    {
        _pathValidator = Substitute.For<IPathValidator>();
        _scopeFactory = Substitute.For<IProjectScopeFactory>();
        _scope = Substitute.For<IProjectScope>();
        _store = Substitute.For<ISymbolStore>();
        _scope.Store.Returns(_store);
        _scope.RepoId.Returns("test-repo-id");
        _scopeFactory.CreateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_scope);
        _pathValidator.ValidatePath(Arg.Any<string>(), Arg.Any<string>()).Returns(callInfo => callInfo.ArgAt<string>(0));

        _tools = new ContextTools(_pathValidator, _scopeFactory);
    }

    // ── Error cases ───────────────────────────────────────────────────

    [Test]
    public async Task InvalidPathReturnsError()
    {
        _pathValidator.ValidatePath(Arg.Any<string>(), Arg.Any<string>())
            .Throws(new ArgumentException("Path traversal detected"));

        var result = await _tools.AssembleContext("/../etc/passwd", "query").ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        await Assert.That(doc.RootElement.GetProperty("error").GetString()).IsEqualTo("Path validation failed");
        await Assert.That(doc.RootElement.GetProperty("code").GetString()).IsEqualTo("INVALID_PATH");
    }

    [Test]
    public async Task EmptyQueryReturnsError()
    {
        var result = await _tools.AssembleContext("/valid/path", "").ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        await Assert.That(doc.RootElement.GetProperty("error").GetString()).IsEqualTo("Query cannot be empty");
        await Assert.That(doc.RootElement.GetProperty("code").GetString()).IsEqualTo("EMPTY_QUERY");
    }

    // ── Zero results ──────────────────────────────────────────────────

    [Test]
    public async Task NoMatchesReturnsHelpfulResponse()
    {
        _store.SearchSymbolsAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(new List<SymbolSearchResult>());

        var result = await _tools.AssembleContext("/valid/path", "NonExistentThing").ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        await Assert.That(doc.RootElement.GetProperty("total_matches").GetInt32()).IsEqualTo(0);
        await Assert.That(doc.RootElement.GetProperty("hint").GetString()).Contains("No symbols matched");
    }

    // ── Successful assembly ───────────────────────────────────────────

    [Test]
    public async Task SuccessfulAssemblyContainsMarkdownSections()
    {
        var searchResults = new List<SymbolSearchResult>
        {
            new(CreateSymbol(1, 1, "PathValidator", "Class", "public class PathValidator"), "src/Validation/PathValidator.cs", 1.0),
            new(CreateSymbol(2, 1, "ValidatePath", "Method", "public string ValidatePath()", parent: "PathValidator"), "src/Validation/PathValidator.cs", 0.8),
        };

        _store.SearchSymbolsAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(searchResults);
        _store.GetFilesByRepoAsync("test-repo-id")
            .Returns(new List<FileRecord>
            {
                new(1, "test-repo-id", "src/Validation/PathValidator.cs", "hash1", 500, 20, 1000, 2000),
            });

        var result = await _tools.AssembleContext("/valid/path", "path validation").ConfigureAwait(false);

        // Result should be Markdown (not JSON) with file overview and metadata footer
        await Assert.That(result).Contains("## File Overview");
        await Assert.That(result).Contains("Context Assembly");
        await Assert.That(result).Contains("PathValidator.cs");
    }

    // ── Budget enforcement ────────────────────────────────────────────

    [Test]
    public async Task BudgetClampedToMinimum()
    {
        _store.SearchSymbolsAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(new List<SymbolSearchResult>());

        var result = await _tools.AssembleContext("/valid/path", "query", budget: -1).ConfigureAwait(false);

        // Should not crash — budget clamped to 1000 minimum
        using var doc = JsonDocument.Parse(result);
        await Assert.That(doc.RootElement.GetProperty("budget").GetInt32()).IsEqualTo(1000);
    }

    [Test]
    public async Task DefaultBudgetIs40000()
    {
        _store.SearchSymbolsAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(new List<SymbolSearchResult>());

        var result = await _tools.AssembleContext("/valid/path", "query").ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        await Assert.That(doc.RootElement.GetProperty("budget").GetInt32()).IsEqualTo(40000);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static Symbol CreateSymbol(
        long id, long fileId, string name, string kind, string signature,
        string? parent = null, int byteOffset = 0, int byteLength = 100) =>
        new(id, fileId, name, kind, signature, parent, byteOffset, byteLength, 1, 10, "Public", null);
}

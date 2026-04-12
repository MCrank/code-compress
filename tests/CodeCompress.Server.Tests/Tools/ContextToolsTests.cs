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

    // ── Multi-word query support ──────────────────────────────────────

    [Test]
    public async Task MultiWordQueryUsesOrLogic()
    {
        var searchResults = new List<SymbolSearchResult>
        {
            new(CreateSymbol(1, 1, "PromptInjectionGuard", "Class", "public class PromptInjectionGuard"), "src/Security/PromptInjectionGuard.cs", 1.0),
        };

        // First call with tokenized OR query should return results
        _store.SearchSymbolsAsync("test-repo-id", "PromptInjectionGuard OR sanitize OR detect OR injection", Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(searchResults);
        _store.GetFilesByRepoAsync("test-repo-id")
            .Returns(new List<FileRecord>
            {
                new(1, "test-repo-id", "src/Security/PromptInjectionGuard.cs", "hash1", 500, 20, 1000, 2000),
            });

        var result = await _tools.AssembleContext("/valid/path", "PromptInjectionGuard sanitize detect injection").ConfigureAwait(false);

        await Assert.That(result).Contains("## File Overview");
        await Assert.That(result).Contains("PromptInjectionGuard.cs");
    }

    [Test]
    public async Task StopwordsStrippedFromMultiWordQuery()
    {
        var searchResults = new List<SymbolSearchResult>
        {
            new(CreateSymbol(1, 1, "AuthHandler", "Class", "public class AuthHandler"), "src/Auth/AuthHandler.cs", 1.0),
        };

        // Stopwords stripped: "the authentication handler for tenants" → "authentication OR handler OR tenants"
        _store.SearchSymbolsAsync("test-repo-id", "authentication OR handler OR tenants", Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(searchResults);
        _store.GetFilesByRepoAsync("test-repo-id")
            .Returns(new List<FileRecord>
            {
                new(1, "test-repo-id", "src/Auth/AuthHandler.cs", "hash1", 500, 20, 1000, 2000),
            });

        var result = await _tools.AssembleContext("/valid/path", "the authentication handler for tenants").ConfigureAwait(false);

        await Assert.That(result).Contains("## File Overview");
        await Assert.That(result).Contains("AuthHandler.cs");
    }

    [Test]
    public async Task MultiWordQueryFallsBackToContainsMatch()
    {
        var searchResults = new List<SymbolSearchResult>
        {
            new(CreateSymbol(1, 1, "TenantFilter", "Class", "public class TenantFilter"), "src/Data/TenantFilter.cs", 1.0),
        };

        // First call with OR query returns empty
        _store.SearchSymbolsAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Is<string?>(p => p == null))
            .Returns(new List<SymbolSearchResult>());

        // Contains-match fallback for individual terms returns results
        _store.SearchSymbolsAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Is<string?>(p => p != null))
            .Returns(searchResults);
        _store.GetFilesByRepoAsync("test-repo-id")
            .Returns(new List<FileRecord>
            {
                new(1, "test-repo-id", "src/Data/TenantFilter.cs", "hash1", 500, 20, 1000, 2000),
            });

        var result = await _tools.AssembleContext("/valid/path", "query filter tenant").ConfigureAwait(false);

        await Assert.That(result).Contains("TenantFilter.cs");
    }

    [Test]
    public async Task SingleWordQueryStillUsesFallback()
    {
        var searchResults = new List<SymbolSearchResult>
        {
            new(CreateSymbol(1, 1, "PathValidator", "Class", "public class PathValidator"), "src/Validation/PathValidator.cs", 1.0),
        };

        // First call returns empty (single word, no OR needed)
        _store.SearchSymbolsAsync("test-repo-id", "Validator", Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Is<string?>(p => p == null))
            .Returns(new List<SymbolSearchResult>());

        // Contains-match fallback for single word
        _store.SearchSymbolsAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Is<string>(p => p != null))
            .Returns(searchResults);
        _store.GetFilesByRepoAsync("test-repo-id")
            .Returns(new List<FileRecord>
            {
                new(1, "test-repo-id", "src/Validation/PathValidator.cs", "hash1", 500, 20, 1000, 2000),
            });

        var result = await _tools.AssembleContext("/valid/path", "Validator").ConfigureAwait(false);

        await Assert.That(result).Contains("PathValidator.cs");
    }

    // ── FTS5 error handling ─────────────────────────────────────────────

    [Test]
    public async Task PrimarySearchDbExceptionRetriesWithLiteralPhrase()
    {
        var callCount = 0;
        var searchResults = new List<SymbolSearchResult>
        {
            new(CreateSymbol(1, 1, "PathValidator", "Class", "public class PathValidator"), "src/Validation/PathValidator.cs", 1.0),
        };

        _store.SearchSymbolsAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(callInfo =>
            {
                callCount++;
                if (callCount == 1)
                {
                    throw new Microsoft.Data.Sqlite.SqliteException("fts5: syntax error", 1);
                }

                return searchResults;
            });
        _store.GetFilesByRepoAsync("test-repo-id")
            .Returns(new List<FileRecord>
            {
                new(1, "test-repo-id", "src/Validation/PathValidator.cs", "hash1", 500, 20, 1000, 2000),
            });

        var result = await _tools.AssembleContext("/valid/path", "Program.cs host config").ConfigureAwait(false);

        // Should succeed via literal phrase retry, not throw
        await Assert.That(result).Contains("## File Overview");
        await Assert.That(result).Contains("PathValidator.cs");
    }

    [Test]
    public async Task PrimarySearchDbExceptionBothAttemptsFailReturnsStructuredError()
    {
        _store.SearchSymbolsAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Throws(new Microsoft.Data.Sqlite.SqliteException("fts5: syntax error", 1));

        var result = await _tools.AssembleContext("/valid/path", "broken OR query").ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        await Assert.That(doc.RootElement.GetProperty("error").GetString()).Contains("FTS5");
        await Assert.That(doc.RootElement.GetProperty("code").GetString()).IsEqualTo("FTS5_QUERY_ERROR");
    }

    [Test]
    public async Task ContainsMatchFallbackDbExceptionSkipsTermAndContinues()
    {
        var callCount = 0;
        var searchResults = new List<SymbolSearchResult>
        {
            new(CreateSymbol(1, 1, "TenantFilter", "Class", "public class TenantFilter"), "src/Data/TenantFilter.cs", 1.0),
        };

        _store.SearchSymbolsAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Is<string?>(p => p == null))
            .Returns(new List<SymbolSearchResult>());

        _store.SearchSymbolsAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Is<string?>(p => p != null))
            .Returns(callInfo =>
            {
                callCount++;
                if (callCount == 1)
                {
                    // First contains-match term fails
                    throw new Microsoft.Data.Sqlite.SqliteException("fts5: syntax error", 1);
                }

                // Second term succeeds
                return searchResults;
            });
        _store.GetFilesByRepoAsync("test-repo-id")
            .Returns(new List<FileRecord>
            {
                new(1, "test-repo-id", "src/Data/TenantFilter.cs", "hash1", 500, 20, 1000, 2000),
            });

        var result = await _tools.AssembleContext("/valid/path", "broken filter tenant").ConfigureAwait(false);

        // Should succeed — skipped the broken term, found results on the next term
        await Assert.That(result).Contains("TenantFilter.cs");
    }

    // ── pathFilter ────────────────────────────────────────────────────

    [Test]
    public async Task PathFilterScopesResultsToMatchingDirectory()
    {
        var srcSymbol = new SymbolSearchResult(
            CreateSymbol(1, 1, "UserService", "Class", "public class UserService"), "src/Services/UserService.cs", 1.0);

        // When pathFilter is provided, SearchSymbolsAsync receives the validated filter
        _store.SearchSymbolsAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Is<string?>(p => p != null), Arg.Any<string?>())
            .Returns(new List<SymbolSearchResult> { srcSymbol });
        _store.GetFilesByRepoAsync("test-repo-id")
            .Returns(new List<FileRecord>
            {
                new(1, "test-repo-id", "src/Services/UserService.cs", "hash1", 500, 20, 1000, 2000),
                new(2, "test-repo-id", "tests/UserServiceTests.cs", "hash2", 300, 10, 1000, 2000),
            });

        var result = await _tools.AssembleContext("/valid/path", "UserService", pathFilter: "src/").ConfigureAwait(false);

        await Assert.That(result).Contains("UserService.cs");
        await Assert.That(result).DoesNotContain("UserServiceTests.cs");
    }

    [Test]
    public async Task NullPathFilterPreservesExistingBehavior()
    {
        var searchResults = new List<SymbolSearchResult>
        {
            new(CreateSymbol(1, 1, "PathValidator", "Class", "public class PathValidator"), "src/Validation/PathValidator.cs", 1.0),
        };

        // When pathFilter is null, SearchSymbolsAsync receives null for pathFilter
        _store.SearchSymbolsAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Is<string?>(p => p == null), Arg.Any<string?>())
            .Returns(searchResults);
        _store.GetFilesByRepoAsync("test-repo-id")
            .Returns(new List<FileRecord>
            {
                new(1, "test-repo-id", "src/Validation/PathValidator.cs", "hash1", 500, 20, 1000, 2000),
            });

        var result = await _tools.AssembleContext("/valid/path", "PathValidator").ConfigureAwait(false);

        await Assert.That(result).Contains("## File Overview");
        await Assert.That(result).Contains("PathValidator.cs");
    }

    [Test]
    [Arguments("../escape")]
    [Arguments("/etc/passwd")]
    [Arguments("src/../../etc")]
    public async Task InvalidPathFilterReturnsStructuredError(string badFilter)
    {
        var result = await _tools.AssembleContext("/valid/path", "query", pathFilter: badFilter).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        await Assert.That(doc.RootElement.GetProperty("code").GetString()).IsEqualTo("INVALID_PATH_FILTER");
    }

    [Test]
    public async Task PathFilterAppliesToContainsMatchFallback()
    {
        var searchResults = new List<SymbolSearchResult>
        {
            new(CreateSymbol(1, 1, "TenantFilter", "Class", "public class TenantFilter"), "src/Data/TenantFilter.cs", 1.0),
        };

        // First call (OR query, with pathFilter) returns empty
        _store.SearchSymbolsAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Is<string?>(p => p != null), Arg.Is<string?>(p => p == null))
            .Returns(new List<SymbolSearchResult>());

        // Contains-match fallback also receives pathFilter
        _store.SearchSymbolsAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Is<string?>(p => p != null), Arg.Is<string?>(p => p != null))
            .Returns(searchResults);
        _store.GetFilesByRepoAsync("test-repo-id")
            .Returns(new List<FileRecord>
            {
                new(1, "test-repo-id", "src/Data/TenantFilter.cs", "hash1", 500, 20, 1000, 2000),
            });

        var result = await _tools.AssembleContext("/valid/path", "filter tenant", pathFilter: "src/").ConfigureAwait(false);

        await Assert.That(result).Contains("TenantFilter.cs");
    }

    [Test]
    public async Task PathFilterWithNoMatchesReturnsZeroResultResponse()
    {
        _store.SearchSymbolsAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(new List<SymbolSearchResult>());

        var result = await _tools.AssembleContext("/valid/path", "UserService", pathFilter: "nonexistent/").ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        await Assert.That(doc.RootElement.GetProperty("total_matches").GetInt32()).IsEqualTo(0);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static Symbol CreateSymbol(
        long id, long fileId, string name, string kind, string signature,
        string? parent = null, int byteOffset = 0, int byteLength = 100) =>
        new(id, fileId, name, kind, signature, parent, byteOffset, byteLength, 1, 10, "Public", null);
}

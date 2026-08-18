using System.Text.Json;
using CodeCompress.Core.Models;
using CodeCompress.Core.Storage;
using CodeCompress.Core.Validation;
using CodeCompress.Server.Scoping;
using CodeCompress.Server.Tools;
using NSubstitute;
using Microsoft.Data.Sqlite;
using NSubstitute.ExceptionExtensions;

namespace CodeCompress.Server.Tests.Tools;

internal sealed class ReferenceToolsTests
{
    private IPathValidator _pathValidator = null!;
    private IProjectScopeFactory _scopeFactory = null!;
    private IProjectScope _scope = null!;
    private ISymbolStore _store = null!;
    private ReferenceTools _tools = null!;

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

        _tools = new ReferenceTools(_pathValidator, _scopeFactory);
    }

    [Test]
    public async Task FindReferencesReturnsAllMatchingLocations()
    {
        var references = new List<ReferenceResult>
        {
            new("src/services/CombatService.luau", 10, "-- context before\nProcessAttack(attacker)\n-- context after", 1.0),
            new("src/services/DamageService.luau", 25, "-- setup\nresult = ProcessAttack(data)\n-- next line", 2.0),
            new("src/tests/CombatTests.luau", 5, "\nProcessAttack(mock)\n-- verify", 3.0),
        };
        _store.FindReferencesAsync("test-repo-id", "ProcessAttack", "/valid/path", 20, null)
            .Returns(references);

        var result = await _tools.FindReferences("/valid/path", "ProcessAttack").ConfigureAwait(false);

        await Assert.That(result.TotalMatches).IsEqualTo(3);
        await Assert.That(result.Results).Count().IsEqualTo(3);
        await Assert.That(result.Results![0].File).IsEqualTo("src/services/CombatService.luau");
        await Assert.That(result.Results![0].Line).IsEqualTo(10);
        await Assert.That(result.Results![0].ContextSnippet).Contains("ProcessAttack");
        await Assert.That(result.Results![0].Rank).IsEqualTo(1);
    }

    [Test]
    public async Task FindReferencesTypeUsagesReturnsAllLocations()
    {
        var references = new List<ReferenceResult>
        {
            new("src/Storage/SqliteSymbolStore.cs", 9, "public sealed class SqliteSymbolStore : ISymbolStore\n{", 1.0),
            new("src/DI/ServiceCollectionExtensions.cs", 15, "services.AddSingleton<ISymbolStore, SqliteSymbolStore>();", 1.0),
            new("src/Server/Tools/QueryTools.cs", 33, "private readonly ISymbolStore _store;\n", 1.0),
            new("tests/Mocks/MockStore.cs", 8, "internal class MockStore : ISymbolStore", 1.0),
            new("src/Scoping/ProjectScope.cs", 12, "public ISymbolStore Store { get; }", 1.0),
        };
        _store.FindReferencesAsync("test-repo-id", "ISymbolStore", "/valid/path", 20, null)
            .Returns(references);

        var result = await _tools.FindReferences("/valid/path", "ISymbolStore").ConfigureAwait(false);

        await Assert.That(result.TotalMatches).IsEqualTo(5);
    }

    [Test]
    public async Task FindReferencesWithPathFilterPassesFilterToStore()
    {
        _store.FindReferencesAsync("test-repo-id", "Logger", Arg.Any<string>(), Arg.Any<int>(), "src")
            .Returns(new List<ReferenceResult>
            {
                new("src/services/LogService.luau", 3, "local Logger = require(...)\n", 1.0),
            });

        var result = await _tools.FindReferences("/valid/path", "Logger", pathFilter: "src").ConfigureAwait(false);

        await _store.Received(1).FindReferencesAsync(
            "test-repo-id", "Logger", "/valid/path", 20, "src").ConfigureAwait(false);

        await Assert.That(result.TotalMatches).IsEqualTo(1);
    }

    [Test]
    public async Task FindReferencesWithLimitRespectsLimit()
    {
        _store.FindReferencesAsync("test-repo-id", "ToString", "/valid/path", 10, null)
            .Returns(new List<ReferenceResult>());

        await _tools.FindReferences("/valid/path", "ToString", limit: 10).ConfigureAwait(false);

        await _store.Received(1).FindReferencesAsync(
            "test-repo-id", "ToString", "/valid/path", 10, null).ConfigureAwait(false);
    }

    [Test]
    public async Task FindReferencesLimitClampedAbove100()
    {
        _store.FindReferencesAsync("test-repo-id", "Foo", "/valid/path", 100, null)
            .Returns(new List<ReferenceResult>());

        await _tools.FindReferences("/valid/path", "Foo", limit: 200).ConfigureAwait(false);

        await _store.Received(1).FindReferencesAsync(
            "test-repo-id", "Foo", "/valid/path", 100, null).ConfigureAwait(false);
    }

    [Test]
    public async Task FindReferencesLimitClampedBelow1()
    {
        _store.FindReferencesAsync("test-repo-id", "Foo", "/valid/path", 1, null)
            .Returns(new List<ReferenceResult>());

        await _tools.FindReferences("/valid/path", "Foo", limit: -5).ConfigureAwait(false);

        await _store.Received(1).FindReferencesAsync(
            "test-repo-id", "Foo", "/valid/path", 1, null).ConfigureAwait(false);
    }

    [Test]
    public async Task FindReferencesNoMatchesReturnsEmptyResults()
    {
        _store.FindReferencesAsync("test-repo-id", "UnusedHelper", "/valid/path", 20, null)
            .Returns(new List<ReferenceResult>());

        var result = await _tools.FindReferences("/valid/path", "UnusedHelper").ConfigureAwait(false);

        await Assert.That(result.TotalMatches).IsEqualTo(0);
        await Assert.That(result.Results).Count().IsEqualTo(0);
    }

    [Test]
    public async Task FindReferencesInvalidPathReturnsError()
    {
        _pathValidator.ValidatePath(Arg.Any<string>(), Arg.Any<string>())
            .Throws(new ArgumentException("Invalid path"));

        var result = await _tools.FindReferences("../../../etc/passwd", "Foo").ConfigureAwait(false);

        await Assert.That(result.Code).IsEqualTo("INVALID_PATH");
    }

    [Test]
    public async Task FindReferencesEmptySymbolNameReturnsError()
    {
        var result = await _tools.FindReferences("/valid/path", "").ConfigureAwait(false);

        await Assert.That(result.Code).IsEqualTo("EMPTY_SYMBOL_NAME");
    }

    [Test]
    public async Task FindReferencesWhitespaceSymbolNameReturnsError()
    {
        var result = await _tools.FindReferences("/valid/path", "   ").ConfigureAwait(false);

        await Assert.That(result.Code).IsEqualTo("EMPTY_SYMBOL_NAME");
    }

    [Test]
    public async Task FindReferencesInvalidPathFilterReturnsError()
    {
        var result = await _tools.FindReferences("/valid/path", "Foo", pathFilter: "../../../etc").ConfigureAwait(false);

        await Assert.That(result.Code).IsEqualTo("INVALID_PATH_FILTER");
    }

    [Test]
    public async Task FindReferencesFts5ErrorReturnsEmptyResults()
    {
        _store.FindReferencesAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string?>())
            .Throws(new SqliteException("FTS5 syntax error", 1));

        var result = await _tools.FindReferences("/valid/path", "bad\"query").ConfigureAwait(false);

        await Assert.That(result.TotalMatches).IsEqualTo(0);
    }

    [Test]
    public async Task FindReferencesSymbolNameSanitizedInResponse()
    {
        _store.FindReferencesAsync("test-repo-id", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string?>())
            .Returns(new List<ReferenceResult>());

        var result = await _tools.FindReferences("/valid/path", "Process<script>Attack").ConfigureAwait(false);

        // Script tags should be stripped from the response
        await Assert.That(result.Symbol).IsEqualTo("ProcessscriptAttack");
    }

    [Test]
    public async Task FindReferencesDoesNotEchoRawPathOnError()
    {
        _pathValidator.ValidatePath(Arg.Any<string>(), Arg.Any<string>())
            .Throws(new ArgumentException("Invalid path"));

        var maliciousPath = "/tmp/<script>alert(1)</script>";
        var result = await _tools.FindReferences(maliciousPath, "Foo").ConfigureAwait(false);

        var serialized = JsonSerializer.Serialize(result);
        await Assert.That(serialized).DoesNotContain("<script>");
        await Assert.That(serialized).DoesNotContain("alert(1)");
    }
}

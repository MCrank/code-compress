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

internal sealed class IndexingToolsTests
{
    private IPathValidator _pathValidator = null!;
    private IProjectScopeFactory _scopeFactory = null!;
    private IProjectScope _scope = null!;
    private IIndexEngine _engine = null!;
    private ISymbolStore _store = null!;
    private IndexingTools _tools = null!;

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

        _tools = new IndexingTools(_pathValidator, _scopeFactory);
    }

    [Test]
    public async Task IndexProjectValidPathReturnsIndexingResults()
    {
        var indexResult = new IndexResult("repo1", 42, 3, 0, 187, 1250);
        _engine.IndexProjectAsync(
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string[]?>(),
            Arg.Any<string[]?>(),
            Arg.Any<CancellationToken>()).Returns(indexResult);

        var result = await _tools.IndexProject("/valid/path").ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("repo_id").GetString()).IsEqualTo("repo1");
        await Assert.That(root.GetProperty("files_indexed").GetInt32()).IsEqualTo(42);
        await Assert.That(root.GetProperty("files_unchanged").GetInt32()).IsEqualTo(3);
        await Assert.That(root.GetProperty("total_files").GetInt32()).IsEqualTo(45);
        await Assert.That(root.GetProperty("symbols_found").GetInt32()).IsEqualTo(187);
        await Assert.That(root.GetProperty("duration_ms").GetInt64()).IsEqualTo(1250);
    }

    [Test]
    public async Task IndexProjectTraversalPathReturnsError()
    {
        _pathValidator.ValidatePath(Arg.Any<string>(), Arg.Any<string>())
            .Throws(new ArgumentException("Path traversal detected"));

        var result = await _tools.IndexProject("/../../../etc/passwd").ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("error").GetString()).IsEqualTo("Path validation failed");
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo("INVALID_PATH");
    }

    [Test]
    public async Task IndexProjectNonExistentPathReturnsError()
    {
        _engine.IndexProjectAsync(
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string[]?>(),
            Arg.Any<string[]?>(),
            Arg.Any<CancellationToken>()).Throws(new DirectoryNotFoundException("Not found"));

        var result = await _tools.IndexProject("/nonexistent/path").ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("error").GetString()).IsEqualTo("Directory not found");
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo("DIRECTORY_NOT_FOUND");
    }

    [Test]
    public async Task IndexProjectWithLanguageFilterPassesToEngine()
    {
        var indexResult = new IndexResult("repo1", 10, 0, 0, 50, 100);
        _engine.IndexProjectAsync(
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string[]?>(),
            Arg.Any<string[]?>(),
            Arg.Any<CancellationToken>()).Returns(indexResult);

        await _tools.IndexProject("/valid/path", language: "luau").ConfigureAwait(false);

        await _engine.Received(1).IndexProjectAsync(
            Arg.Any<string>(),
            "luau",
            Arg.Any<string[]?>(),
            Arg.Any<string[]?>(),
            Arg.Any<CancellationToken>()).ConfigureAwait(false);
    }

    [Test]
    public async Task IndexProjectWithGlobPatternsPassesToEngine()
    {
        var include = new[] { "**/*.lua" };
        var exclude = new[] { "**/test/**" };
        var indexResult = new IndexResult("repo1", 5, 0, 0, 20, 50);
        _engine.IndexProjectAsync(
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string[]?>(),
            Arg.Any<string[]?>(),
            Arg.Any<CancellationToken>()).Returns(indexResult);

        await _tools.IndexProject("/valid/path", includePatterns: include, excludePatterns: exclude).ConfigureAwait(false);

        await _engine.Received(1).IndexProjectAsync(
            Arg.Any<string>(),
            Arg.Any<string?>(),
            include,
            exclude,
            Arg.Any<CancellationToken>()).ConfigureAwait(false);
    }

    [Test]
    public async Task SnapshotCreateValidInputsReturnsSnapshotInfo()
    {
        var repo = new Repository("test-repo-id", "/valid/path", "project", "luau", 1000, 42, 187);
        _store.GetRepositoryAsync("test-repo-id").Returns(repo);
        _store.CreateSnapshotAsync(Arg.Any<IndexSnapshot>()).Returns(1L);

        var result = await _tools.SnapshotCreate("/valid/path", "pre-refactor").ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("snapshot_id").GetInt64()).IsEqualTo(1L);
        await Assert.That(root.GetProperty("label").GetString()).IsEqualTo("pre-refactor");
        await Assert.That(root.GetProperty("file_count").GetInt32()).IsEqualTo(42);
        await Assert.That(root.GetProperty("symbol_count").GetInt32()).IsEqualTo(187);
    }

    [Test]
    public async Task SnapshotCreateMaliciousLabelIsSanitized()
    {
        var repo = new Repository("test-repo-id", "/valid/path", "project", "luau", 1000, 10, 50);
        _store.GetRepositoryAsync("test-repo-id").Returns(repo);
        _store.CreateSnapshotAsync(Arg.Any<IndexSnapshot>()).Returns(1L);

        var result = await _tools.SnapshotCreate("/valid/path", "pre-<script>alert('xss')</script>refactor").ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        var label = doc.RootElement.GetProperty("label").GetString();
        await Assert.That(label).IsEqualTo("pre-scriptalertxssscriptrefactor");
    }

    [Test]
    public async Task SnapshotCreateLongLabelIsTruncated()
    {
        var repo = new Repository("test-repo-id", "/valid/path", "project", "luau", 1000, 10, 50);
        _store.GetRepositoryAsync("test-repo-id").Returns(repo);
        _store.CreateSnapshotAsync(Arg.Any<IndexSnapshot>()).Returns(1L);

        var longLabel = new string('a', 200);
        var result = await _tools.SnapshotCreate("/valid/path", longLabel).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        var label = doc.RootElement.GetProperty("label").GetString();
        await Assert.That(label!.Length).IsLessThanOrEqualTo(128);
    }

    [Test]
    public async Task SnapshotCreateInvalidPathReturnsError()
    {
        _pathValidator.ValidatePath(Arg.Any<string>(), Arg.Any<string>())
            .Throws(new ArgumentException("Path traversal detected"));

        var result = await _tools.SnapshotCreate("/../invalid", "test-label").ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("error").GetString()).IsEqualTo("Path validation failed");
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo("INVALID_PATH");
    }

    [Test]
    public async Task InvalidateCacheValidPathReturnsSuccess()
    {
        var files = new List<FileRecord>
        {
            new(1, "test-repo-id", "file1.lua", "hash1", 100, 10, 1000, 2000),
            new(2, "test-repo-id", "file2.lua", "hash2", 200, 20, 1000, 2000),
        };
        _store.GetFilesByRepoAsync("test-repo-id").Returns(files);

        var result = await _tools.InvalidateCache("/valid/path").ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("success").GetBoolean()).IsTrue();
        await Assert.That(root.GetProperty("message").GetString())
            .IsEqualTo("Cache invalidated. Next index operation will perform a full reparse.");

        await _store.Received(2).DeleteSymbolsByFileAsync(Arg.Any<long>()).ConfigureAwait(false);
        await _store.Received(2).DeleteDependenciesByFileAsync(Arg.Any<long>()).ConfigureAwait(false);
        await _store.Received(2).DeleteFileAsync(Arg.Any<long>()).ConfigureAwait(false);
        await _store.Received(1).DeleteRepositoryAsync("test-repo-id").ConfigureAwait(false);
    }

    [Test]
    public async Task InvalidateCacheInvalidPathReturnsError()
    {
        _pathValidator.ValidatePath(Arg.Any<string>(), Arg.Any<string>())
            .Throws(new ArgumentException("Path traversal detected"));

        var result = await _tools.InvalidateCache("/../invalid").ConfigureAwait(false);

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("error").GetString()).IsEqualTo("Path validation failed");
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo("INVALID_PATH");
    }

    [Test]
    public async Task SanitizeLabelRemovesUnsafeCharacters()
    {
        var sanitized = IndexingTools.SanitizeLabel("hello<world>&\"test'");
        await Assert.That(sanitized).IsEqualTo("helloworldtest");
    }

    [Test]
    public async Task SanitizeLabelTruncatesTo128Characters()
    {
        var longLabel = new string('x', 200);
        var sanitized = IndexingTools.SanitizeLabel(longLabel);
        await Assert.That(sanitized.Length).IsEqualTo(128);
    }

    [Test]
    public async Task IndexProjectDoesNotEchoRawPath()
    {
        var distinctivePath = "/very/unique/distinctive/test/path/12345";
        var indexResult = new IndexResult("repo1", 10, 0, 0, 50, 100);
        _engine.IndexProjectAsync(
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string[]?>(),
            Arg.Any<string[]?>(),
            Arg.Any<CancellationToken>()).Returns(indexResult);

        var result = await _tools.IndexProject(distinctivePath).ConfigureAwait(false);

        await Assert.That(result).DoesNotContain(distinctivePath);
    }
}

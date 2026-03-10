using CodeCompress.Core.Indexing;
using CodeCompress.Core.Models;
using CodeCompress.Core.Parsers;
using CodeCompress.Core.Storage;
using CodeCompress.Core.Validation;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeCompress.Core.Tests.Indexing;

internal sealed class IndexEngineTests
{
    private string _tempDir = null!;
    private IFileHasher _fileHasher = null!;
    private IChangeTracker _changeTracker = null!;
    private ISymbolStore _symbolStore = null!;
    private IPathValidator _pathValidator = null!;
    private ILogger<IndexEngine> _logger = null!;
    private StubLuauParser _luauParser = null!;
    private StubCSharpParser _csharpParser = null!;
    private IndexEngine _engine = null!;

    [Before(Test)]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"IndexEngineTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _fileHasher = Substitute.For<IFileHasher>();
        _changeTracker = Substitute.For<IChangeTracker>();
        _symbolStore = Substitute.For<ISymbolStore>();
        _pathValidator = Substitute.For<IPathValidator>();
        _logger = Substitute.For<ILogger<IndexEngine>>();
        _luauParser = new StubLuauParser();
        _csharpParser = new StubCSharpParser();

        // Default: path validator passes through
        _pathValidator.ValidatePath(Arg.Any<string>(), Arg.Any<string>())
            .Returns(callInfo => callInfo.ArgAt<string>(0));

        // Default: no stored files
        _symbolStore.GetFilesByRepoAsync(Arg.Any<string>())
            .Returns(Task.FromResult<IReadOnlyList<FileRecord>>([]));

        // Default: inserted file can be re-queried
        _symbolStore.GetFileByPathAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(callInfo => Task.FromResult<FileRecord?>(
                new FileRecord(1, callInfo.ArgAt<string>(0), callInfo.ArgAt<string>(1), "hash", 0, 0, 0, 0)));

        _engine = new IndexEngine(
            _fileHasher,
            _changeTracker,
            new ILanguageParser[] { _luauParser, _csharpParser },
            _symbolStore,
            _pathValidator,
            _logger);
    }

    [After(Test)]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private string CreateFile(string relativePath, string content = "")
    {
        var fullPath = Path.Combine(_tempDir, relativePath);
        var dir = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    private void SetupHasherReturns(Dictionary<string, string> hashes)
    {
        _fileHasher.HashFilesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(hashes));
    }

    private void SetupChangeSet(ChangeSet changeSet)
    {
        _changeTracker.DetectChanges(
            Arg.Any<Dictionary<string, string>>(),
            Arg.Any<Dictionary<string, string>>())
            .Returns(changeSet);
    }

    // ── Test 1: Full index of new project ─────────────────────────────

    [Test]
    public async Task FullIndexOfNewProjectParsesAllFiles()
    {
        var f1 = CreateFile("src/main.luau", "-- main");
        var f2 = CreateFile("src/utils.luau", "-- utils");
        var f3 = CreateFile("src/lib.luau", "-- lib");

        SetupHasherReturns(new Dictionary<string, string>
        {
            [f1] = "hash1", [f2] = "hash2", [f3] = "hash3",
        });

        SetupChangeSet(new ChangeSet(
            ["src\\main.luau", "src\\utils.luau", "src\\lib.luau"],
            [], [], []));

        var result = await _engine.IndexProjectAsync(_tempDir).ConfigureAwait(false);

        await Assert.That(result.FilesIndexed).IsEqualTo(3);
        await Assert.That(result.FilesDeleted).IsEqualTo(0);
    }

    // ── Test 2: Incremental — one modified file ──────────────────────

    [Test]
    public async Task IncrementalIndexReindexesOnlyModifiedFile()
    {
        var f1 = CreateFile("a.luau", "-- a");
        var f2 = CreateFile("b.luau", "-- b");

        SetupHasherReturns(new Dictionary<string, string>
        {
            [f1] = "hash1_new", [f2] = "hash2",
        });

        _symbolStore.GetFilesByRepoAsync(Arg.Any<string>())
            .Returns(Task.FromResult<IReadOnlyList<FileRecord>>(new List<FileRecord>
            {
                new(1, "repo", "a.luau", "hash1_old", 10, 1, 0, 0),
                new(2, "repo", "b.luau", "hash2", 10, 1, 0, 0),
            }));

        SetupChangeSet(new ChangeSet(
            [], ["a.luau"], [], ["b.luau"]));

        var result = await _engine.IndexProjectAsync(_tempDir).ConfigureAwait(false);

        await Assert.That(result.FilesIndexed).IsEqualTo(1);
        await Assert.That(result.FilesSkipped).IsEqualTo(1);
    }

    // ── Test 3: Incremental — no changes ─────────────────────────────

    [Test]
    public async Task NoChangesReturnsEarlyWithZeroIndexed()
    {
        var f1 = CreateFile("a.luau", "-- a");

        SetupHasherReturns(new Dictionary<string, string>
        {
            [f1] = "hash1",
        });

        _symbolStore.GetFilesByRepoAsync(Arg.Any<string>())
            .Returns(Task.FromResult<IReadOnlyList<FileRecord>>(new List<FileRecord>
            {
                new(1, "repo", "a.luau", "hash1", 10, 1, 0, 0),
            }));

        SetupChangeSet(new ChangeSet([], [], [], ["a.luau"]));

        var result = await _engine.IndexProjectAsync(_tempDir).ConfigureAwait(false);

        await Assert.That(result.FilesIndexed).IsEqualTo(0);
        await Assert.That(result.FilesSkipped).IsEqualTo(1);
    }

    // ── Test 4: Deleted file ─────────────────────────────────────────

    [Test]
    public async Task DeletedFileRemovesSymbolsFromStore()
    {
        // No files on disk — but stored has one
        _symbolStore.GetFilesByRepoAsync(Arg.Any<string>())
            .Returns(Task.FromResult<IReadOnlyList<FileRecord>>(new List<FileRecord>
            {
                new(1, "repo", "deleted.luau", "hash1", 10, 1, 0, 0),
            }));

        // Hasher returns empty (no files found)
        SetupHasherReturns([]);

        // But we need at least one file for discovery — let's create a file and set up the change tracker
        CreateFile("remaining.luau", "-- still here");
        var remaining = Path.Combine(_tempDir, "remaining.luau");
        SetupHasherReturns(new Dictionary<string, string>
        {
            [remaining] = "hash_remaining",
        });

        SetupChangeSet(new ChangeSet(
            [], [], ["deleted.luau"], ["remaining.luau"]));

        var result = await _engine.IndexProjectAsync(_tempDir).ConfigureAwait(false);

        await Assert.That(result.FilesDeleted).IsEqualTo(1);
        await _symbolStore.Received(1).DeleteSymbolsByFileAsync(1).ConfigureAwait(false);
        await _symbolStore.Received(1).DeleteFileAsync(1).ConfigureAwait(false);
    }

    // ── Test 5: Language filter — Luau only ──────────────────────────

    [Test]
    public async Task LanguageFilterRestrictsToMatchingExtensions()
    {
        CreateFile("main.luau", "-- luau");
        CreateFile("main.cs", "// csharp");

        var luauPath = Path.Combine(_tempDir, "main.luau");
        SetupHasherReturns(new Dictionary<string, string>
        {
            [luauPath] = "hash1",
        });

        SetupChangeSet(new ChangeSet(["main.luau"], [], [], []));

        await _engine.IndexProjectAsync(_tempDir, language: "luau").ConfigureAwait(false);

        // Only the .luau file should be hashed — the .cs file should be filtered out
        await _fileHasher.Received(1).HashFilesAsync(
            Arg.Is<IEnumerable<string>>(paths => paths.All(p => p.EndsWith(".luau", StringComparison.Ordinal))),
            Arg.Any<CancellationToken>()).ConfigureAwait(false);
    }

    // ── Test 6: Exclude patterns respected ───────────────────────────

    [Test]
    public async Task ExcludePatternsFilterOutMatchingFiles()
    {
        CreateFile("src/main.luau", "-- main");
        CreateFile("excluded/test.luau", "-- test");

        // Capture what files are passed to the hasher
        var hashedFiles = new List<string>();
        _fileHasher.HashFilesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                hashedFiles.AddRange(callInfo.ArgAt<IEnumerable<string>>(0));
                var mainPath = Path.Combine(_tempDir, "src", "main.luau");
                return Task.FromResult(new Dictionary<string, string> { [mainPath] = "hash1" });
            });

        SetupChangeSet(new ChangeSet(["src\\main.luau"], [], [], []));

        await _engine.IndexProjectAsync(
            _tempDir,
            excludePatterns: ["excluded/**"]).ConfigureAwait(false);

        // Convert to relative paths for clear assertion
        var relativeHashedFiles = hashedFiles.Select(p => Path.GetRelativePath(_tempDir, p)).ToList();
        await Assert.That(relativeHashedFiles).Count().IsEqualTo(1);
        await Assert.That(relativeHashedFiles[0]).Contains("main.luau");
    }

    // ── Test 7: Include patterns respected ───────────────────────────

    [Test]
    public async Task IncludePatternsKeepOnlyMatchingFiles()
    {
        CreateFile("src/main.luau", "-- main");
        CreateFile("lib/other.luau", "-- other");

        var mainPath = Path.Combine(_tempDir, "src", "main.luau");
        SetupHasherReturns(new Dictionary<string, string>
        {
            [mainPath] = "hash1",
        });

        SetupChangeSet(new ChangeSet(["src\\main.luau"], [], [], []));

        await _engine.IndexProjectAsync(
            _tempDir,
            includePatterns: ["src/**/*.luau"]).ConfigureAwait(false);

        await _fileHasher.Received(1).HashFilesAsync(
            Arg.Is<IEnumerable<string>>(paths =>
                paths.All(p => p.Contains("src", StringComparison.OrdinalIgnoreCase))),
            Arg.Any<CancellationToken>()).ConfigureAwait(false);
    }

    // ── Test 8: Unknown file extensions skipped ──────────────────────

    [Test]
    public async Task UnknownExtensionsAreSkipped()
    {
        CreateFile("readme.txt", "text");
        CreateFile("main.luau", "-- luau");

        var mainPath = Path.Combine(_tempDir, "main.luau");
        SetupHasherReturns(new Dictionary<string, string>
        {
            [mainPath] = "hash1",
        });

        SetupChangeSet(new ChangeSet(["main.luau"], [], [], []));

        await _engine.IndexProjectAsync(_tempDir).ConfigureAwait(false);

        // Only .luau file should be passed to hasher, not .txt
        await _fileHasher.Received(1).HashFilesAsync(
            Arg.Is<IEnumerable<string>>(paths =>
                paths.All(p => p.EndsWith(".luau", StringComparison.Ordinal))),
            Arg.Any<CancellationToken>()).ConfigureAwait(false);
    }

    // ── Test 9: Mixed-language project ───────────────────────────────

    [Test]
    public async Task MixedLanguageProjectDispatchesToCorrectParsers()
    {
        var luauFile = CreateFile("main.luau", "-- luau code");
        var csFile = CreateFile("main.cs", "// csharp code");

        SetupHasherReturns(new Dictionary<string, string>
        {
            [luauFile] = "hash1",
            [csFile] = "hash2",
        });

        SetupChangeSet(new ChangeSet(["main.luau", "main.cs"], [], [], []));

        await _engine.IndexProjectAsync(_tempDir).ConfigureAwait(false);

        await Assert.That(_luauParser.ParsedFiles).Contains("main.luau");
        await Assert.That(_csharpParser.ParsedFiles).Contains("main.cs");
    }

    // ── Test 10: Default excludes applied ────────────────────────────

    [Test]
    public async Task DefaultExcludesFilterOutStandardDirectories()
    {
        CreateFile("src/main.luau", "-- main");
        CreateFile(".git/config.luau", "-- git");
        CreateFile("node_modules/dep.luau", "-- dep");
        CreateFile("bin/Debug/out.luau", "-- bin");

        var mainPath = Path.Combine(_tempDir, "src", "main.luau");
        SetupHasherReturns(new Dictionary<string, string>
        {
            [mainPath] = "hash1",
        });

        SetupChangeSet(new ChangeSet(["src\\main.luau"], [], [], []));

        await _engine.IndexProjectAsync(_tempDir).ConfigureAwait(false);

        await _fileHasher.Received(1).HashFilesAsync(
            Arg.Is<IEnumerable<string>>(paths =>
                paths.Count() == 1 &&
                paths.All(p => p.Contains("src", StringComparison.OrdinalIgnoreCase))),
            Arg.Any<CancellationToken>()).ConfigureAwait(false);
    }

    // ── Test 11: Invalid project root ────────────────────────────────

    [Test]
    public async Task InvalidProjectRootThrowsException()
    {
        _pathValidator.ValidatePath(Arg.Any<string>(), Arg.Any<string>())
            .Throws(new ArgumentException("Path resolves outside the project root."));

        await Assert.That(async () =>
            await _engine.IndexProjectAsync("/bad/path").ConfigureAwait(false))
            .Throws<ArgumentException>();
    }

    // ── Test 12: Cancellation during hashing ─────────────────────────

    [Test]
    public async Task CancellationDuringHashingThrowsOperationCancelled()
    {
        CreateFile("main.luau", "-- main");

        _fileHasher.HashFilesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        await Assert.That(async () =>
            await _engine.IndexProjectAsync(_tempDir).ConfigureAwait(false))
            .Throws<OperationCanceledException>();
    }

    // ── Test 13: Parser failure for one file ─────────────────────────

    [Test]
    public async Task ParserFailureForOneFileDoesNotAbortOthers()
    {
        var goodFile = CreateFile("good.luau", "-- good");
        var badFile = CreateFile("bad.luau", "-- bad");

        SetupHasherReturns(new Dictionary<string, string>
        {
            [goodFile] = "hash1",
            [badFile] = "hash2",
        });

        SetupChangeSet(new ChangeSet(["good.luau", "bad.luau"], [], [], []));

        // Make the parser throw for the bad file
        _luauParser.ThrowForFile = "bad.luau";

        var result = await _engine.IndexProjectAsync(_tempDir).ConfigureAwait(false);

        // The engine should report 2 files indexed (it attempted both)
        await Assert.That(result.FilesIndexed).IsEqualTo(2);
        // The good file should still have been stored
        await _symbolStore.Received().InsertFilesAsync(Arg.Any<IReadOnlyList<FileRecord>>()).ConfigureAwait(false);
    }

    // ── Stub Parsers (can't mock ILanguageParser — ReadOnlySpan<byte>) ─

    private sealed class StubLuauParser : ILanguageParser
    {
        private readonly List<string> _parsedFiles = [];
        public IReadOnlyList<string> ParsedFiles => _parsedFiles;
        public string? ThrowForFile { get; set; }

        public string LanguageId => "luau";
        public IReadOnlyList<string> FileExtensions { get; } = [".luau", ".lua"];

        public ParseResult Parse(string filePath, ReadOnlySpan<byte> content)
        {
            if (ThrowForFile is not null &&
                filePath.Contains(ThrowForFile, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Simulated parse failure");
            }

            _parsedFiles.Add(filePath);
            return new ParseResult(
            [
                new SymbolInfo(
                    $"Func_{Path.GetFileNameWithoutExtension(filePath)}",
                    SymbolKind.Function,
                    $"function {Path.GetFileNameWithoutExtension(filePath)}()",
                    null, 0, 10, 1, 5, Visibility.Public, null),
            ],
            []);
        }
    }

    private sealed class StubCSharpParser : ILanguageParser
    {
        private readonly List<string> _parsedFiles = [];
        public IReadOnlyList<string> ParsedFiles => _parsedFiles;

        public string LanguageId => "csharp";
        public IReadOnlyList<string> FileExtensions { get; } = [".cs"];

        public ParseResult Parse(string filePath, ReadOnlySpan<byte> content)
        {
            _parsedFiles.Add(filePath);
            return new ParseResult(
            [
                new SymbolInfo(
                    $"Class_{Path.GetFileNameWithoutExtension(filePath)}",
                    SymbolKind.Class,
                    $"class {Path.GetFileNameWithoutExtension(filePath)}",
                    null, 0, 10, 1, 5, Visibility.Public, null),
            ],
            []);
        }
    }
}

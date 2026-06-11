using System.Text;
using CodeCompress.Core.Indexing;
using CodeCompress.Core.Models;
using CodeCompress.Core.Parsers;
using CodeCompress.Core.Storage;
using CodeCompress.Core.Validation;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace CodeCompress.Integration.Tests;

internal sealed class RubyEndToEndTests : IDisposable
{
    private SqliteConnection _connection = null!;
    private SqliteSymbolStore _store = null!;
    private IndexEngine _engine = null!;
    private IGitIgnoreFilter _gitIgnoreFilter = null!;
    private string _sampleProjectPath = null!;
    private string _repoId = null!;

    public void Dispose() => _connection?.Dispose();

    [Before(Test)]
    public async Task SetUp()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync().ConfigureAwait(false);
        await Migrations.ApplyAsync(_connection).ConfigureAwait(false);
        _store = new SqliteSymbolStore(_connection);

        var parsers = new ILanguageParser[] { new RubyParser() };
        _gitIgnoreFilter = Substitute.For<IGitIgnoreFilter>();
        _gitIgnoreFilter.GetIgnoredPathsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>()).Returns(new HashSet<string>());
        _engine = new IndexEngine(new FileHasher(), new ChangeTracker(), parsers, _store,
            new PathValidatorService(), _gitIgnoreFilter, NullLogger<IndexEngine>.Instance);

        _sampleProjectPath = FindSamplePath();
        _repoId = IndexEngine.ComputeRepoId(Path.GetFullPath(_sampleProjectPath));
    }

    [After(Test)]
    public async Task TearDown() => await _connection.DisposeAsync().ConfigureAwait(false);

    // ── Indexing ──────────────────────────────────────────────────────

    [Test]
    public async Task IndexProjectCorrectCounts()
    {
        var result = await _engine.IndexProjectAsync(_sampleProjectPath, "ruby").ConfigureAwait(false);
        await Assert.That(result.FilesIndexed).IsEqualTo(6);
        await Assert.That(result.SymbolsFound).IsGreaterThanOrEqualTo(25);
    }

    // ── Query — classes ───────────────────────────────────────────────

    [Test]
    public async Task FindsClass()
    {
        await IndexAsync().ConfigureAwait(false);
        var s = await _store.GetSymbolByNameAsync(_repoId, "User").ConfigureAwait(false);
        await Assert.That(s).IsNotNull();
        await Assert.That(s!.Kind).IsEqualTo("Class");
    }

    [Test]
    public async Task FindsUserServiceClass()
    {
        await IndexAsync().ConfigureAwait(false);
        var s = await _store.GetSymbolByNameAsync(_repoId, "UserService").ConfigureAwait(false);
        await Assert.That(s).IsNotNull();
        await Assert.That(s!.Kind).IsEqualTo("Class");
    }

    [Test]
    public async Task FindsNotificationClass()
    {
        await IndexAsync().ConfigureAwait(false);
        var s = await _store.GetSymbolByNameAsync(_repoId, "Notification").ConfigureAwait(false);
        await Assert.That(s).IsNotNull();
        await Assert.That(s!.Kind).IsEqualTo("Class");
    }

    // ── Query — modules ───────────────────────────────────────────────

    [Test]
    public async Task FindsModule()
    {
        await IndexAsync().ConfigureAwait(false);
        var s = await _store.GetSymbolByNameAsync(_repoId, "BaseEntity").ConfigureAwait(false);
        await Assert.That(s).IsNotNull();
        await Assert.That(s!.Kind).IsEqualTo("Module");
    }

    [Test]
    public async Task FindsRepositoryModule()
    {
        await IndexAsync().ConfigureAwait(false);
        var s = await _store.GetSymbolByNameAsync(_repoId, "Repository").ConfigureAwait(false);
        await Assert.That(s).IsNotNull();
        await Assert.That(s!.Kind).IsEqualTo("Module");
    }

    [Test]
    public async Task FindsStringUtilsModule()
    {
        await IndexAsync().ConfigureAwait(false);
        var s = await _store.GetSymbolByNameAsync(_repoId, "StringUtils").ConfigureAwait(false);
        await Assert.That(s).IsNotNull();
        await Assert.That(s!.Kind).IsEqualTo("Module");
    }

    // ── Query — methods ───────────────────────────────────────────────

    [Test]
    public async Task FindsInstanceMethod()
    {
        await IndexAsync().ConfigureAwait(false);
        var s = await _store.GetSymbolByNameAsync(_repoId, "initialize").ConfigureAwait(false);
        await Assert.That(s).IsNotNull();
        await Assert.That(s!.Kind).IsEqualTo("Method");
    }

    [Test]
    public async Task InstanceMethodParentIsClass()
    {
        await IndexAsync().ConfigureAwait(false);
        // greet is defined on User
        var s = await _store.GetSymbolByNameAsync(_repoId, "User:greet").ConfigureAwait(false);
        await Assert.That(s).IsNotNull();
        await Assert.That(s!.ParentSymbol).IsEqualTo("User");
    }

    [Test]
    public async Task FindsSingletonMethod()
    {
        await IndexAsync().ConfigureAwait(false);
        // find_by_email is def self.find_by_email on User
        var s = await _store.GetSymbolByNameAsync(_repoId, "User:find_by_email").ConfigureAwait(false);
        await Assert.That(s).IsNotNull();
        await Assert.That(s!.Kind).IsEqualTo("Method");
    }

    [Test]
    public async Task FindsModuleMethod()
    {
        await IndexAsync().ConfigureAwait(false);
        var s = await _store.GetSymbolByNameAsync(_repoId, "StringUtils:truncate").ConfigureAwait(false);
        await Assert.That(s).IsNotNull();
        await Assert.That(s!.Kind).IsEqualTo("Method");
        await Assert.That(s.ParentSymbol).IsEqualTo("StringUtils");
    }

    // ── Query — constants ─────────────────────────────────────────────

    [Test]
    public async Task FindsConstant()
    {
        await IndexAsync().ConfigureAwait(false);
        var s = await _store.GetSymbolByNameAsync(_repoId, "MAX_NAME_LENGTH").ConfigureAwait(false);
        await Assert.That(s).IsNotNull();
        await Assert.That(s!.Kind).IsEqualTo("Constant");
    }

    [Test]
    public async Task FindsNotificationSeverityConstant()
    {
        await IndexAsync().ConfigureAwait(false);
        var s = await _store.GetSymbolByNameAsync(_repoId, "SEVERITY_INFO").ConfigureAwait(false);
        await Assert.That(s).IsNotNull();
        await Assert.That(s!.Kind).IsEqualTo("Constant");
    }

    // ── Outline ───────────────────────────────────────────────────────

    [Test]
    public async Task OutlineContainsAllSymbolKinds()
    {
        await IndexAsync().ConfigureAwait(false);
        var outline = await _store.GetProjectOutlineAsync(
            _repoId, includePrivate: true, groupBy: "kind", maxDepth: 0).ConfigureAwait(false);

        var kinds = CollectSymbolKinds(outline.Groups);
        await Assert.That(kinds).Contains("Class");
        await Assert.That(kinds).Contains("Module");
        await Assert.That(kinds).Contains("Method");
        await Assert.That(kinds).Contains("Constant");
    }

    // ── Search ────────────────────────────────────────────────────────

    [Test]
    public async Task SearchFindsSymbols()
    {
        await IndexAsync().ConfigureAwait(false);
        var results = await _store.SearchSymbolsAsync(_repoId, "User", null, 20).ConfigureAwait(false);
        await Assert.That(results.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task SearchFindsByParentName()
    {
        await IndexAsync().ConfigureAwait(false);
        var results = await _store.SearchSymbolsAsync(_repoId, "UserService create_user", null, 10).ConfigureAwait(false);
        await Assert.That(results.Count).IsGreaterThan(0);
    }

    // ── Byte offset accuracy ──────────────────────────────────────────

    [Test]
    public async Task ByteOffsetPointsToClassKeyword()
    {
        await IndexAsync().ConfigureAwait(false);
        var s = await _store.GetSymbolByNameAsync(_repoId, "User").ConfigureAwait(false);
        await Assert.That(s).IsNotNull();

        var filePath = Path.Combine(_sampleProjectPath, "lib", "models", "user.rb");
        var bytes = await File.ReadAllBytesAsync(filePath).ConfigureAwait(false);
        var slice = Encoding.UTF8.GetString(bytes, s!.ByteOffset, 5);
        await Assert.That(slice).IsEqualTo("class");
    }

    // ── Doc comments ──────────────────────────────────────────────────

    [Test]
    public async Task DocCommentExtractedForClass()
    {
        await IndexAsync().ConfigureAwait(false);
        var s = await _store.GetSymbolByNameAsync(_repoId, "User").ConfigureAwait(false);
        await Assert.That(s).IsNotNull();
        await Assert.That(s!.DocComment).IsNotNull();
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private async Task IndexAsync() =>
        await _engine.IndexProjectAsync(_sampleProjectPath, "ruby").ConfigureAwait(false);

    private static string FindSamplePath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "samples", "ruby-sample-project");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new DirectoryNotFoundException("Could not find samples/ruby-sample-project");
    }

    private static HashSet<string> CollectSymbolKinds(IReadOnlyList<OutlineGroup> groups)
    {
        var kinds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            foreach (var symbol in group.Symbols)
                kinds.Add(symbol.Kind);
            foreach (var kind in CollectSymbolKinds(group.Children))
                kinds.Add(kind);
        }
        return kinds;
    }
}

using CodeCompress.Core.Indexing;
using CodeCompress.Core.Models;
using CodeCompress.Core.Parsers;
using CodeCompress.Core.Storage;
using CodeCompress.Core.Validation;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeCompress.Integration.Tests;

internal sealed class RustEndToEndTests : IDisposable
{
    private SqliteConnection _connection = null!;
    private SqliteSymbolStore _store = null!;
    private IndexEngine _engine = null!;
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

        var parsers = new ILanguageParser[] { new RustParser() };
        _engine = new IndexEngine(new FileHasher(), new ChangeTracker(), parsers, _store,
            new PathValidatorService(), NullLogger<IndexEngine>.Instance);

        _sampleProjectPath = FindSamplePath();
        _repoId = IndexEngine.ComputeRepoId(Path.GetFullPath(_sampleProjectPath));
    }

    [After(Test)]
    public async Task TearDown() => await _connection.DisposeAsync().ConfigureAwait(false);

    [Test]
    public async Task IndexProjectCorrectCounts()
    {
        var result = await _engine.IndexProjectAsync(_sampleProjectPath, "rust").ConfigureAwait(false);
        await Assert.That(result.FilesIndexed).IsEqualTo(4);
        await Assert.That(result.SymbolsFound).IsGreaterThanOrEqualTo(20);
    }

    [Test]
    public async Task FindsStruct()
    {
        await IndexAsync().ConfigureAwait(false);
        var s = await _store.GetSymbolByNameAsync(_repoId, "User").ConfigureAwait(false);
        await Assert.That(s).IsNotNull();
        await Assert.That(s!.Kind).IsEqualTo("Class");
    }

    [Test]
    public async Task FindsEnum()
    {
        await IndexAsync().ConfigureAwait(false);
        var s = await _store.GetSymbolByNameAsync(_repoId, "Role").ConfigureAwait(false);
        await Assert.That(s).IsNotNull();
        await Assert.That(s!.Kind).IsEqualTo("Enum");
    }

    [Test]
    public async Task FindsTrait()
    {
        await IndexAsync().ConfigureAwait(false);
        var s = await _store.GetSymbolByNameAsync(_repoId, "Identifiable").ConfigureAwait(false);
        await Assert.That(s).IsNotNull();
        await Assert.That(s!.Kind).IsEqualTo("Interface");
    }

    [Test]
    public async Task FindsImplMethod()
    {
        await IndexAsync().ConfigureAwait(false);
        var s = await _store.GetSymbolByNameAsync(_repoId, "User:new").ConfigureAwait(false);
        await Assert.That(s).IsNotNull();
        await Assert.That(s!.Kind).IsEqualTo("Method");
        await Assert.That(s.ParentSymbol).IsEqualTo("User");
    }

    [Test]
    public async Task FindsFunction()
    {
        await IndexAsync().ConfigureAwait(false);
        var s = await _store.GetSymbolByNameAsync(_repoId, "is_null_or_empty").ConfigureAwait(false);
        await Assert.That(s).IsNotNull();
        await Assert.That(s!.Kind).IsEqualTo("Function");
    }

    [Test]
    public async Task FindsConstant()
    {
        await IndexAsync().ConfigureAwait(false);
        var s = await _store.GetSymbolByNameAsync(_repoId, "MAX_NAME_LENGTH").ConfigureAwait(false);
        await Assert.That(s).IsNotNull();
        await Assert.That(s!.Kind).IsEqualTo("Constant");
    }

    [Test]
    public async Task FindsTypeAlias()
    {
        await IndexAsync().ConfigureAwait(false);
        var s = await _store.GetSymbolByNameAsync(_repoId, "AppResult").ConfigureAwait(false);
        await Assert.That(s).IsNotNull();
        await Assert.That(s!.Kind).IsEqualTo("Type");
    }

    [Test]
    public async Task FindsModule()
    {
        await IndexAsync().ConfigureAwait(false);
        var s = await _store.GetSymbolByNameAsync(_repoId, "models").ConfigureAwait(false);
        await Assert.That(s).IsNotNull();
        await Assert.That(s!.Kind).IsEqualTo("Module");
    }

    [Test]
    public async Task SearchFindsRustSymbols()
    {
        await IndexAsync().ConfigureAwait(false);
        var results = await _store.SearchSymbolsAsync(_repoId, "User", null, 20).ConfigureAwait(false);
        await Assert.That(results.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task DocCommentCaptured()
    {
        await IndexAsync().ConfigureAwait(false);
        var s = await _store.GetSymbolByNameAsync(_repoId, "User").ConfigureAwait(false);
        await Assert.That(s).IsNotNull();
        await Assert.That(s!.DocComment).IsNotNull();
        await Assert.That(s.DocComment!).Contains("registered user");
    }

    private async Task IndexAsync() =>
        await _engine.IndexProjectAsync(_sampleProjectPath, "rust").ConfigureAwait(false);

    private static string FindSamplePath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "samples", "rust-sample-project");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not find samples/rust-sample-project");
    }
}

using CodeCompress.Core.Indexing;
using CodeCompress.Core.Models;
using CodeCompress.Core.Parsers;
using CodeCompress.Core.Storage;
using CodeCompress.Core.Validation;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeCompress.Integration.Tests;

internal sealed class GoEndToEndTests : IDisposable
{
    private SqliteConnection _connection = null!;
    private SqliteSymbolStore _store = null!;
    private IndexEngine _engine = null!;
    private string _sampleProjectPath = null!;
    private string _repoId = null!;

    public void Dispose()
    {
        _connection?.Dispose();
    }

    [Before(Test)]
    public async Task SetUp()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync().ConfigureAwait(false);

        await Migrations.ApplyAsync(_connection).ConfigureAwait(false);

        _store = new SqliteSymbolStore(_connection);

        var parsers = new ILanguageParser[] { new GoParser() };
        var fileHasher = new FileHasher();
        var changeTracker = new ChangeTracker();
        var pathValidator = new PathValidatorService();

        _engine = new IndexEngine(
            fileHasher,
            changeTracker,
            parsers,
            _store,
            pathValidator,
            NullLogger<IndexEngine>.Instance);

        _sampleProjectPath = FindGoSampleProjectPath();
        _repoId = IndexEngine.ComputeRepoId(Path.GetFullPath(_sampleProjectPath));
    }

    [After(Test)]
    public async Task TearDown()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    // ── Indexing Tests ───────────────────────────────────────────────────

    [Test]
    public async Task IndexProjectGoSampleCorrectFilesAndSymbolCounts()
    {
        var result = await _engine.IndexProjectAsync(_sampleProjectPath, "go").ConfigureAwait(false);

        await Assert.That(result.RepoId).IsEqualTo(_repoId);
        await Assert.That(result.FilesIndexed).IsEqualTo(6);
        await Assert.That(result.SymbolsFound).IsGreaterThanOrEqualTo(25);
    }

    // ── Type Tests ────────────────────────────────────────────────────────

    [Test]
    public async Task FindsStructEntity()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbol = await _store.GetSymbolByNameAsync(_repoId, "Entity").ConfigureAwait(false);

        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Class");
        await Assert.That(symbol.Visibility).IsEqualTo("Public");
    }

    [Test]
    public async Task FindsInterfaceAuditable()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbol = await _store.GetSymbolByNameAsync(_repoId, "Auditable").ConfigureAwait(false);

        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Interface");
    }

    [Test]
    public async Task FindsGenericInterfaceRepository()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbol = await _store.GetSymbolByNameAsync(_repoId, "Repository").ConfigureAwait(false);

        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Interface");
        await Assert.That(symbol.Signature).Contains("[T any, ID comparable]");
    }

    [Test]
    public async Task FindsNamedTypeRole()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbol = await _store.GetSymbolByNameAsync(_repoId, "Role").ConfigureAwait(false);

        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Type");
    }

    // ── Function Tests ────────────────────────────────────────────────────

    [Test]
    public async Task FindsFunctionNewUser()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbol = await _store.GetSymbolByNameAsync(_repoId, "NewUser").ConfigureAwait(false);

        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Function");
        await Assert.That(symbol.Visibility).IsEqualTo("Public");
    }

    [Test]
    public async Task FindsMethodOnStruct()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbol = await _store.GetSymbolByNameAsync(_repoId, "User:AuditID").ConfigureAwait(false);

        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Method");
        await Assert.That(symbol.ParentSymbol).IsEqualTo("User");
    }

    [Test]
    public async Task FindsGenericFunction()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbol = await _store.GetSymbolByNameAsync(_repoId, "Audit").ConfigureAwait(false);

        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Function");
    }

    // ── Constant Tests ────────────────────────────────────────────────────

    [Test]
    public async Task FindsConstant()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbol = await _store.GetSymbolByNameAsync(_repoId, "MaxNameLength").ConfigureAwait(false);

        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Constant");
    }

    // ── Search Tests ──────────────────────────────────────────────────────

    [Test]
    public async Task SearchFindsGoSymbols()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.SearchSymbolsAsync(_repoId, "User", null, 20).ConfigureAwait(false);

        await Assert.That(results.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task SearchFindsMethodByParentName()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.SearchSymbolsAsync(_repoId, "UserService CreateUser", null, 10).ConfigureAwait(false);

        await Assert.That(results.Count).IsGreaterThan(0);
        await Assert.That(results[0].Symbol.Name).IsEqualTo("CreateUser");
    }

    // ── Doc Comment Tests ─────────────────────────────────────────────────

    [Test]
    public async Task DocCommentCapturedOnStruct()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbol = await _store.GetSymbolByNameAsync(_repoId, "Entity").ConfigureAwait(false);

        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.DocComment).IsNotNull();
        await Assert.That(symbol.DocComment!).Contains("base type");
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private async Task IndexSampleProjectAsync()
    {
        await _engine.IndexProjectAsync(_sampleProjectPath, "go").ConfigureAwait(false);
    }

    private static string FindGoSampleProjectPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "samples", "go-sample-project");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not find samples/go-sample-project");
    }
}

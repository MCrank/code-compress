using CodeCompress.Core.Indexing;
using CodeCompress.Core.Models;
using CodeCompress.Core.Parsers;
using CodeCompress.Core.Storage;
using CodeCompress.Core.Validation;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeCompress.Integration.Tests;

internal sealed class JavaEndToEndTests : IDisposable
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

        var parsers = new ILanguageParser[] { new JavaParser() };
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

        _sampleProjectPath = FindJavaSampleProjectPath();
        _repoId = IndexEngine.ComputeRepoId(Path.GetFullPath(_sampleProjectPath));
    }

    [After(Test)]
    public async Task TearDown()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    // ── Indexing Tests ───────────────────────────────────────────────────

    [Test]
    public async Task IndexProjectJavaSampleCorrectFilesAndSymbolCounts()
    {
        var result = await _engine.IndexProjectAsync(_sampleProjectPath, "java").ConfigureAwait(false);

        await Assert.That(result.RepoId).IsEqualTo(_repoId);
        await Assert.That(result.FilesIndexed).IsEqualTo(8);
        await Assert.That(result.SymbolsFound).IsGreaterThanOrEqualTo(30);
    }

    // ── Type Tests ────────────────────────────────────────────────────────

    [Test]
    public async Task FindsAbstractClassBaseEntity()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbol = await _store.GetSymbolByNameAsync(_repoId, "BaseEntity").ConfigureAwait(false);

        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Class");
        await Assert.That(symbol.Signature).Contains("abstract");
    }

    [Test]
    public async Task FindsFinalClassUser()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbol = await _store.GetSymbolByNameAsync(_repoId, "User").ConfigureAwait(false);

        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Class");
        await Assert.That(symbol.Signature).Contains("extends BaseEntity");
        await Assert.That(symbol.Signature).Contains("implements Auditable");
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
    public async Task FindsEnumUserRole()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbol = await _store.GetSymbolByNameAsync(_repoId, "UserRole").ConfigureAwait(false);

        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Enum");
        await Assert.That(symbol.ParentSymbol).IsEqualTo("User");
    }

    [Test]
    public async Task FindsRecordNotification()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbol = await _store.GetSymbolByNameAsync(_repoId, "Notification").ConfigureAwait(false);

        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Record");
    }

    [Test]
    public async Task FindsAnnotationTypeEventHandler()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbol = await _store.GetSymbolByNameAsync(_repoId, "EventHandler").ConfigureAwait(false);

        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Type");
    }

    [Test]
    public async Task FindsGenericInterfaceRepository()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbol = await _store.GetSymbolByNameAsync(_repoId, "Repository").ConfigureAwait(false);

        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Interface");
        await Assert.That(symbol.Signature).Contains("<T, ID>");
    }

    // ── Method Tests ──────────────────────────────────────────────────────

    [Test]
    public async Task FindsMethodOnClass()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbol = await _store.GetSymbolByNameAsync(_repoId, "UserService:createUser").ConfigureAwait(false);

        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Method");
        await Assert.That(symbol.ParentSymbol).IsEqualTo("UserService");
    }

    [Test]
    public async Task FindsInnerClassBuilder()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbol = await _store.GetSymbolByNameAsync(_repoId, "Builder").ConfigureAwait(false);

        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Class");
        await Assert.That(symbol.ParentSymbol).IsEqualTo("User");
    }

    // ── Constant Tests ────────────────────────────────────────────────────

    [Test]
    public async Task FindsStaticFinalConstant()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbol = await _store.GetSymbolByNameAsync(_repoId, "User:MAX_NAME_LENGTH").ConfigureAwait(false);

        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Constant");
    }

    // ── Search Tests ──────────────────────────────────────────────────────

    [Test]
    public async Task SearchFindsJavaSymbols()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.SearchSymbolsAsync(_repoId, "User", null, 20).ConfigureAwait(false);

        await Assert.That(results.Count).IsGreaterThan(0);
    }

    // ── Dependency Tests ──────────────────────────────────────────────────

    [Test]
    public async Task SearchFindsMethodByParentName()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        // Verify the FTS5 parent_symbol index works for Java symbols
        var results = await _store.SearchSymbolsAsync(_repoId, "UserService createUser", null, 10).ConfigureAwait(false);

        await Assert.That(results.Count).IsGreaterThan(0);
        await Assert.That(results[0].Symbol.Name).IsEqualTo("createUser");
    }

    // ── Doc Comment Tests ─────────────────────────────────────────────────

    [Test]
    public async Task JavadocCapturedOnClass()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbol = await _store.GetSymbolByNameAsync(_repoId, "BaseEntity").ConfigureAwait(false);

        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.DocComment).IsNotNull();
        await Assert.That(symbol.DocComment!).Contains("Base class");
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private async Task IndexSampleProjectAsync()
    {
        await _engine.IndexProjectAsync(_sampleProjectPath, "java").ConfigureAwait(false);
    }

    private static string FindJavaSampleProjectPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "samples", "java-sample-project");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not find samples/java-sample-project");
    }
}

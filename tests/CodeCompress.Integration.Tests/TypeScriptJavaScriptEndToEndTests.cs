using CodeCompress.Core.Indexing;
using CodeCompress.Core.Models;
using CodeCompress.Core.Parsers;
using CodeCompress.Core.Storage;
using CodeCompress.Core.Validation;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeCompress.Integration.Tests;

internal sealed class TypeScriptJavaScriptEndToEndTests : IDisposable
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

        var parsers = new ILanguageParser[] { new TypeScriptJavaScriptParser() };
        var fileHasher = new FileHasher();
        var changeTracker = new ChangeTracker();
        var pathValidator = new PathValidatorService();

        _engine = new IndexEngine(
            fileHasher, changeTracker, parsers, _store, pathValidator,
            NullLogger<IndexEngine>.Instance);

        _sampleProjectPath = FindSampleProjectPath();
        _repoId = IndexEngine.ComputeRepoId(Path.GetFullPath(_sampleProjectPath));
    }

    [After(Test)]
    public async Task TearDown()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task IndexProjectCorrectFilesAndSymbolCounts()
    {
        var result = await _engine.IndexProjectAsync(_sampleProjectPath, "typescript").ConfigureAwait(false);

        await Assert.That(result.RepoId).IsEqualTo(_repoId);
        await Assert.That(result.FilesIndexed).IsEqualTo(7);
        await Assert.That(result.SymbolsFound).IsGreaterThanOrEqualTo(25);
    }

    [Test]
    public async Task FindsExportedClass()
    {
        await IndexAsync().ConfigureAwait(false);
        var symbol = await _store.GetSymbolByNameAsync(_repoId, "User").ConfigureAwait(false);
        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Class");
    }

    [Test]
    public async Task FindsAbstractClass()
    {
        await IndexAsync().ConfigureAwait(false);
        var symbol = await _store.GetSymbolByNameAsync(_repoId, "BaseEntity").ConfigureAwait(false);
        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Class");
    }

    [Test]
    public async Task FindsInterface()
    {
        await IndexAsync().ConfigureAwait(false);
        var symbol = await _store.GetSymbolByNameAsync(_repoId, "Identifiable").ConfigureAwait(false);
        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Interface");
    }

    [Test]
    public async Task FindsEnum()
    {
        await IndexAsync().ConfigureAwait(false);
        var symbol = await _store.GetSymbolByNameAsync(_repoId, "UserRole").ConfigureAwait(false);
        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Enum");
    }

    [Test]
    public async Task FindsTypeAlias()
    {
        await IndexAsync().ConfigureAwait(false);
        var symbol = await _store.GetSymbolByNameAsync(_repoId, "UserResult").ConfigureAwait(false);
        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Type");
    }

    [Test]
    public async Task FindsFunction()
    {
        await IndexAsync().ConfigureAwait(false);
        var symbol = await _store.GetSymbolByNameAsync(_repoId, "createNotification").ConfigureAwait(false);
        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Function");
    }

    [Test]
    public async Task FindsArrowFunction()
    {
        await IndexAsync().ConfigureAwait(false);
        var symbol = await _store.GetSymbolByNameAsync(_repoId, "createInfoNotification").ConfigureAwait(false);
        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Function");
    }

    [Test]
    public async Task FindsMethodOnClass()
    {
        await IndexAsync().ConfigureAwait(false);
        var symbol = await _store.GetSymbolByNameAsync(_repoId, "UserService:createUser").ConfigureAwait(false);
        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Method");
    }

    [Test]
    public async Task FindsConstant()
    {
        await IndexAsync().ConfigureAwait(false);
        var symbol = await _store.GetSymbolByNameAsync(_repoId, "MAX_NAME_LENGTH").ConfigureAwait(false);
        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Constant");
    }

    [Test]
    public async Task FindsJavaScriptClass()
    {
        await IndexAsync().ConfigureAwait(false);
        var symbol = await _store.GetSymbolByNameAsync(_repoId, "PageResult").ConfigureAwait(false);
        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Class");
    }

    [Test]
    public async Task SearchFindsSymbols()
    {
        await IndexAsync().ConfigureAwait(false);
        var results = await _store.SearchSymbolsAsync(_repoId, "User", null, 20).ConfigureAwait(false);
        await Assert.That(results.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task DocCommentCaptured()
    {
        await IndexAsync().ConfigureAwait(false);
        var symbol = await _store.GetSymbolByNameAsync(_repoId, "BaseEntity").ConfigureAwait(false);
        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.DocComment).IsNotNull();
        await Assert.That(symbol.DocComment!).Contains("Base entity");
    }

    [Test]
    public async Task DependenciesIncludeImports()
    {
        await IndexAsync().ConfigureAwait(false);
        var results = await _store.SearchSymbolsAsync(_repoId, "UserService createUser", null, 10).ConfigureAwait(false);
        await Assert.That(results.Count).IsGreaterThan(0);
    }

    private async Task IndexAsync()
    {
        await _engine.IndexProjectAsync(_sampleProjectPath, "typescript").ConfigureAwait(false);
    }

    private static string FindSampleProjectPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "samples", "typescript-sample-project");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not find samples/typescript-sample-project");
    }
}

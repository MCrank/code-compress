using CodeCompress.Core.Indexing;
using CodeCompress.Core.Models;
using CodeCompress.Core.Parsers;
using CodeCompress.Core.Storage;
using CodeCompress.Core.Validation;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeCompress.Integration.Tests;

internal sealed class JsonConfigEndToEndTests : IDisposable
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

        var parsers = new ILanguageParser[] { new JsonConfigParser() };
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

        _sampleProjectPath = FindJsonConfigSampleProjectPath();
        _repoId = IndexEngine.ComputeRepoId(Path.GetFullPath(_sampleProjectPath));
    }

    [After(Test)]
    public async Task TearDown()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    // ── Indexing Tests ───────────────────────────────────────────────────

    [Test]
    public async Task IndexProjectJsonConfigSampleCorrectFilesAndSymbolCounts()
    {
        var result = await _engine.IndexProjectAsync(_sampleProjectPath, "json").ConfigureAwait(false);

        await Assert.That(result.RepoId).IsEqualTo(_repoId);
        await Assert.That(result.FilesIndexed).IsEqualTo(3);
        await Assert.That(result.SymbolsFound).IsGreaterThanOrEqualTo(40);
    }

    // ── Query Tests ──────────────────────────────────────────────────────

    [Test]
    public async Task ProjectOutlineJsonConfigSymbolsGroupedCorrectly()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var outline = await _store.GetProjectOutlineAsync(
            _repoId, includePrivate: true, groupBy: "file", maxDepth: 0).ConfigureAwait(false);

        await Assert.That(outline.Groups).Count().IsEqualTo(3);

        var allSymbolKinds = CollectSymbolKinds(outline.Groups);
        await Assert.That(allSymbolKinds).Contains("ConfigKey");
    }

    [Test]
    public async Task NestedKeysUseColonSeparatedQualifiedNames()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        // Logging:LogLevel:Default should be a qualified name
        var results = await _store.SearchSymbolsAsync(
            _repoId, "Default", kind: "ConfigKey", limit: 20).ConfigureAwait(false);

        var logLevelDefault = results.FirstOrDefault(r =>
            r.Symbol.Name == "Logging:LogLevel:Default");

        await Assert.That(logLevelDefault).IsNotNull();
        await Assert.That(logLevelDefault!.Symbol.ParentSymbol).IsEqualTo("Logging:LogLevel");
    }

    [Test]
    public async Task TopLevelKeysHaveNullParent()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.SearchSymbolsAsync(
            _repoId, "AppName", kind: "ConfigKey", limit: 10).ConfigureAwait(false);

        var appName = results.FirstOrDefault(r => r.Symbol.Name == "AppName");
        await Assert.That(appName).IsNotNull();
        await Assert.That(appName!.Symbol.ParentSymbol).IsNull();
    }

    [Test]
    public async Task ObjectSectionsShowKeyCount()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.SearchSymbolsAsync(
            _repoId, "ConnectionStrings", kind: "ConfigKey", limit: 10).ConfigureAwait(false);

        var section = results.FirstOrDefault(r => r.Symbol.Name == "ConnectionStrings");
        await Assert.That(section).IsNotNull();
        await Assert.That(section!.Symbol.Signature).Contains("key");
    }

    [Test]
    public async Task ArrayValuesShowItemCount()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.SearchSymbolsAsync(
            _repoId, "AllowedHosts", kind: "ConfigKey", limit: 10).ConfigureAwait(false);

        var array = results.FirstOrDefault(r => r.Symbol.Name == "AllowedHosts");
        await Assert.That(array).IsNotNull();
        await Assert.That(array!.Symbol.Signature).Contains("item");
    }

    [Test]
    public async Task BooleanValueInSignature()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.SearchSymbolsAsync(
            _repoId, "EnableMetrics", kind: "ConfigKey", limit: 10).ConfigureAwait(false);

        var flag = results.FirstOrDefault(r => r.Symbol.Name == "EnableMetrics");
        await Assert.That(flag).IsNotNull();
        await Assert.That(flag!.Symbol.Signature).Contains("true");
    }

    [Test]
    public async Task NullValueInSignature()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.SearchSymbolsAsync(
            _repoId, "MaintenanceWindow", kind: "ConfigKey", limit: 10).ConfigureAwait(false);

        var nullVal = results.FirstOrDefault(r => r.Symbol.Name == "MaintenanceWindow");
        await Assert.That(nullVal).IsNotNull();
        await Assert.That(nullVal!.Symbol.Signature).Contains("null");
    }

    [Test]
    public async Task DeeplyNestedKeysFullyQualified()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        // Authentication:Providers:Google:ClientId — 4 levels deep
        var results = await _store.SearchSymbolsAsync(
            _repoId, "ClientId", kind: "ConfigKey", limit: 20).ConfigureAwait(false);

        var googleClientId = results.FirstOrDefault(r =>
            r.Symbol.Name == "Authentication:Providers:Google:ClientId");

        await Assert.That(googleClientId).IsNotNull();
        await Assert.That(googleClientId!.Symbol.ParentSymbol).IsEqualTo("Authentication:Providers:Google");
    }

    [Test]
    public async Task Utf8MultiByteKeysIndexedCorrectly()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        // i18n.json has Japanese translations
        var results = await _store.SearchSymbolsAsync(
            _repoId, "WelcomeMessage", kind: "ConfigKey", limit: 20).ConfigureAwait(false);

        // Should find entries in both Translations and Localized sections
        await Assert.That(results).Count().IsGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task EmptyObjectAndArrayIndexed()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.SearchSymbolsAsync(
            _repoId, "EmptyConfig", kind: "ConfigKey", limit: 10).ConfigureAwait(false);

        var emptyObj = results.FirstOrDefault(r =>
            r.Symbol.Name.EndsWith("EmptyConfig", StringComparison.Ordinal));

        await Assert.That(emptyObj).IsNotNull();
    }

    [Test]
    public async Task DependencyGraphIsEmpty()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var graph = await _store.GetDependencyGraphAsync(
            _repoId, rootFile: null, direction: "dependencies", depth: 50).ConfigureAwait(false);

        // JSON config files don't have dependencies
        await Assert.That(graph.Nodes).Count().IsEqualTo(3);
        await Assert.That(graph.Edges).Count().IsEqualTo(0);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task IndexSampleProjectAsync()
    {
        await _engine.IndexProjectAsync(_sampleProjectPath, "json").ConfigureAwait(false);
    }

    private static string FindJsonConfigSampleProjectPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "CodeCompress.slnx")))
            {
                var samplePath = Path.Combine(dir, "samples", "json-config-sample-project");
                if (Directory.Exists(samplePath))
                {
                    return Path.GetFullPath(samplePath);
                }
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException(
            "Could not find samples/json-config-sample-project from " + AppContext.BaseDirectory);
    }

    private static HashSet<string> CollectSymbolKinds(IReadOnlyList<OutlineGroup> groups)
    {
        var kinds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            foreach (var symbol in group.Symbols)
            {
                kinds.Add(symbol.Kind);
            }

            foreach (var kind in CollectSymbolKinds(group.Children))
            {
                kinds.Add(kind);
            }
        }

        return kinds;
    }
}

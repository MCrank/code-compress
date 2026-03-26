using CodeCompress.Core.Indexing;
using CodeCompress.Core.Models;
using CodeCompress.Core.Parsers;
using CodeCompress.Core.Storage;
using CodeCompress.Core.Validation;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeCompress.Integration.Tests;

internal sealed class YamlConfigEndToEndTests : IDisposable
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

        var parsers = new ILanguageParser[] { new YamlConfigParser() };
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

        _sampleProjectPath = FindYamlConfigSampleProjectPath();
        _repoId = IndexEngine.ComputeRepoId(Path.GetFullPath(_sampleProjectPath));
    }

    [After(Test)]
    public async Task TearDown()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    // ── Indexing Tests ───────────────────────────────────────────────────

    [Test]
    public async Task IndexProjectYamlConfigSampleCorrectFilesAndSymbolCounts()
    {
        var result = await _engine.IndexProjectAsync(_sampleProjectPath, "yaml-config").ConfigureAwait(false);

        await Assert.That(result.RepoId).IsEqualTo(_repoId);
        await Assert.That(result.FilesIndexed).IsEqualTo(3);
        await Assert.That(result.SymbolsFound).IsGreaterThanOrEqualTo(40);
    }

    // ── Query Tests ──────────────────────────────────────────────────────

    [Test]
    public async Task ProjectOutlineYamlConfigSymbolsGroupedCorrectly()
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

        var results = await _store.SearchSymbolsAsync(
            _repoId, "dns_service_ip", kind: "ConfigKey", limit: 20).ConfigureAwait(false);

        var dnsIp = results.FirstOrDefault(r =>
            r.Symbol.Name == "networking:dns_service_ip");

        await Assert.That(dnsIp).IsNotNull();
        await Assert.That(dnsIp!.Symbol.ParentSymbol).IsEqualTo("networking");
    }

    [Test]
    public async Task TopLevelKeysHaveNullParent()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.SearchSymbolsAsync(
            _repoId, "cluster_name", kind: "ConfigKey", limit: 10).ConfigureAwait(false);

        var clusterName = results.FirstOrDefault(r => r.Symbol.Name == "cluster_name");
        await Assert.That(clusterName).IsNotNull();
        await Assert.That(clusterName!.Symbol.ParentSymbol).IsNull();
    }

    [Test]
    public async Task ObjectSectionsShowKeyCount()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.SearchSymbolsAsync(
            _repoId, "networking", kind: "ConfigKey", limit: 10).ConfigureAwait(false);

        var section = results.FirstOrDefault(r => r.Symbol.Name == "networking");
        await Assert.That(section).IsNotNull();
        await Assert.That(section!.Symbol.Signature).Contains("key");
    }

    [Test]
    public async Task ScalarArrayValuesShowItemCount()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.SearchSymbolsAsync(
            _repoId, "allowed_cidr_blocks", kind: "ConfigKey", limit: 10).ConfigureAwait(false);

        var array = results.FirstOrDefault(r => r.Symbol.Name == "allowed_cidr_blocks");
        await Assert.That(array).IsNotNull();
        await Assert.That(array!.Symbol.Signature).Contains("item");
    }

    [Test]
    public async Task ObjectArrayItemsIndexedWithIndex()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.SearchSymbolsAsync(
            _repoId, "vm_size", kind: "ConfigKey", limit: 20).ConfigureAwait(false);

        var firstPoolVmSize = results.FirstOrDefault(r =>
            r.Symbol.Name == "node_pools:0:vm_size");

        await Assert.That(firstPoolVmSize).IsNotNull();
        await Assert.That(firstPoolVmSize!.Symbol.ParentSymbol).IsEqualTo("node_pools:0");
    }

    [Test]
    public async Task BooleanValueInSignature()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.SearchSymbolsAsync(
            _repoId, "enable_monitoring", kind: "ConfigKey", limit: 10).ConfigureAwait(false);

        var flag = results.FirstOrDefault(r => r.Symbol.Name == "enable_monitoring");
        await Assert.That(flag).IsNotNull();
        await Assert.That(flag!.Symbol.Signature).Contains("true");
    }

    [Test]
    public async Task NullValueInSignature()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.SearchSymbolsAsync(
            _repoId, "maintenance_window", kind: "ConfigKey", limit: 10).ConfigureAwait(false);

        var nullVal = results.FirstOrDefault(r => r.Symbol.Name == "maintenance_window");
        await Assert.That(nullVal).IsNotNull();
        await Assert.That(nullVal!.Symbol.Signature).Contains("null");
    }

    [Test]
    public async Task DeeplyNestedKeysFullyQualified()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        // node_pools:0:labels:role — 4 levels deep (array item > nested mapping)
        var results = await _store.SearchSymbolsAsync(
            _repoId, "role", kind: "ConfigKey", limit: 20).ConfigureAwait(false);

        var roleLabel = results.FirstOrDefault(r =>
            r.Symbol.Name == "node_pools:0:labels:role");

        await Assert.That(roleLabel).IsNotNull();
        await Assert.That(roleLabel!.Symbol.ParentSymbol).IsEqualTo("node_pools:0:labels");
    }

    [Test]
    public async Task Utf8MultiByteKeysIndexedCorrectly()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        // i18n.yaml has Japanese translations
        var results = await _store.SearchSymbolsAsync(
            _repoId, "welcome_message", kind: "ConfigKey", limit: 20).ConfigureAwait(false);

        // Should find entries in both translations and localized sections
        await Assert.That(results).Count().IsGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task EmptyMappingAndSequenceIndexed()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.SearchSymbolsAsync(
            _repoId, "empty_config", kind: "ConfigKey", limit: 10).ConfigureAwait(false);

        var emptyObj = results.FirstOrDefault(r =>
            r.Symbol.Name.EndsWith("empty_config", StringComparison.Ordinal));

        await Assert.That(emptyObj).IsNotNull();
    }

    [Test]
    public async Task InlineMappingParsedCorrectly()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.SearchSymbolsAsync(
            _repoId, "environment", kind: "ConfigKey", limit: 20).ConfigureAwait(false);

        // settings.yaml has tags: {environment: production, ...}
        var tagEnv = results.FirstOrDefault(r =>
            r.Symbol.Name == "tags:environment");

        await Assert.That(tagEnv).IsNotNull();
    }

    [Test]
    public async Task YmlExtensionIndexed()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        // docker-compose.yml should be indexed
        var outline = await _store.GetProjectOutlineAsync(
            _repoId, includePrivate: true, groupBy: "file", maxDepth: 0).ConfigureAwait(false);

        var fileNames = outline.Groups.Select(g => g.Name).ToList();
        var hasYml = fileNames.Any(f => f.EndsWith(".yml", StringComparison.OrdinalIgnoreCase));
        await Assert.That(hasYml).IsTrue();
    }

    [Test]
    public async Task DependencyGraphIsEmpty()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var graph = await _store.GetDependencyGraphAsync(
            _repoId, rootFile: null, direction: "dependencies", depth: 50).ConfigureAwait(false);

        // YAML config files don't have dependencies
        await Assert.That(graph.Nodes).Count().IsEqualTo(3);
        await Assert.That(graph.Edges).Count().IsEqualTo(0);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task IndexSampleProjectAsync()
    {
        await _engine.IndexProjectAsync(_sampleProjectPath, "yaml-config").ConfigureAwait(false);
    }

    private static string FindYamlConfigSampleProjectPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "CodeCompress.slnx")))
            {
                var samplePath = Path.Combine(dir, "samples", "yaml-config-sample-project");
                if (Directory.Exists(samplePath))
                {
                    return Path.GetFullPath(samplePath);
                }
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException(
            "Could not find samples/yaml-config-sample-project from " + AppContext.BaseDirectory);
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

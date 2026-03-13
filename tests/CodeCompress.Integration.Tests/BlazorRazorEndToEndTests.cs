using CodeCompress.Core.Indexing;
using CodeCompress.Core.Models;
using CodeCompress.Core.Parsers;
using CodeCompress.Core.Storage;
using CodeCompress.Core.Validation;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeCompress.Integration.Tests;

internal sealed class BlazorRazorEndToEndTests : IDisposable
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

        var parsers = new ILanguageParser[] { new BlazorRazorParser() };
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

        _sampleProjectPath = FindBlazorRazorSampleProjectPath();
        _repoId = IndexEngine.ComputeRepoId(Path.GetFullPath(_sampleProjectPath));
    }

    [After(Test)]
    public async Task TearDown()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    // ── Indexing Tests ───────────────────────────────────────────────────

    [Test]
    public async Task IndexProjectBlazorSampleCorrectFilesAndSymbolCounts()
    {
        var result = await _engine.IndexProjectAsync(_sampleProjectPath, "blazor").ConfigureAwait(false);

        await Assert.That(result.RepoId).IsEqualTo(_repoId);
        await Assert.That(result.FilesIndexed).IsEqualTo(7);
        await Assert.That(result.SymbolsFound).IsGreaterThanOrEqualTo(30);
    }

    // ── Query Tests ──────────────────────────────────────────────────────

    [Test]
    public async Task ProjectOutlineBlazorSymbolsGroupedCorrectly()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var outline = await _store.GetProjectOutlineAsync(
            _repoId, includePrivate: true, groupBy: "file", maxDepth: 0).ConfigureAwait(false);

        await Assert.That(outline.Groups).Count().IsGreaterThanOrEqualTo(1);

        var allSymbolKinds = CollectSymbolKinds(outline.Groups);
        await Assert.That(allSymbolKinds).Contains("Class");
        await Assert.That(allSymbolKinds).Contains("Constant");
        await Assert.That(allSymbolKinds).Contains("Method");
    }

    [Test]
    public async Task GetSymbolComponentClassReturnsCounterComponent()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbol = await _store.GetSymbolByNameAsync(_repoId, "Counter").ConfigureAwait(false);

        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Class");
        await Assert.That(symbol.Signature).Contains("Razor component");
    }

    [Test]
    public async Task SearchSymbolsFindsInjectDirectives()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.SearchSymbolsAsync(
            _repoId, "WeatherService", kind: null, limit: 10).ConfigureAwait(false);

        await Assert.That(results).Count().IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task DependencyGraphCapturesInjectAndUsingDirectives()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var graph = await _store.GetDependencyGraphAsync(
            _repoId, rootFile: null, direction: "dependencies", depth: 50).ConfigureAwait(false);

        await Assert.That(graph.Edges).Count().IsGreaterThan(0);
    }

    [Test]
    public async Task AllComponentsHaveClassSymbol()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var outline = await _store.GetProjectOutlineAsync(
            _repoId, includePrivate: true, groupBy: "file", maxDepth: 0).ConfigureAwait(false);

        foreach (var group in outline.Groups)
        {
            var hasClass = group.Symbols.Any(s => s.Kind == "Class");
            await Assert.That(hasClass)
                .IsTrue()
                .Because($"File '{group.Name}' should have a Class (component) symbol");
        }
    }

    [Test]
    public async Task PageDirectivesExtractedAsSymbols()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        // @page symbols have "@page" in their name — use outline to find them
        // since "@" is a special FTS5 character
        var outline = await _store.GetProjectOutlineAsync(
            _repoId, includePrivate: true, groupBy: "file", maxDepth: 0).ConfigureAwait(false);

        var allSymbols = new List<Symbol>();
        CollectSymbolsRecursive(outline.Groups, allSymbols);

        var pageSymbols = allSymbols
            .Where(s => s.Name.StartsWith("@page", StringComparison.Ordinal))
            .ToList();

        await Assert.That(pageSymbols).Count().IsGreaterThanOrEqualTo(4);
    }

    private static void CollectSymbolsRecursive(IReadOnlyList<OutlineGroup> groups, List<Symbol> symbols)
    {
        foreach (var group in groups)
        {
            symbols.AddRange(group.Symbols);
            CollectSymbolsRecursive(group.Children, symbols);
        }
    }

    [Test]
    public async Task CodeBlockMethodsDiscoverableViaSearch()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.SearchSymbolsAsync(
            _repoId, "IncrementCount", kind: "Method", limit: 10).ConfigureAwait(false);

        await Assert.That(results).Count().IsGreaterThanOrEqualTo(1);
        await Assert.That(results[0].Symbol.Kind).IsEqualTo("Method");
    }

    [Test]
    public async Task InheritsDirectiveExtracted()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.SearchSymbolsAsync(
            _repoId, "inherits", kind: null, limit: 10).ConfigureAwait(false);

        await Assert.That(results).Count().IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task ImplementsDirectiveExtracted()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.SearchSymbolsAsync(
            _repoId, "implements", kind: null, limit: 10).ConfigureAwait(false);

        await Assert.That(results).Count().IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task LegacyFunctionsBlockMembersExtracted()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.SearchSymbolsAsync(
            _repoId, "DoWork", kind: "Method", limit: 10).ConfigureAwait(false);

        await Assert.That(results).Count().IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task EmptyComponentHasOnlyClassSymbol()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbol = await _store.GetSymbolByNameAsync(_repoId, "EmptyComponent").ConfigureAwait(false);

        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Class");
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task IndexSampleProjectAsync()
    {
        await _engine.IndexProjectAsync(_sampleProjectPath, null).ConfigureAwait(false);
    }

    private static string FindBlazorRazorSampleProjectPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "CodeCompress.slnx")))
            {
                var samplePath = Path.Combine(dir, "samples", "blazor-razor-sample-project");
                if (Directory.Exists(samplePath))
                {
                    return Path.GetFullPath(samplePath);
                }
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException(
            "Could not find samples/blazor-razor-sample-project from " + AppContext.BaseDirectory);
    }

    private static int CountOutlineSymbols(IReadOnlyList<OutlineGroup> groups)
    {
        var count = 0;
        foreach (var group in groups)
        {
            count += group.Symbols.Count;
            count += CountOutlineSymbols(group.Children);
        }

        return count;
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

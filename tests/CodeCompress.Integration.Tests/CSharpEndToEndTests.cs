using CodeCompress.Core.Indexing;
using CodeCompress.Core.Models;
using CodeCompress.Core.Parsers;
using CodeCompress.Core.Storage;
using CodeCompress.Core.Validation;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeCompress.Integration.Tests;

internal sealed class CSharpEndToEndTests : IDisposable
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

        var parsers = new ILanguageParser[] { new CSharpParser() };
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

        _sampleProjectPath = FindCSharpSampleProjectPath();
        _repoId = IndexEngine.ComputeRepoId(Path.GetFullPath(_sampleProjectPath));
    }

    [After(Test)]
    public async Task TearDown()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    // ── Indexing Tests ───────────────────────────────────────────────────

    [Test]
    public async Task IndexProjectCSharpSampleCorrectFilesAndSymbolCounts()
    {
        var result = await _engine.IndexProjectAsync(_sampleProjectPath, "csharp").ConfigureAwait(false);

        await Assert.That(result.RepoId).IsEqualTo(_repoId);
        await Assert.That(result.FilesIndexed).IsEqualTo(8);
        await Assert.That(result.SymbolsFound).IsEqualTo(57);
    }

    // ── Query Tests ──────────────────────────────────────────────────────

    [Test]
    public async Task ProjectOutlineCSharpSymbolsGroupedCorrectly()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var outline = await _store.GetProjectOutlineAsync(
            _repoId, includePrivate: true, groupBy: "file", maxDepth: 0).ConfigureAwait(false);

        await Assert.That(outline.Groups).Count().IsGreaterThanOrEqualTo(1);

        var totalSymbols = CountOutlineSymbols(outline.Groups);
        await Assert.That(totalSymbols).IsEqualTo(57);

        // Verify some specific symbol kinds appear
        var allSymbolKinds = CollectSymbolKinds(outline.Groups);
        await Assert.That(allSymbolKinds).Contains("Class");
        await Assert.That(allSymbolKinds).Contains("Method");
        await Assert.That(allSymbolKinds).Contains("Interface");
        await Assert.That(allSymbolKinds).Contains("Module");
        await Assert.That(allSymbolKinds).Contains("Constant");
        await Assert.That(allSymbolKinds).Contains("Type");
    }

    [Test]
    public async Task GetSymbolCSharpMethodReturnsSourceCode()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbol = await _store.GetSymbolByNameAsync(
            _repoId, "CombatService:ProcessAttackAsync").ConfigureAwait(false);

        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Name).IsEqualTo("ProcessAttackAsync");
        await Assert.That(symbol.Kind).IsEqualTo("Method");
        await Assert.That(symbol.ParentSymbol).IsEqualTo("CombatService");
        await Assert.That(symbol.Visibility).IsEqualTo("Public");
        await Assert.That(symbol.ByteOffset).IsGreaterThan(0);
        await Assert.That(symbol.ByteLength).IsGreaterThan(0);

        // Verify source code can be read from the actual file
        var files = await _store.GetFilesByRepoAsync(_repoId).ConfigureAwait(false);
        var file = files.First(f => f.RelativePath.Contains("CombatService", StringComparison.Ordinal)
                                    && !f.RelativePath.Contains("ICombat", StringComparison.Ordinal));
        var fullPath = Path.Combine(_sampleProjectPath, file.RelativePath);
        var bytes = await File.ReadAllBytesAsync(fullPath).ConfigureAwait(false);
        var sourceCode = System.Text.Encoding.UTF8.GetString(bytes, symbol.ByteOffset, symbol.ByteLength);

        await Assert.That(sourceCode).Contains("ProcessAttackAsync");
    }

    [Test]
    public async Task GetSymbolCSharpRecordReturnsDeclaration()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        // The record is stored as a Class kind
        var symbols = await _store.GetSymbolsByNamesAsync(_repoId, ["Player"]).ConfigureAwait(false);
        var record = symbols.FirstOrDefault(s => s.Kind == "Class" && s.Name == "Player");

        await Assert.That(record).IsNotNull();
        await Assert.That(record!.Signature).Contains("record");
        await Assert.That(record.Signature).Contains("Player");
    }

    [Test]
    public async Task SearchSymbolsCSharpNamesReturnsRankedResults()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.SearchSymbolsAsync(
            _repoId, "ProcessAttackAsync", kind: null, limit: 10).ConfigureAwait(false);

        await Assert.That(results).Count().IsGreaterThanOrEqualTo(1);

        var names = results.Select(r => r.Symbol.Name).ToList();
        await Assert.That(names).Contains("ProcessAttackAsync");
    }

    [Test]
    public async Task GetModuleApiCSharpServiceReturnsPublicApi()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var combatServiceRelPath = GetStoredRelativePath("CombatService.cs", excludePattern: "ICombat");
        var moduleApi = await _store.GetModuleApiAsync(_repoId, combatServiceRelPath).ConfigureAwait(false);

        await Assert.That(moduleApi.File.RelativePath).IsEqualTo(combatServiceRelPath);

        // Public methods in CombatService
        var methodNames = moduleApi.Symbols
            .Where(s => s.Kind == "Method")
            .Select(s => s.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        await Assert.That(methodNames).Contains("ProcessAttackAsync");
        await Assert.That(methodNames).Contains("CalculateDamage");
        await Assert.That(methodNames).Contains("HealAsync");
        await Assert.That(methodNames).Contains("CombatService"); // constructor
    }

    [Test]
    public async Task DependencyGraphUsingStatementsCaptured()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var graph = await _store.GetDependencyGraphAsync(
            _repoId, rootFile: null, direction: "dependencies", depth: 50).ConfigureAwait(false);

        // Nodes include files and their dependency targets (using statement namespaces)
        await Assert.That(graph.Nodes).Count().IsGreaterThanOrEqualTo(8);

        // Using statements create dependency edges
        await Assert.That(graph.Edges).Count().IsGreaterThan(0);
    }

    [Test]
    public async Task NamespaceExtractionAllFilesHaveNamespace()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var outline = await _store.GetProjectOutlineAsync(
            _repoId, includePrivate: true, groupBy: "file", maxDepth: 0).ConfigureAwait(false);

        // Each file group should have a Module symbol (namespace)
        foreach (var group in outline.Groups)
        {
            var symbols = group.Symbols;
            var hasModule = symbols.Any(s => s.Kind == "Module");
            await Assert.That(hasModule)
                .IsTrue()
                .Because($"File group '{group.Name}' should have a Module (namespace) symbol");
        }
    }

    [Test]
    public async Task NestedTypesParentChainCorrect()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        // ItemRarity is a nested enum inside Inventory
        var symbols = await _store.GetSymbolsByNamesAsync(_repoId, ["ItemRarity"]).ConfigureAwait(false);

        await Assert.That(symbols).Count().IsGreaterThanOrEqualTo(1);
        var itemRarity = symbols.First(s => s.Name == "ItemRarity");
        await Assert.That(itemRarity.ParentSymbol).IsEqualTo("Inventory");
        await Assert.That(itemRarity.Kind).IsEqualTo("Type");
    }

    [Test]
    public async Task XmlDocCommentsCapturedOnSymbols()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        // CombatService class should have a doc comment
        var symbols = await _store.GetSymbolsByNamesAsync(_repoId, ["CombatService"]).ConfigureAwait(false);
        var combatClass = symbols.First(s => s.Kind == "Class" && s.Name == "CombatService");

        await Assert.That(combatClass.DocComment).IsNotNull();
        await Assert.That(combatClass.DocComment!).Contains("Implementation of combat operations");

        // ProcessAttackAsync should have a doc comment
        var processAttack = await _store.GetSymbolByNameAsync(
            _repoId, "CombatService:ProcessAttackAsync").ConfigureAwait(false);

        await Assert.That(processAttack).IsNotNull();
        await Assert.That(processAttack!.DocComment).IsNotNull();
        await Assert.That(processAttack.DocComment!).Contains("Processes an attack");
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task IndexSampleProjectAsync()
    {
        await _engine.IndexProjectAsync(_sampleProjectPath, "csharp").ConfigureAwait(false);
    }

    private static string FindCSharpSampleProjectPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "CodeCompress.slnx")))
            {
                var samplePath = Path.Combine(dir, "samples", "csharp-sample-project");
                if (Directory.Exists(samplePath))
                {
                    return Path.GetFullPath(samplePath);
                }
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException(
            "Could not find samples/csharp-sample-project from " + AppContext.BaseDirectory);
    }

    private string GetStoredRelativePath(string fileName, string? excludePattern = null)
    {
        var files = _store.GetFilesByRepoAsync(_repoId).GetAwaiter().GetResult();
        var match = files.FirstOrDefault(f =>
            f.RelativePath.Contains(
                Path.GetFileNameWithoutExtension(fileName), StringComparison.OrdinalIgnoreCase)
            && (excludePattern is null
                || !f.RelativePath.Contains(excludePattern, StringComparison.OrdinalIgnoreCase)));
        return match?.RelativePath
               ?? throw new FileNotFoundException($"File not found in index: {fileName}");
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

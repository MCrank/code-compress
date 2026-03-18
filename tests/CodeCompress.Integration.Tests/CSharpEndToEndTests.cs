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
        await Assert.That(result.FilesIndexed).IsEqualTo(15);
        await Assert.That(result.SymbolsFound).IsEqualTo(99);
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
        await Assert.That(totalSymbols).IsEqualTo(99);

        // Verify some specific symbol kinds appear
        var allSymbolKinds = CollectSymbolKinds(outline.Groups);
        await Assert.That(allSymbolKinds).Contains("Class");
        await Assert.That(allSymbolKinds).Contains("Record");
        await Assert.That(allSymbolKinds).Contains("Method");
        await Assert.That(allSymbolKinds).Contains("Interface");
        await Assert.That(allSymbolKinds).Contains("Module");
        await Assert.That(allSymbolKinds).Contains("Constant");
        await Assert.That(allSymbolKinds).Contains("Enum");
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
                                    && !f.RelativePath.Contains("ICombat", StringComparison.Ordinal)
                                    && !f.RelativePath.Contains("Advanced", StringComparison.Ordinal));
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
        var record = symbols.FirstOrDefault(s => s.Kind == "Record" && s.Name == "Player");

        await Assert.That(record).IsNotNull();
        await Assert.That(record!.Signature).Contains("record");
        await Assert.That(record.Signature).Contains("Player");
    }

    [Test]
    public async Task GetSymbolBySimpleNameFindsRecord()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        // get_symbol uses GetSymbolByNameAsync — simple name lookup
        var symbol = await _store.GetSymbolByNameAsync(_repoId, "Player").ConfigureAwait(false);

        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Name).IsEqualTo("Player");
        await Assert.That(symbol.Kind).IsEqualTo("Record");
    }

    [Test]
    public async Task SearchSymbolsByKindRecordReturnsOnlyRecords()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.SearchSymbolsAsync(
            _repoId, "Player", kind: "Record", limit: 10).ConfigureAwait(false);

        await Assert.That(results).Count().IsGreaterThanOrEqualTo(1);
        await Assert.That(results.All(r => r.Symbol.Kind == "Record")).IsTrue();
    }

    [Test]
    public async Task SearchSymbolsByKindEnumReturnsOnlyEnums()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.SearchSymbolsAsync(
            _repoId, "GameState", kind: "Enum", limit: 10).ConfigureAwait(false);

        await Assert.That(results).Count().IsGreaterThanOrEqualTo(1);
        await Assert.That(results.All(r => r.Symbol.Kind == "Enum")).IsTrue();
    }

    [Test]
    public async Task SearchSymbolsByKindTypeAlsoReturnsEnums()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.SearchSymbolsAsync(
            _repoId, "GameState", kind: "Type", limit: 10).ConfigureAwait(false);

        await Assert.That(results).Count().IsGreaterThanOrEqualTo(1);
        await Assert.That(results.Any(r => r.Symbol.Kind == "Enum")).IsTrue();
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
        await Assert.That(itemRarity.Kind).IsEqualTo("Enum");
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

    [Test]
    public async Task NestedMethodByteOffsetReturnsOnlyMethodBody()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        // Get the nested method
        var method = await _store.GetSymbolByNameAsync(
            _repoId, "CombatService:ProcessAttackAsync").ConfigureAwait(false);

        // Get the parent class
        var parentSymbols = await _store.GetSymbolsByNamesAsync(_repoId, ["CombatService"]).ConfigureAwait(false);
        var parentClass = parentSymbols.First(s => s.Kind == "Class");

        await Assert.That(method).IsNotNull();
        await Assert.That(method!.ByteLength).IsLessThan(parentClass.ByteLength);

        // Read the method source and verify it contains method content, not entire class
        var files = await _store.GetFilesByRepoAsync(_repoId).ConfigureAwait(false);
        var file = files.First(f => f.RelativePath.Contains("CombatService", StringComparison.Ordinal)
                                    && !f.RelativePath.Contains("ICombat", StringComparison.Ordinal)
                                    && !f.RelativePath.Contains("Advanced", StringComparison.Ordinal));
        var fullPath = Path.Combine(_sampleProjectPath, file.RelativePath);
        var bytes = await File.ReadAllBytesAsync(fullPath).ConfigureAwait(false);
        var methodSource = System.Text.Encoding.UTF8.GetString(bytes, method.ByteOffset, method.ByteLength);

        await Assert.That(methodSource).Contains("ProcessAttackAsync");
        // Method source should NOT contain the class declaration
        await Assert.That(methodSource).DoesNotContain("public class CombatService");
    }

    // ── New Construct Coverage Tests ───────────────────────────────────

    [Test]
    public async Task StructSymbolIndexedWithCorrectKind()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbols = await _store.GetSymbolsByNamesAsync(_repoId, ["Vector2"]).ConfigureAwait(false);
        var structSymbol = symbols.First(s => s.Name == "Vector2" && s.Kind == "Class");

        await Assert.That(structSymbol).IsNotNull();
        await Assert.That(structSymbol.Signature).Contains("struct");
    }

    [Test]
    public async Task ReadonlyRecordStructIndexedAsRecord()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbol = await _store.GetSymbolByNameAsync(_repoId, "Point").ConfigureAwait(false);

        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Record");
        await Assert.That(symbol.Signature).Contains("record struct");
    }

    [Test]
    public async Task SealedClassIndexedCorrectly()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbols = await _store.GetSymbolsByNamesAsync(_repoId, ["GameConfig"]).ConfigureAwait(false);
        var sealedClass = symbols.First(s => s.Name == "GameConfig" && s.Kind == "Class");

        await Assert.That(sealedClass).IsNotNull();
        await Assert.That(sealedClass.Signature).Contains("sealed");
    }

    [Test]
    public async Task OperatorOverloadIndexedAsMethod()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        // Find operator methods in the Vector2 file
        var files = await _store.GetFilesByRepoAsync(_repoId).ConfigureAwait(false);
        var vectorFile = files.First(f => f.RelativePath.EndsWith("Vector2.cs", StringComparison.Ordinal));
        var symbols = await _store.GetSymbolsByFileAsync(vectorFile.Id).ConfigureAwait(false);

        var operators = symbols.Where(s => s.Name.StartsWith("operator", StringComparison.Ordinal)).ToList();
        await Assert.That(operators).Count().IsGreaterThanOrEqualTo(1);
        await Assert.That(operators[0].Kind).IsEqualTo("Method");
    }

    [Test]
    public async Task IndexerIndexedAsMethod()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbol = await _store.GetSymbolByNameAsync(
            _repoId, "GameConfig:this[]").ConfigureAwait(false);

        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Method");
    }

    [Test]
    public async Task FinalizerIndexedAsMethod()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbol = await _store.GetSymbolByNameAsync(
            _repoId, "CombatService:~CombatService").ConfigureAwait(false);

        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Method");
    }

    [Test]
    public async Task VirtualOverrideMethodsIndexed()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        // Virtual method in CombatService
        var virtualMethod = await _store.GetSymbolByNameAsync(
            _repoId, "CombatService:CalculateDamage").ConfigureAwait(false);
        await Assert.That(virtualMethod).IsNotNull();
        await Assert.That(virtualMethod!.Signature).Contains("virtual");

        // Override in AdvancedCombatService
        var overrideMethod = await _store.GetSymbolByNameAsync(
            _repoId, "AdvancedCombatService:CalculateDamage").ConfigureAwait(false);
        await Assert.That(overrideMethod).IsNotNull();
        await Assert.That(overrideMethod!.Signature).Contains("override");
    }

    [Test]
    public async Task ClassPrimaryConstructorIndexed()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbols = await _store.GetSymbolsByNamesAsync(_repoId, ["GameEngine"]).ConfigureAwait(false);
        var engineClass = symbols.First(s => s.Name == "GameEngine" && s.Kind == "Class");

        await Assert.That(engineClass).IsNotNull();
        await Assert.That(engineClass.Signature).Contains("GameEngine");
    }

    [Test]
    public async Task PartialRecordBothFilesContribute()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        // Player is partial across Player.cs and Player.Generated.cs
        // Both should have symbols with Parent = Player
        var statusSummary = await _store.GetSymbolByNameAsync(
            _repoId, "Player:StatusSummary").ConfigureAwait(false);

        await Assert.That(statusSummary).IsNotNull();
        await Assert.That(statusSummary!.ParentSymbol).IsEqualTo("Player");
    }

    [Test]
    public async Task FileScopedTypeIndexedAsPrivate()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbols = await _store.GetSymbolsByNamesAsync(_repoId, ["InternalHelper"]).ConfigureAwait(false);

        await Assert.That(symbols).Count().IsGreaterThanOrEqualTo(1);
        var helper = symbols.First(s => s.Name == "InternalHelper");
        // Parser maps 'file' modifier to Private visibility
        await Assert.That(helper.Visibility).IsEqualTo("Private");
    }

    [Test]
    public async Task BlockScopedNamespaceIndexedAsModule()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        // Vector2.cs uses block-scoped namespace — find it via file symbols
        var files = await _store.GetFilesByRepoAsync(_repoId).ConfigureAwait(false);
        var vectorFile = files.First(f => f.RelativePath.EndsWith("Vector2.cs", StringComparison.Ordinal));
        var symbols = await _store.GetSymbolsByFileAsync(vectorFile.Id).ConfigureAwait(false);

        var namespaceSymbol = symbols.FirstOrDefault(s => s.Kind == "Module");
        await Assert.That(namespaceSymbol).IsNotNull();
        await Assert.That(namespaceSymbol!.Name).IsEqualTo("GameProject.Models");
    }

    // ── Record Primary Constructor Parameter Tests ────────────────────

    [Test]
    public async Task RecordPrimaryConstructorParamsSearchable()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        // Player record has params: Name, Level, Health
        var results = await _store.SearchSymbolsAsync(
            _repoId, "Name", kind: "Constant", limit: 20).ConfigureAwait(false);

        var playerName = results.FirstOrDefault(r =>
            r.Symbol.Name == "Name" && r.Symbol.ParentSymbol == "Player");

        await Assert.That(playerName).IsNotNull();
        await Assert.That(playerName!.Symbol.Kind).IsEqualTo("Constant");
    }

    [Test]
    public async Task RecordPrimaryConstructorParamsExpandable()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        // Point record struct has params: X, Y
        var symbol = await _store.GetSymbolByNameAsync(
            _repoId, "Point:X").ConfigureAwait(false);

        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Constant");
        await Assert.That(symbol.ParentSymbol).IsEqualTo("Point");
    }

    [Test]
    public async Task ClassPrimaryConstructorParamsSearchableInSample()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        // GameEngine has primary constructor params: combat, inventory
        var results = await _store.SearchSymbolsAsync(
            _repoId, "combat", kind: "Constant", limit: 20).ConfigureAwait(false);

        var param = results.FirstOrDefault(r =>
            r.Symbol.Name == "combat" && r.Symbol.ParentSymbol == "GameEngine");

        await Assert.That(param).IsNotNull();
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
            (f.RelativePath.EndsWith(Path.DirectorySeparatorChar + fileName, StringComparison.OrdinalIgnoreCase)
             || f.RelativePath.EndsWith(Path.AltDirectorySeparatorChar + fileName, StringComparison.OrdinalIgnoreCase)
             || string.Equals(f.RelativePath, fileName, StringComparison.OrdinalIgnoreCase))
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

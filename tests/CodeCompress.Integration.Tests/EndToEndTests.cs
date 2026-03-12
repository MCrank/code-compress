using CodeCompress.Core.Indexing;
using CodeCompress.Core.Models;
using CodeCompress.Core.Parsers;
using CodeCompress.Core.Storage;
using CodeCompress.Core.Validation;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeCompress.Integration.Tests;

internal sealed class EndToEndTests : IDisposable
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

        var parsers = new ILanguageParser[] { new LuauParser() };
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

        _sampleProjectPath = FindSampleProjectPath();
        _repoId = IndexEngine.ComputeRepoId(Path.GetFullPath(_sampleProjectPath));
    }

    [After(Test)]
    public async Task TearDown()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    // ── Indexing Tests ───────────────────────────────────────────────────

    [Test]
    public async Task IndexProjectLuauSampleCorrectFilesAndSymbolCounts()
    {
        var result = await _engine.IndexProjectAsync(_sampleProjectPath, "luau").ConfigureAwait(false);

        await Assert.That(result.RepoId).IsEqualTo(_repoId);
        await Assert.That(result.FilesIndexed).IsEqualTo(8);
        // 8 files with classes, methods, functions, exports across the sample project
        await Assert.That(result.SymbolsFound).IsGreaterThanOrEqualTo(50);
    }

    // ── Query Tests ──────────────────────────────────────────────────────

    [Test]
    public async Task ProjectOutlineReturnsGroupedSymbols()
    {
        var result = await _engine.IndexProjectAsync(_sampleProjectPath, "luau").ConfigureAwait(false);

        var outline = await _store.GetProjectOutlineAsync(_repoId, includePrivate: true, groupBy: "file", maxDepth: 0).ConfigureAwait(false);

        await Assert.That(outline.Groups).Count().IsGreaterThanOrEqualTo(1);

        var totalSymbols = CountOutlineSymbols(outline.Groups);
        await Assert.That(totalSymbols).IsEqualTo(result.SymbolsFound);
    }

    [Test]
    public async Task ProjectOutlinePublicOnlyExcludesPrivateSymbols()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var allOutline = await _store.GetProjectOutlineAsync(_repoId, includePrivate: true, groupBy: "file", maxDepth: 0).ConfigureAwait(false);
        var publicOutline = await _store.GetProjectOutlineAsync(_repoId, includePrivate: false, groupBy: "file", maxDepth: 0).ConfigureAwait(false);

        var allSymbols = CountOutlineSymbols(allOutline.Groups);
        var publicSymbols = CountOutlineSymbols(publicOutline.Groups);

        // Public symbols should be fewer than all symbols (some are private/local)
        await Assert.That(publicSymbols).IsLessThan(allSymbols);
        await Assert.That(publicSymbols).IsGreaterThan(0);
    }

    [Test]
    public async Task GetSymbolSpecificFunctionReturnsSourceCode()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbol = await _store.GetSymbolByNameAsync(_repoId, "CombatService:ProcessAttack").ConfigureAwait(false);

        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Name).IsEqualTo("ProcessAttack");
        await Assert.That(symbol.Kind).IsEqualTo("Method");
        await Assert.That(symbol.ParentSymbol).IsEqualTo("CombatService");
        await Assert.That(symbol.Visibility).IsEqualTo("Public");
        await Assert.That(symbol.ByteOffset).IsGreaterThan(0);
        await Assert.That(symbol.ByteLength).IsGreaterThan(0);

        // Verify source code can be read from the actual file
        var files = await _store.GetFilesByRepoAsync(_repoId).ConfigureAwait(false);
        var file = files.First(f => f.RelativePath.Contains("CombatService", StringComparison.Ordinal));
        var fullPath = Path.Combine(_sampleProjectPath, file.RelativePath);
        var bytes = await File.ReadAllBytesAsync(fullPath).ConfigureAwait(false);
        var sourceCode = System.Text.Encoding.UTF8.GetString(bytes, symbol.ByteOffset, symbol.ByteLength);

        await Assert.That(sourceCode).Contains("ProcessAttack");
        await Assert.That(sourceCode).Contains("end");
    }

    [Test]
    public async Task GetSymbolsBatchRetrieveAllCorrect()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        // GetSymbolsByNamesAsync matches on unqualified name column
        var names = new[] { "CalculateDamage", "FindPath", "AddItem" };
        var symbols = await _store.GetSymbolsByNamesAsync(_repoId, names).ConfigureAwait(false);

        await Assert.That(symbols).Count().IsEqualTo(3);

        var symbolNames = symbols.Select(s => s.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        await Assert.That(symbolNames[0]).IsEqualTo("AddItem");
        await Assert.That(symbolNames[1]).IsEqualTo("CalculateDamage");
        await Assert.That(symbolNames[2]).IsEqualTo("FindPath");
    }

    [Test]
    public async Task GetModuleApiCombatServiceReturnsPublicApi()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        // Use the OS-native path separator as stored by IndexEngine
        var combatServiceRelPath = GetStoredRelativePath("CombatService.luau");
        var moduleApi = await _store.GetModuleApiAsync(_repoId, combatServiceRelPath).ConfigureAwait(false);

        await Assert.That(moduleApi.File.RelativePath).IsEqualTo(combatServiceRelPath);

        // Module API returns public symbols only
        var methodNames = moduleApi.Symbols
            .Where(s => s.Kind == "Method")
            .Select(s => s.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        await Assert.That(methodNames).Contains("ProcessAttack");
        await Assert.That(methodNames).Contains("CalculateDamage");
        await Assert.That(methodNames).Contains("ApplyDamage");
    }

    [Test]
    public async Task SearchSymbolsByNameReturnsRankedResults()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        // FTS5 does exact token matching; search for a full symbol name
        var results = await _store.SearchSymbolsAsync(_repoId, "ProcessAttack", kind: null, limit: 10).ConfigureAwait(false);

        await Assert.That(results).Count().IsGreaterThanOrEqualTo(1);

        var names = results.Select(r => r.Symbol.Name).ToList();
        await Assert.That(names).Contains("ProcessAttack");
    }

    [Test]
    public async Task SearchSymbolsByKindFiltersCorrectly()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.SearchSymbolsAsync(_repoId, "FindPath", kind: "Method", limit: 10).ConfigureAwait(false);

        await Assert.That(results).Count().IsGreaterThanOrEqualTo(1);
        await Assert.That(results.All(r => r.Symbol.Kind == "Method")).IsTrue();
    }

    // ── Snapshot Tests ───────────────────────────────────────────────────

    [Test]
    public async Task SnapshotCreateStoresCurrentState()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var snapshot = new IndexSnapshot(0, _repoId, "v1.0", DateTimeOffset.UtcNow.ToUnixTimeSeconds(), string.Empty);
        var snapshotId = await _store.CreateSnapshotAsync(snapshot).ConfigureAwait(false);

        await Assert.That(snapshotId).IsGreaterThan(0);

        var retrieved = await _store.GetSnapshotByLabelAsync(_repoId, "v1.0").ConfigureAwait(false);

        await Assert.That(retrieved).IsNotNull();
        await Assert.That(retrieved!.SnapshotLabel).IsEqualTo("v1.0");
        await Assert.That(retrieved.FileHashes).IsNotEqualTo(string.Empty);
    }

    [Test]
    public async Task ChangesSinceAfterModificationReportsAccurateDelta()
    {
        // Simulate modification by creating a temp copy
        var tempDir = Path.Combine(Path.GetTempPath(), $"codecompress-test-{Guid.NewGuid():N}");
        try
        {
            CopyDirectory(_sampleProjectPath, tempDir);

            var tempConnection = new SqliteConnection("Data Source=:memory:");
            await tempConnection.OpenAsync().ConfigureAwait(false);
            await Migrations.ApplyAsync(tempConnection).ConfigureAwait(false);

            await using (tempConnection.ConfigureAwait(false))
            {
                var tempStore = new SqliteSymbolStore(tempConnection);
                var tempEngine = new IndexEngine(
                    new FileHasher(),
                    new ChangeTracker(),
                    new ILanguageParser[] { new LuauParser() },
                    tempStore,
                    new PathValidatorService(),
                    NullLogger<IndexEngine>.Instance);

                var tempRepoId = IndexEngine.ComputeRepoId(Path.GetFullPath(tempDir));

                // First index builds initial state
                var firstResult = await tempEngine.IndexProjectAsync(tempDir, "luau").ConfigureAwait(false);
                await Assert.That(firstResult.FilesIndexed).IsEqualTo(8);

                // Create snapshot of current state
                var tempSnapshot = new IndexSnapshot(0, tempRepoId, "before-change", DateTimeOffset.UtcNow.ToUnixTimeSeconds(), string.Empty);
                await tempStore.CreateSnapshotAsync(tempSnapshot).ConfigureAwait(false);

                // Modify CombatService after snapshot
                var combatServicePath = Path.Combine(tempDir, "src", "server", "Services", "CombatService.luau");
                var content = await File.ReadAllTextAsync(combatServicePath).ConfigureAwait(false);
                content = content.Replace(
                    "return CombatService",
                    """
                    function CombatService:Heal(target: Player, amount: number)
                        local health = target:GetAttribute("Health") or 100
                        target:SetAttribute("Health", math.min(100, health + amount))
                    end

                    return CombatService
                    """,
                    StringComparison.Ordinal);
                await File.WriteAllTextAsync(combatServicePath, content).ConfigureAwait(false);

                // Re-index to pick up modification
                var secondResult = await tempEngine.IndexProjectAsync(tempDir, "luau").ConfigureAwait(false);
                await Assert.That(secondResult.FilesIndexed).IsEqualTo(1); // Only modified file

                // Verify snapshot still exists and has data
                var savedSnapshot = await tempStore.GetSnapshotByLabelAsync(tempRepoId, "before-change").ConfigureAwait(false);
                await Assert.That(savedSnapshot).IsNotNull();
                await Assert.That(savedSnapshot!.FileHashes).IsNotEqualTo(string.Empty);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    // ── Dependency Graph Tests ───────────────────────────────────────────

    [Test]
    public async Task DependencyGraphFullProjectReturnsFileNodes()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        // Use "dependencies" direction which traverses outgoing deps
        var graph = await _store.GetDependencyGraphAsync(_repoId, rootFile: null, direction: "dependencies", depth: 50).ConfigureAwait(false);

        // LuauParser does not extract require() dependencies yet,
        // so nodes are the indexed files and edges are empty
        await Assert.That(graph.Nodes).Count().IsEqualTo(8);
        await Assert.That(graph.Edges).Count().IsEqualTo(0);
    }

    [Test]
    public async Task DependencyGraphSingleFileReturnsCorrectNode()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var combatServiceRelPath = GetStoredRelativePath("CombatService.luau");
        var graph = await _store.GetDependencyGraphAsync(_repoId, rootFile: combatServiceRelPath, direction: "dependencies", depth: 50).ConfigureAwait(false);

        await Assert.That(graph.Nodes).Count().IsEqualTo(1);
        await Assert.That(graph.Nodes[0]).IsEqualTo(combatServiceRelPath);
    }

    // ── File Tree Tests ──────────────────────────────────────────────────

    [Test]
    public async Task FileTreeMatchesSampleStructure()
    {
        var srcDir = Path.Combine(_sampleProjectPath, "src");
        await Assert.That(Directory.Exists(srcDir)).IsTrue();

        var serverDir = Path.Combine(srcDir, "server");
        var clientDir = Path.Combine(srcDir, "client");
        var sharedDir = Path.Combine(srcDir, "shared");

        await Assert.That(Directory.Exists(serverDir)).IsTrue();
        await Assert.That(Directory.Exists(clientDir)).IsTrue();
        await Assert.That(Directory.Exists(sharedDir)).IsTrue();

        var luauFiles = Directory.GetFiles(_sampleProjectPath, "*.luau", SearchOption.AllDirectories);
        await Assert.That(luauFiles).Count().IsEqualTo(8);
    }

    [Test]
    public async Task FileTreeRespectsMaxDepthByVerifyingStructure()
    {
        var servicesDir = Path.Combine(_sampleProjectPath, "src", "server", "Services");
        await Assert.That(Directory.Exists(servicesDir)).IsTrue();

        var serviceFiles = Directory.GetFiles(servicesDir, "*.luau");
        await Assert.That(serviceFiles).Count().IsEqualTo(3);
    }

    // ── Cache Invalidation Tests ─────────────────────────────────────────

    [Test]
    public async Task InvalidateCacheForcesFullReindex()
    {
        var firstResult = await _engine.IndexProjectAsync(_sampleProjectPath, "luau").ConfigureAwait(false);
        await Assert.That(firstResult.FilesIndexed).IsEqualTo(8);
        var expectedSymbols = firstResult.SymbolsFound;

        // Second index should skip all (no changes)
        var secondResult = await _engine.IndexProjectAsync(_sampleProjectPath, "luau").ConfigureAwait(false);
        await Assert.That(secondResult.FilesIndexed).IsEqualTo(0);

        // Invalidate: delete all stored data
        var files = await _store.GetFilesByRepoAsync(_repoId).ConfigureAwait(false);
        var fileIds = files.Select(f => f.Id).ToList();
        foreach (var fileId in fileIds)
        {
            await _store.DeleteSymbolsByFileAsync(fileId).ConfigureAwait(false);
            await _store.DeleteDependenciesByFileAsync(fileId).ConfigureAwait(false);
            await _store.DeleteFileAsync(fileId).ConfigureAwait(false);
        }

        await _store.DeleteRepositoryAsync(_repoId).ConfigureAwait(false);

        // Third index should re-process all files
        var thirdResult = await _engine.IndexProjectAsync(_sampleProjectPath, "luau").ConfigureAwait(false);
        await Assert.That(thirdResult.FilesIndexed).IsEqualTo(8);
        await Assert.That(thirdResult.SymbolsFound).IsEqualTo(expectedSymbols);
    }

    // ── Full Round Trip Test ─────────────────────────────────────────────

    [Test]
    public async Task FullRoundTripIndexQueryModifyReindexDelta()
    {
        // 1. Index the sample project
        var indexResult = await _engine.IndexProjectAsync(_sampleProjectPath, "luau").ConfigureAwait(false);
        await Assert.That(indexResult.FilesIndexed).IsEqualTo(8);
        await Assert.That(indexResult.SymbolsFound).IsGreaterThanOrEqualTo(50);

        // 2. Query: verify outline
        var outline = await _store.GetProjectOutlineAsync(_repoId, includePrivate: true, groupBy: "file", maxDepth: 0).ConfigureAwait(false);
        await Assert.That(CountOutlineSymbols(outline.Groups)).IsEqualTo(indexResult.SymbolsFound);

        // 3. Query: verify specific symbol
        var symbol = await _store.GetSymbolByNameAsync(_repoId, "AIService:FindPath").ConfigureAwait(false);
        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Method");

        // 4. Query: search symbols by exact name
        var searchResults = await _store.SearchSymbolsAsync(_repoId, "InventoryService", kind: null, limit: 20).ConfigureAwait(false);
        await Assert.That(searchResults).Count().IsGreaterThanOrEqualTo(1);

        // 5. Create snapshot
        var snapshot = new IndexSnapshot(0, _repoId, "round-trip-v1", DateTimeOffset.UtcNow.ToUnixTimeSeconds(), string.Empty);
        var snapshotId = await _store.CreateSnapshotAsync(snapshot).ConfigureAwait(false);
        await Assert.That(snapshotId).IsGreaterThan(0);

        // 6. Re-index (no changes expected)
        var reindexResult = await _engine.IndexProjectAsync(_sampleProjectPath, "luau").ConfigureAwait(false);
        await Assert.That(reindexResult.FilesIndexed).IsEqualTo(0);

        // 7. Verify snapshot is retrievable
        var retrieved = await _store.GetSnapshotByLabelAsync(_repoId, "round-trip-v1").ConfigureAwait(false);
        await Assert.That(retrieved).IsNotNull();
        await Assert.That(retrieved!.FileHashes).IsNotEqualTo(string.Empty);
    }

    // ── Search Text (File Content FTS) Tests ───────────────────────────

    [Test]
    public async Task SearchTextReturnsResultsAfterIndexing()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.SearchTextAsync(_repoId, "CombatService", null, 10).ConfigureAwait(false);

        await Assert.That(results.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task SearchTextFindsStringLiteralsInFileContent()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        // Search for a keyword that appears in file content but may not be a symbol name
        var results = await _store.SearchTextAsync(_repoId, "function", null, 50).ConfigureAwait(false);

        await Assert.That(results.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task SearchTextReturnsEmptyForNonexistentContent()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.SearchTextAsync(_repoId, "zzz_nonexistent_token_xyz", null, 10).ConfigureAwait(false);

        await Assert.That(results).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SearchTextReturnsEmptyForWrongRepo()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.SearchTextAsync("wrong-repo-id", "CombatService", null, 10).ConfigureAwait(false);

        await Assert.That(results).Count().IsEqualTo(0);
    }

    // ── Glob Pattern Search Tests ────────────────────────────────────────

    [Test]
    public async Task SearchSymbolsFts5PrefixMatchFindsResults()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        // FTS5 prefix query — "Combat*" should match CombatService, etc.
        var results = await _store.SearchSymbolsAsync(_repoId, "Combat*", kind: null, limit: 20).ConfigureAwait(false);

        await Assert.That(results).Count().IsGreaterThanOrEqualTo(1);
        var names = results.Select(r => r.Symbol.Name).ToList();
        await Assert.That(names.Any(n => n.Contains("Combat", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task SearchSymbolsSqlLikeSuffixMatchFindsResults()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        // Suffix pattern — should use SQL LIKE '%Service'
        var results = await _store.SearchSymbolsAsync(_repoId, query: string.Empty, kind: null, limit: 20, nameLikePattern: "%Service").ConfigureAwait(false);

        await Assert.That(results).Count().IsGreaterThanOrEqualTo(1);
        await Assert.That(results.All(r => r.Symbol.Name.EndsWith("Service", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task SearchSymbolsSqlLikeContainsMatchFindsResults()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        // Contains pattern — should use SQL LIKE '%Combat%'
        var results = await _store.SearchSymbolsAsync(_repoId, query: string.Empty, kind: null, limit: 20, nameLikePattern: "%Combat%").ConfigureAwait(false);

        await Assert.That(results).Count().IsGreaterThanOrEqualTo(1);
        await Assert.That(results.All(r => r.Symbol.Name.Contains("Combat", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task SearchSymbolsEmptyQueryWithNoLikePatternReturnsEmpty()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.SearchSymbolsAsync(_repoId, query: string.Empty, kind: null, limit: 20).ConfigureAwait(false);

        await Assert.That(results).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SearchSymbolsWithPathFilterScopesToDirectory()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        // Filter by the Services subdirectory path
        var servicesPath = GetDirectoryPrefix("CombatService.luau");
        var filteredResults = await _store.SearchSymbolsAsync(_repoId, "Combat*", kind: null, limit: 20, pathFilter: servicesPath).ConfigureAwait(false);

        // Filtered results should be subset
        await Assert.That(filteredResults).Count().IsGreaterThanOrEqualTo(1);
        await Assert.That(filteredResults.All(r => r.FilePath.StartsWith(servicesPath + "/", StringComparison.OrdinalIgnoreCase)
            || r.FilePath.StartsWith(servicesPath + "\\", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task SearchSymbolsWithNonMatchingPathFilterReturnsEmpty()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.SearchSymbolsAsync(_repoId, "CombatService", kind: null, limit: 20, pathFilter: "nonexistent/path").ConfigureAwait(false);

        await Assert.That(results).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SearchTextWithPathFilterScopesToDirectory()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var servicesPath = GetDirectoryPrefix("CombatService.luau");
        var results = await _store.SearchTextAsync(_repoId, "function", glob: null, limit: 50, pathFilter: servicesPath).ConfigureAwait(false);

        await Assert.That(results).Count().IsGreaterThanOrEqualTo(1);
        await Assert.That(results.All(r => r.FilePath.StartsWith(servicesPath + "/", StringComparison.OrdinalIgnoreCase)
            || r.FilePath.StartsWith(servicesPath + "\\", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task SearchSymbolsSqlLikeWithKindFilterCombines()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        // LIKE '%Service' with kind "Class" — should find service classes
        var results = await _store.SearchSymbolsAsync(_repoId, query: string.Empty, kind: "Class", limit: 20, nameLikePattern: "%Service").ConfigureAwait(false);

        await Assert.That(results).Count().IsGreaterThanOrEqualTo(1);
        await Assert.That(results.All(r => r.Symbol.Kind == "Class" && r.Symbol.Name.EndsWith("Service", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    // ── Find References Tests ────────────────────────────────────────────

    [Test]
    public async Task FindReferencesFindsCallSitesAcrossFiles()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        // "CombatService" should appear in multiple files (definition + usages)
        var results = await _store.FindReferencesAsync(_repoId, "CombatService", _sampleProjectPath, 50).ConfigureAwait(false);

        await Assert.That(results.Count).IsGreaterThanOrEqualTo(1);
        await Assert.That(results.All(r => r.ContextSnippet.Contains("CombatService", StringComparison.Ordinal))).IsTrue();
        await Assert.That(results.All(r => r.Line > 0)).IsTrue();
    }

    [Test]
    public async Task FindReferencesReturnsLineNumbersAndSnippets()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.FindReferencesAsync(_repoId, "ProcessAttack", _sampleProjectPath, 50).ConfigureAwait(false);

        await Assert.That(results.Count).IsGreaterThanOrEqualTo(1);

        foreach (var result in results)
        {
            // Line numbers are 1-based
            await Assert.That(result.Line).IsGreaterThan(0);
            // Snippet should contain the symbol name
            await Assert.That(result.ContextSnippet).Contains("ProcessAttack");
            // Snippet should have context (multiple lines)
            await Assert.That(result.ContextSnippet).Contains("\n");
        }
    }

    [Test]
    public async Task FindReferencesWithPathFilterScopesToDirectory()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var servicesPath = GetDirectoryPrefix("CombatService.luau");
        var results = await _store.FindReferencesAsync(_repoId, "CombatService", _sampleProjectPath, 50, servicesPath).ConfigureAwait(false);

        await Assert.That(results.Count).IsGreaterThanOrEqualTo(1);
        await Assert.That(results.All(r => r.FilePath.StartsWith(servicesPath + "/", StringComparison.OrdinalIgnoreCase)
            || r.FilePath.StartsWith(servicesPath + "\\", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task FindReferencesRespectsLimit()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.FindReferencesAsync(_repoId, "function", _sampleProjectPath, 3).ConfigureAwait(false);

        await Assert.That(results.Count).IsLessThanOrEqualTo(3);
    }

    [Test]
    public async Task FindReferencesNoMatchesReturnsEmpty()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.FindReferencesAsync(_repoId, "zzz_completely_nonexistent_xyz", _sampleProjectPath, 20).ConfigureAwait(false);

        await Assert.That(results).Count().IsEqualTo(0);
    }

    [Test]
    public async Task FindReferencesEmptySymbolNameReturnsEmpty()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.FindReferencesAsync(_repoId, "", _sampleProjectPath, 20).ConfigureAwait(false);

        await Assert.That(results).Count().IsEqualTo(0);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Gets the directory prefix for a file in the index (without trailing separator).
    /// </summary>
    private string GetDirectoryPrefix(string fileName)
    {
        var relativePath = GetStoredRelativePath(fileName);
        var separator = relativePath.Contains('/', StringComparison.Ordinal) ? '/' : '\\';
        var lastSep = relativePath.LastIndexOf(separator);
        return lastSep >= 0 ? relativePath[..lastSep] : string.Empty;
    }


    private async Task IndexSampleProjectAsync()
    {
        await _engine.IndexProjectAsync(_sampleProjectPath, "luau").ConfigureAwait(false);
    }

    private static string FindSampleProjectPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "CodeCompress.slnx")))
            {
                var samplePath = Path.Combine(dir, "samples", "luau-sample-project");
                if (Directory.Exists(samplePath))
                {
                    return Path.GetFullPath(samplePath);
                }
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException(
            "Could not find samples/luau-sample-project from " + AppContext.BaseDirectory);
    }

    /// <summary>
    /// Gets the relative path as stored in the database (OS-native separators).
    /// </summary>
    private string GetStoredRelativePath(string fileName)
    {
        var files = _store.GetFilesByRepoAsync(_repoId).GetAwaiter().GetResult();
        var match = files.FirstOrDefault(f => f.RelativePath.Contains(
            Path.GetFileNameWithoutExtension(fileName), StringComparison.OrdinalIgnoreCase));
        return match?.RelativePath ?? throw new FileNotFoundException($"File not found in index: {fileName}");
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

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (var dir in Directory.GetDirectories(source))
        {
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
        }
    }
}

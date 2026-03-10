using CodeCompress.Core.Indexing;
using CodeCompress.Core.Models;
using CodeCompress.Core.Parsers;
using CodeCompress.Core.Storage;
using CodeCompress.Core.Validation;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeCompress.Integration.Tests;

internal sealed class MixedLanguageTests : IDisposable
{
    private SqliteConnection _connection = null!;
    private SqliteSymbolStore _store = null!;
    private IndexEngine _engine = null!;
    private string _tempDir = null!;
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

        // Register BOTH parsers
        var parsers = new ILanguageParser[] { new LuauParser(), new CSharpParser() };
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

        _tempDir = Path.Combine(Path.GetTempPath(), $"codecompress-mixed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        CreateMixedLanguageFiles();

        _repoId = IndexEngine.ComputeRepoId(Path.GetFullPath(_tempDir));
    }

    [After(Test)]
    public async Task TearDown()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);

        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    // ── Mixed Language Tests ─────────────────────────────────────────────

    [Test]
    public async Task MixedProjectBothParsersContribute()
    {
        var result = await _engine.IndexProjectAsync(_tempDir).ConfigureAwait(false);

        // 2 files: 1 .cs + 1 .luau
        await Assert.That(result.FilesIndexed).IsEqualTo(2);
        await Assert.That(result.SymbolsFound).IsGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task MixedProjectProjectOutlineShowsBothLanguages()
    {
        await _engine.IndexProjectAsync(_tempDir).ConfigureAwait(false);

        var outline = await _store.GetProjectOutlineAsync(
            _repoId, includePrivate: true, groupBy: "file", maxDepth: 0).ConfigureAwait(false);

        var allSymbolNames = new List<string>();
        CollectSymbolNames(outline.Groups, allSymbolNames);

        // C# symbols
        await Assert.That(allSymbolNames).Contains("PlayerService");

        // Luau symbols
        await Assert.That(allSymbolNames).Contains("greet");
    }

    [Test]
    public async Task MixedProjectSearchSymbolsFindsBothLanguages()
    {
        await _engine.IndexProjectAsync(_tempDir).ConfigureAwait(false);

        // Search for C# symbol
        var csharpResults = await _store.SearchSymbolsAsync(
            _repoId, "PlayerService", kind: null, limit: 10).ConfigureAwait(false);
        await Assert.That(csharpResults).Count().IsGreaterThanOrEqualTo(1);

        // Search for Luau symbol
        var luauResults = await _store.SearchSymbolsAsync(
            _repoId, "greet", kind: null, limit: 10).ConfigureAwait(false);
        await Assert.That(luauResults).Count().IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task ParserSelectionByExtensionCorrect()
    {
        await _engine.IndexProjectAsync(_tempDir).ConfigureAwait(false);

        var files = await _store.GetFilesByRepoAsync(_repoId).ConfigureAwait(false);

        var csFile = files.First(f => f.RelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));
        var luauFile = files.First(f => f.RelativePath.EndsWith(".luau", StringComparison.OrdinalIgnoreCase));

        // C# file should have C#-style symbols (namespace = Module, class = Class)
        var csSymbols = await _store.GetSymbolsByFileAsync(csFile.Id).ConfigureAwait(false);
        var csKinds = csSymbols.Select(s => s.Kind).ToHashSet(StringComparer.Ordinal);
        await Assert.That(csKinds).Contains("Module"); // namespace
        await Assert.That(csKinds).Contains("Class"); // class

        // Luau file should have Luau-style symbols (function = Function)
        var luauSymbols = await _store.GetSymbolsByFileAsync(luauFile.Id).ConfigureAwait(false);
        var luauKinds = luauSymbols.Select(s => s.Kind).ToHashSet(StringComparer.Ordinal);
        await Assert.That(luauKinds).Contains("Function");
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private void CreateMixedLanguageFiles()
    {
        // C# file
        var csContent = """
            using System;

            namespace MixedProject;

            public class PlayerService
            {
                public string GetName()
                {
                    return "Player";
                }
            }
            """;
        File.WriteAllText(Path.Combine(_tempDir, "PlayerService.cs"), csContent);

        // Luau file
        var luauContent = """
            function greet(name: string): string
                return "Hello, " .. name
            end

            return greet
            """;
        File.WriteAllText(Path.Combine(_tempDir, "greeter.luau"), luauContent);
    }

    private static void CollectSymbolNames(IReadOnlyList<OutlineGroup> groups, List<string> names)
    {
        foreach (var group in groups)
        {
            foreach (var symbol in group.Symbols)
            {
                names.Add(symbol.Name);
            }

            CollectSymbolNames(group.Children, names);
        }
    }
}

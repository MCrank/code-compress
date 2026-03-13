using CodeCompress.Core.Indexing;
using CodeCompress.Core.Models;
using CodeCompress.Core.Parsers;
using CodeCompress.Core.Storage;
using CodeCompress.Core.Validation;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeCompress.Integration.Tests;

internal sealed class TerraformEndToEndTests : IDisposable
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

        var parsers = new ILanguageParser[] { new TerraformParser() };
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

        _sampleProjectPath = FindTerraformSampleProjectPath();
        _repoId = IndexEngine.ComputeRepoId(Path.GetFullPath(_sampleProjectPath));
    }

    [After(Test)]
    public async Task TearDown()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    // ── Indexing Tests ───────────────────────────────────────────────────

    [Test]
    public async Task IndexProjectTerraformSampleCorrectFilesAndSymbolCounts()
    {
        var result = await _engine.IndexProjectAsync(_sampleProjectPath, "terraform").ConfigureAwait(false);

        await Assert.That(result.RepoId).IsEqualTo(_repoId);
        await Assert.That(result.FilesIndexed).IsEqualTo(8); // 7 .tf + 1 .tfvars
        await Assert.That(result.SymbolsFound).IsGreaterThanOrEqualTo(35);
    }

    // ── Query Tests ──────────────────────────────────────────────────────

    [Test]
    public async Task ProjectOutlineTerraformSymbolsGroupedCorrectly()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var outline = await _store.GetProjectOutlineAsync(
            _repoId, includePrivate: true, groupBy: "file", maxDepth: 0).ConfigureAwait(false);

        await Assert.That(outline.Groups).Count().IsGreaterThanOrEqualTo(1);

        var allSymbolKinds = CollectSymbolKinds(outline.Groups);
        await Assert.That(allSymbolKinds).Contains("Class");     // resource, data
        await Assert.That(allSymbolKinds).Contains("Constant");  // variable, locals
        await Assert.That(allSymbolKinds).Contains("Export");    // output
        await Assert.That(allSymbolKinds).Contains("Module");    // module, terraform, provider
        await Assert.That(allSymbolKinds).Contains("ConfigKey"); // .tfvars
    }

    [Test]
    public async Task GetSymbolResourceReturnsAwsInstance()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbol = await FindSymbolByNameAsync("aws_instance.web").ConfigureAwait(false);

        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Class");
        await Assert.That(symbol.Visibility).IsEqualTo("Public");
    }

    [Test]
    public async Task GetSymbolDataSourceReturnsAwsAmi()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbol = await FindSymbolByNameAsync("data.aws_ami.ubuntu").ConfigureAwait(false);

        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Class");
    }

    [Test]
    public async Task GetSymbolVariableReturnsWithDocComment()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbol = await FindSymbolByNameAsync("var.aws_region").ConfigureAwait(false);

        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Constant");
        await Assert.That(symbol.DocComment).IsNotNull();
        await Assert.That(symbol.DocComment!).Contains("AWS region");
    }

    [Test]
    public async Task GetSymbolOutputReturnsExportKind()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbol = await FindSymbolByNameAsync("output.web_instance_ids").ConfigureAwait(false);

        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Export");
        await Assert.That(symbol.DocComment).IsNotNull();
    }

    [Test]
    public async Task GetSymbolLocalsReturnsPrivateConstant()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbol = await FindSymbolByNameAsync("local.common_tags").ConfigureAwait(false);

        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Constant");
        await Assert.That(symbol.Visibility).IsEqualTo("Private");
    }

    [Test]
    public async Task GetSymbolModuleReturnsModuleKind()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbol = await FindSymbolByNameAsync("module.vpc").ConfigureAwait(false);

        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Module");
    }

    [Test]
    public async Task SearchSymbolsByKindClassReturnsResourcesAndDataSources()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.SearchSymbolsAsync(
            _repoId, "aws", kind: "Class", limit: 20).ConfigureAwait(false);

        await Assert.That(results).Count().IsGreaterThanOrEqualTo(5);
    }

    [Test]
    public async Task SearchSymbolsByKindExportReturnsOutputs()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var results = await _store.SearchSymbolsAsync(
            _repoId, "output", kind: "Export", limit: 20).ConfigureAwait(false);

        await Assert.That(results).Count().IsGreaterThanOrEqualTo(4);
    }

    [Test]
    public async Task DependencyGraphCapturesModuleSources()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var graph = await _store.GetDependencyGraphAsync(
            _repoId, rootFile: null, direction: "dependencies", depth: 50).ConfigureAwait(false);

        await Assert.That(graph.Edges).Count().IsGreaterThanOrEqualTo(3);
    }

    [Test]
    public async Task TerraformBlockExtracted()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbol = await _store.GetSymbolByNameAsync(_repoId, "terraform").ConfigureAwait(false);

        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Module");
    }

    [Test]
    public async Task ProviderBlockExtracted()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var symbol = await FindSymbolByNameAsync("provider.aws").ConfigureAwait(false);

        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Module");
    }

    [Test]
    public async Task TfvarsFileProducesConfigKeySymbols()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var outline = await _store.GetProjectOutlineAsync(
            _repoId, includePrivate: true, groupBy: "file", maxDepth: 0).ConfigureAwait(false);

        var allSymbolKinds = CollectSymbolKinds(outline.Groups);
        await Assert.That(allSymbolKinds).Contains("ConfigKey");
    }

    [Test]
    public async Task HeredocDoesNotConfuseBlockBoundaries()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        // compute.tf has heredoc in aws_instance.web
        var webSymbol = await FindSymbolByNameAsync("aws_instance.web").ConfigureAwait(false);
        await Assert.That(webSymbol).IsNotNull();
        await Assert.That(webSymbol!.ByteLength).IsGreaterThan(0);

        // Also verify the resource after the heredoc (aws_eip.web)
        var eipSymbol = await FindSymbolByNameAsync("aws_eip.web").ConfigureAwait(false);
        await Assert.That(eipSymbol).IsNotNull();
    }

    [Test]
    public async Task IamPolicyHeredocJsonDoesNotConfuseParsing()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        // database.tf has heredoc JSON with braces in aws_iam_policy.db_access
        var symbol = await FindSymbolByNameAsync("aws_iam_policy.db_access").ConfigureAwait(false);

        await Assert.That(symbol).IsNotNull();
        await Assert.That(symbol!.Kind).IsEqualTo("Class");
    }

    [Test]
    public async Task VariableDescriptionCapturedAsDocComment()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var envVar = await FindSymbolByNameAsync("var.environment").ConfigureAwait(false);

        await Assert.That(envVar).IsNotNull();
        await Assert.That(envVar!.DocComment).IsNotNull();
        await Assert.That(envVar.DocComment!).Contains("Deployment environment");
    }

    [Test]
    public async Task AllVariablesExtractedFromVariablesTf()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        var allSymbols = await GetAllSymbolsAsync().ConfigureAwait(false);
        var varSymbols = allSymbols
            .Where(s => s.Name.StartsWith("var.", StringComparison.Ordinal))
            .ToList();

        await Assert.That(varSymbols).Count().IsGreaterThanOrEqualTo(9);
    }

    [Test]
    public async Task DynamicBlockDoesNotCreateExtraSymbols()
    {
        await IndexSampleProjectAsync().ConfigureAwait(false);

        // network.tf has aws_security_group.web with dynamic ingress — should be 1 symbol
        var allSymbols = await GetAllSymbolsAsync().ConfigureAwait(false);
        var sgWebSymbols = allSymbols.Where(s => s.Name == "aws_security_group.web").ToList();

        await Assert.That(sgWebSymbols).Count().IsEqualTo(1);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task IndexSampleProjectAsync()
    {
        await _engine.IndexProjectAsync(_sampleProjectPath, "terraform").ConfigureAwait(false);
    }

    /// <summary>
    /// Finds a symbol by exact name across all indexed files.
    /// GetSymbolByNameAsync splits on '.' which breaks Terraform's dotted names,
    /// so we query all files and search by name directly.
    /// </summary>
    private async Task<Symbol?> FindSymbolByNameAsync(string name)
    {
        var files = await _store.GetFilesByRepoAsync(_repoId).ConfigureAwait(false);

        foreach (var file in files)
        {
            var symbols = await _store.GetSymbolsByFileAsync(file.Id).ConfigureAwait(false);
            var match = symbols.FirstOrDefault(
                s => string.Equals(s.Name, name, StringComparison.Ordinal));
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private async Task<List<Symbol>> GetAllSymbolsAsync()
    {
        var files = await _store.GetFilesByRepoAsync(_repoId).ConfigureAwait(false);
        var allSymbols = new List<Symbol>();

        foreach (var file in files)
        {
            var symbols = await _store.GetSymbolsByFileAsync(file.Id).ConfigureAwait(false);
            allSymbols.AddRange(symbols);
        }

        return allSymbols;
    }

    private static string FindTerraformSampleProjectPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "CodeCompress.slnx")))
            {
                var samplePath = Path.Combine(dir, "samples", "terraform-sample-project");
                if (Directory.Exists(samplePath))
                {
                    return Path.GetFullPath(samplePath);
                }
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException(
            "Could not find samples/terraform-sample-project from " + AppContext.BaseDirectory);
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

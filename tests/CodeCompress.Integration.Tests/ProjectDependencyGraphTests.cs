using CodeCompress.Core.Indexing;
using CodeCompress.Core.Models;
using CodeCompress.Core.Parsers;
using CodeCompress.Core.Storage;
using CodeCompress.Core.Validation;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeCompress.Integration.Tests;

internal sealed class ProjectDependencyGraphTests : IDisposable
{
    private SqliteConnection _connection = null!;
    private SqliteSymbolStore _store = null!;
    private IndexEngine _engine = null!;
    private string _tempDir = null!;
    private string _repoId = null!;

    public void Dispose()
    {
        _connection?.Dispose();

        if (_tempDir is not null && Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch (IOException) { /* best effort cleanup */ }
        }
    }

    [Before(Test)]
    public async Task SetUp()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync().ConfigureAwait(false);
        await Migrations.ApplyAsync(_connection).ConfigureAwait(false);

        _store = new SqliteSymbolStore(_connection);

        // Register both CSharp and DotNetProject parsers
        var parsers = new ILanguageParser[] { new CSharpParser(), new DotNetProjectParser() };
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

        _tempDir = Path.Combine(Path.GetTempPath(), "codecompress-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);

        await CreateMultiProjectStructureAsync().ConfigureAwait(false);

        _repoId = IndexEngine.ComputeRepoId(Path.GetFullPath(_tempDir));
    }

    [After(Test)]
    public async Task TearDown()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    // ── Tests ────────────────────────────────────────────────────────────

    [Test]
    public async Task ProjectDependencyGraphShowsInterProjectReferences()
    {
        await IndexAsync().ConfigureAwait(false);

        var result = await _store.GetProjectDependencyGraphAsync(_repoId, projectFilter: null)
            .ConfigureAwait(false);

        // Should find both project files
        await Assert.That(result.Projects).Count().IsGreaterThanOrEqualTo(2);

        var projectNames = result.Projects.Select(p => p.Name).ToList();
        await Assert.That(projectNames).Contains("MyApp");
        await Assert.That(projectNames).Contains("MyCore");
    }

    [Test]
    public async Task ProjectDependencyGraphShowsEdgeFromAppToCore()
    {
        await IndexAsync().ConfigureAwait(false);

        var result = await _store.GetProjectDependencyGraphAsync(_repoId, projectFilter: null)
            .ConfigureAwait(false);

        // MyApp references MyCore
        var appToCore = result.Edges.FirstOrDefault(
            e => e.FromProject == "MyApp" && e.ToProject == "MyCore");

        await Assert.That(appToCore).IsNotNull();
    }

    [Test]
    public async Task ProjectDependencyGraphShowsSharedPublicTypes()
    {
        await IndexAsync().ConfigureAwait(false);

        var result = await _store.GetProjectDependencyGraphAsync(_repoId, projectFilter: null)
            .ConfigureAwait(false);

        var appToCore = result.Edges.First(
            e => e.FromProject == "MyApp" && e.ToProject == "MyCore");

        // MyCore has public interface IService and class ServiceBase
        await Assert.That(appToCore.SharedTypes).Contains("Interface: IService");
        await Assert.That(appToCore.SharedTypes).Contains("Class: ServiceBase");
    }

    [Test]
    public async Task ProjectFilterReturnsOnlyMatchingProjects()
    {
        await IndexAsync().ConfigureAwait(false);

        var result = await _store.GetProjectDependencyGraphAsync(_repoId, projectFilter: "MyApp")
            .ConfigureAwait(false);

        await Assert.That(result.Projects).Count().IsEqualTo(1);
        await Assert.That(result.Projects[0].Name).IsEqualTo("MyApp");
    }

    [Test]
    public async Task NoProjectFilesReturnsEmptyResult()
    {
        // Index a directory with no .csproj files
        var emptyDir = Path.Combine(_tempDir, "empty");
        Directory.CreateDirectory(emptyDir);
        await File.WriteAllTextAsync(Path.Combine(emptyDir, "test.cs"), "namespace Test; public class Foo { }").ConfigureAwait(false);

        var emptyRepoId = IndexEngine.ComputeRepoId(Path.GetFullPath(emptyDir));
        await _engine.IndexProjectAsync(emptyDir, "csharp").ConfigureAwait(false);

        var result = await _store.GetProjectDependencyGraphAsync(emptyRepoId, projectFilter: null)
            .ConfigureAwait(false);

        await Assert.That(result.Projects).Count().IsEqualTo(0);
        await Assert.That(result.Edges).Count().IsEqualTo(0);
    }

    [Test]
    public async Task ProjectWithNoReferencesHasNoEdges()
    {
        await IndexAsync().ConfigureAwait(false);

        var result = await _store.GetProjectDependencyGraphAsync(_repoId, projectFilter: "MyCore")
            .ConfigureAwait(false);

        // MyCore doesn't reference anything
        var coreEdges = result.Edges.Where(e => e.FromProject == "MyCore").ToList();
        await Assert.That(coreEdges).Count().IsEqualTo(0);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task IndexAsync()
    {
        await _engine.IndexProjectAsync(_tempDir).ConfigureAwait(false);
    }

    private async Task CreateMultiProjectStructureAsync()
    {
        // Create src/MyCore/MyCore.csproj
        var coreDir = Path.Combine(_tempDir, "src", "MyCore");
        Directory.CreateDirectory(coreDir);

        await File.WriteAllTextAsync(Path.Combine(coreDir, "MyCore.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """).ConfigureAwait(false);

        await File.WriteAllTextAsync(Path.Combine(coreDir, "IService.cs"),
            """
            namespace MyCore;
            public interface IService
            {
                void Execute();
            }
            """).ConfigureAwait(false);

        await File.WriteAllTextAsync(Path.Combine(coreDir, "ServiceBase.cs"),
            """
            namespace MyCore;
            public class ServiceBase : IService
            {
                public virtual void Execute() { }
            }
            """).ConfigureAwait(false);

        // Internal type should NOT appear in shared types
        await File.WriteAllTextAsync(Path.Combine(coreDir, "InternalHelper.cs"),
            """
            namespace MyCore;
            internal class InternalHelper
            {
                public static void DoWork() { }
            }
            """).ConfigureAwait(false);

        // Create src/MyApp/MyApp.csproj with ProjectReference to MyCore
        var appDir = Path.Combine(_tempDir, "src", "MyApp");
        Directory.CreateDirectory(appDir);

        await File.WriteAllTextAsync(Path.Combine(appDir, "MyApp.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\MyCore\MyCore.csproj" />
              </ItemGroup>
            </Project>
            """).ConfigureAwait(false);

        await File.WriteAllTextAsync(Path.Combine(appDir, "Program.cs"),
            """
            namespace MyApp;
            public class Program
            {
                public static void Main() { }
            }
            """).ConfigureAwait(false);
    }
}

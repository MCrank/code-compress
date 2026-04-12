using CodeCompress.Core.Indexing;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace CodeCompress.Core.Tests.Indexing;

internal sealed class GitIgnoreFilterTests
{
    private string _tempDir = null!;
    private ILogger<GitIgnoreFilter> _logger = null!;
    private GitIgnoreFilter _filter = null!;

    [Before(Test)]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"GitIgnoreFilterTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _logger = Substitute.For<ILogger<GitIgnoreFilter>>();
        _filter = new GitIgnoreFilter(_logger);
    }

    [After(Test)]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    // ── Non-git directory fallback ────────────────────────────────────

    [Test]
    public async Task NonGitDirectoryReturnsEmptySet()
    {
        var paths = new List<string> { "src/Program.cs", "src/Models/User.cs" };

        var result = await _filter.GetIgnoredPathsAsync(_tempDir, paths).ConfigureAwait(false);

        await Assert.That(result).Count().IsEqualTo(0);
    }

    // ── Empty paths ──────────────────────────────────────────────────

    [Test]
    public async Task EmptyPathListReturnsEmptySet()
    {
        var result = await _filter.GetIgnoredPathsAsync(_tempDir, []).ConfigureAwait(false);

        await Assert.That(result).Count().IsEqualTo(0);
    }

    // ── Real git repo — gitignore filtering ──────────────────────────

    [Test]
    public async Task GitRepoWithGitignoreFiltersIgnoredPaths()
    {
        await RunGitAsync(_tempDir, "init").ConfigureAwait(false);
        await RunGitAsync(_tempDir, "config user.email test@test.com").ConfigureAwait(false);
        await RunGitAsync(_tempDir, "config user.name Test").ConfigureAwait(false);

        await File.WriteAllTextAsync(Path.Combine(_tempDir, ".gitignore"), "dist/\ncoverage/\n").ConfigureAwait(false);

        CreateFile("src/Program.cs", "class Program {}");
        CreateFile("dist/bundle.js", "var x = 1;");
        CreateFile("coverage/report.html", "<html></html>");

        var paths = new List<string> { "src/Program.cs", "dist/bundle.js", "coverage/report.html" };
        var result = await _filter.GetIgnoredPathsAsync(_tempDir, paths).ConfigureAwait(false);

        await Assert.That(result.Count).IsGreaterThanOrEqualTo(2);
        await Assert.That(result).Contains(Path.Combine("dist", "bundle.js"));
        await Assert.That(result).Contains(Path.Combine("coverage", "report.html"));
        await Assert.That(result).DoesNotContain(Path.Combine("src", "Program.cs"));
    }

    [Test]
    public async Task GitRepoWithNoGitignoreReturnsEmptySet()
    {
        await RunGitAsync(_tempDir, "init").ConfigureAwait(false);

        CreateFile("src/Program.cs", "class Program {}");

        var paths = new List<string> { "src/Program.cs" };
        var result = await _filter.GetIgnoredPathsAsync(_tempDir, paths).ConfigureAwait(false);

        await Assert.That(result).Count().IsEqualTo(0);
    }

    [Test]
    public async Task GitRepoWithNegationPatternIncludesNegatedFile()
    {
        await RunGitAsync(_tempDir, "init").ConfigureAwait(false);
        await RunGitAsync(_tempDir, "config user.email test@test.com").ConfigureAwait(false);
        await RunGitAsync(_tempDir, "config user.name Test").ConfigureAwait(false);

        await File.WriteAllTextAsync(Path.Combine(_tempDir, ".gitignore"), "*.log\n!important.log\n").ConfigureAwait(false);

        CreateFile("debug.log", "debug");
        CreateFile("important.log", "important");
        CreateFile("src/Program.cs", "class Program {}");

        var paths = new List<string> { "debug.log", "important.log", "src/Program.cs" };
        var result = await _filter.GetIgnoredPathsAsync(_tempDir, paths).ConfigureAwait(false);

        await Assert.That(result).Contains("debug.log");
        await Assert.That(result).DoesNotContain("important.log");
        await Assert.That(result).DoesNotContain(Path.Combine("src", "Program.cs"));
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private void CreateFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_tempDir, relativePath);
        var dir = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(fullPath, content);
    }

    private static async Task RunGitAsync(string workingDirectory, string arguments)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = System.Diagnostics.Process.Start(psi)!;
        await process.WaitForExitAsync().ConfigureAwait(false);
    }
}

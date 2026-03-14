using CodeCompress.Core.Indexing;

namespace CodeCompress.Core.Tests.Indexing;

internal sealed class ProjectRootResolverTests
{
    private ProjectRootResolver _resolver = null!;

    [Before(Test)]
    public void SetUp()
    {
        _resolver = new ProjectRootResolver();
    }

    [Test]
    public async Task ResolveProjectRootReturnsGitRootWhenGitExists()
    {
        // The test project itself is inside a git repo
        var thisDir = AppContext.BaseDirectory;
        var result = _resolver.ResolveProjectRoot(thisDir);

        // Should walk up and find the .git directory
        await Assert.That(Directory.Exists(Path.Combine(result, ".git"))).IsTrue();
    }

    [Test]
    public async Task ResolveProjectRootReturnsExactRootWhenPathIsRoot()
    {
        // Find the actual git root for this repo
        var thisDir = AppContext.BaseDirectory;
        var gitRoot = _resolver.ResolveProjectRoot(thisDir);

        // Passing the root itself should return the same root
        var result = _resolver.ResolveProjectRoot(gitRoot);

        await Assert.That(result).IsEqualTo(gitRoot);
    }

    [Test]
    public async Task ResolveProjectRootFallsBackToGivenPathWhenNoGit()
    {
        // Use a temp directory with no .git anywhere above it
        // The filesystem root won't have .git, so we go deep enough
        var tempDir = Path.Combine(Path.GetTempPath(), $"codecompress-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var result = _resolver.ResolveProjectRoot(tempDir);

            // Should fall back to the given path since no .git exists
            await Assert.That(result).IsEqualTo(Path.GetFullPath(tempDir));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public async Task ResolveProjectRootFindsNearestGitInNestedRepos()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codecompress-test-{Guid.NewGuid():N}");
        var outerGit = Path.Combine(tempDir, ".git");
        var innerDir = Path.Combine(tempDir, "packages", "sub-repo");
        var innerGit = Path.Combine(innerDir, ".git");
        var deepDir = Path.Combine(innerDir, "src", "lib");

        Directory.CreateDirectory(outerGit);
        Directory.CreateDirectory(innerGit);
        Directory.CreateDirectory(deepDir);

        try
        {
            var result = _resolver.ResolveProjectRoot(deepDir);

            // Should resolve to the inner repo (nearest .git), not the outer
            await Assert.That(result).IsEqualTo(Path.GetFullPath(innerDir));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public async Task ResolveProjectRootReturnsCanonicalPath()
    {
        var thisDir = AppContext.BaseDirectory;
        var result = _resolver.ResolveProjectRoot(thisDir);

        // Result should be a full, canonical path
        await Assert.That(Path.IsPathRooted(result)).IsTrue();
        await Assert.That(result).IsEqualTo(Path.GetFullPath(result));
    }

    [Test]
    public async Task ResolveProjectRootThrowsForNullOrEmpty()
    {
        await Assert.That(() => _resolver.ResolveProjectRoot(null!)).ThrowsException();
        await Assert.That(() => _resolver.ResolveProjectRoot("")).ThrowsException();
        await Assert.That(() => _resolver.ResolveProjectRoot("  ")).ThrowsException();
    }
}

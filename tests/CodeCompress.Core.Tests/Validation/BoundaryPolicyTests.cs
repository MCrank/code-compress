using CodeCompress.Core.Validation;

namespace CodeCompress.Core.Tests.Validation;

internal sealed class BoundaryPolicyTests
{
    private static readonly string Root = OperatingSystem.IsWindows()
        ? @"C:\work\repoA"
        : "/work/repoA";

    private static readonly string Parent = OperatingSystem.IsWindows()
        ? @"C:\work"
        : "/work";

    private static readonly string Descendant = OperatingSystem.IsWindows()
        ? @"C:\work\repoA\src\File.cs"
        : "/work/repoA/src/File.cs";

    private static readonly string SiblingUnderParent = OperatingSystem.IsWindows()
        ? @"C:\work\repoB"
        : "/work/repoB";

    private static readonly string Outside = OperatingSystem.IsWindows()
        ? @"C:\other\secret"
        : "/other/secret";

    private static readonly string AllowedRoot = OperatingSystem.IsWindows()
        ? @"C:\shared\libs"
        : "/shared/libs";

    private static readonly string AllowedChild = OperatingSystem.IsWindows()
        ? @"C:\shared\libs\common"
        : "/shared/libs/common";

    private static readonly string SecondAllowedRoot = OperatingSystem.IsWindows()
        ? @"C:\extra\tools"
        : "/extra/tools";

    // ── Construction ─────────────────────────────────────────────

    [Test]
    public async Task ConstructorCanonicalizesBoundaryRoot()
    {
        var policy = new BoundaryPolicy(Root);

        await Assert.That(policy.BoundaryRoot).IsEqualTo(Path.GetFullPath(Root));
    }

    [Test]
    public async Task ConstructorWithNoAllowedRootsExposesEmptyList()
    {
        var policy = new BoundaryPolicy(Root);

        await Assert.That(policy.AllowedRoots).Count().IsEqualTo(0);
    }

    // ── IsWithinBoundary ─────────────────────────────────────────

    [Test]
    public async Task IsWithinBoundaryReturnsTrueForRootItself()
    {
        var policy = new BoundaryPolicy(Root);

        await Assert.That(policy.IsWithinBoundary(Root)).IsTrue();
    }

    [Test]
    public async Task IsWithinBoundaryReturnsTrueForDescendant()
    {
        var policy = new BoundaryPolicy(Root);

        await Assert.That(policy.IsWithinBoundary(Descendant)).IsTrue();
    }

    [Test]
    public async Task IsWithinBoundaryReturnsTrueForSiblingRepoUnderBoundaryRoot()
    {
        // When the boundary root is the parent directory, all child repos are siblings within it.
        var policy = new BoundaryPolicy(Parent);

        await Assert.That(policy.IsWithinBoundary(Root)).IsTrue();
        await Assert.That(policy.IsWithinBoundary(SiblingUnderParent)).IsTrue();
    }

    [Test]
    public async Task IsWithinBoundaryReturnsFalseForOutsidePath()
    {
        var policy = new BoundaryPolicy(Root);

        await Assert.That(policy.IsWithinBoundary(Outside)).IsFalse();
    }

    [Test]
    public async Task IsWithinBoundaryReturnsFalseForParentTraversalEscape()
    {
        var policy = new BoundaryPolicy(Root);

        await Assert.That(policy.IsWithinBoundary(Root + "/../repoZ")).IsFalse();
    }

    [Test]
    public async Task IsWithinBoundaryReturnsFalseForAncestorOfRoot()
    {
        var policy = new BoundaryPolicy(Root);

        await Assert.That(policy.IsWithinBoundary(Parent)).IsFalse();
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public async Task IsWithinBoundaryReturnsFalseForEmptyPath(string path)
    {
        var policy = new BoundaryPolicy(Root);

        await Assert.That(policy.IsWithinBoundary(path)).IsFalse();
    }

    // ── Allowlist ────────────────────────────────────────────────

    [Test]
    public async Task IsWithinBoundaryReturnsTrueForPathInAllowlist()
    {
        var policy = new BoundaryPolicy(Root, [AllowedRoot]);

        await Assert.That(policy.IsWithinBoundary(AllowedChild)).IsTrue();
    }

    [Test]
    public async Task IsWithinBoundaryStillRejectsPathOutsideBoundaryAndAllowlist()
    {
        var policy = new BoundaryPolicy(Root, [AllowedRoot]);

        await Assert.That(policy.IsWithinBoundary(Outside)).IsFalse();
    }

    // ── EnsureWithinBoundary ─────────────────────────────────────

    [Test]
    public async Task EnsureWithinBoundaryReturnsCanonicalPathForInsidePath()
    {
        var policy = new BoundaryPolicy(Root);

        var result = policy.EnsureWithinBoundary(Descendant);

        await Assert.That(result).IsEqualTo(Path.GetFullPath(Descendant));
    }

    [Test]
    public async Task EnsureWithinBoundaryThrowsBoundaryViolationForOutsidePath()
    {
        var policy = new BoundaryPolicy(Root);

        await Assert.ThrowsAsync<BoundaryViolationException>(
            () => Task.FromResult(policy.EnsureWithinBoundary(Outside)));
    }

    [Test]
    public async Task EnsureWithinBoundaryThrowsExceptionAssignableToArgumentException()
    {
        // Existing tool catch blocks catch ArgumentException — boundary violations must flow through them.
        var policy = new BoundaryPolicy(Root);

        await Assert.ThrowsAsync<ArgumentException>(
            () => Task.FromResult(policy.EnsureWithinBoundary(Outside)));
    }

    // ── FromEnvironment / Create ─────────────────────────────────

    [Test]
    public async Task CreateUsesCurrentDirectoryWhenRootEnvIsNull()
    {
        var policy = BoundaryPolicy.Create(rootEnv: null, allowedRootsEnv: null, currentDirectory: Root);

        await Assert.That(policy.BoundaryRoot).IsEqualTo(Path.GetFullPath(Root));
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public async Task CreateUsesCurrentDirectoryWhenRootEnvIsBlank(string rootEnv)
    {
        var policy = BoundaryPolicy.Create(rootEnv, allowedRootsEnv: null, currentDirectory: Root);

        await Assert.That(policy.BoundaryRoot).IsEqualTo(Path.GetFullPath(Root));
    }

    [Test]
    public async Task CreateUsesRootEnvWhenProvided()
    {
        var policy = BoundaryPolicy.Create(rootEnv: Parent, allowedRootsEnv: null, currentDirectory: Root);

        await Assert.That(policy.BoundaryRoot).IsEqualTo(Path.GetFullPath(Parent));
    }

    [Test]
    public async Task CreateParsesAllowedRootsSplitByPathSeparator()
    {
        var allowedEnv = string.Join(Path.PathSeparator, AllowedRoot, SecondAllowedRoot);

        var policy = BoundaryPolicy.Create(rootEnv: Root, allowedRootsEnv: allowedEnv, currentDirectory: Root);

        await Assert.That(policy.AllowedRoots).Count().IsEqualTo(2);
        await Assert.That(policy.AllowedRoots).Contains(Path.GetFullPath(AllowedRoot));
        await Assert.That(policy.AllowedRoots).Contains(Path.GetFullPath(SecondAllowedRoot));
    }

    [Test]
    public async Task CreateIgnoresEmptyAllowedRootEntries()
    {
        var allowedEnv = $"{Path.PathSeparator}{AllowedRoot}{Path.PathSeparator}{Path.PathSeparator}";

        var policy = BoundaryPolicy.Create(rootEnv: Root, allowedRootsEnv: allowedEnv, currentDirectory: Root);

        await Assert.That(policy.AllowedRoots).Count().IsEqualTo(1);
        await Assert.That(policy.AllowedRoots).Contains(Path.GetFullPath(AllowedRoot));
    }

    [Test]
    public async Task CreateRejectsFilesystemRootAllowlistEntryAsTooBroad()
    {
        var filesystemRoot = OperatingSystem.IsWindows() ? @"C:\" : "/";

        var policy = BoundaryPolicy.Create(rootEnv: Root, allowedRootsEnv: filesystemRoot, currentDirectory: Root);

        await Assert.That(policy.AllowedRoots).Count().IsEqualTo(0);
    }

    // ── Symlink escape (defense in depth, real filesystem) ───────

    [Test]
    public async Task IsWithinBoundaryRejectsSymlinkEscapingBoundary()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"cc-boundary-{Guid.NewGuid():N}");
        var boundaryDir = Path.Combine(baseDir, "boundary");
        var outsideDir = Path.Combine(baseDir, "outside");
        Directory.CreateDirectory(boundaryDir);
        Directory.CreateDirectory(outsideDir);
        var linkPath = Path.Combine(boundaryDir, "link");

        try
        {
            try
            {
                Directory.CreateSymbolicLink(linkPath, outsideDir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Symlink creation requires privilege (e.g. Windows without Developer Mode) — skip.
                return;
            }

            var policy = new BoundaryPolicy(boundaryDir);

            await Assert.That(policy.IsWithinBoundary(linkPath)).IsFalse();
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }
}

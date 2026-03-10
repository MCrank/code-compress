using CodeCompress.Core.Validation;

namespace CodeCompress.Core.Tests.Validation;

internal sealed class PathValidatorTests
{
    private static readonly string Root = OperatingSystem.IsWindows()
        ? @"C:\project"
        : "/project";

    private static readonly string SubDir = OperatingSystem.IsWindows()
        ? @"C:\project\src"
        : "/project/src";

    private static readonly string FileInRoot = OperatingSystem.IsWindows()
        ? @"C:\project\src\Foo.cs"
        : "/project/src/Foo.cs";

    // ── Happy Path ──────────────────────────────────────────────

    [Test]
    public async Task ValidatePathAbsolutePathWithinRootReturnsCanonicalized()
    {
        var result = PathValidator.ValidatePath(FileInRoot, Root);

        await Assert.That(result).IsEqualTo(FileInRoot);
    }

    [Test]
    public async Task ValidatePathExactlyEqualToRootIsAccepted()
    {
        var result = PathValidator.ValidatePath(Root, Root);

        await Assert.That(result).IsEqualTo(Root);
    }

    [Test]
    public async Task ValidatePathSubdirectoryPathIsAccepted()
    {
        var result = PathValidator.ValidatePath(SubDir, Root);

        await Assert.That(result).IsEqualTo(SubDir);
    }

    [Test]
    public async Task ValidateRelativePathValidRelativeResolvesCorrectly()
    {
        var result = PathValidator.ValidateRelativePath("src/Foo.cs", Root);

        var expected = Path.Combine(Root, "src", "Foo.cs");
        await Assert.That(result).IsEqualTo(Path.GetFullPath(expected));
    }

    [Test]
    public async Task IsWithinRootValidPathReturnsTrue()
    {
        var result = PathValidator.IsWithinRoot(FileInRoot, Root);

        await Assert.That(result).IsTrue();
    }

    // ── Traversal Attack Tests ──────────────────────────────────

    [Test]
    [Arguments("/../../../etc/passwd")]
    [Arguments("/../../windows/system32/config/sam")]
    [Arguments("/../")]
    [Arguments("/./../../outside")]
    [Arguments("/src/../../outside")]
    public async Task ValidatePathTraversalAttemptsThrows(string maliciousSegment)
    {
        var maliciousPath = Root + maliciousSegment;

        await Assert.ThrowsAsync<ArgumentException>(
            () => Task.FromResult(PathValidator.ValidatePath(maliciousPath, Root)));
    }

    [Test]
    public async Task ValidateRelativePathTraversalViaRelativeThrows()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Task.FromResult(PathValidator.ValidateRelativePath("../../etc/passwd", Root)));
    }

    [Test]
    public async Task ValidatePathDotDotThatStaysWithinRootIsAccepted()
    {
        var pathWithDotDot = Path.Combine(Root, "src", "..", "lib", "Foo.cs");

        var result = PathValidator.ValidatePath(pathWithDotDot, Root);

        var expected = Path.Combine(Root, "lib", "Foo.cs");
        await Assert.That(result).IsEqualTo(Path.GetFullPath(expected));
    }

    // ── Input Validation Tests ──────────────────────────────────

    [Test]
    public async Task ValidatePathNullInputThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Task.FromResult(PathValidator.ValidatePath(null!, Root)));
    }

    [Test]
    public async Task ValidatePathEmptyInputThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Task.FromResult(PathValidator.ValidatePath("", Root)));
    }

    [Test]
    public async Task ValidatePathWhitespaceInputThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Task.FromResult(PathValidator.ValidatePath("   ", Root)));
    }

    [Test]
    public async Task ValidatePathNullProjectRootThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Task.FromResult(PathValidator.ValidatePath(FileInRoot, null!)));
    }

    // ── Null Byte Tests ─────────────────────────────────────────

    [Test]
    public async Task ValidatePathNullByteThrowsArgumentException()
    {
        var pathWithNull = Root + "/src/evil\0.cs";

        await Assert.ThrowsAsync<ArgumentException>(
            () => Task.FromResult(PathValidator.ValidatePath(pathWithNull, Root)));
    }

    // ── Platform-Specific Tests ─────────────────────────────────

    [Test]
    public async Task ValidatePathMixedSeparatorsNormalizedAndAccepted()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var mixedPath = @"C:\project/src\Foo.cs";
        var result = PathValidator.ValidatePath(mixedPath, @"C:\project");

        await Assert.That(result).IsEqualTo(@"C:\project\src\Foo.cs");
    }

    [Test]
    public async Task ValidatePathUncPathRejected()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await Assert.ThrowsAsync<ArgumentException>(
            () => Task.FromResult(PathValidator.ValidatePath(@"\\server\share\file.cs", @"C:\project")));
    }

    [Test]
    public async Task ValidatePathCaseSensitivityOnWindowsAccepted()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var result = PathValidator.ValidatePath(@"C:\PROJECT\SRC\Foo.cs", @"C:\project");

        await Assert.That(result).IsEqualTo(@"C:\PROJECT\SRC\Foo.cs");
    }

    // ── Edge Case Tests ─────────────────────────────────────────

    [Test]
    public async Task ValidatePathTrailingSlashOnRootDoesNotAffectValidation()
    {
        var rootWithSlash = Root + Path.DirectorySeparatorChar;

        var result = PathValidator.ValidatePath(FileInRoot, rootWithSlash);

        await Assert.That(result).IsEqualTo(FileInRoot);
    }

    [Test]
    public async Task ValidatePathDoubleSlashesNormalizedAndAccepted()
    {
        string pathWithDoubleSlash;
        if (OperatingSystem.IsWindows())
        {
            pathWithDoubleSlash = @"C:\project\\src\\Foo.cs";
        }
        else
        {
            pathWithDoubleSlash = "/project//src//Foo.cs";
        }

        var result = PathValidator.ValidatePath(pathWithDoubleSlash, Root);

        await Assert.That(result).IsEqualTo(FileInRoot);
    }

    [Test]
    public async Task ValidatePathPrefixButNotChildRejected()
    {
        string sibling;
        if (OperatingSystem.IsWindows())
        {
            sibling = @"C:\project-other\file.cs";
        }
        else
        {
            sibling = "/project-other/file.cs";
        }

        await Assert.ThrowsAsync<ArgumentException>(
            () => Task.FromResult(PathValidator.ValidatePath(sibling, Root)));
    }

    [Test]
    public async Task IsWithinRootInvalidPathReturnsFalseWithoutThrowing()
    {
        var result = PathValidator.IsWithinRoot("", Root);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsWithinRootTraversalPathReturnsFalse()
    {
        var malicious = Root + "/../../../etc/passwd";

        var result = PathValidator.IsWithinRoot(malicious, Root);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsWithinRootNullInputReturnsFalse()
    {
        var result = PathValidator.IsWithinRoot(null!, Root);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task ValidatePathUrlEncodedDotsNotDecoded()
    {
        // %2e%2e should be treated as literal characters, not ".."
        var literalPath = Path.Combine(Root, "src", "%2e%2e", "Foo.cs");

        var result = PathValidator.ValidatePath(literalPath, Root);

        await Assert.That(result).IsEqualTo(Path.GetFullPath(literalPath));
    }
}

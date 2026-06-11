using CodeCompress.Core.Validation;

namespace CodeCompress.Core.Tests.Validation;

internal sealed class PathValidatorServiceTests
{
    private static readonly string Boundary = OperatingSystem.IsWindows()
        ? @"C:\work\repoA"
        : "/work/repoA";

    private static readonly string InsidePath = OperatingSystem.IsWindows()
        ? @"C:\work\repoA\src"
        : "/work/repoA/src";

    private static readonly string OutsidePath = OperatingSystem.IsWindows()
        ? @"C:\other\repoB"
        : "/other/repoB";

    // ── Unrestricted (parameterless) — backward compatible ───────

    [Test]
    public async Task UnrestrictedServiceAllowsAnyValidPath()
    {
        var service = new PathValidatorService();

        var result = service.ValidatePath(OutsidePath, OutsidePath);

        await Assert.That(result).IsEqualTo(Path.GetFullPath(OutsidePath));
    }

    // ── Boundary-aware (DI) — clamps to boundary ─────────────────

    [Test]
    public async Task BoundaryAwareServiceAllowsPathWithinBoundary()
    {
        var service = new PathValidatorService(new BoundaryPolicy(Boundary));

        var result = service.ValidatePath(InsidePath, InsidePath);

        await Assert.That(result).IsEqualTo(Path.GetFullPath(InsidePath));
    }

    [Test]
    public async Task BoundaryAwareServiceRejectsPathOutsideBoundary()
    {
        var service = new PathValidatorService(new BoundaryPolicy(Boundary));

        await Assert.ThrowsAsync<ArgumentException>(
            () => Task.FromResult(service.ValidatePath(OutsidePath, OutsidePath)));
    }

    [Test]
    public async Task BoundaryAwareServiceRejectsOutsidePathWithBoundaryViolation()
    {
        var service = new PathValidatorService(new BoundaryPolicy(Boundary));

        await Assert.ThrowsAsync<BoundaryViolationException>(
            () => Task.FromResult(service.ValidatePath(OutsidePath, OutsidePath)));
    }

    [Test]
    public async Task BoundaryAwareServiceIsWithinRootReturnsFalseForOutsidePath()
    {
        var service = new PathValidatorService(new BoundaryPolicy(Boundary));

        var result = service.IsWithinRoot(OutsidePath, OutsidePath);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task BoundaryAwareServiceStillEnforcesTraversalWithinSuppliedRoot()
    {
        // Boundary enforcement is additive — the original traversal check still applies.
        var service = new PathValidatorService(new BoundaryPolicy(Boundary));

        await Assert.ThrowsAsync<ArgumentException>(
            () => Task.FromResult(service.ValidatePath(InsidePath + "/../../escape", Boundary)));
    }
}

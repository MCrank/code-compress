using CodeCompress.Core.Models;

namespace CodeCompress.Core.Tests.Models;

internal sealed class BlastRadiusResultTests
{
    [Test]
    public async Task EmptyResultHasZeroTotalAffected()
    {
        var result = new BlastRadiusResult(0, []);
        await Assert.That(result.TotalAffected).IsEqualTo(0);
        await Assert.That(result.Depths).Count().IsEqualTo(0);
    }

    [Test]
    public async Task DepthGroupsFilesCorrectly()
    {
        var depths = new List<BlastRadiusDepth>
        {
            new(1, ["B.cs", "C.cs"]),
            new(2, ["D.cs"]),
        };
        var result = new BlastRadiusResult(3, depths);

        await Assert.That(result.TotalAffected).IsEqualTo(3);
        await Assert.That(result.Depths).Count().IsEqualTo(2);
        await Assert.That(result.Depths[0].Depth).IsEqualTo(1);
        await Assert.That(result.Depths[0].Files).Count().IsEqualTo(2);
        await Assert.That(result.Depths[1].Depth).IsEqualTo(2);
        await Assert.That(result.Depths[1].Files).Count().IsEqualTo(1);
    }

    [Test]
    public async Task EqualityTwoIdenticalResultsAreEqual()
    {
        var files = new List<string> { "B.cs" };
        var depths = new List<BlastRadiusDepth> { new(1, files) };
        var r1 = new BlastRadiusResult(1, depths);
        var r2 = new BlastRadiusResult(1, depths);

        await Assert.That(r1).IsEqualTo(r2);
    }
}

using CodeCompress.Core.Models;

namespace CodeCompress.Core.Tests.Models;

internal sealed class EdgeKindTests
{
    [Test]
    public async Task HasFiveMembers()
    {
        var values = Enum.GetValues<EdgeKind>();
        await Assert.That(values).Count().IsEqualTo(5);
    }

    [Arguments(EdgeKind.Imports, 0)]
    [Arguments(EdgeKind.Calls, 1)]
    [Arguments(EdgeKind.Implements, 2)]
    [Arguments(EdgeKind.Inherits, 3)]
    [Arguments(EdgeKind.References, 4)]
    [Test]
    public async Task EnumMemberHasExpectedIntValue(EdgeKind kind, int expectedValue)
    {
        await Assert.That((int)kind).IsEqualTo(expectedValue);
    }

    [Test]
    public async Task DefaultDependencyInfoEdgeKindIsImports()
    {
        var info = new DependencyInfo("some.path", null);
        await Assert.That(info.EdgeKind).IsEqualTo(EdgeKind.Imports);
    }

    [Test]
    public async Task DependencyInfoEdgeKindCanBeSetExplicitly()
    {
        var info = new DependencyInfo("Base", null, EdgeKind.Inherits);
        await Assert.That(info.EdgeKind).IsEqualTo(EdgeKind.Inherits);
    }

    [Test]
    public async Task DefaultDependencyEdgeKindIsImports()
    {
        var dep = new Dependency(0, 1, "some.path", null, null);
        await Assert.That(dep.EdgeKind).IsEqualTo("imports");
    }

    [Test]
    public async Task DependencyEdgeKindCanBeSetExplicitly()
    {
        var dep = new Dependency(0, 1, "IFoo", null, null, "implements");
        await Assert.That(dep.EdgeKind).IsEqualTo("implements");
    }

    [Test]
    public async Task DependencyEdgeEdgeKindDefaultsToNull()
    {
        var edge = new DependencyEdge("A.cs", "B.cs", null);
        await Assert.That(edge.EdgeKind).IsNull();
    }

    [Test]
    public async Task DependencyEdgeEdgeKindCanBeSetExplicitly()
    {
        var edge = new DependencyEdge("A.cs", "B.cs", null, "inherits");
        await Assert.That(edge.EdgeKind).IsEqualTo("inherits");
    }
}

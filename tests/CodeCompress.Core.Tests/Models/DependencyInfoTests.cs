using CodeCompress.Core.Models;

namespace CodeCompress.Core.Tests.Models;

internal sealed class DependencyInfoTests
{
    [Test]
    public async Task ConstructionWithAliasSetsPropertiesCorrectly()
    {
        var dependency = new DependencyInfo(
            RequirePath: "game/ReplicatedStorage/Utils",
            Alias: "Utils");

        await Assert.That(dependency.RequirePath).IsEqualTo("game/ReplicatedStorage/Utils");
        await Assert.That(dependency.Alias).IsEqualTo("Utils");
    }

    [Test]
    public async Task ConstructionWithNullAliasAllowsNull()
    {
        var dependency = new DependencyInfo(
            RequirePath: "game/ServerScriptService/Main",
            Alias: null);

        await Assert.That(dependency.RequirePath).IsEqualTo("game/ServerScriptService/Main");
        await Assert.That(dependency.Alias).IsNull();
    }

    [Test]
    public async Task EqualityIdenticalInstancesAreEqual()
    {
        var dep1 = new DependencyInfo("path/to/module", "Mod");
        var dep2 = new DependencyInfo("path/to/module", "Mod");

        await Assert.That(dep1).IsEqualTo(dep2);
    }

    [Test]
    public async Task EqualityDifferentRequirePathAreNotEqual()
    {
        var dep1 = new DependencyInfo("path/to/moduleA", "Mod");
        var dep2 = new DependencyInfo("path/to/moduleB", "Mod");

        await Assert.That(dep1).IsNotEqualTo(dep2);
    }
}

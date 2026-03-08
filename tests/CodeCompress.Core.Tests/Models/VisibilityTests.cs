using CodeCompress.Core.Models;

namespace CodeCompress.Core.Tests.Models;

internal sealed class VisibilityTests
{
    [Test]
    [Arguments(Visibility.Public, 0)]
    [Arguments(Visibility.Private, 1)]
    [Arguments(Visibility.Local, 2)]
    public async Task EnumMemberHasExpectedIntValue(Visibility visibility, int expectedValue)
    {
        var actualValue = (int)visibility;

        await Assert.That(actualValue).IsEqualTo(expectedValue);
    }

    [Test]
    public async Task GetValuesReturnsExactlyThreeMembers()
    {
        var values = Enum.GetValues<Visibility>();

        await Assert.That(values).Count().IsEqualTo(3);
    }
}

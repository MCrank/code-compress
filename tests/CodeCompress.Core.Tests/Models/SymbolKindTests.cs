using CodeCompress.Core.Models;

namespace CodeCompress.Core.Tests.Models;

internal sealed class SymbolKindTests
{
    [Test]
    [Arguments(SymbolKind.Function, 0)]
    [Arguments(SymbolKind.Method, 1)]
    [Arguments(SymbolKind.Type, 2)]
    [Arguments(SymbolKind.Class, 3)]
    [Arguments(SymbolKind.Interface, 4)]
    [Arguments(SymbolKind.Export, 5)]
    [Arguments(SymbolKind.Constant, 6)]
    [Arguments(SymbolKind.Module, 7)]
    public async Task EnumMemberHasExpectedIntValue(SymbolKind kind, int expectedValue)
    {
        var actualValue = (int)kind;

        await Assert.That(actualValue).IsEqualTo(expectedValue);
    }

    [Test]
    public async Task GetValuesReturnsExactlyEightMembers()
    {
        var values = Enum.GetValues<SymbolKind>();

        await Assert.That(values).Count().IsEqualTo(8);
    }
}

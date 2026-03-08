using CodeCompress.Core.Models;

namespace CodeCompress.Core.Tests.Models;

internal sealed class ParseResultTests
{
    [Test]
    public async Task ConstructionWithEmptyListsCreatesValidInstance()
    {
        var result = new ParseResult(
            Symbols: [],
            Dependencies: []);

        await Assert.That(result.Symbols).Count().IsEqualTo(0);
        await Assert.That(result.Dependencies).Count().IsEqualTo(0);
    }

    [Test]
    public async Task ConstructionWithPopulatedListsContainsExpectedItems()
    {
        var symbol = CreateSampleSymbol("Init", SymbolKind.Function);
        var dependency = new DependencyInfo("game/ReplicatedStorage/Config", "Config");

        var result = new ParseResult(
            Symbols: [symbol],
            Dependencies: [dependency]);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Dependencies).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SymbolsContainsCorrectSymbolInfo()
    {
        var symbol = CreateSampleSymbol("Calculate", SymbolKind.Method);
        var result = new ParseResult(Symbols: [symbol], Dependencies: []);

        await Assert.That(result.Symbols[0].Name).IsEqualTo("Calculate");
        await Assert.That(result.Symbols[0].Kind).IsEqualTo(SymbolKind.Method);
    }

    [Test]
    public async Task DependenciesContainsCorrectDependencyInfo()
    {
        var dependency = new DependencyInfo("shared/Utils", "Utils");
        var result = new ParseResult(Symbols: [], Dependencies: [dependency]);

        await Assert.That(result.Dependencies[0].RequirePath).IsEqualTo("shared/Utils");
        await Assert.That(result.Dependencies[0].Alias).IsEqualTo("Utils");
    }

    [Test]
    public async Task EqualitySameListInstancesAreEqual()
    {
        var symbol = CreateSampleSymbol("Run", SymbolKind.Function);
        var dependency = new DependencyInfo("lib/Core", "Core");

        IReadOnlyList<SymbolInfo> symbols = [symbol];
        IReadOnlyList<DependencyInfo> dependencies = [dependency];

        var result1 = new ParseResult(Symbols: symbols, Dependencies: dependencies);
        var result2 = new ParseResult(Symbols: symbols, Dependencies: dependencies);

        await Assert.That(result1).IsEqualTo(result2);
    }

    private static SymbolInfo CreateSampleSymbol(string name, SymbolKind kind)
    {
        return new SymbolInfo(
            Name: name,
            Kind: kind,
            Signature: $"function {name}()",
            ParentSymbol: null,
            ByteOffset: 0,
            ByteLength: 50,
            LineStart: 1,
            LineEnd: 5,
            Visibility: Visibility.Public,
            DocComment: null);
    }
}

using CodeCompress.Core.Models;

namespace CodeCompress.Core.Tests.Models;

internal sealed class SymbolInfoTests
{
    [Test]
    public async Task ConstructionWithAllPropertiesSetsValuesCorrectly()
    {
        var symbol = new SymbolInfo(
            Name: "DoWork",
            Kind: SymbolKind.Method,
            Signature: "public void DoWork(int count)",
            ParentSymbol: "MyClass",
            ByteOffset: 100,
            ByteLength: 250,
            LineStart: 10,
            LineEnd: 20,
            Visibility: Visibility.Public,
            DocComment: "/// Does some work.");

        await Assert.That(symbol.Name).IsEqualTo("DoWork");
        await Assert.That(symbol.Kind).IsEqualTo(SymbolKind.Method);
        await Assert.That(symbol.Signature).IsEqualTo("public void DoWork(int count)");
        await Assert.That(symbol.ParentSymbol).IsEqualTo("MyClass");
        await Assert.That(symbol.ByteOffset).IsEqualTo(100);
        await Assert.That(symbol.ByteLength).IsEqualTo(250);
        await Assert.That(symbol.LineStart).IsEqualTo(10);
        await Assert.That(symbol.LineEnd).IsEqualTo(20);
        await Assert.That(symbol.Visibility).IsEqualTo(Visibility.Public);
        await Assert.That(symbol.DocComment).IsEqualTo("/// Does some work.");
    }

    [Test]
    public async Task ConstructionWithNullOptionalPropertiesAllowsNulls()
    {
        var symbol = new SymbolInfo(
            Name: "TopLevelFunction",
            Kind: SymbolKind.Function,
            Signature: "local function TopLevelFunction()",
            ParentSymbol: null,
            ByteOffset: 0,
            ByteLength: 50,
            LineStart: 1,
            LineEnd: 5,
            Visibility: Visibility.Local,
            DocComment: null);

        await Assert.That(symbol.ParentSymbol).IsNull();
        await Assert.That(symbol.DocComment).IsNull();
    }

    [Test]
    public async Task EqualityIdenticalInstancesAreEqual()
    {
        var symbol1 = CreateSampleSymbol();
        var symbol2 = CreateSampleSymbol();

        await Assert.That(symbol1).IsEqualTo(symbol2);
    }

    [Test]
    public async Task EqualityDifferentNameAreNotEqual()
    {
        var symbol1 = CreateSampleSymbol();
        var symbol2 = symbol1 with { Name = "DifferentName" };

        await Assert.That(symbol1).IsNotEqualTo(symbol2);
    }

    [Test]
    public async Task WithExpressionCreatesModifiedCopy()
    {
        var original = CreateSampleSymbol();
        var modified = original with { Kind = SymbolKind.Class, LineEnd = 99 };

        await Assert.That(modified.Name).IsEqualTo(original.Name);
        await Assert.That(modified.Kind).IsEqualTo(SymbolKind.Class);
        await Assert.That(modified.LineEnd).IsEqualTo(99);
        await Assert.That(original.Kind).IsEqualTo(SymbolKind.Function);
        await Assert.That(original.LineEnd).IsEqualTo(10);
    }

    private static SymbolInfo CreateSampleSymbol()
    {
        return new SymbolInfo(
            Name: "SampleFunc",
            Kind: SymbolKind.Function,
            Signature: "function SampleFunc()",
            ParentSymbol: "SampleModule",
            ByteOffset: 0,
            ByteLength: 100,
            LineStart: 1,
            LineEnd: 10,
            Visibility: Visibility.Public,
            DocComment: "A sample function.");
    }
}

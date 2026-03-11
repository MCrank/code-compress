using System.Text;
using CodeCompress.Core.Models;
using CodeCompress.Core.Parsers;

namespace CodeCompress.Core.Tests.Parsers;

internal sealed class DotNetProjectParserTests
{
    private readonly DotNetProjectParser _parser = new();

    private ParseResult Parse(string source)
    {
        var bytes = Encoding.UTF8.GetBytes(source);
        return _parser.Parse("test.csproj", bytes);
    }

    // ── Interface contract ──────────────────────────────────────

    [Test]
    public async Task LanguageIdIsDotnetProject()
    {
        await Assert.That(_parser.LanguageId).IsEqualTo("dotnet-project");
    }

    [Test]
    public async Task FileExtensionsContainsCsproj()
    {
        await Assert.That(_parser.FileExtensions).Contains(".csproj");
    }

    [Test]
    public async Task FileExtensionsContainsFsproj()
    {
        await Assert.That(_parser.FileExtensions).Contains(".fsproj");
    }

    [Test]
    public async Task FileExtensionsContainsVbproj()
    {
        await Assert.That(_parser.FileExtensions).Contains(".vbproj");
    }

    [Test]
    public async Task FileExtensionsContainsProps()
    {
        await Assert.That(_parser.FileExtensions).Contains(".props");
    }

    // ── Empty / Malformed input ─────────────────────────────────

    [Test]
    public async Task EmptyContentReturnsEmptyResult()
    {
        var result = Parse("");

        await Assert.That(result.Symbols).Count().IsEqualTo(0);
        await Assert.That(result.Dependencies).Count().IsEqualTo(0);
    }

    [Test]
    public async Task MalformedXmlReturnsEmptyResult()
    {
        var result = Parse("<Project><broken");

        await Assert.That(result.Symbols).Count().IsEqualTo(0);
        await Assert.That(result.Dependencies).Count().IsEqualTo(0);
    }

    [Test]
    public async Task XmlWithNoRecognizedElementsReturnsEmptyResult()
    {
        var source = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <Description>Some app</Description>
              </PropertyGroup>
            </Project>
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(0);
    }

    // ── PackageReference ────────────────────────────────────────

    [Test]
    public async Task PackageReferenceWithVersionExtracted()
    {
        var source = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
              </ItemGroup>
            </Project>
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);

        var sym = result.Symbols[0];
        await Assert.That(sym.Name).IsEqualTo("Newtonsoft.Json");
        await Assert.That(sym.Kind).IsEqualTo(SymbolKind.Module);
        await Assert.That(sym.Signature).IsEqualTo("PackageReference: Newtonsoft.Json (13.0.3)");
        await Assert.That(sym.Visibility).IsEqualTo(Visibility.Public);
        await Assert.That(sym.ParentSymbol).IsNull();
        await Assert.That(sym.DocComment).IsNull();
    }

    [Test]
    public async Task PackageReferenceWithoutVersionIsCentrallyManaged()
    {
        var source = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Microsoft.Data.Sqlite" />
              </ItemGroup>
            </Project>
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Signature)
            .IsEqualTo("PackageReference: Microsoft.Data.Sqlite (centrally managed)");
    }

    [Test]
    public async Task PackageReferenceVersionAsChildElement()
    {
        var source = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Serilog">
                  <Version>3.1.0</Version>
                </PackageReference>
              </ItemGroup>
            </Project>
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Signature)
            .IsEqualTo("PackageReference: Serilog (3.1.0)");
    }

    [Test]
    public async Task MultiplePackageReferencesAllExtracted()
    {
        var source = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="PackageA" Version="1.0" />
                <PackageReference Include="PackageB" Version="2.0" />
                <PackageReference Include="PackageC" Version="3.0" />
              </ItemGroup>
            </Project>
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(3);
        await Assert.That(result.Symbols[0].Name).IsEqualTo("PackageA");
        await Assert.That(result.Symbols[1].Name).IsEqualTo("PackageB");
        await Assert.That(result.Symbols[2].Name).IsEqualTo("PackageC");
    }

    // ── PackageVersion ──────────────────────────────────────────

    [Test]
    public async Task PackageVersionExtractedFromProps()
    {
        var source = """
            <Project>
              <ItemGroup>
                <PackageVersion Include="TUnit" Version="1.19.11" />
              </ItemGroup>
            </Project>
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);

        var sym = result.Symbols[0];
        await Assert.That(sym.Name).IsEqualTo("TUnit");
        await Assert.That(sym.Kind).IsEqualTo(SymbolKind.Module);
        await Assert.That(sym.Signature).IsEqualTo("PackageVersion: TUnit (1.19.11)");
    }

    // ── ProjectReference ────────────────────────────────────────

    [Test]
    public async Task ProjectReferenceExtractedAsSymbol()
    {
        var source = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\Core\MyApp.Core.csproj" />
              </ItemGroup>
            </Project>
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);

        var sym = result.Symbols[0];
        await Assert.That(sym.Name).IsEqualTo("MyApp.Core");
        await Assert.That(sym.Kind).IsEqualTo(SymbolKind.Module);
        await Assert.That(sym.Signature).IsEqualTo(@"ProjectReference: ..\Core\MyApp.Core.csproj");
    }

    [Test]
    public async Task ProjectReferenceAddedAsDependency()
    {
        var source = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\Core\MyApp.Core.csproj" />
              </ItemGroup>
            </Project>
            """;

        var result = Parse(source);

        await Assert.That(result.Dependencies).Count().IsEqualTo(1);
        await Assert.That(result.Dependencies[0].RequirePath).IsEqualTo(@"..\Core\MyApp.Core.csproj");
        await Assert.That(result.Dependencies[0].Alias).IsNull();
    }

    [Test]
    public async Task ProjectReferenceNameExtractedFromComplexPath()
    {
        var source = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\..\libs\Shared\Shared.Utils.fsproj" />
              </ItemGroup>
            </Project>
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols[0].Name).IsEqualTo("Shared.Utils");
    }

    // ── Build Properties ────────────────────────────────────────

    [Test]
    public async Task TargetFrameworkExtractedAsConstant()
    {
        var source = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);

        var sym = result.Symbols[0];
        await Assert.That(sym.Name).IsEqualTo("TargetFramework");
        await Assert.That(sym.Kind).IsEqualTo(SymbolKind.Constant);
        await Assert.That(sym.Signature).IsEqualTo("TargetFramework: net10.0");
    }

    [Test]
    public async Task LangVersionExtractedAsConstant()
    {
        var source = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <LangVersion>14</LangVersion>
              </PropertyGroup>
            </Project>
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Signature).IsEqualTo("LangVersion: 14");
    }

    [Test]
    public async Task TargetFrameworksPluralHandled()
    {
        var source = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
              </PropertyGroup>
            </Project>
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Name).IsEqualTo("TargetFrameworks");
        await Assert.That(result.Symbols[0].Signature).IsEqualTo("TargetFrameworks: net8.0;net9.0;net10.0");
    }

    [Test]
    public async Task EmptyPropertyValueSkipped()
    {
        var source = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework></TargetFramework>
              </PropertyGroup>
            </Project>
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(0);
    }

    // ── ByteOffset Accuracy ─────────────────────────────────────

    [Test]
    public async Task ByteOffsetPointsToCorrectPosition()
    {
        var source = "<Project>\n  <ItemGroup>\n    <PackageReference Include=\"Foo\" Version=\"1.0\" />\n  </ItemGroup>\n</Project>";
        var bytes = Encoding.UTF8.GetBytes(source);

        var result = _parser.Parse("test.csproj", bytes);

        var sym = result.Symbols[0];

        // PackageReference is on line 3, byte offset should point to start of that line
        var line3Offset = source.IndexOf("    <PackageReference", StringComparison.Ordinal);
        await Assert.That(sym.ByteOffset).IsEqualTo(line3Offset);
        await Assert.That(sym.ByteLength).IsGreaterThan(0);
    }

    [Test]
    public async Task LineNumbersAreOneBased()
    {
        var source = "<Project>\n  <ItemGroup>\n    <PackageReference Include=\"Foo\" Version=\"1.0\" />\n  </ItemGroup>\n</Project>";

        var result = Parse(source);

        var sym = result.Symbols[0];
        await Assert.That(sym.LineStart).IsEqualTo(3);
    }

    // ── Old-style namespaced project ────────────────────────────

    [Test]
    public async Task OldStyleNamespacedProjectHandled()
    {
        var source = """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup>
                <TargetFramework>net48</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
              </ItemGroup>
            </Project>
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(2);
        await Assert.That(result.Symbols[0].Name).IsEqualTo("TargetFramework");
        await Assert.That(result.Symbols[1].Name).IsEqualTo("Newtonsoft.Json");
    }

    // ── Edge Cases ──────────────────────────────────────────────

    [Test]
    public async Task EmptyIncludeAttributeSkipped()
    {
        var source = """
            <Project>
              <ItemGroup>
                <PackageReference Include="" Version="1.0" />
              </ItemGroup>
            </Project>
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(0);
    }

    [Test]
    public async Task MissingIncludeAttributeSkipped()
    {
        var source = """
            <Project>
              <ItemGroup>
                <PackageReference Version="1.0" />
              </ItemGroup>
            </Project>
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(0);
    }

    [Test]
    public async Task MixedElementsProduceCorrectCounts()
    {
        var source = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="PackageA" Version="1.0" />
                <PackageReference Include="PackageB" />
                <ProjectReference Include="..\Lib\Lib.csproj" />
              </ItemGroup>
            </Project>
            """;

        var result = Parse(source);

        // 2 properties + 2 packages + 1 project ref = 5 symbols
        await Assert.That(result.Symbols).Count().IsEqualTo(5);
        // 1 project ref dependency
        await Assert.That(result.Dependencies).Count().IsEqualTo(1);

        // Verify kinds
        var constants = result.Symbols.Where(s => s.Kind == SymbolKind.Constant).ToList();
        var modules = result.Symbols.Where(s => s.Kind == SymbolKind.Module).ToList();
        await Assert.That(constants).Count().IsEqualTo(2);
        await Assert.That(modules).Count().IsEqualTo(3);
    }
}

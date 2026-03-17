using System.Globalization;
using System.Text;
using CodeCompress.Core.Models;
using CodeCompress.Core.Parsers;

namespace CodeCompress.Core.Tests.Parsers;

internal sealed class JsonConfigParserTests
{
    private readonly JsonConfigParser _parser = new();

    private ParseResult Parse(string source)
    {
        var bytes = Encoding.UTF8.GetBytes(source);
        return _parser.Parse("appsettings.json", bytes);
    }

    // ── Interface contract ──────────────────────────────────────

    [Test]
    public async Task LanguageIdIsJsonConfig()
    {
        await Assert.That(_parser.LanguageId).IsEqualTo("json-config");
    }

    [Test]
    public async Task FileExtensionsContainsJson()
    {
        await Assert.That(_parser.FileExtensions).Contains(".json");
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
    public async Task MalformedJsonReturnsEmptyResult()
    {
        var result = Parse("{broken json");

        await Assert.That(result.Symbols).Count().IsEqualTo(0);
        await Assert.That(result.Dependencies).Count().IsEqualTo(0);
    }

    [Test]
    public async Task EmptyObjectReturnsEmptyResult()
    {
        var result = Parse("{}");

        await Assert.That(result.Symbols).Count().IsEqualTo(0);
    }

    // ── Top-level primitive keys ────────────────────────────────

    [Test]
    public async Task TopLevelStringKeyExtracted()
    {
        var source = """
            {
              "AppName": "MyApp"
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);

        var sym = result.Symbols[0];
        await Assert.That(sym.Name).IsEqualTo("AppName");
        await Assert.That(sym.Kind).IsEqualTo(SymbolKind.ConfigKey);
        await Assert.That(sym.Signature).IsEqualTo("AppName: \"MyApp\"");
        await Assert.That(sym.Visibility).IsEqualTo(Visibility.Public);
        await Assert.That(sym.ParentSymbol).IsNull();
    }

    [Test]
    public async Task TopLevelNumberKeyExtracted()
    {
        var source = """
            {
              "Port": 8080
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Signature).IsEqualTo("Port: 8080");
    }

    [Test]
    public async Task TopLevelBooleanKeyExtracted()
    {
        var source = """
            {
              "EnableFeature": true
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Signature).IsEqualTo("EnableFeature: true");
    }

    [Test]
    public async Task TopLevelNullKeyExtracted()
    {
        var source = """
            {
              "OptionalValue": null
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Signature).IsEqualTo("OptionalValue: null");
    }

    // ── Nested objects ──────────────────────────────────────────

    [Test]
    public async Task NestedObjectKeysUseColonSeparatedQualifiedNames()
    {
        var source = """
            {
              "Logging": {
                "LogLevel": {
                  "Default": "Information"
                }
              }
            }
            """;

        var result = Parse(source);

        // Should produce: Logging (section), Logging:LogLevel (section), Logging:LogLevel:Default (leaf)
        var names = result.Symbols.Select(s => s.Name).ToList();
        await Assert.That(names).Contains("Logging");
        await Assert.That(names).Contains("Logging:LogLevel");
        await Assert.That(names).Contains("Logging:LogLevel:Default");
    }

    [Test]
    public async Task NestedObjectSectionHasObjectSignature()
    {
        var source = """
            {
              "ConnectionStrings": {
                "Default": "Server=localhost"
              }
            }
            """;

        var result = Parse(source);

        var section = result.Symbols.First(s => s.Name == "ConnectionStrings");
        await Assert.That(section.Signature).IsEqualTo("ConnectionStrings: { ... } (1 key)");
        await Assert.That(section.Kind).IsEqualTo(SymbolKind.ConfigKey);
    }

    [Test]
    public async Task NestedLeafHasParentSymbol()
    {
        var source = """
            {
              "ConnectionStrings": {
                "Default": "Server=localhost"
              }
            }
            """;

        var result = Parse(source);

        var leaf = result.Symbols.First(s => s.Name == "ConnectionStrings:Default");
        await Assert.That(leaf.ParentSymbol).IsEqualTo("ConnectionStrings");
        await Assert.That(leaf.Signature).IsEqualTo("ConnectionStrings:Default: \"Server=localhost\"");
    }

    // ── Arrays ──────────────────────────────────────────────────

    [Test]
    public async Task ArrayKeyExtractedWithElementCount()
    {
        var source = """
            {
              "AllowedHosts": ["localhost", "example.com", "*.test"]
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);

        var sym = result.Symbols[0];
        await Assert.That(sym.Name).IsEqualTo("AllowedHosts");
        await Assert.That(sym.Signature).IsEqualTo("AllowedHosts: [ ... ] (3 items)");
        await Assert.That(sym.Kind).IsEqualTo(SymbolKind.ConfigKey);
    }

    [Test]
    public async Task EmptyArrayExtracted()
    {
        var source = """
            {
              "Items": []
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Signature).IsEqualTo("Items: [ ... ] (0 items)");
    }

    // ── Multiple keys ───────────────────────────────────────────

    [Test]
    public async Task MultipleTopLevelKeysAllExtracted()
    {
        var source = """
            {
              "Key1": "value1",
              "Key2": 42,
              "Key3": true
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(3);
        await Assert.That(result.Symbols[0].Name).IsEqualTo("Key1");
        await Assert.That(result.Symbols[1].Name).IsEqualTo("Key2");
        await Assert.That(result.Symbols[2].Name).IsEqualTo("Key3");
    }

    // ── Realistic appsettings.json ──────────────────────────────

    [Test]
    public async Task RealisticAppSettingsProducesExpectedSymbolCount()
    {
        var source = """
            {
              "Logging": {
                "LogLevel": {
                  "Default": "Information",
                  "Microsoft": "Warning"
                }
              },
              "ConnectionStrings": {
                "Default": "Server=localhost;Database=mydb"
              },
              "AllowedHosts": "*"
            }
            """;

        var result = Parse(source);

        // Logging (section) + Logging:LogLevel (section) + Logging:LogLevel:Default + Logging:LogLevel:Microsoft
        // + ConnectionStrings (section) + ConnectionStrings:Default
        // + AllowedHosts
        // = 7 symbols
        await Assert.That(result.Symbols).Count().IsEqualTo(7);
    }

    // ── ByteOffset accuracy ─────────────────────────────────────

    [Test]
    public async Task ByteOffsetIsNonNegative()
    {
        var source = """
            {
              "Key": "value"
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols[0].ByteOffset).IsGreaterThanOrEqualTo(0);
        await Assert.That(result.Symbols[0].ByteLength).IsGreaterThan(0);
    }

    [Test]
    public async Task LineNumbersAreOneBased()
    {
        var source = "{\n  \"Key\": \"value\"\n}";

        var result = Parse(source);

        await Assert.That(result.Symbols[0].LineStart).IsGreaterThanOrEqualTo(1);
    }

    // ── Dependencies always empty ───────────────────────────────

    [Test]
    public async Task DependenciesAlwaysEmpty()
    {
        var source = """
            {
              "Key": "value"
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Dependencies).Count().IsEqualTo(0);
    }

    // ── Top-level array root ────────────────────────────────────

    [Test]
    public async Task TopLevelArrayReturnsEmptyResult()
    {
        var result = Parse("[1, 2, 3]");

        await Assert.That(result.Symbols).Count().IsEqualTo(0);
    }

    // ── Deeply nested keys ──────────────────────────────────────

    [Test]
    public async Task DeeplyNestedKeysHaveFullQualifiedName()
    {
        var source = """
            {
              "A": {
                "B": {
                  "C": {
                    "D": "deep"
                  }
                }
              }
            }
            """;

        var result = Parse(source);

        var deepest = result.Symbols.First(s => s.Name == "A:B:C:D");
        await Assert.That(deepest.Signature).IsEqualTo("A:B:C:D: \"deep\"");
        await Assert.That(deepest.ParentSymbol).IsEqualTo("A:B:C");
    }

    // ── Section key count in signature ──────────────────────────

    [Test]
    public async Task SectionWithMultipleKeysShowsCorrectCount()
    {
        var source = """
            {
              "Section": {
                "A": 1,
                "B": 2,
                "C": 3
              }
            }
            """;

        var result = Parse(source);

        var section = result.Symbols.First(s => s.Name == "Section");
        await Assert.That(section.Signature).IsEqualTo("Section: { ... } (3 keys)");
    }

    // ── Multi-byte UTF-8 character handling ──────────────────────

    [Test]
    public async Task JsonWithMultiByteValuesDoesNotCrash()
    {
        var source = """
            {
              "name": "José García",
              "city": "München",
              "greeting": "こんにちは"
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(3);
        await Assert.That(result.Symbols[0].Name).IsEqualTo("name");
        await Assert.That(result.Symbols[1].Name).IsEqualTo("city");
        await Assert.That(result.Symbols[2].Name).IsEqualTo("greeting");
    }

    [Test]
    public async Task JsonWithMultiBytePropertyKeysParses()
    {
        var source = """
            {
              "名前": "太郎",
              "都市": "東京"
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(2);
        await Assert.That(result.Symbols[0].Name).IsEqualTo("名前");
        await Assert.That(result.Symbols[1].Name).IsEqualTo("都市");
    }

    [Test]
    public async Task JsonWithEmojiValuesParses()
    {
        var source = """
            {
              "status": "✅ Active",
              "icon": "🎮",
              "label": "Player 1"
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(3);
        await Assert.That(result.Symbols[2].Name).IsEqualTo("label");
    }

    [Test]
    public async Task DeeplyNestedJsonWithMultiByteCharsParsesAllKeys()
    {
        var source = """
            {
              "config": {
                "display": {
                  "title": "Ölüm Vadisi",
                  "subtitle": "Spëcîäl Çhàrs"
                }
              }
            }
            """;

        var result = Parse(source);

        var names = result.Symbols.Select(s => s.Name).ToList();
        await Assert.That(names).Contains("config:display:title");
        await Assert.That(names).Contains("config:display:subtitle");
    }

    [Test]
    public async Task DuplicateKeyNamesWithMultiByteContentResolveCorrectly()
    {
        var source = """
            {
              "parent": {
                "name": "José"
              },
              "child": {
                "name": "María"
              }
            }
            """;

        var result = Parse(source);

        var nameSymbols = result.Symbols.Where(s => s.Name.EndsWith("name", StringComparison.Ordinal)).ToList();
        await Assert.That(nameSymbols).Count().IsEqualTo(2);
        await Assert.That(nameSymbols[0].Name).IsEqualTo("parent:name");
        await Assert.That(nameSymbols[1].Name).IsEqualTo("child:name");
        // The two "name" keys should have different byte offsets
        await Assert.That(nameSymbols[0].ByteOffset).IsNotEqualTo(nameSymbols[1].ByteOffset);
    }

    [Test]
    public async Task ByteOffsetsAreCorrectForMultiByteContent()
    {
        var source = "{\"key\": \"café\", \"next\": \"value\"}";
        var bytes = Encoding.UTF8.GetBytes(source);

        var result = _parser.Parse("test.json", bytes);

        await Assert.That(result.Symbols).Count().IsEqualTo(2);
        // "next" key should have a byte offset that points to the right location
        var nextSym = result.Symbols.First(s => s.Name == "next");
        var byteSlice = Encoding.UTF8.GetString(bytes.AsSpan(nextSym.ByteOffset, 6));
        await Assert.That(byteSlice).IsEqualTo("\"next\"");
    }

    [Test]
    public async Task ManyPropertiesWithScatteredMultiByteCharsAllParse()
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        for (var i = 0; i < 50; i++)
        {
            var comma = i < 49 ? "," : "";
            sb.AppendLine(CultureInfo.InvariantCulture, $"  \"key{i}\": \"value{i} — entrée №{i}\"{comma}");
        }

        sb.AppendLine("}");

        var result = Parse(sb.ToString());

        await Assert.That(result.Symbols).Count().IsEqualTo(50);
    }
}

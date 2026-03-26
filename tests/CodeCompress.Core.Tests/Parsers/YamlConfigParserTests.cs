using System.Globalization;
using System.Text;
using CodeCompress.Core.Models;
using CodeCompress.Core.Parsers;

namespace CodeCompress.Core.Tests.Parsers;

internal sealed class YamlConfigParserTests
{
    private readonly YamlConfigParser _parser = new();

    private ParseResult Parse(string source)
    {
        var bytes = Encoding.UTF8.GetBytes(source);
        return _parser.Parse("settings.yaml", bytes);
    }

    // ── Interface contract ──────────────────────────────────────

    [Test]
    public async Task LanguageIdIsYamlConfig()
    {
        await Assert.That(_parser.LanguageId).IsEqualTo("yaml-config");
    }

    [Test]
    public async Task FileExtensionsContainsYaml()
    {
        await Assert.That(_parser.FileExtensions).Contains(".yaml");
    }

    [Test]
    public async Task FileExtensionsContainsYml()
    {
        await Assert.That(_parser.FileExtensions).Contains(".yml");
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
    public async Task MalformedYamlReturnsEmptyResult()
    {
        var result = Parse("{{broken yaml: [}");

        await Assert.That(result.Symbols).Count().IsEqualTo(0);
        await Assert.That(result.Dependencies).Count().IsEqualTo(0);
    }

    [Test]
    public async Task CommentsOnlyReturnsEmptyResult()
    {
        var source = """
            # This is a comment
            # Another comment
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(0);
        await Assert.That(result.Dependencies).Count().IsEqualTo(0);
    }

    [Test]
    public async Task WhitespaceOnlyReturnsEmptyResult()
    {
        var result = Parse("   \n   \n   ");

        await Assert.That(result.Symbols).Count().IsEqualTo(0);
    }

    // ── Top-level primitive keys ────────────────────────────────

    [Test]
    public async Task TopLevelStringKeyExtracted()
    {
        var source = """
            name: "my-cluster"
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);

        var sym = result.Symbols[0];
        await Assert.That(sym.Name).IsEqualTo("name");
        await Assert.That(sym.Kind).IsEqualTo(SymbolKind.ConfigKey);
        await Assert.That(sym.Signature).IsEqualTo("name: \"my-cluster\"");
        await Assert.That(sym.Visibility).IsEqualTo(Visibility.Public);
        await Assert.That(sym.ParentSymbol).IsNull();
    }

    [Test]
    public async Task TopLevelUnquotedStringKeyExtracted()
    {
        var source = """
            region: us-east-1
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Signature).IsEqualTo("region: \"us-east-1\"");
    }

    [Test]
    public async Task TopLevelNumberKeyExtracted()
    {
        var source = """
            port: 8080
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Signature).IsEqualTo("port: 8080");
    }

    [Test]
    public async Task TopLevelFloatKeyExtracted()
    {
        var source = """
            tick_rate: 60.5
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Signature).IsEqualTo("tick_rate: 60.5");
    }

    [Test]
    public async Task TopLevelBooleanTrueKeyExtracted()
    {
        var source = """
            enabled: true
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Signature).IsEqualTo("enabled: true");
    }

    [Test]
    public async Task TopLevelBooleanFalseKeyExtracted()
    {
        var source = """
            debug: false
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Signature).IsEqualTo("debug: false");
    }

    [Test]
    public async Task TopLevelNullKeyExtracted()
    {
        var source = """
            maintenance_window: null
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Signature).IsEqualTo("maintenance_window: null");
    }

    [Test]
    public async Task TopLevelTildeNullKeyExtracted()
    {
        var source = """
            optional_value: ~
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Signature).IsEqualTo("optional_value: null");
    }

    // ── Nested objects ──────────────────────────────────────────

    [Test]
    public async Task NestedMappingKeysUseColonSeparatedQualifiedNames()
    {
        var source = """
            logging:
              log_level:
                default: Information
            """;

        var result = Parse(source);

        var names = result.Symbols.Select(s => s.Name).ToList();
        await Assert.That(names).Contains("logging");
        await Assert.That(names).Contains("logging:log_level");
        await Assert.That(names).Contains("logging:log_level:default");
    }

    [Test]
    public async Task NestedMappingSectionHasObjectSignature()
    {
        var source = """
            connection_strings:
              default: "Server=localhost"
            """;

        var result = Parse(source);

        var section = result.Symbols.First(s => s.Name == "connection_strings");
        await Assert.That(section.Signature).IsEqualTo("connection_strings: { ... } (1 key)");
        await Assert.That(section.Kind).IsEqualTo(SymbolKind.ConfigKey);
    }

    [Test]
    public async Task NestedLeafHasParentSymbol()
    {
        var source = """
            connection_strings:
              default: "Server=localhost"
            """;

        var result = Parse(source);

        var leaf = result.Symbols.First(s => s.Name == "connection_strings:default");
        await Assert.That(leaf.ParentSymbol).IsEqualTo("connection_strings");
        await Assert.That(leaf.Signature).IsEqualTo("connection_strings:default: \"Server=localhost\"");
    }

    [Test]
    public async Task DeeplyNestedKeysHaveFullQualifiedName()
    {
        var source = """
            a:
              b:
                c:
                  d: deep
            """;

        var result = Parse(source);

        var deepest = result.Symbols.First(s => s.Name == "a:b:c:d");
        await Assert.That(deepest.Signature).IsEqualTo("a:b:c:d: \"deep\"");
        await Assert.That(deepest.ParentSymbol).IsEqualTo("a:b:c");
    }

    [Test]
    public async Task SectionWithMultipleKeysShowsCorrectCount()
    {
        var source = """
            section:
              a: 1
              b: 2
              c: 3
            """;

        var result = Parse(source);

        var section = result.Symbols.First(s => s.Name == "section");
        await Assert.That(section.Signature).IsEqualTo("section: { ... } (3 keys)");
    }

    [Test]
    public async Task EmptyMappingShowsZeroKeys()
    {
        var source = """
            empty_section: {}
            """;

        var result = Parse(source);

        var section = result.Symbols.First(s => s.Name == "empty_section");
        await Assert.That(section.Signature).IsEqualTo("empty_section: { ... } (0 keys)");
    }

    // ── Scalar arrays (summary only) ────────────────────────────

    [Test]
    public async Task ScalarArrayExtractedWithItemCount()
    {
        var source = """
            allowed_ips:
              - 10.0.0.1
              - 10.0.0.2
              - 10.0.0.3
            """;

        var result = Parse(source);

        // Only the array itself, not individual items
        var arraySymbols = result.Symbols.Where(s => s.Name.StartsWith("allowed_ips", StringComparison.Ordinal)).ToList();
        await Assert.That(arraySymbols).Count().IsEqualTo(1);

        var sym = arraySymbols[0];
        await Assert.That(sym.Name).IsEqualTo("allowed_ips");
        await Assert.That(sym.Signature).IsEqualTo("allowed_ips: [ ... ] (3 items)");
        await Assert.That(sym.Kind).IsEqualTo(SymbolKind.ConfigKey);
    }

    [Test]
    public async Task InlineScalarArrayExtractedWithItemCount()
    {
        var source = """
            tags: [web, api, production]
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Signature).IsEqualTo("tags: [ ... ] (3 items)");
    }

    [Test]
    public async Task EmptyArrayExtracted()
    {
        var source = """
            items: []
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Signature).IsEqualTo("items: [ ... ] (0 items)");
    }

    [Test]
    public async Task SingleItemArrayUsesItemSingular()
    {
        var source = """
            hosts:
              - localhost
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols[0].Signature).IsEqualTo("hosts: [ ... ] (1 item)");
    }

    // ── Object arrays (indexed individually) ─────────────────────

    [Test]
    public async Task ObjectArrayItemsIndexedWithZeroBasedIndex()
    {
        var source = """
            node_pools:
              - name: system
                vm_size: Standard_D4s_v5
                count: 3
              - name: user
                vm_size: Standard_E8s_v5
                count: 5
            """;

        var result = Parse(source);

        var names = result.Symbols.Select(s => s.Name).ToList();

        // The array itself
        await Assert.That(names).Contains("node_pools");

        // First item's children
        await Assert.That(names).Contains("node_pools:0:name");
        await Assert.That(names).Contains("node_pools:0:vm_size");
        await Assert.That(names).Contains("node_pools:0:count");

        // Second item's children
        await Assert.That(names).Contains("node_pools:1:name");
        await Assert.That(names).Contains("node_pools:1:vm_size");
        await Assert.That(names).Contains("node_pools:1:count");
    }

    [Test]
    public async Task ObjectArrayItemChildrenHaveCorrectParent()
    {
        var source = """
            services:
              - name: web
                port: 80
            """;

        var result = Parse(source);

        var nameSymbol = result.Symbols.First(s => s.Name == "services:0:name");
        await Assert.That(nameSymbol.ParentSymbol).IsEqualTo("services:0");
    }

    [Test]
    public async Task ObjectArrayParentShowsItemCount()
    {
        var source = """
            services:
              - name: web
              - name: api
            """;

        var result = Parse(source);

        var array = result.Symbols.First(s => s.Name == "services");
        await Assert.That(array.Signature).IsEqualTo("services: [ ... ] (2 items)");
    }

    [Test]
    public async Task MixedArrayTreatedAsObjectArrayWhenFirstItemIsMapping()
    {
        var source = """
            items:
              - name: first
                value: 1
              - name: second
                value: 2
            """;

        var result = Parse(source);

        var names = result.Symbols.Select(s => s.Name).ToList();
        await Assert.That(names).Contains("items:0:name");
        await Assert.That(names).Contains("items:1:value");
    }

    [Test]
    public async Task NestedObjectWithinArrayItemIndexed()
    {
        var source = """
            pools:
              - name: system
                autoscaling:
                  min: 1
                  max: 10
            """;

        var result = Parse(source);

        var names = result.Symbols.Select(s => s.Name).ToList();
        await Assert.That(names).Contains("pools:0:autoscaling");
        await Assert.That(names).Contains("pools:0:autoscaling:min");
        await Assert.That(names).Contains("pools:0:autoscaling:max");
    }

    // ── Multiple top-level keys ──────────────────────────────────

    [Test]
    public async Task MultipleTopLevelKeysAllExtracted()
    {
        var source = """
            name: my-app
            version: 2.4.1
            port: 8080
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(3);
        await Assert.That(result.Symbols[0].Name).IsEqualTo("name");
        await Assert.That(result.Symbols[1].Name).IsEqualTo("version");
        await Assert.That(result.Symbols[2].Name).IsEqualTo("port");
    }

    // ── Realistic infrastructure YAML ────────────────────────────

    [Test]
    public async Task RealisticSettingsYamlProducesExpectedSymbolCount()
    {
        var source = """
            kubernetes_version: "1.29"
            resource_group: rg-prod-eus
            node_pools:
              - name: system
                vm_size: Standard_D4s_v5
                count: 3
              - name: user
                vm_size: Standard_E8s_v5
                count: 5
            networking:
              vnet_cidr: "10.0.0.0/16"
              service_cidr: "10.1.0.0/16"
            """;

        var result = Parse(source);

        // kubernetes_version, resource_group = 2 top-level scalars
        // node_pools (array) = 1
        // node_pools:0 (item) + 3 children = 4
        // node_pools:1 (item) + 3 children = 4
        // networking (section) + 2 children = 3
        // Total = 14
        await Assert.That(result.Symbols).Count().IsEqualTo(14);
    }

    // ── ByteOffset accuracy ─────────────────────────────────────

    [Test]
    public async Task ByteOffsetIsNonNegative()
    {
        var source = """
            key: value
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols[0].ByteOffset).IsGreaterThanOrEqualTo(0);
        await Assert.That(result.Symbols[0].ByteLength).IsGreaterThan(0);
    }

    [Test]
    public async Task LineNumbersAreOneBased()
    {
        var source = "key: value\nnext: item";

        var result = Parse(source);

        await Assert.That(result.Symbols[0].LineStart).IsEqualTo(1);
        await Assert.That(result.Symbols[1].LineStart).IsEqualTo(2);
    }

    [Test]
    public async Task ByteOffsetPointsToCorrectLocation()
    {
        var source = "name: hello\nport: 8080";
        var bytes = Encoding.UTF8.GetBytes(source);

        var result = _parser.Parse("test.yaml", bytes);

        var portSym = result.Symbols.First(s => s.Name == "port");
        var byteSlice = Encoding.UTF8.GetString(bytes.AsSpan(portSym.ByteOffset, 4));
        await Assert.That(byteSlice).IsEqualTo("port");
    }

    // ── Dependencies always empty ───────────────────────────────

    [Test]
    public async Task DependenciesAlwaysEmpty()
    {
        var source = """
            key: value
            """;

        var result = Parse(source);

        await Assert.That(result.Dependencies).Count().IsEqualTo(0);
    }

    // ── Top-level sequence root ──────────────────────────────────

    [Test]
    public async Task TopLevelSequenceReturnsEmptyResult()
    {
        var source = """
            - item1
            - item2
            - item3
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(0);
    }

    [Test]
    public async Task TopLevelScalarReturnsEmptyResult()
    {
        var result = Parse("just a string");

        await Assert.That(result.Symbols).Count().IsEqualTo(0);
    }

    // ── Multi-byte UTF-8 character handling ──────────────────────

    [Test]
    public async Task YamlWithMultiByteValuesDoesNotCrash()
    {
        var source = """
            name: "José García"
            city: "München"
            greeting: "こんにちは"
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(3);
        await Assert.That(result.Symbols[0].Name).IsEqualTo("name");
        await Assert.That(result.Symbols[1].Name).IsEqualTo("city");
        await Assert.That(result.Symbols[2].Name).IsEqualTo("greeting");
    }

    [Test]
    public async Task YamlWithMultiBytePropertyKeysParses()
    {
        var source = """
            名前: 太郎
            都市: 東京
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(2);
        await Assert.That(result.Symbols[0].Name).IsEqualTo("名前");
        await Assert.That(result.Symbols[1].Name).IsEqualTo("都市");
    }

    [Test]
    public async Task YamlWithEmojiValuesParses()
    {
        var source = """
            status: "✅ Active"
            icon: "🎮"
            label: Player 1
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(3);
        await Assert.That(result.Symbols[2].Name).IsEqualTo("label");
    }

    [Test]
    public async Task ByteOffsetsAreCorrectForMultiByteContent()
    {
        var source = "name: café\nnext: value";
        var bytes = Encoding.UTF8.GetBytes(source);

        var result = _parser.Parse("test.yaml", bytes);

        await Assert.That(result.Symbols).Count().IsEqualTo(2);
        var nextSym = result.Symbols.First(s => s.Name == "next");
        var byteSlice = Encoding.UTF8.GetString(bytes.AsSpan(nextSym.ByteOffset, 4));
        await Assert.That(byteSlice).IsEqualTo("next");
    }

    [Test]
    public async Task ManyPropertiesWithScatteredMultiByteCharsAllParse()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < 50; i++)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"key{i}: \"value{i} — entrée №{i}\"");
        }

        var result = Parse(sb.ToString());

        await Assert.That(result.Symbols).Count().IsEqualTo(50);
    }

    // ── YAML-specific edge cases ─────────────────────────────────

    [Test]
    public async Task MultiLineStringValueExtracted()
    {
        var source = """
            description: |
              This is a multi-line
              string value
            name: test
            """;

        var result = Parse(source);

        var names = result.Symbols.Select(s => s.Name).ToList();
        await Assert.That(names).Contains("description");
        await Assert.That(names).Contains("name");
    }

    [Test]
    public async Task FoldedStringValueExtracted()
    {
        var source = """
            summary: >
              This is a folded
              string value
            version: "1.0"
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(2);
    }

    [Test]
    public async Task InlineCommentsIgnored()
    {
        var source = """
            port: 8080  # default port
            name: app   # application name
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(2);
        await Assert.That(result.Symbols[0].Signature).IsEqualTo("port: 8080");
    }

    [Test]
    public async Task InlineMappingParsed()
    {
        var source = """
            limits: {cpu: "500m", memory: "256Mi"}
            """;

        var result = Parse(source);

        var names = result.Symbols.Select(s => s.Name).ToList();
        await Assert.That(names).Contains("limits");
        await Assert.That(names).Contains("limits:cpu");
        await Assert.That(names).Contains("limits:memory");
    }
}

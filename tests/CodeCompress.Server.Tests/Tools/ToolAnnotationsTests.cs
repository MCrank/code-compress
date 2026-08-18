using System.Reflection;
using CodeCompress.Server.Tools;
using ModelContextProtocol.Server;

namespace CodeCompress.Server.Tests.Tools;

internal sealed class ToolAnnotationsTests
{
    private sealed record ExpectedAnnotation(bool ReadOnly, bool Destructive, bool Idempotent, bool OpenWorld, string Title);

    private static readonly Dictionary<string, ExpectedAnnotation> Expected = new(StringComparer.Ordinal)
    {
        ["index_project"] = new ExpectedAnnotation(ReadOnly: false, Destructive: false, Idempotent: true, OpenWorld: false, Title: "Index Project"),
        ["snapshot_create"] = new ExpectedAnnotation(ReadOnly: false, Destructive: false, Idempotent: false, OpenWorld: false, Title: "Create Snapshot"),
        ["invalidate_cache"] = new ExpectedAnnotation(ReadOnly: false, Destructive: true, Idempotent: true, OpenWorld: false, Title: "Invalidate Cache"),
        ["stop_server"] = new ExpectedAnnotation(ReadOnly: false, Destructive: true, Idempotent: false, OpenWorld: false, Title: "Stop Server"),
        ["list_repos"] = new ExpectedAnnotation(ReadOnly: true, Destructive: false, Idempotent: true, OpenWorld: false, Title: "List Indexed Repositories"),
        ["project_outline"] = new ExpectedAnnotation(ReadOnly: true, Destructive: false, Idempotent: true, OpenWorld: false, Title: "Get Project Outline"),
        ["get_module_api"] = new ExpectedAnnotation(ReadOnly: true, Destructive: false, Idempotent: true, OpenWorld: false, Title: "Get Module API"),
        ["get_symbol"] = new ExpectedAnnotation(ReadOnly: true, Destructive: false, Idempotent: true, OpenWorld: false, Title: "Get Symbol Source"),
        ["expand_symbol"] = new ExpectedAnnotation(ReadOnly: true, Destructive: false, Idempotent: true, OpenWorld: false, Title: "Expand Symbol"),
        ["get_symbols"] = new ExpectedAnnotation(ReadOnly: true, Destructive: false, Idempotent: true, OpenWorld: false, Title: "Get Multiple Symbols"),
        ["search_symbols"] = new ExpectedAnnotation(ReadOnly: true, Destructive: false, Idempotent: true, OpenWorld: false, Title: "Search Symbols"),
        ["topic_outline"] = new ExpectedAnnotation(ReadOnly: true, Destructive: false, Idempotent: true, OpenWorld: false, Title: "Get Topic Outline"),
        ["search_text"] = new ExpectedAnnotation(ReadOnly: true, Destructive: false, Idempotent: true, OpenWorld: false, Title: "Search Text"),
        ["get_hot_path"] = new ExpectedAnnotation(ReadOnly: true, Destructive: false, Idempotent: true, OpenWorld: false, Title: "Get Hot Path"),
        ["dependency_graph"] = new ExpectedAnnotation(ReadOnly: true, Destructive: false, Idempotent: true, OpenWorld: false, Title: "Get Dependency Graph"),
        ["blast_radius"] = new ExpectedAnnotation(ReadOnly: true, Destructive: false, Idempotent: true, OpenWorld: false, Title: "Get Blast Radius"),
        ["find_unused_symbols"] = new ExpectedAnnotation(ReadOnly: true, Destructive: false, Idempotent: true, OpenWorld: false, Title: "Find Unused Symbols"),
        ["project_dependencies"] = new ExpectedAnnotation(ReadOnly: true, Destructive: false, Idempotent: true, OpenWorld: false, Title: "Get Project Dependencies"),
        ["changes_since"] = new ExpectedAnnotation(ReadOnly: true, Destructive: false, Idempotent: true, OpenWorld: false, Title: "Get Changes Since Snapshot"),
        ["file_tree"] = new ExpectedAnnotation(ReadOnly: true, Destructive: false, Idempotent: true, OpenWorld: false, Title: "Get File Tree"),
        ["find_references"] = new ExpectedAnnotation(ReadOnly: true, Destructive: false, Idempotent: true, OpenWorld: false, Title: "Find References"),
        ["assemble_context"] = new ExpectedAnnotation(ReadOnly: true, Destructive: false, Idempotent: true, OpenWorld: false, Title: "Assemble Context"),
    };

    [Test]
    public async Task ExactlyTwentyTwoToolsAreDeclared()
    {
        var actual = GetAllToolAttributes();

        await Assert.That(actual).Count().IsEqualTo(22);
    }

    [Test]
    public async Task DiscoveredToolNamesMatchExpectedSetExactly()
    {
        var actualNames = GetAllToolAttributes().Select(a => a.Name!).ToHashSet(StringComparer.Ordinal);
        var expectedNames = Expected.Keys.ToHashSet(StringComparer.Ordinal);

        await Assert.That(actualNames.SetEquals(expectedNames)).IsTrue();
    }

    [Test]
    public async Task EveryToolDeclaresItsExactAnnotationTuple()
    {
        var actualByName = GetAllToolAttributes().ToDictionary(a => a.Name!, StringComparer.Ordinal);

        foreach (var (name, expected) in Expected)
        {
            await Assert.That(actualByName.ContainsKey(name)).IsTrue();

            var attr = actualByName[name];

            await Assert.That(attr.ReadOnly).IsEqualTo(expected.ReadOnly);
            await Assert.That(attr.Destructive).IsEqualTo(expected.Destructive);
            await Assert.That(attr.Idempotent).IsEqualTo(expected.Idempotent);
            await Assert.That(attr.OpenWorld).IsEqualTo(expected.OpenWorld);
            await Assert.That(attr.Title).IsEqualTo(expected.Title);
        }
    }

    private static List<McpServerToolAttribute> GetAllToolAttributes() =>
        typeof(IndexingTools).Assembly
            .GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>())
            .Where(a => a is not null)
            .Select(a => a!)
            .ToList();
}

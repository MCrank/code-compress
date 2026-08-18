using System.Text.Json;
using CodeCompress.Server;
using ModelContextProtocol.Protocol;
using TUnit.Assertions.Enums;

namespace CodeCompress.Server.Tests;

internal sealed class CacheHintsTests
{
    [Test]
    public async Task ApplyToToolsResultSetsOneHourPrivateTtl()
    {
        var result = new ListToolsResult { Tools = [] };

        var updated = CacheHints.ApplyToToolsResult(result);

        await Assert.That(updated.TimeToLive).IsEqualTo(TimeSpan.FromHours(1));
        await Assert.That(updated.CacheScope).IsEqualTo(CacheScope.Private);
    }

    [Test]
    public async Task ApplyToPromptsResultSetsOneHourPrivateTtl()
    {
        var result = new ListPromptsResult { Prompts = [] };

        var updated = CacheHints.ApplyToPromptsResult(result);

        await Assert.That(updated.TimeToLive).IsEqualTo(TimeSpan.FromHours(1));
        await Assert.That(updated.CacheScope).IsEqualTo(CacheScope.Private);
    }

    [Test]
    public async Task ApplyToToolsResultSortsToolsByNameOrdinal()
    {
        var result = new ListToolsResult
        {
            Tools =
            [
                MakeTool("search_symbols"),
                MakeTool("assemble_context"),
                MakeTool("get_symbol"),
            ],
        };

        var updated = CacheHints.ApplyToToolsResult(result);

        var names = updated.Tools.Select(t => t.Name).ToList();
        string[] expected = ["assemble_context", "get_symbol", "search_symbols"];
        await Assert.That(names).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    [Test]
    public async Task ApplyToPromptsResultSortsPromptsByNameOrdinal()
    {
        var result = new ListPromptsResult
        {
            Prompts =
            [
                MakePrompt("review_changes"),
                MakePrompt("debug_symbol"),
                MakePrompt("explore_codebase"),
            ],
        };

        var updated = CacheHints.ApplyToPromptsResult(result);

        var names = updated.Prompts.Select(p => p.Name).ToList();
        string[] expected = ["debug_symbol", "explore_codebase", "review_changes"];
        await Assert.That(names).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    [Test]
    public async Task SortingIsDeterministicRegardlessOfInputOrder()
    {
        var firstOrder = new ListToolsResult
        {
            Tools = [MakeTool("c_tool"), MakeTool("a_tool"), MakeTool("b_tool")],
        };
        var secondOrder = new ListToolsResult
        {
            Tools = [MakeTool("b_tool"), MakeTool("c_tool"), MakeTool("a_tool")],
        };

        var firstResult = CacheHints.ApplyToToolsResult(firstOrder);
        var secondResult = CacheHints.ApplyToToolsResult(secondOrder);

        await Assert.That(firstResult.Tools.Select(t => t.Name)).IsEquivalentTo(secondResult.Tools.Select(t => t.Name), CollectionOrdering.Matching);
    }

    private static Tool MakeTool(string name) => new()
    {
        Name = name,
        InputSchema = JsonDocument.Parse("""{"type":"object"}""").RootElement,
    };

    private static Prompt MakePrompt(string name) => new() { Name = name };
}

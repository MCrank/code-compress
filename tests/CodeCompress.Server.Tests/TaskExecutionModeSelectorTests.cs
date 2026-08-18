using ModelContextProtocol.Extensions.Tasks;

namespace CodeCompress.Server.Tests;

internal sealed class TaskExecutionModeSelectorTests
{
    [Test]
    public async Task IndexProjectSelectsOptional()
    {
        var mode = TaskExecutionModeSelector.Select("index_project");

        await Assert.That(mode).IsEqualTo(McpTaskExecutionMode.Optional);
    }

    [Test]
    [Arguments("get_symbol")]
    [Arguments("search_symbols")]
    [Arguments("invalidate_cache")]
    [Arguments("stop_server")]
    [Arguments("snapshot_create")]
    public async Task OtherToolsSelectSynchronous(string toolName)
    {
        var mode = TaskExecutionModeSelector.Select(toolName);

        await Assert.That(mode).IsEqualTo(McpTaskExecutionMode.Synchronous);
    }

    [Test]
    public async Task NullToolNameSelectsSynchronous()
    {
        var mode = TaskExecutionModeSelector.Select(null);

        await Assert.That(mode).IsEqualTo(McpTaskExecutionMode.Synchronous);
    }

    [Test]
    public async Task EmptyToolNameSelectsSynchronous()
    {
        var mode = TaskExecutionModeSelector.Select(string.Empty);

        await Assert.That(mode).IsEqualTo(McpTaskExecutionMode.Synchronous);
    }

    [Test]
    public async Task ToolNameIsCaseSensitive()
    {
        var mode = TaskExecutionModeSelector.Select("INDEX_PROJECT");

        await Assert.That(mode).IsEqualTo(McpTaskExecutionMode.Synchronous);
    }
}

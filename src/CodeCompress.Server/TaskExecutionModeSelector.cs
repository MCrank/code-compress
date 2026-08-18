using ModelContextProtocol.Extensions.Tasks;

namespace CodeCompress.Server;

/// <summary>
/// Selects MCP Tasks execution mode per tool call. Only index_project (5-120s on first run) opts
/// into task-optional execution; every other tool returns in well under a second and stays synchronous.
/// </summary>
internal static class TaskExecutionModeSelector
{
    private const string IndexProjectToolName = "index_project";

    public static McpTaskExecutionMode Select(string? toolName) =>
        string.Equals(toolName, IndexProjectToolName, StringComparison.Ordinal)
            ? McpTaskExecutionMode.Optional
            : McpTaskExecutionMode.Synchronous;
}

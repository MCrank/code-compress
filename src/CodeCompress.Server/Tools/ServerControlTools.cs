using System.ComponentModel;
using CodeCompress.Core.Contracts;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;

namespace CodeCompress.Server.Tools;

[McpServerToolType]
internal sealed class ServerControlTools
{
    private readonly IHostApplicationLifetime _lifetime;

    public ServerControlTools(IHostApplicationLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(lifetime);

        _lifetime = lifetime;
    }

    [McpServerTool(Name = "stop_server", Title = "Stop Server", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Gracefully shut down the CodeCompress MCP server. Claude Code will automatically restart it on the next tool call. Use this to release DLL locks during development or free resources. Returns JSON: {success: true, message}.")]
    public async Task<StopServerResult> StopServer()
    {
        // Schedule shutdown after a brief delay so the response can be sent first
        _ = Task.Run(async () =>
        {
            await Task.Delay(500).ConfigureAwait(false);
            _lifetime.StopApplication();
        });

        return await Task.FromResult(new StopServerResult
        {
            Success = true,
            Message = "Server is shutting down. It will restart automatically on the next tool call.",
        }).ConfigureAwait(false);
    }
}

using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;

namespace CodeCompress.Server.Tools;

[McpServerToolType]
internal sealed class ServerControlTools
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly IHostApplicationLifetime _lifetime;

    public ServerControlTools(IHostApplicationLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(lifetime);

        _lifetime = lifetime;
    }

    [McpServerTool(Name = "stop_server")]
    [Description("Gracefully shut down the CodeCompress MCP server. Claude Code will automatically restart it on the next tool call. Use this to release DLL locks during development or free resources.")]
    public async Task<string> StopServer()
    {
        // Schedule shutdown after a brief delay so the response can be sent first
        _ = Task.Run(async () =>
        {
            await Task.Delay(500).ConfigureAwait(false);
            _lifetime.StopApplication();
        });

        return await Task.FromResult(JsonSerializer.Serialize(
            new
            {
                Success = true,
                Message = "Server is shutting down. It will restart automatically on the next tool call.",
            },
            SerializerOptions)).ConfigureAwait(false);
    }
}

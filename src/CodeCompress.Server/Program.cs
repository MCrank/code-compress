using CodeCompress.Server;
using CodeCompress.Server.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(options =>
{
    // All log output must go to stderr — stdout is reserved for the MCP JSON-RPC transport
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

// Bind idle timeout from CLI args (--idle-timeout) and env var (CODECOMPRESS_IDLE_TIMEOUT)
builder.Services.Configure<IdleTimeoutOptions>(options =>
{
    var cliValue = builder.Configuration["idle-timeout"];
    var envValue = Environment.GetEnvironmentVariable("CODECOMPRESS_IDLE_TIMEOUT");

    // CLI argument takes precedence over environment variable
    var raw = cliValue ?? envValue;

    if (raw is not null && int.TryParse(raw, out var seconds))
    {
        options.IdleTimeout = Math.Max(0, seconds);
    }
});

builder.Services
    .AddCodeCompressServer()
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync().ConfigureAwait(false);

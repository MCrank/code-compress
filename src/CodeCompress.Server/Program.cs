using CodeCompress.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(options =>
{
    // All log output must go to stderr — stdout is reserved for the MCP JSON-RPC transport
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services
    .AddCodeCompressServer()
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync().ConfigureAwait(false);

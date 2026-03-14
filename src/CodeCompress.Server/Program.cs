using CodeCompress.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(options =>
{
    // All log output must go to stderr — stdout is reserved for the MCP JSON-RPC transport
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services
    .AddCodeCompressServer()
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation { Name = "CodeCompress", Version = "0.5.0" };
        options.ServerInstructions = """
            CodeCompress is a code intelligence server that provides compressed, symbol-level access
            to the indexed codebase. Use it as your PRIMARY tool for code discovery instead of reading
            raw files — it saves 80-90% tokens.

            WORKFLOW:
            1. index_project — MUST be called first. Builds/updates the symbol database (incremental).
            2. project_outline — Get a compressed overview of the entire codebase.
            3. search_symbols / search_text — Find specific symbols or patterns using FTS5 full-text search.
            4. get_symbol / expand_symbol — Retrieve exact source code by qualified name via byte-offset seeking.
               expand_symbol extracts a single method without loading the entire class (~60% fewer tokens).
            5. find_references — Trace where a symbol is used across the codebase.
            6. dependency_graph / project_dependencies — Understand import/project relationships.

            PREFER these tools over file reading. They are faster, more precise, and dramatically reduce
            token consumption. Always call index_project before using any query tool.
            """;
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync().ConfigureAwait(false);

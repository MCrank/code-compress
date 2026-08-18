using System.ComponentModel;
using System.Text.Json;
using CodeCompress.Core.Registry;
using ModelContextProtocol.Server;

namespace CodeCompress.Server.Tools;

[McpServerToolType]
internal sealed class RegistryTools
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly IRegistryService _registryService;

    public RegistryTools(IRegistryService registryService)
    {
        ArgumentNullException.ThrowIfNull(registryService);
        _registryService = registryService;
    }

    [McpServerTool(Name = "list_repos", Title = "List Indexed Repositories", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(
        "List all projects that have been indexed in the global CodeCompress database. " +
        "Returns an empty array when no projects have been indexed. " +
        "Each entry includes project_root, display_name, file_count, symbol_count, last_indexed, and status. " +
        "status is 'healthy' when the last index succeeded, 'error' when it failed. " +
        "~20-200 tokens depending on repo count. " +
        "Returns JSON array: [{project_root, display_name, file_count, symbol_count, last_indexed, status}]. " +
        "Next: call index_project on the desired project_root before querying symbols.")]
    public async Task<string> ListRepos(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        var repos = await _registryService.ListAsync().ConfigureAwait(false);

        var response = repos.Select(r => new
        {
            r.ProjectRoot,
            r.DisplayName,
            r.FileCount,
            r.SymbolCount,
            r.LastIndexed,
            Status = r.LastError is null ? "healthy" : "error",
        });

        return JsonSerializer.Serialize(response, SerializerOptions);
    }
}

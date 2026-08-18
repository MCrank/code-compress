using System.ComponentModel;
using CodeCompress.Core.Contracts;
using CodeCompress.Core.Registry;
using ModelContextProtocol.Server;

namespace CodeCompress.Server.Tools;

[McpServerToolType]
internal sealed class RegistryTools
{
    private readonly IRegistryService _registryService;

    public RegistryTools(IRegistryService registryService)
    {
        ArgumentNullException.ThrowIfNull(registryService);
        _registryService = registryService;
    }

    [McpServerTool(Name = "list_repos", Title = "List Indexed Repositories", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description(
        "List all projects that have been indexed in the global CodeCompress database. " +
        "Returns an empty array when no projects have been indexed. " +
        "Each entry includes project_root, display_name, file_count, symbol_count, last_indexed, and status. " +
        "status is 'healthy' when the last index succeeded, 'error' when it failed. " +
        "~20-200 tokens depending on repo count. " +
        "Returns JSON: {repos: [{project_root, display_name, file_count, symbol_count, last_indexed, status}]}. " +
        "Next: call index_project on the desired project_root before querying symbols.")]
    public async Task<ListReposResult> ListRepos(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        var repos = await _registryService.ListAsync().ConfigureAwait(false);

        return new ListReposResult
        {
            Repos = repos.Select(r => new RepoEntryContract
            {
                ProjectRoot = r.ProjectRoot,
                DisplayName = r.DisplayName,
                FileCount = r.FileCount,
                SymbolCount = r.SymbolCount,
                LastIndexed = r.LastIndexed,
                Status = r.LastError is null ? "healthy" : "error",
            }).ToList(),
        };
    }
}

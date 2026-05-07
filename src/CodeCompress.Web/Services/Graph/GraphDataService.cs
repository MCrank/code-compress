namespace CodeCompress.Web.Services.Graph;

public sealed class GraphDataService
{
    private static readonly Dictionary<string, string> s_languageColors = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"]    = "#178600",
        [".razor"] = "#512bd4",
        [".ts"]    = "#3178c6",
        [".tsx"]   = "#3178c6",
        [".js"]    = "#f7df1e",
        [".jsx"]   = "#f7df1e",
        [".py"]    = "#3572a5",
        [".go"]    = "#00add8",
        [".rs"]    = "#dea584",
        [".java"]  = "#b07219",
        [".tf"]    = "#7b42bc",
    };

    private static readonly Dictionary<string, string> s_languageIcons = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"]    = "code-2",
        [".razor"] = "layout",
        [".ts"]    = "file-code",
        [".tsx"]   = "file-code",
        [".js"]    = "file-code",
        [".jsx"]   = "file-code",
        [".py"]    = "file-code",
        [".go"]    = "file-code",
        [".rs"]    = "file-code",
        [".java"]  = "file-code",
        [".tf"]    = "database",
        [".json"]  = "braces",
        [".yaml"]  = "file-text",
        [".yml"]   = "file-text",
    };

    private readonly IIndexFacade _facade;

    public GraphDataService(IIndexFacade facade)
    {
        ArgumentNullException.ThrowIfNull(facade);
        _facade = facade;
    }

    public static string GetLanguageColor(string relativePath)
    {
        var ext = Path.GetExtension(relativePath);
        return s_languageColors.TryGetValue(ext, out var color) ? color : "#6b7280";
    }

    public static string GetLanguageIcon(string relativePath)
    {
        var ext = Path.GetExtension(relativePath);
        return s_languageIcons.TryGetValue(ext, out var icon) ? icon : "file";
    }

    public async Task<(IReadOnlyList<FileNodeViewModel> Nodes, IReadOnlyList<EdgeViewModel> Edges)> GetGraphAsync(
        string projectRoot, CancellationToken ct = default)
    {
        var graph = await _facade.GetDependencyGraphAsync(projectRoot, ct).ConfigureAwait(false);

        var nodes = graph.Nodes
            .Select(path => new FileNodeViewModel(
                path,
                Path.GetExtension(path),
                Path.GetDirectoryName(path)?.Replace('\\', '/') ?? string.Empty))
            .ToList();

        var edges = graph.Edges
            .Select(e => new EdgeViewModel(e.From, e.To, e.EdgeKind ?? "imports"))
            .ToList();

        return (nodes, edges);
    }
}

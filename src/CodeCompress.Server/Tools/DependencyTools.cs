using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CodeCompress.Core.Models;
using CodeCompress.Core.Validation;
using CodeCompress.Server.Scoping;
using ModelContextProtocol.Server;

namespace CodeCompress.Server.Tools;

[McpServerToolType]
internal sealed class DependencyTools
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private static readonly HashSet<string> ValidDirections = new(StringComparer.OrdinalIgnoreCase)
    {
        "dependencies", "dependents", "both",
    };

    private readonly IPathValidator _pathValidator;
    private readonly IProjectScopeFactory _scopeFactory;

    public DependencyTools(IPathValidator pathValidator, IProjectScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(pathValidator);
        ArgumentNullException.ThrowIfNull(scopeFactory);

        _pathValidator = pathValidator;
        _scopeFactory = scopeFactory;
    }

    [McpServerTool(Name = "dependency_graph")]
    [Description("Get the import/require dependency graph for a project or a specific file.")]
    public async Task<string> DependencyGraph(
        [Description("Absolute path to the project root directory")] string path,
        [Description("Start traversal from a specific file (relative path). Omit for full project graph.")] string? rootFile = null,
        [Description("Direction: dependencies (outgoing), dependents (incoming), or both")] string direction = "both",
        [Description("Maximum traversal depth (1-50). Omit for unlimited.")] int? depth = null,
        CancellationToken cancellationToken = default)
    {
        string validatedPath;
        try
        {
            validatedPath = _pathValidator.ValidatePath(path, path);
        }
        catch (ArgumentException)
        {
            return SerializeError("Path validation failed", "INVALID_PATH");
        }

        if (!ValidDirections.Contains(direction))
        {
            return SerializeError(
                "Invalid direction. Must be one of: dependencies, dependents, both",
                "INVALID_DIRECTION");
        }

        if (rootFile is not null)
        {
            try
            {
                _pathValidator.ValidateRelativePath(rootFile, validatedPath);
            }
            catch (ArgumentException)
            {
                return SerializeError("Path validation failed", "INVALID_PATH");
            }
        }

        var clampedDepth = depth.HasValue ? Math.Clamp(depth.Value, 1, 50) : 50;

        var scope = await _scopeFactory.CreateAsync(validatedPath, cancellationToken).ConfigureAwait(false);
        await using (scope.ConfigureAwait(false))
        {
            var graph = await scope.Store.GetDependencyGraphAsync(
                scope.RepoId, rootFile, direction, clampedDepth).ConfigureAwait(false);

            // Non-existent root file returns empty graph
            if (rootFile is not null && graph.Nodes.Count == 0)
            {
                return SerializeError("File not found in index", "FILE_NOT_FOUND");
            }

            return FormatGraph(graph, rootFile, direction, clampedDepth);
        }
    }

    private static string FormatGraph(DependencyGraph graph, string? rootFile, string direction, int depth)
    {
        var sb = new StringBuilder();

        // Header
        if (rootFile is not null)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"Dependency graph for \"{rootFile}\" (depth: {depth}, direction: {direction}):");
        }
        else
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"Dependency graph (full project, direction: {direction}):");
        }

        sb.AppendLine();

        // Build edge lookups: From->To = "requires", To->From = "required by"
        var outgoing = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var incoming = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var edge in graph.Edges)
        {
            if (!outgoing.TryGetValue(edge.From, out var outList))
            {
                outList = [];
                outgoing[edge.From] = outList;
            }

            outList.Add(edge.To);

            if (!incoming.TryGetValue(edge.To, out var inList))
            {
                inList = [];
                incoming[edge.To] = inList;
            }

            inList.Add(edge.From);
        }

        var showOutgoing = string.Equals(direction, "dependencies", StringComparison.OrdinalIgnoreCase)
            || string.Equals(direction, "both", StringComparison.OrdinalIgnoreCase);
        var showIncoming = string.Equals(direction, "dependents", StringComparison.OrdinalIgnoreCase)
            || string.Equals(direction, "both", StringComparison.OrdinalIgnoreCase);

        // Render each node
        foreach (var node in graph.Nodes)
        {
            sb.AppendLine(node);

            if (showOutgoing)
            {
                var deps = outgoing.TryGetValue(node, out var outList)
                    ? string.Join(", ", outList.Order(StringComparer.Ordinal))
                    : "(none)";
                sb.AppendLine(CultureInfo.InvariantCulture, $"  requires -> {deps}");
            }

            if (showIncoming)
            {
                var deps = incoming.TryGetValue(node, out var inList)
                    ? string.Join(", ", inList.Order(StringComparer.Ordinal))
                    : "(none)";
                sb.AppendLine(CultureInfo.InvariantCulture, $"  required by -> {deps}");
            }

            sb.AppendLine();
        }

        // Full project summary
        if (rootFile is null)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"Total: {graph.Nodes.Count} files, {graph.Edges.Count} dependency edges");
        }

        return sb.ToString();
    }

    private static string SerializeError(string error, string code) =>
        JsonSerializer.Serialize(new { Error = error, Code = code }, SerializerOptions);
}

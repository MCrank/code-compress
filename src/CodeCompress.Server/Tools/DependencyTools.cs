using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CodeCompress.Core.Models;
using CodeCompress.Core.Validation;
using CodeCompress.Server.Scoping;
using CodeCompress.Server.Services;
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
    private readonly IActivityTracker _activityTracker;

    public DependencyTools(IPathValidator pathValidator, IProjectScopeFactory scopeFactory, IActivityTracker activityTracker)
    {
        ArgumentNullException.ThrowIfNull(pathValidator);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(activityTracker);

        _pathValidator = pathValidator;
        _scopeFactory = scopeFactory;
        _activityTracker = activityTracker;
    }

    [McpServerTool(Name = "dependency_graph")]
    [Description("Get the import/require dependency graph for a project or specific file. Shows which files depend on which others.")]
    public async Task<string> DependencyGraph(
        [Description("ABSOLUTE path to the project root directory — the same root used with index_project (e.g., 'C:\\Projects\\MyGame' or '/home/user/my-project'). Must NOT be a subdirectory or relative path.")] string path,
        [Description("Start traversal from a specific file (relative path). Omit for full project graph.")] string? rootFile = null,
        [Description("Direction: dependencies (outgoing), dependents (incoming), or both")] string direction = "both",
        [Description("Maximum traversal depth (1-50). Omit for unlimited.")] int? depth = null,
        CancellationToken cancellationToken = default)
    {
        _activityTracker.RecordActivity();

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

    [McpServerTool(Name = "project_dependencies")]
    [Description("Show inter-project dependency relationships in a .NET solution. Parses ProjectReference entries from indexed .csproj files to build a project-level dependency graph with shared public types.")]
    public async Task<string> ProjectDependencies(
        [Description("ABSOLUTE path to the project root directory — the same root used with index_project (e.g., 'C:\\Projects\\MySolution' or '/home/user/my-solution'). Must NOT be a subdirectory or relative path.")] string path,
        [Description("Filter to projects whose name contains this string (case-insensitive). Omit for all projects.")] string? projectFilter = null,
        CancellationToken cancellationToken = default)
    {
        _activityTracker.RecordActivity();

        string validatedPath;
        try
        {
            validatedPath = _pathValidator.ValidatePath(path, path);
        }
        catch (ArgumentException)
        {
            return SerializeError("Path validation failed", "INVALID_PATH");
        }

        var scope = await _scopeFactory.CreateAsync(validatedPath, cancellationToken).ConfigureAwait(false);
        await using (scope.ConfigureAwait(false))
        {
            var result = await scope.Store.GetProjectDependencyGraphAsync(scope.RepoId, projectFilter)
                .ConfigureAwait(false);

            if (result.Projects.Count == 0)
            {
                return SerializeError("No project files (.csproj/.fsproj/.vbproj) found in index. Run index_project first.", "NO_PROJECTS");
            }

            return FormatProjectDependencies(result, projectFilter);
        }
    }

    private static string FormatProjectDependencies(ProjectDependencyResult result, string? projectFilter)
    {
        var sb = new StringBuilder();

        // Header
        if (projectFilter is not null)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"Project dependencies (filter: \"{projectFilter}\"):");
        }
        else
        {
            sb.AppendLine("Project dependencies:");
        }

        sb.AppendLine();

        // Build edge lookups
        var outgoing = new Dictionary<string, List<ProjectDependencyEdge>>(StringComparer.Ordinal);
        var incoming = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var edge in result.Edges)
        {
            if (!outgoing.TryGetValue(edge.FromProject, out var outList))
            {
                outList = [];
                outgoing[edge.FromProject] = outList;
            }

            outList.Add(edge);

            if (!incoming.TryGetValue(edge.ToProject, out var inList))
            {
                inList = [];
                incoming[edge.ToProject] = inList;
            }

            inList.Add(edge.FromProject);
        }

        // Render each project node
        foreach (var project in result.Projects)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"[{project.Name}] ({project.RelativePath})");

            // Show outgoing references
            if (outgoing.TryGetValue(project.Name, out var refs))
            {
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"  references -> {string.Join(", ", refs.Select(r => r.ToProject).Order(StringComparer.Ordinal))}");

                // Show shared types per reference
                foreach (var edge in refs.OrderBy(e => e.ToProject, StringComparer.Ordinal))
                {
                    if (edge.SharedTypes.Count > 0)
                    {
                        sb.AppendLine(CultureInfo.InvariantCulture,
                            $"    via {edge.ToProject}: {string.Join(", ", edge.SharedTypes)}");
                    }
                }
            }
            else
            {
                sb.AppendLine("  references -> (none)");
            }

            // Show incoming references
            if (incoming.TryGetValue(project.Name, out var inRefs))
            {
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"  referenced by -> {string.Join(", ", inRefs.Order(StringComparer.Ordinal))}");
            }
            else
            {
                sb.AppendLine("  referenced by -> (none)");
            }

            sb.AppendLine();
        }

        sb.AppendLine(CultureInfo.InvariantCulture,
            $"Total: {result.Projects.Count} projects, {result.Edges.Count} project references");

        return sb.ToString();
    }

    private static string SerializeError(string error, string code) =>
        JsonSerializer.Serialize(new { Error = error, Code = code }, SerializerOptions);
}

using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CodeCompress.Core.Validation;
using CodeCompress.Server.Scoping;
using ModelContextProtocol.Server;

namespace CodeCompress.Server.Tools;

[McpServerToolType]
internal sealed class QueryTools
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private static readonly HashSet<string> ValidGroupByValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "file", "kind", "directory",
    };

    private readonly IPathValidator _pathValidator;
    private readonly IProjectScopeFactory _scopeFactory;

    public QueryTools(IPathValidator pathValidator, IProjectScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(pathValidator);
        ArgumentNullException.ThrowIfNull(scopeFactory);

        _pathValidator = pathValidator;
        _scopeFactory = scopeFactory;
    }

    [McpServerTool(Name = "project_outline")]
    [Description("Get a compressed, token-efficient overview of an entire codebase with signatures only.")]
    public async Task<string> ProjectOutline(
        [Description("Absolute path to the project root directory")] string path,
        [Description("Include private/local symbols")] bool includePrivate = false,
        [Description("Grouping strategy: file, kind, or directory")] string groupBy = "file",
        [Description("Limit directory traversal depth (null for unlimited)")] int? maxDepth = null,
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

        if (!ValidGroupByValues.Contains(groupBy))
        {
            return SerializeError("Invalid group_by value. Must be one of: file, kind, directory", "INVALID_GROUP_BY");
        }

        var scope = await _scopeFactory.CreateAsync(validatedPath, cancellationToken).ConfigureAwait(false);
        await using (scope.ConfigureAwait(false))
        {
            var outline = await scope.Store.GetProjectOutlineAsync(
                scope.RepoId,
                includePrivate,
                groupBy,
                maxDepth ?? 0).ConfigureAwait(false);

            return FormatOutline(outline);
        }
    }

    [McpServerTool(Name = "get_module_api")]
    [Description("Get the full public API surface of a single module file.")]
    public async Task<string> GetModuleApi(
        [Description("Absolute path to the project root directory")] string path,
        [Description("Relative path to the module file (e.g., src/services/CombatService.luau)")] string modulePath,
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

        try
        {
            _pathValidator.ValidateRelativePath(modulePath, validatedPath);
        }
        catch (ArgumentException)
        {
            return SerializeError("Path validation failed", "INVALID_PATH");
        }

        try
        {
            var scope = await _scopeFactory.CreateAsync(validatedPath, cancellationToken).ConfigureAwait(false);
            await using (scope.ConfigureAwait(false))
            {
                var moduleApi = await scope.Store.GetModuleApiAsync(scope.RepoId, modulePath).ConfigureAwait(false);

                var response = new
                {
                    Module = moduleApi.File.RelativePath,
                    Symbols = moduleApi.Symbols.Select(s => new
                    {
                        s.Name,
                        s.Kind,
                        Parent = s.ParentSymbol,
                        s.Signature,
                        Line = s.LineStart,
                        s.DocComment,
                    }),
                    Dependencies = moduleApi.Dependencies.Select(d => new
                    {
                        d.RequiresPath,
                        d.Alias,
                    }),
                };

                return JsonSerializer.Serialize(response, SerializerOptions);
            }
        }
        catch (FileNotFoundException)
        {
            return SerializeError("Module not found", "MODULE_NOT_FOUND");
        }
    }

    private static string FormatOutline(Core.Models.ProjectOutline outline)
    {
        var sb = new StringBuilder();
        var totalSymbols = CountSymbols(outline.Groups);
        sb.AppendLine(CultureInfo.InvariantCulture, $"# Project Outline ({totalSymbols} symbols)");

        foreach (var group in outline.Groups)
        {
            RenderGroup(sb, group, depth: 0);
        }

        return sb.ToString();
    }

    private static void RenderGroup(StringBuilder sb, Core.Models.OutlineGroup group, int depth)
    {
        var prefix = depth switch
        {
            0 => "## ",
            1 => "### ",
            _ => new string('#', depth + 2) + " ",
        };

        sb.Append(prefix).AppendLine(group.Name);

        foreach (var symbol in group.Symbols)
        {
            var indent = new string(' ', (depth + 1) * 2);
#pragma warning disable CA1308 // Normalize strings to uppercase — lowercase is the intended display format
            sb.Append(indent)
                .Append(symbol.Visibility.ToLowerInvariant())
                .Append(' ')
                .Append(symbol.Kind.ToLowerInvariant())
                .Append(' ')
                .AppendLine(symbol.Signature);
#pragma warning restore CA1308
        }

        foreach (var child in group.Children)
        {
            RenderGroup(sb, child, depth + 1);
        }
    }

    private static int CountSymbols(IReadOnlyList<Core.Models.OutlineGroup> groups)
    {
        var count = 0;
        foreach (var group in groups)
        {
            count += group.Symbols.Count;
            count += CountSymbols(group.Children);
        }

        return count;
    }

    private static string SerializeError(string error, string code) =>
        JsonSerializer.Serialize(new { Error = error, Code = code }, SerializerOptions);
}

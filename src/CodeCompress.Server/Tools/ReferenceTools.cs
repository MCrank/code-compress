using System.ComponentModel;
using System.Text.Json;
using CodeCompress.Core.Models;
using CodeCompress.Core.Storage;
using CodeCompress.Core.Validation;
using CodeCompress.Server.Scoping;
using ModelContextProtocol.Server;

namespace CodeCompress.Server.Tools;

[McpServerToolType]
internal sealed class ReferenceTools
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly IPathValidator _pathValidator;
    private readonly IProjectScopeFactory _scopeFactory;

    public ReferenceTools(IPathValidator pathValidator, IProjectScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(pathValidator);
        ArgumentNullException.ThrowIfNull(scopeFactory);

        _pathValidator = pathValidator;
        _scopeFactory = scopeFactory;
    }

    [McpServerTool(Name = "find_references")]
    [Description("Find all locations where a symbol is referenced across the indexed codebase — far faster than grep since content is pre-indexed. Returns file paths, line numbers, and 3-line context snippets. Works for functions, types, interfaces, DI registrations, and any text pattern. Use to trace usage before refactoring. Requires index_project to have been called first. Returns JSON: {symbol, total_matches, results: [{file, line, context_snippet, rank}]}. Errors return JSON {error, code, retryable}. Codes: INVALID_PATH, EMPTY_SYMBOL_NAME, INVALID_PATH_FILTER.")]
    public async Task<string> FindReferences(
        [Description("ABSOLUTE path to the project root directory — the same root used with index_project (e.g., 'C:\\Projects\\MyGame' or '/home/user/my-project'). Must NOT be a subdirectory or relative path.")] string path,
        [Description("Symbol name to search for references (e.g., 'ProcessAttack', 'ISymbolStore'). Does not need to exist in the symbol table — text search is used.")] string symbolName,
        [Description("Filter results to files under this relative directory path (e.g., 'src/')")] string? pathFilter = null,
        [Description("Maximum results to return (1-100, default 20). Values outside this range are clamped.")] int limit = 20,
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

        if (string.IsNullOrWhiteSpace(symbolName))
        {
            return SerializeError("Symbol name cannot be empty", "EMPTY_SYMBOL_NAME");
        }

        string? validatedPathFilter = null;
        if (pathFilter is not null)
        {
            try
            {
                validatedPathFilter = PathValidator.ValidatePathFilter(pathFilter);
            }
            catch (ArgumentException)
            {
                return SerializeError("Invalid path filter", "INVALID_PATH_FILTER");
            }
        }

        var clampedLimit = Math.Clamp(limit, 1, 100);

        var scope = await _scopeFactory.CreateAsync(validatedPath, cancellationToken).ConfigureAwait(false);
        await using (scope.ConfigureAwait(false))
        {
            IReadOnlyList<ReferenceResult> results;
            try
            {
                results = await scope.Store.FindReferencesAsync(
                    scope.RepoId, symbolName, validatedPath, clampedLimit, validatedPathFilter).ConfigureAwait(false);
            }
            catch (System.Data.Common.DbException)
            {
                // FTS5 syntax error — return empty results
                results = [];
            }

            var response = new
            {
                Symbol = SanitizeSymbolName(symbolName),
                TotalMatches = results.Count,
                Results = results.Select((r, index) => new
                {
                    File = r.FilePath,
                    r.Line,
                    ContextSnippet = r.ContextSnippet,
                    Rank = index + 1,
                }),
            };

            return JsonSerializer.Serialize(response, SerializerOptions);
        }
    }

    private static string SanitizeSymbolName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        // Allow only alphanumeric, colon, underscore, dot, hyphen
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var c in name.Where(c => char.IsLetterOrDigit(c) || c is ':' or '_' or '.' or '-'))
        {
            sb.Append(c);
        }

        var result = sb.ToString();
        return result.Length > 256 ? result[..256] : result;
    }

    private static string SerializeError(string error, string code, string? guidance = null) =>
        guidance is null
            ? JsonSerializer.Serialize(new { Error = error, Code = code, Retryable = false }, SerializerOptions)
            : JsonSerializer.Serialize(new { Error = error, Code = code, Retryable = false, Guidance = guidance }, SerializerOptions);
}

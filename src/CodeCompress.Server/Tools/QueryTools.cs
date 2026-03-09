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

    [McpServerTool(Name = "get_symbol")]
    [Description("Get the source code of a specific symbol by its qualified name using byte-offset seeking.")]
    public async Task<string> GetSymbol(
        [Description("Absolute path to the project root directory")] string path,
        [Description("Fully qualified symbol name (e.g., CombatService:ProcessAttack)")] string symbolName,
        [Description("Include 5 lines of context before and after the symbol")] bool includeContext = false,
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

        var scope = await _scopeFactory.CreateAsync(validatedPath, cancellationToken).ConfigureAwait(false);
        await using (scope.ConfigureAwait(false))
        {
            var symbol = await scope.Store.GetSymbolByNameAsync(scope.RepoId, symbolName).ConfigureAwait(false);
            if (symbol is null)
            {
                return JsonSerializer.Serialize(
                    new { Error = "Symbol not found", Code = "SYMBOL_NOT_FOUND", Symbol = SanitizeSymbolName(symbolName) },
                    SerializerOptions);
            }

            var files = await scope.Store.GetFilesByRepoAsync(scope.RepoId).ConfigureAwait(false);
            var file = files.FirstOrDefault(f => f.Id == symbol.FileId);
            if (file is null)
            {
                return SerializeError("File not found for symbol", "FILE_NOT_FOUND");
            }

            string resolvedPath;
            try
            {
                resolvedPath = _pathValidator.ValidatePath(
                    Path.Combine(validatedPath, file.RelativePath), validatedPath);
            }
            catch (ArgumentException)
            {
                return SerializeError("Path validation failed", "INVALID_PATH");
            }

            var sourceCode = includeContext
                ? await ReadSourceCodeWithContextAsync(resolvedPath, symbol.ByteOffset, symbol.ByteLength).ConfigureAwait(false)
                : await ReadSourceCodeAsync(resolvedPath, symbol.ByteOffset, symbol.ByteLength).ConfigureAwait(false);

            var response = new
            {
                symbol.Name,
                symbol.Kind,
                Parent = symbol.ParentSymbol,
                File = file.RelativePath,
                symbol.LineStart,
                symbol.LineEnd,
                symbol.Signature,
                SourceCode = sourceCode,
            };

            return JsonSerializer.Serialize(response, SerializerOptions);
        }
    }

    [McpServerTool(Name = "get_symbols")]
    [Description("Batch retrieve source code for multiple symbols by their qualified names.")]
    public async Task<string> GetSymbols(
        [Description("Absolute path to the project root directory")] string path,
        [Description("Array of fully qualified symbol names")] string[] symbolNames,
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

        if (symbolNames is null || symbolNames.Length == 0)
        {
            return SerializeError("No symbol names provided", "EMPTY_SYMBOL_NAMES");
        }

        if (symbolNames.Length > 50)
        {
            return SerializeError("Too many symbols requested. Maximum is 50", "SYMBOL_LIMIT_EXCEEDED");
        }

        var scope = await _scopeFactory.CreateAsync(validatedPath, cancellationToken).ConfigureAwait(false);
        await using (scope.ConfigureAwait(false))
        {
            var foundSymbols = await scope.Store.GetSymbolsByNamesAsync(scope.RepoId, symbolNames).ConfigureAwait(false);
            var files = await scope.Store.GetFilesByRepoAsync(scope.RepoId).ConfigureAwait(false);
            var fileMap = files.ToDictionary(f => f.Id);

            // Build set of qualified names that were found
            var foundQualifiedNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var symbol in foundSymbols)
            {
                var qualifiedName = symbol.ParentSymbol is not null
                    ? $"{symbol.ParentSymbol}:{symbol.Name}"
                    : symbol.Name;
                foundQualifiedNames.Add(qualifiedName);
            }

            // Group found symbols by file for efficient reading
            var symbolsByFile = foundSymbols
                .GroupBy(s => s.FileId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var results = new List<object>();

            foreach (var (fileId, fileSymbols) in symbolsByFile)
            {
                if (!fileMap.TryGetValue(fileId, out var file))
                {
                    continue;
                }

                string resolvedPath;
                try
                {
                    resolvedPath = _pathValidator.ValidatePath(
                        Path.Combine(validatedPath, file.RelativePath), validatedPath);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                foreach (var symbol in fileSymbols)
                {
                    var sourceCode = await ReadSourceCodeAsync(resolvedPath, symbol.ByteOffset, symbol.ByteLength).ConfigureAwait(false);
                    results.Add(new
                    {
                        symbol.Name,
                        symbol.Kind,
                        Parent = symbol.ParentSymbol,
                        File = file.RelativePath,
                        symbol.LineStart,
                        symbol.LineEnd,
                        symbol.Signature,
                        SourceCode = sourceCode,
                    });
                }
            }

            // Determine which requested names were not found
            var errors = symbolNames
                .Where(name => !foundQualifiedNames.Contains(name))
                .Select(name => new
                {
                    Symbol = SanitizeSymbolName(name),
                    Error = "Symbol not found",
                    Code = "SYMBOL_NOT_FOUND",
                })
                .ToList();

            var response = new
            {
                Results = results,
                Errors = errors,
            };

            return JsonSerializer.Serialize(response, SerializerOptions);
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

    private static async Task<string> ReadSourceCodeAsync(string filePath, int byteOffset, int byteLength)
    {
        var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using (stream.ConfigureAwait(false))
        {
            stream.Seek(byteOffset, SeekOrigin.Begin);
            var buffer = new byte[byteLength];
            var bytesRead = 0;
            while (bytesRead < byteLength)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(bytesRead, byteLength - bytesRead)).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                bytesRead += read;
            }

            return Encoding.UTF8.GetString(buffer, 0, bytesRead);
        }
    }

    private static async Task<string> ReadSourceCodeWithContextAsync(string filePath, int byteOffset, int byteLength, int contextLines = 5)
    {
        var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using (stream.ConfigureAwait(false))
        {
            // Find start position by scanning backward for context lines
            var startOffset = await FindContextStartAsync(stream, byteOffset, contextLines).ConfigureAwait(false);

            // Find end position by scanning forward for context lines
            var endOffset = await FindContextEndAsync(stream, byteOffset + byteLength, contextLines).ConfigureAwait(false);

            // Read the full region
            var length = endOffset - startOffset;
            stream.Seek(startOffset, SeekOrigin.Begin);
            var buffer = new byte[length];
            var bytesRead = 0;
            while (bytesRead < length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(bytesRead, length - bytesRead)).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                bytesRead += read;
            }

            return Encoding.UTF8.GetString(buffer, 0, bytesRead);
        }
    }

    private static async Task<int> FindContextStartAsync(FileStream stream, int symbolStart, int lines)
    {
        if (symbolStart == 0)
        {
            return 0;
        }

        // Read backward from symbolStart to find `lines` newline characters
        var searchStart = Math.Max(0, symbolStart - 4096);
        var searchLength = symbolStart - searchStart;
        stream.Seek(searchStart, SeekOrigin.Begin);
        var buffer = new byte[searchLength];
        _ = await stream.ReadAsync(buffer.AsMemory(0, searchLength)).ConfigureAwait(false);

        var newlineCount = 0;
        for (var i = searchLength - 1; i >= 0; i--)
        {
            if (buffer[i] == (byte)'\n')
            {
                newlineCount++;
                if (newlineCount == lines)
                {
                    return searchStart + i + 1;
                }
            }
        }

        return searchStart;
    }

    private static async Task<int> FindContextEndAsync(FileStream stream, int symbolEnd, int lines)
    {
        stream.Seek(symbolEnd, SeekOrigin.Begin);
        var buffer = new byte[4096];
        var bytesRead = await stream.ReadAsync(buffer).ConfigureAwait(false);

        var newlineCount = 0;
        for (var i = 0; i < bytesRead; i++)
        {
            if (buffer[i] == (byte)'\n')
            {
                newlineCount++;
                if (newlineCount == lines)
                {
                    return symbolEnd + i + 1;
                }
            }
        }

        return symbolEnd + bytesRead;
    }

    private static string SanitizeSymbolName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        // Allow only alphanumeric, colon, underscore, dot, hyphen
        var sb = new StringBuilder(name.Length);
        foreach (var c in name.Where(c => char.IsLetterOrDigit(c) || c is ':' or '_' or '.' or '-'))
        {
            sb.Append(c);
        }

        var result = sb.ToString();
        return result.Length > 256 ? result[..256] : result;
    }

    private static string SerializeError(string error, string code) =>
        JsonSerializer.Serialize(new { Error = error, Code = code }, SerializerOptions);
}

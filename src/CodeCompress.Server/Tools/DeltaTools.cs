using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodeCompress.Core.Models;
using CodeCompress.Core.Validation;
using CodeCompress.Server.Scoping;
using ModelContextProtocol.Server;

namespace CodeCompress.Server.Tools;

[McpServerToolType]
internal sealed partial class DeltaTools
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private static readonly HashSet<string> DefaultExcludedDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", "bin", "obj", ".vs", ".idea", "Packages", "__pycache__",
    };

    private readonly IPathValidator _pathValidator;
    private readonly IProjectScopeFactory _scopeFactory;

    public DeltaTools(IPathValidator pathValidator, IProjectScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(pathValidator);
        ArgumentNullException.ThrowIfNull(scopeFactory);

        _pathValidator = pathValidator;
        _scopeFactory = scopeFactory;
    }

    [McpServerTool(Name = "changes_since")]
    [Description("Show what changed since a named snapshot: new, modified, and deleted files with symbol-level diffs.")]
    public async Task<string> ChangesSince(
        [Description("Absolute path to the project root directory")] string path,
        [Description("Label of the snapshot to compare against")] string snapshotLabel,
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

        var sanitizedLabel = SanitizeLabel(snapshotLabel);

        var scope = await _scopeFactory.CreateAsync(validatedPath, cancellationToken).ConfigureAwait(false);
        await using (scope.ConfigureAwait(false))
        {
            var snapshot = await scope.Store.GetSnapshotByLabelAsync(scope.RepoId, sanitizedLabel).ConfigureAwait(false);

            if (snapshot is null)
            {
                var available = await scope.Store.GetSnapshotsByRepoAsync(scope.RepoId).ConfigureAwait(false);
                var labels = available.Select(s => SanitizeLabel(s.SnapshotLabel)).Where(l => l.Length > 0).ToList();

                return JsonSerializer.Serialize(
                    new
                    {
                        Error = "Snapshot not found",
                        Code = "SNAPSHOT_NOT_FOUND",
                        AvailableSnapshots = labels,
                    },
                    SerializerOptions);
            }

            var snapshotHashes = DeserializeHashes(snapshot.FileHashes);
            var snapshotSymbols = DeserializeSymbols(snapshot.SymbolsJson);

            var currentFiles = await scope.Store.GetFilesByRepoAsync(scope.RepoId).ConfigureAwait(false);
            var currentByPath = new Dictionary<string, FileRecord>(StringComparer.Ordinal);
            foreach (var file in currentFiles)
            {
                currentByPath[file.RelativePath] = file;
            }

            // Classify files
            var newFiles = new List<FileRecord>();
            var modifiedFiles = new List<FileRecord>();
            var deletedPaths = new List<string>();

            foreach (var file in currentFiles)
            {
                if (!snapshotHashes.TryGetValue(file.RelativePath, out var oldHash))
                {
                    newFiles.Add(file);
                }
                else if (!string.Equals(oldHash, file.ContentHash, StringComparison.Ordinal))
                {
                    modifiedFiles.Add(file);
                }
            }

            deletedPaths.AddRange(snapshotHashes.Keys.Where(p => !currentByPath.ContainsKey(p)));

            // Build output
            var sb = new StringBuilder();
            sb.AppendLine(CultureInfo.InvariantCulture, $"Changes since snapshot \"{sanitizedLabel}\":");
            sb.AppendLine();

            var totalAdded = 0;
            var totalModified = 0;
            var totalRemoved = 0;

            // New files
            sb.AppendLine(CultureInfo.InvariantCulture, $"New files ({newFiles.Count}):");
            foreach (var file in newFiles)
            {
                var symbols = await scope.Store.GetSymbolsByFileAsync(file.Id).ConfigureAwait(false);
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {file.RelativePath} \u2014 {symbols.Count} symbols");
                totalAdded += symbols.Count;
            }

            sb.AppendLine();

            // Modified files with symbol diffs
            sb.AppendLine(CultureInfo.InvariantCulture, $"Modified files ({modifiedFiles.Count}):");
            foreach (var file in modifiedFiles)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {file.RelativePath}");

                var currentSymbols = await scope.Store.GetSymbolsByFileAsync(file.Id).ConfigureAwait(false);
                snapshotSymbols.TryGetValue(file.RelativePath, out var oldSymbols);
                oldSymbols ??= [];

                var oldByKey = new Dictionary<string, SymbolSummary>(StringComparer.Ordinal);
                foreach (var s in oldSymbols)
                {
                    oldByKey[$"{s.Name}|{s.Kind}"] = s;
                }

                var newByKey = new Dictionary<string, Symbol>(StringComparer.Ordinal);
                foreach (var s in currentSymbols)
                {
                    newByKey[$"{s.Name}|{s.Kind}"] = s;
                }

                // Added symbols (in new but not in old)
                foreach (var (key, symbol) in newByKey)
                {
                    if (!oldByKey.ContainsKey(key))
                    {
                        sb.AppendLine(CultureInfo.InvariantCulture, $"    + {symbol.Signature}");
                        totalAdded++;
                    }
                }

                // Modified symbols (in both but signature changed)
                foreach (var (key, symbol) in newByKey)
                {
                    if (oldByKey.TryGetValue(key, out var oldSymbol) &&
                        !string.Equals(oldSymbol.Signature, symbol.Signature, StringComparison.Ordinal))
                    {
                        sb.AppendLine(CultureInfo.InvariantCulture, $"    ~ {symbol.Signature}");
                        totalModified++;
                    }
                }

                // Removed symbols (in old but not in new)
                foreach (var (key, oldSymbol) in oldByKey)
                {
                    if (!newByKey.ContainsKey(key))
                    {
                        sb.AppendLine(CultureInfo.InvariantCulture, $"    - {oldSymbol.Signature}");
                        totalRemoved++;
                    }
                }
            }

            sb.AppendLine();

            // Deleted files
            sb.AppendLine(CultureInfo.InvariantCulture, $"Deleted files ({deletedPaths.Count}):");
            foreach (var deletedPath in deletedPaths)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {deletedPath}");
            }

            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"Summary: +{totalAdded} added, ~{totalModified} modified, -{totalRemoved} removed symbols");

            return sb.ToString();
        }
    }

    [McpServerTool(Name = "file_tree")]
    [Description("Get an annotated directory tree with file counts and line counts per directory.")]
    public async Task<string> FileTree(
        [Description("Absolute path to the project root directory")] string path,
        [Description("Maximum directory depth (1-20, default 5)")] int maxDepth = 5,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        string validatedPath;
        try
        {
            validatedPath = _pathValidator.ValidatePath(path, path);
        }
        catch (ArgumentException)
        {
            return SerializeError("Path validation failed", "INVALID_PATH");
        }

        var clampedDepth = Math.Clamp(maxDepth, 1, 20);

        if (!Directory.Exists(validatedPath))
        {
            return SerializeError("Directory not found", "DIRECTORY_NOT_FOUND");
        }

        var sb = new StringBuilder();
        await Task.Run(() => BuildTree(sb, validatedPath, validatedPath, clampedDepth, 0), CancellationToken.None).ConfigureAwait(false);

        return sb.ToString();
    }

    private static void BuildTree(StringBuilder sb, string rootPath, string currentPath, int maxDepth, int currentDepth)
    {
        if (currentDepth >= maxDepth)
        {
            return;
        }

        var indent = new string(' ', currentDepth * 2);

        try
        {
            var entries = Directory.GetDirectories(currentPath)
                .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var dir in entries)
            {
                var dirName = Path.GetFileName(dir);

                if (DefaultExcludedDirs.Contains(dirName))
                {
                    continue;
                }

                var (fileCount, lineCount) = CountFilesAndLines(dir);
                sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}{dirName}/ ({fileCount:N0} files, {lineCount:N0} lines)");

                BuildTree(sb, rootPath, dir, maxDepth, currentDepth + 1);
            }

            // List files at current depth
            var files = Directory.GetFiles(currentPath)
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                var lines = CountFileLines(file);
                sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}{fileName} ({lines:N0} lines)");
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Skip directories we can't access
        }
        catch (IOException)
        {
            // Skip on I/O errors
        }
    }

    private static (int FileCount, int LineCount) CountFilesAndLines(string dirPath)
    {
        var fileCount = 0;
        var lineCount = 0;

        try
        {
            foreach (var file in Directory.EnumerateFiles(dirPath, "*", SearchOption.AllDirectories))
            {
                // Check if file is in an excluded directory
                var relativePath = Path.GetRelativePath(dirPath, file);
                if (IsInExcludedDirectory(relativePath))
                {
                    continue;
                }

                fileCount++;
                lineCount += CountFileLines(file);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Skip inaccessible dirs
        }
        catch (IOException)
        {
            // Skip on I/O errors
        }

        return (fileCount, lineCount);
    }

    private static bool IsInExcludedDirectory(string relativePath)
    {
        var span = relativePath.AsSpan();
        while (span.Length > 0)
        {
            var sepIndex = span.IndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (sepIndex < 0)
            {
                break;
            }

            var part = span[..sepIndex].ToString();
            if (DefaultExcludedDirs.Contains(part))
            {
                return true;
            }

            span = span[(sepIndex + 1)..];
        }

        return false;
    }

    private static int CountFileLines(string filePath)
    {
        try
        {
            var bytes = File.ReadAllBytes(filePath);
            if (bytes.Length == 0)
            {
                return 0;
            }

            var count = 0;
            foreach (var b in bytes)
            {
                if (b == (byte)'\n')
                {
                    count++;
                }
            }

            // Count the last line if it doesn't end with a newline
            if (bytes[^1] != (byte)'\n')
            {
                count++;
            }

            return count;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    internal static string SanitizeLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return string.Empty;
        }

        var sanitized = SafeLabelPattern().Replace(label, string.Empty);

        if (sanitized.Length > 256)
        {
            sanitized = sanitized[..256];
        }

        return sanitized.Trim();
    }

    private static Dictionary<string, string> DeserializeHashes(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static Dictionary<string, List<SymbolSummary>> DeserializeSymbols(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return new Dictionary<string, List<SymbolSummary>>(StringComparer.Ordinal);
        }

        return JsonSerializer.Deserialize<Dictionary<string, List<SymbolSummary>>>(json)
            ?? new Dictionary<string, List<SymbolSummary>>(StringComparer.Ordinal);
    }

    private static string SerializeError(string error, string code) =>
        JsonSerializer.Serialize(new { Error = error, Code = code }, SerializerOptions);

    [GeneratedRegex(@"[^a-zA-Z0-9 _.\-]")]
    private static partial Regex SafeLabelPattern();
}

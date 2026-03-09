using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using CodeCompress.Core.Models;
using CodeCompress.Core.Parsers;
using CodeCompress.Core.Storage;
using CodeCompress.Core.Validation;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Logging;

namespace CodeCompress.Core.Indexing;

public sealed partial class IndexEngine : IIndexEngine
{
    private static readonly string[] DefaultExcludeDirs =
    [
        ".git", "node_modules", "bin", "obj",
        "Packages", "build", ".vs", ".idea"
    ];

    private static readonly string[] DefaultExcludeExtensions =
    [
        ".rbxlx", ".rbxl"
    ];

    private readonly IFileHasher _fileHasher;
    private readonly IChangeTracker _changeTracker;
    private readonly ISymbolStore _symbolStore;
    private readonly IPathValidator _pathValidator;
    private readonly ILogger<IndexEngine> _logger;
    private readonly Dictionary<string, ILanguageParser> _parsersByExtension;
    private readonly Dictionary<string, ILanguageParser> _parsersByLanguageId;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to parse file: {FilePath}")]
    private partial void LogParseWarning(Exception ex, string filePath);

    public IndexEngine(
        IFileHasher fileHasher,
        IChangeTracker changeTracker,
        IEnumerable<ILanguageParser> parsers,
        ISymbolStore symbolStore,
        IPathValidator pathValidator,
        ILogger<IndexEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(fileHasher);
        ArgumentNullException.ThrowIfNull(changeTracker);
        ArgumentNullException.ThrowIfNull(parsers);
        ArgumentNullException.ThrowIfNull(symbolStore);
        ArgumentNullException.ThrowIfNull(pathValidator);
        ArgumentNullException.ThrowIfNull(logger);

        _fileHasher = fileHasher;
        _changeTracker = changeTracker;
        _symbolStore = symbolStore;
        _pathValidator = pathValidator;
        _logger = logger;

        _parsersByExtension = new Dictionary<string, ILanguageParser>(StringComparer.OrdinalIgnoreCase);
        _parsersByLanguageId = new Dictionary<string, ILanguageParser>(StringComparer.OrdinalIgnoreCase);

        foreach (var parser in parsers)
        {
            _parsersByLanguageId[parser.LanguageId] = parser;
            foreach (var ext in parser.FileExtensions)
            {
                _parsersByExtension[ext] = parser;
            }
        }
    }

    public async Task<IndexResult> IndexProjectAsync(
        string projectRoot,
        string? language = null,
        string[]? includePatterns = null,
        string[]? excludePatterns = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectRoot);
        var sw = Stopwatch.StartNew();

        // 1. Validate project root
        var canonicalRoot = _pathValidator.ValidatePath(projectRoot, projectRoot);
        if (!Directory.Exists(canonicalRoot))
        {
            throw new DirectoryNotFoundException($"Project root does not exist.");
        }

        // 2. Compute repo ID early (needed for all return paths)
        var repoId = ComputeRepoId(canonicalRoot);

        // 3. Discover source files
        var discoveredFiles = DiscoverFiles(canonicalRoot, language, includePatterns, excludePatterns);

        if (discoveredFiles.Count == 0)
        {
            return new IndexResult(repoId, 0, 0, 0, 0, sw.ElapsedMilliseconds);
        }

        // 3. Hash all discovered files
        cancellationToken.ThrowIfCancellationRequested();
        var currentAbsoluteHashes = await _fileHasher.HashFilesAsync(discoveredFiles, cancellationToken).ConfigureAwait(false);

        // Convert absolute paths to relative paths
        var currentHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var absoluteByRelative = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (absPath, hash) in currentAbsoluteHashes)
        {
            var relPath = Path.GetRelativePath(canonicalRoot, absPath);
            currentHashes[relPath] = hash;
            absoluteByRelative[relPath] = absPath;
        }

        // 5. Load stored hashes
        var storedFiles = await _symbolStore.GetFilesByRepoAsync(repoId).ConfigureAwait(false);

        var storedHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var storedFileByPath = new Dictionary<string, FileRecord>(StringComparer.OrdinalIgnoreCase);

        foreach (var f in storedFiles)
        {
            storedHashes[f.RelativePath] = f.ContentHash;
            storedFileByPath[f.RelativePath] = f;
        }

        // 5. Detect changes
        var changeSet = _changeTracker.DetectChanges(currentHashes, storedHashes);

        // 6. Early return if no changes
        if (!changeSet.HasChanges)
        {
            return new IndexResult(repoId, 0, currentHashes.Count, 0, 0, sw.ElapsedMilliseconds);
        }

        // 7. Parse new and modified files
        var filesToParse = new List<string>(changeSet.NewFiles.Count + changeSet.ModifiedFiles.Count);
        filesToParse.AddRange(changeSet.NewFiles);
        filesToParse.AddRange(changeSet.ModifiedFiles);

        var parseResults = new ConcurrentDictionary<string, ParseResult>(StringComparer.OrdinalIgnoreCase);
        var totalSymbols = 0;

        await Parallel.ForEachAsync(
            filesToParse,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = cancellationToken,
            },
            async (relPath, ct) =>
            {
                if (!absoluteByRelative.TryGetValue(relPath, out var absPath))
                {
                    return;
                }

                var ext = Path.GetExtension(absPath);
                if (!_parsersByExtension.TryGetValue(ext, out var parser))
                {
                    return;
                }

                try
                {
                    var bytes = await File.ReadAllBytesAsync(absPath, ct).ConfigureAwait(false);
                    var result = parser.Parse(relPath, bytes);
                    parseResults[relPath] = result;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
#pragma warning disable CA1031 // Catch general exception to allow other files to continue indexing
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    LogParseWarning(ex, relPath);
                }
            }).ConfigureAwait(false);

        // 8. Update store — delete removed files
        foreach (var delPath in changeSet.DeletedFiles)
        {
            if (storedFileByPath.TryGetValue(delPath, out var delFile))
            {
                await _symbolStore.DeleteSymbolsByFileAsync(delFile.Id).ConfigureAwait(false);
                await _symbolStore.DeleteDependenciesByFileAsync(delFile.Id).ConfigureAwait(false);
                await _symbolStore.DeleteFileAsync(delFile.Id).ConfigureAwait(false);
            }
        }

        // Update modified files
        foreach (var modPath in changeSet.ModifiedFiles)
        {
            if (storedFileByPath.TryGetValue(modPath, out var modFile))
            {
                await _symbolStore.DeleteSymbolsByFileAsync(modFile.Id).ConfigureAwait(false);
                await _symbolStore.DeleteDependenciesByFileAsync(modFile.Id).ConfigureAwait(false);

                var updatedFile = modFile with
                {
                    ContentHash = currentHashes[modPath],
                    IndexedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                };
                await _symbolStore.UpdateFileAsync(updatedFile).ConfigureAwait(false);

                if (parseResults.TryGetValue(modPath, out var pr))
                {
                    var symbols = ConvertSymbols(pr.Symbols, modFile.Id);
                    await _symbolStore.InsertSymbolsAsync(symbols).ConfigureAwait(false);
                    Interlocked.Add(ref totalSymbols, symbols.Count);

                    var deps = ConvertDependencies(pr.Dependencies, modFile.Id);
                    await _symbolStore.InsertDependenciesAsync(deps).ConfigureAwait(false);
                }
            }
        }

        // Insert new files
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var newFileRecords = new List<FileRecord>();

        foreach (var newPath in changeSet.NewFiles)
        {
            if (!absoluteByRelative.TryGetValue(newPath, out var absPath))
            {
                continue;
            }

            var fileInfo = new FileInfo(absPath);
            newFileRecords.Add(new FileRecord(
                0,
                repoId,
                newPath,
                currentHashes[newPath],
                fileInfo.Length,
                0,
                fileInfo.LastWriteTimeUtc.Ticks,
                now));
        }

        await _symbolStore.InsertFilesAsync(newFileRecords).ConfigureAwait(false);

        // Insert symbols for new files (need to re-query for generated IDs)
        foreach (var newPath in changeSet.NewFiles)
        {
            var newFile = await _symbolStore.GetFileByPathAsync(repoId, newPath).ConfigureAwait(false);

            if (newFile is not null && parseResults.TryGetValue(newPath, out var pr))
            {
                var symbols = ConvertSymbols(pr.Symbols, newFile.Id);
                await _symbolStore.InsertSymbolsAsync(symbols).ConfigureAwait(false);
                Interlocked.Add(ref totalSymbols, symbols.Count);

                var deps = ConvertDependencies(pr.Dependencies, newFile.Id);
                await _symbolStore.InsertDependenciesAsync(deps).ConfigureAwait(false);
            }
        }

        // 9. Update repository metadata
        var repo = new Repository(
            repoId,
            canonicalRoot,
            Path.GetFileName(canonicalRoot),
            language ?? "mixed",
            now,
            currentHashes.Count,
            totalSymbols);

        await _symbolStore.UpsertRepositoryAsync(repo).ConfigureAwait(false);

        return new IndexResult(
            repoId,
            filesToParse.Count,
            changeSet.UnchangedFiles.Count,
            changeSet.DeletedFiles.Count,
            totalSymbols,
            sw.ElapsedMilliseconds);
    }

    private List<string> DiscoverFiles(
        string canonicalRoot,
        string? language,
        string[]? includePatterns,
        string[]? excludePatterns)
    {
        // Determine which extensions to consider
        HashSet<string>? allowedExtensions = null;

        if (language is not null && _parsersByLanguageId.TryGetValue(language, out var langParser))
        {
            allowedExtensions = new HashSet<string>(langParser.FileExtensions, StringComparer.OrdinalIgnoreCase);
        }

        var excludeExtSet = new HashSet<string>(DefaultExcludeExtensions, StringComparer.OrdinalIgnoreCase);

        // Build glob matchers for include/exclude patterns
        Matcher? includeMatcher = null;
        Matcher? excludeMatcher = null;

        if (includePatterns is { Length: > 0 })
        {
            includeMatcher = new Matcher();
            foreach (var pattern in includePatterns)
            {
                includeMatcher.AddInclude(pattern);
            }
        }

        if (excludePatterns is { Length: > 0 })
        {
            excludeMatcher = new Matcher();
            foreach (var pattern in excludePatterns)
            {
                excludeMatcher.AddInclude(pattern);
            }
        }

        var results = new List<string>();
        var defaultExcludeSet = new HashSet<string>(DefaultExcludeDirs, StringComparer.OrdinalIgnoreCase);

        foreach (var absPath in Directory.EnumerateFiles(canonicalRoot, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
        }))
        {
            var relPath = Path.GetRelativePath(canonicalRoot, absPath);

            // Skip default excluded directories
            if (IsInExcludedDirectory(relPath, defaultExcludeSet))
            {
                continue;
            }

            var ext = Path.GetExtension(absPath);

            // Skip default excluded extensions
            if (excludeExtSet.Contains(ext))
            {
                continue;
            }

            // Skip extensions without a registered parser
            if (!_parsersByExtension.ContainsKey(ext))
            {
                continue;
            }

            // Apply language filter
            if (allowedExtensions is not null && !allowedExtensions.Contains(ext))
            {
                continue;
            }

            // Apply caller-supplied exclude patterns
            if (excludeMatcher is not null)
            {
                var match = excludeMatcher.Match(relPath.Replace('\\', '/'));
                if (match.HasMatches)
                {
                    continue;
                }
            }

            // Apply caller-supplied include patterns
            if (includeMatcher is not null)
            {
                var match = includeMatcher.Match(relPath.Replace('\\', '/'));
                if (!match.HasMatches)
                {
                    continue;
                }
            }

            results.Add(absPath);
        }

        return results;
    }

    private static bool IsInExcludedDirectory(string relativePath, HashSet<string> excludedDirs)
    {
        // Check each path segment against the excluded directory names
        var dir = Path.GetDirectoryName(relativePath);
        while (dir is not null && dir.Length > 0)
        {
            var segment = Path.GetFileName(dir);
            if (excludedDirs.Contains(segment))
            {
                return true;
            }

            dir = Path.GetDirectoryName(dir);
        }

        return false;
    }

    private static List<Symbol> ConvertSymbols(IReadOnlyList<SymbolInfo> infos, long fileId)
    {
        var symbols = new List<Symbol>(infos.Count);

        foreach (var s in infos)
        {
            symbols.Add(new Symbol(
                0,
                fileId,
                s.Name,
                s.Kind.ToString(),
                s.Signature,
                s.ParentSymbol,
                s.ByteOffset,
                s.ByteLength,
                s.LineStart,
                s.LineEnd,
                s.Visibility.ToString(),
                s.DocComment));
        }

        return symbols;
    }

    private static List<Dependency> ConvertDependencies(IReadOnlyList<DependencyInfo> infos, long fileId)
    {
        var deps = new List<Dependency>(infos.Count);

        foreach (var d in infos)
        {
            deps.Add(new Dependency(0, fileId, d.RequirePath, null, d.Alias));
        }

        return deps;
    }

    public static string ComputeRepoId(string canonicalRoot)
    {
        ArgumentNullException.ThrowIfNull(canonicalRoot);
        var bytes = Encoding.UTF8.GetBytes(canonicalRoot.ToUpperInvariant());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}

using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace CodeCompress.Core.Indexing;

public sealed partial class GitIgnoreFilter : IGitIgnoreFilter
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(30);

    private readonly ILogger<GitIgnoreFilter> _logger;

    [LoggerMessage(Level = LogLevel.Debug, Message = "No .git directory found in {ProjectRoot} — skipping gitignore filtering")]
    private partial void LogNoGitDirectory(string projectRoot);

    [LoggerMessage(Level = LogLevel.Debug, Message = "git binary not found — skipping gitignore filtering")]
    private partial void LogGitNotFound();

    [LoggerMessage(Level = LogLevel.Warning, Message = "git check-ignore timed out after {Timeout}s — skipping gitignore filtering")]
    private partial void LogGitTimeout(double timeout);

    [LoggerMessage(Level = LogLevel.Debug, Message = "git check-ignore identified {Count} ignored files")]
    private partial void LogIgnoredCount(int count);

    public GitIgnoreFilter(ILogger<GitIgnoreFilter> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public async Task<HashSet<string>> GetIgnoredPathsAsync(
        string projectRoot,
        IReadOnlyList<string> relativePaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectRoot);
        ArgumentNullException.ThrowIfNull(relativePaths);

        if (relativePaths.Count == 0)
        {
            return [];
        }

        // Check if this is a git repository
        var gitDir = Path.Combine(projectRoot, ".git");
        if (!Directory.Exists(gitDir) && !File.Exists(gitDir))
        {
            LogNoGitDirectory(projectRoot);
            return [];
        }

        try
        {
            return await RunGitCheckIgnoreAsync(projectRoot, relativePaths, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout from our internal CTS — not caller cancellation
            LogGitTimeout(ProcessTimeout.TotalSeconds);
            return [];
        }
        catch (OperationCanceledException)
        {
            // Caller cancelled — propagate
            throw;
        }
#pragma warning disable CA1031 // Catch general exception for graceful fallback
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
#pragma warning restore CA1031
        {
            // git not found on PATH, or I/O error
            LogGitNotFound();
            return [];
        }
    }

    private async Task<HashSet<string>> RunGitCheckIgnoreAsync(
        string projectRoot,
        IReadOnlyList<string> relativePaths,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ProcessTimeout);
        var linkedToken = timeoutCts.Token;

        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = "check-ignore --stdin -z",
            WorkingDirectory = projectRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        try
        {
            // Write all paths to stdin, null-separated
            var stdin = process.StandardInput;
            foreach (var path in relativePaths)
            {
                // Normalize to forward slashes for git
                var normalized = path.Replace('\\', '/');
                await stdin.WriteAsync(normalized).ConfigureAwait(false);
                await stdin.WriteAsync('\0').ConfigureAwait(false);
            }

            stdin.Close(); // Signal EOF to git

            // Read stdout — git returns ignored paths, null-separated
            var stdout = await process.StandardOutput.ReadToEndAsync(linkedToken).ConfigureAwait(false);

            await process.WaitForExitAsync(linkedToken).ConfigureAwait(false);

            // Parse null-separated output
            var ignoredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (stdout.Length > 0)
            {
                var parts = stdout.Split('\0', StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    // Normalize back to OS-native separator for matching
                    var normalized = part.Replace('/', Path.DirectorySeparatorChar);
                    ignoredPaths.Add(normalized);
                }
            }

            LogIgnoredCount(ignoredPaths.Count);
            return ignoredPaths;
        }
        finally
        {
            // Ensure the git process is killed on timeout or cancellation
            // Process.Dispose() only releases the handle — it does not kill the child process
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Process already exited between HasExited check and Kill — safe to ignore
                }
            }
        }
    }
}

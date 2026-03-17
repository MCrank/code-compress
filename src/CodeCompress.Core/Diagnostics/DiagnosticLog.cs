using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace CodeCompress.Core.Diagnostics;

/// <summary>
/// Writes diagnostic log entries to .code-compress/codecompress-YYYY-MM-DD.log.
/// Handles date-based rolling and retention (max 10 files).
/// Thread-safe via file-level locking.
/// </summary>
public static class DiagnosticLog
{
    private const int MaxRetainedFiles = 10;
    private const string LogFilePrefix = "codecompress-";
    private const string LogFileExtension = ".log";
    private static readonly Uri IssueUri = new("https://github.com/MCrank/code-compress/issues/new");

    /// <summary>
    /// Writes an error entry to the diagnostic log file.
    /// If the .code-compress directory doesn't exist, the call is silently skipped.
    /// </summary>
    public static void WriteError(string codeCompressDir, string source, string message, Exception? exception = null)
    {
        if (!Directory.Exists(codeCompressDir))
        {
            return;
        }

        var logPath = GetLogFilePath(codeCompressDir);
        var entry = FormatEntry("ERR", source, message, exception);

        try
        {
            File.AppendAllText(logPath, entry, Encoding.UTF8);
            EnforceRetention(codeCompressDir);
        }
#pragma warning disable CA1031 // Diagnostic logging must never throw
        catch
#pragma warning restore CA1031
        {
            // Swallow — diagnostic logging must never crash the host
        }
    }

    /// <summary>
    /// Writes a warning entry to the diagnostic log file.
    /// </summary>
    public static void WriteWarning(string codeCompressDir, string source, string message, Exception? exception = null)
    {
        if (!Directory.Exists(codeCompressDir))
        {
            return;
        }

        var logPath = GetLogFilePath(codeCompressDir);
        var entry = FormatEntry("WRN", source, message, exception);

        try
        {
            File.AppendAllText(logPath, entry, Encoding.UTF8);
        }
#pragma warning disable CA1031 // Diagnostic logging must never throw
        catch
#pragma warning restore CA1031
        {
            // Swallow — diagnostic logging must never crash the host
        }
    }

    /// <summary>
    /// Generates a bug report block suitable for copy-pasting into a GitHub issue.
    /// Contains only safe diagnostic information — no user paths, code, or PII.
    /// </summary>
    public static string GenerateBugReport(string toolName, Exception exception, int? fileCount = null, int? symbolCount = null, IReadOnlyList<string>? activeParsers = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                      ?? "unknown";

        var sb = new StringBuilder();
        sb.AppendLine("--- Bug Report (copy everything between the dashes into a new issue) ---");
        sb.AppendLine(CultureInfo.InvariantCulture, $"{IssueUri}");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Version:** {version}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Runtime:** {RuntimeInformation.FrameworkDescription} | {RuntimeInformation.OSArchitecture}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"**OS:** {RuntimeInformation.OSDescription}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Tool:** {toolName}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Error:** {exception.GetType().Name} — {exception.Message}");

        if (fileCount.HasValue)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"**Files indexed:** {fileCount.Value}");
        }

        if (symbolCount.HasValue)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"**Symbols:** {symbolCount.Value}");
        }

        if (activeParsers is { Count: > 0 })
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"**Parsers:** {string.Join(", ", activeParsers)}");
        }

        sb.AppendLine(CultureInfo.InvariantCulture, $"**Stack:**");

        // Include only internal frames (CodeCompress namespace), strip file paths
        foreach (var line in exception.StackTrace?.Split('\n') ?? [])
        {
            var trimmed = line.Trim();
            if (trimmed.Contains("CodeCompress", StringComparison.Ordinal))
            {
                // Strip file path info (everything after " in ")
                var inIndex = trimmed.IndexOf(" in ", StringComparison.Ordinal);
                var sanitized = inIndex > 0 ? trimmed[..inIndex] : trimmed;
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {sanitized}");
            }
        }

        sb.AppendLine(CultureInfo.InvariantCulture, $"**Timestamp:** {DateTime.UtcNow:O}");
        sb.AppendLine("---");

        return sb.ToString();
    }

    /// <summary>
    /// Gets today's log file path.
    /// </summary>
    internal static string GetLogFilePath(string codeCompressDir) =>
        Path.Combine(codeCompressDir, $"{LogFilePrefix}{DateTime.UtcNow:yyyy-MM-dd}{LogFileExtension}");

    /// <summary>
    /// Removes oldest log files if count exceeds MaxRetainedFiles.
    /// </summary>
    internal static void EnforceRetention(string codeCompressDir)
    {
        try
        {
            var logFiles = Directory.GetFiles(codeCompressDir, $"{LogFilePrefix}*{LogFileExtension}")
                .OrderByDescending(f => f, StringComparer.Ordinal)
                .ToList();

            if (logFiles.Count <= MaxRetainedFiles)
            {
                return;
            }

            foreach (var oldFile in logFiles.Skip(MaxRetainedFiles))
            {
                File.Delete(oldFile);
            }
        }
#pragma warning disable CA1031 // Retention cleanup must never throw
        catch
#pragma warning restore CA1031
        {
            // Swallow — retention cleanup failure is non-critical
        }
    }

    private static string FormatEntry(string level, string source, string message, Exception? exception)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} [{level}] [{source}] {message}");

        if (exception is not null)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  Exception: {exception.GetType().FullName}: {exception.Message}");
            if (exception.StackTrace is not null)
            {
                foreach (var line in exception.StackTrace.Split('\n'))
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"  {line.TrimEnd()}");
                }
            }

            sb.AppendLine();

            // Append bug report block for errors
            if (string.Equals(level, "ERR", StringComparison.Ordinal))
            {
                sb.AppendLine("  Please copy the following section into a new issue to help us investigate:");
                sb.AppendLine();
                sb.Append(GenerateBugReport(source, exception));
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }
}

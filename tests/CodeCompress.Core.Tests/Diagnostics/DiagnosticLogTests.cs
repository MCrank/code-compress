using CodeCompress.Core.Diagnostics;

namespace CodeCompress.Core.Tests.Diagnostics;

internal sealed class DiagnosticLogTests
{
    private string _tempDir = null!;

    [Before(Test)]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"DiagnosticLogTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [After(Test)]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Test]
    public async Task WriteErrorCreatesLogFile()
    {
        DiagnosticLog.WriteError(_tempDir, "TestSource", "Test error message");

        var logFiles = Directory.GetFiles(_tempDir, "codecompress-*.log");
        await Assert.That(logFiles).Count().IsEqualTo(1);
    }

    [Test]
    public async Task WriteErrorIncludesMessageInLogFile()
    {
        DiagnosticLog.WriteError(_tempDir, "TestSource", "Something went wrong");

        var logFile = Directory.GetFiles(_tempDir, "codecompress-*.log").Single();
        var content = await File.ReadAllTextAsync(logFile).ConfigureAwait(false);

        await Assert.That(content).Contains("[ERR]");
        await Assert.That(content).Contains("[TestSource]");
        await Assert.That(content).Contains("Something went wrong");
    }

    [Test]
    public async Task WriteErrorWithExceptionIncludesStackTrace()
    {
        var exception = new InvalidOperationException("Test exception");

        DiagnosticLog.WriteError(_tempDir, "TestSource", "Error occurred", exception);

        var logFile = Directory.GetFiles(_tempDir, "codecompress-*.log").Single();
        var content = await File.ReadAllTextAsync(logFile).ConfigureAwait(false);

        await Assert.That(content).Contains("InvalidOperationException");
        await Assert.That(content).Contains("Test exception");
    }

    [Test]
    public async Task WriteErrorWithExceptionIncludesBugReport()
    {
        var exception = new InvalidOperationException("Test exception");

        DiagnosticLog.WriteError(_tempDir, "TestSource", "Error occurred", exception);

        var logFile = Directory.GetFiles(_tempDir, "codecompress-*.log").Single();
        var content = await File.ReadAllTextAsync(logFile).ConfigureAwait(false);

        await Assert.That(content).Contains("Bug Report");
        await Assert.That(content).Contains("issues/new");
    }

    [Test]
    public async Task WriteWarningDoesNotIncludeBugReport()
    {
        var exception = new InvalidOperationException("Test warning");

        DiagnosticLog.WriteWarning(_tempDir, "TestSource", "Warning occurred", exception);

        var logFile = Directory.GetFiles(_tempDir, "codecompress-*.log").Single();
        var content = await File.ReadAllTextAsync(logFile).ConfigureAwait(false);

        await Assert.That(content).Contains("[WRN]");
        await Assert.That(content).DoesNotContain("Bug Report");
    }

    [Test]
    public async Task WriteErrorSkipsIfDirectoryDoesNotExist()
    {
        var nonExistent = Path.Combine(_tempDir, "does-not-exist");

        DiagnosticLog.WriteError(nonExistent, "TestSource", "Should not crash");

        var logFiles = Directory.GetFiles(_tempDir, "codecompress-*.log");
        await Assert.That(logFiles).Count().IsEqualTo(0);
    }

    [Test]
    public async Task EnforceRetentionKeepsOnlyTenFiles()
    {
        // Create 12 log files with different dates
        for (var i = 1; i <= 12; i++)
        {
            var fileName = $"codecompress-2026-01-{i:D2}.log";
            await File.WriteAllTextAsync(Path.Combine(_tempDir, fileName), "test").ConfigureAwait(false);
        }

        DiagnosticLog.EnforceRetention(_tempDir);

        var remaining = Directory.GetFiles(_tempDir, "codecompress-*.log");
        await Assert.That(remaining).Count().IsEqualTo(10);
    }

    [Test]
    public async Task EnforceRetentionDeletesOldestFiles()
    {
        for (var i = 1; i <= 12; i++)
        {
            var fileName = $"codecompress-2026-01-{i:D2}.log";
            await File.WriteAllTextAsync(Path.Combine(_tempDir, fileName), "test").ConfigureAwait(false);
        }

        DiagnosticLog.EnforceRetention(_tempDir);

        // Oldest two (01, 02) should be deleted
        await Assert.That(File.Exists(Path.Combine(_tempDir, "codecompress-2026-01-01.log"))).IsFalse();
        await Assert.That(File.Exists(Path.Combine(_tempDir, "codecompress-2026-01-02.log"))).IsFalse();
        // Newest should remain
        await Assert.That(File.Exists(Path.Combine(_tempDir, "codecompress-2026-01-12.log"))).IsTrue();
    }

    [Test]
    public async Task GenerateBugReportContainsRequiredFields()
    {
        var exception = new InvalidOperationException("Test error");
        var report = DiagnosticLog.GenerateBugReport("index_project", exception, fileCount: 147, symbolCount: 1200, activeParsers: ["csharp", "json-config"]);

        await Assert.That(report).Contains("**Version:**");
        await Assert.That(report).Contains("**Runtime:**");
        await Assert.That(report).Contains("**OS:**");
        await Assert.That(report).Contains("**Tool:** index_project");
        await Assert.That(report).Contains("**Error:** InvalidOperationException");
        await Assert.That(report).Contains("**Files indexed:** 147");
        await Assert.That(report).Contains("**Symbols:** 1200");
        await Assert.That(report).Contains("**Parsers:** csharp, json-config");
        await Assert.That(report).Contains("**Timestamp:**");
    }

    [Test]
    public async Task GenerateBugReportStripsFilePathsFromStackTrace()
    {
        Exception? caught = null;
        try
        {
            throw new InvalidOperationException("Test error");
        }
        catch (InvalidOperationException ex)
        {
            caught = ex;
        }

        var report = DiagnosticLog.GenerateBugReport("get_symbol", caught!);

        // Stack trace lines should not contain " in " file path references
        var stackLines = report.Split('\n')
            .Where(line => line.TrimStart().StartsWith("at ", StringComparison.Ordinal))
            .ToList();

        foreach (var line in stackLines)
        {
            await Assert.That(line).DoesNotContain(" in ");
        }
    }

    [Test]
    public async Task GenerateBugReportIncludesBugReportBoundaryMarkers()
    {
        var exception = new InvalidOperationException("Test");
        var report = DiagnosticLog.GenerateBugReport("test_tool", exception);

        await Assert.That(report).Contains("--- Bug Report");
        await Assert.That(report).Contains("---");
        await Assert.That(report).Contains("issues/new");
    }

    [Test]
    public async Task LogFilePathUsesTodaysDate()
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var logPath = DiagnosticLog.GetLogFilePath(_tempDir);

        await Assert.That(logPath).Contains($"codecompress-{today}.log");
    }
}

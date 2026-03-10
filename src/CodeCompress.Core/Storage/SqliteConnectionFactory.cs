using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace CodeCompress.Core.Storage;

public sealed class SqliteConnectionFactory : IConnectionFactory
{
    internal const string DbDirectoryName = ".code-compress";
    internal const string DbFileName = "index.db";

    private const string ReadmeContent =
        """
        # .code-compress

        This directory contains the [CodeCompress](https://github.com/MCrank/code-compress) index database.

        CodeCompress is an MCP server that indexes codebases and provides AI agents with compressed,
        surgical access to code symbols — reducing token consumption by 80-90%.

        ## What's in here?

        - **index.db** — SQLite database containing parsed symbols, file metadata, and FTS indexes.
        - **index.db-wal / index.db-shm** — SQLite WAL (write-ahead log) files. Safe to delete when
          the database is not in use; they will be recreated automatically.

        ## Can I commit this?

        Yes. Committing `index.db` lets other developers (and CI) skip the initial full index.
        The index is updated incrementally — only changed files are re-parsed.

        Add the WAL files to `.gitignore`:
        ```
        .code-compress/index.db-wal
        .code-compress/index.db-shm
        ```
        """;

    public async Task<SqliteConnection> CreateConnectionAsync(string projectRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRootPath);

        var fullPath = Path.GetFullPath(projectRootPath);
        if (!Path.IsPathRooted(fullPath))
        {
            throw new ArgumentException("Project root path must be an absolute path.", nameof(projectRootPath));
        }

        if (projectRootPath.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Project root path must not contain path traversal.", nameof(projectRootPath));
        }

        var dbDirectory = Path.Combine(fullPath, DbDirectoryName);
        Directory.CreateDirectory(dbDirectory);

        EnsureReadmeExists(dbDirectory);

        var dbPath = Path.Combine(dbDirectory, DbFileName);
        var connection = new SqliteConnection($"Data Source={dbPath};Foreign Keys=True");
        await connection.OpenAsync().ConfigureAwait(false);

        await ExecutePragmaAsync(connection, "PRAGMA journal_mode=WAL;").ConfigureAwait(false);
        await ExecutePragmaAsync(connection, "PRAGMA synchronous=NORMAL;").ConfigureAwait(false);

        await Migrations.ApplyAsync(connection).ConfigureAwait(false);

        return connection;
    }

    internal static string ComputeRepoHash(string fullPath)
    {
        var normalized = fullPath
            .Replace('\\', '/')
            .TrimEnd('/');

        if (OperatingSystem.IsWindows())
        {
            normalized = normalized.ToUpperInvariant();
        }

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexStringLower(hashBytes);
    }

    private static void EnsureReadmeExists(string dbDirectory)
    {
        var readmePath = Path.Combine(dbDirectory, "README.md");
        if (!File.Exists(readmePath))
        {
            File.WriteAllText(readmePath, ReadmeContent);
        }
    }

    private static async Task ExecutePragmaAsync(SqliteConnection connection, string pragma)
    {
        using var command = connection.CreateCommand();
#pragma warning disable CA2100 // PRAGMA strings are static literals, not user input
        command.CommandText = pragma;
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}

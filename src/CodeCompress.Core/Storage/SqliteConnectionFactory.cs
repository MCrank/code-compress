using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace CodeCompress.Core.Storage;

public sealed class SqliteConnectionFactory : IConnectionFactory
{
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

        var repoHash = ComputeRepoHash(fullPath);

        var dbDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codecompress");
        Directory.CreateDirectory(dbDirectory);

        var dbPath = Path.Combine(dbDirectory, $"{repoHash}.db");
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

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexStringLower(hashBytes);
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

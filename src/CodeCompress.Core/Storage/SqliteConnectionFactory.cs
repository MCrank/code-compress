using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace CodeCompress.Core.Storage;

public sealed class SqliteConnectionFactory : IConnectionFactory
{
    internal const string DbDirectoryName = ".code-compress";
    internal const string DbFileName = "index.db";

    public static string GlobalCodeCompressDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        DbDirectoryName);

    public async Task<SqliteConnection> CreateConnectionAsync(string projectRootPath)
    {
        // projectRootPath is retained for interface compatibility; DB is always global.
        _ = projectRootPath;

        Directory.CreateDirectory(GlobalCodeCompressDir);

        var dbPath = Path.Combine(GlobalCodeCompressDir, DbFileName);
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

    private static async Task ExecutePragmaAsync(SqliteConnection connection, string pragma)
    {
        using var command = connection.CreateCommand();
#pragma warning disable CA2100 // PRAGMA strings are static literals, not user input
        command.CommandText = pragma;
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}

using CodeCompress.Core.Models;
using CodeCompress.Core.Storage;

namespace CodeCompress.Core.Registry;

internal sealed class RegistryService : IRegistryService
{
    private readonly IConnectionFactory _connectionFactory;

    public RegistryService(IConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<RepositoryRecord>> ListAsync()
    {
        var connection = await _connectionFactory.CreateConnectionAsync(
            SqliteConnectionFactory.GlobalCodeCompressDir).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var store = new SqliteSymbolStore(connection);
            var repos = await store.GetAllRepositoriesAsync().ConfigureAwait(false);
            return [.. repos.Select(MapToRecord)];
        }
    }

    public async Task UpdateStatsAsync(string projectRoot, int fileCount, int symbolCount, DateTimeOffset lastIndexed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);

        var repoId = SqliteConnectionFactory.ComputeRepoHash(Path.GetFullPath(projectRoot));
        var connection = await _connectionFactory.CreateConnectionAsync(
            SqliteConnectionFactory.GlobalCodeCompressDir).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            using var cmd = connection.CreateCommand();
#pragma warning disable CA2100 // SQL is a static literal, not user input
            cmd.CommandText =
                """
                UPDATE repositories
                SET file_count = @fileCount, symbol_count = @symbolCount, last_indexed = @lastIndexed
                WHERE id = @id
                """;
#pragma warning restore CA2100
            cmd.Parameters.AddWithValue("@fileCount", fileCount);
            cmd.Parameters.AddWithValue("@symbolCount", symbolCount);
            cmd.Parameters.AddWithValue("@lastIndexed", lastIndexed.ToUnixTimeSeconds());
            cmd.Parameters.AddWithValue("@id", repoId);
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    public async Task DeregisterAsync(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);

        var repoId = SqliteConnectionFactory.ComputeRepoHash(Path.GetFullPath(projectRoot));
        var connection = await _connectionFactory.CreateConnectionAsync(
            SqliteConnectionFactory.GlobalCodeCompressDir).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var store = new SqliteSymbolStore(connection);
            var files = await store.GetFilesByRepoAsync(repoId).ConfigureAwait(false);

            foreach (var file in files)
            {
                await store.DeleteSymbolsByFileAsync(file.Id).ConfigureAwait(false);
                await store.DeleteDependenciesByFileAsync(file.Id).ConfigureAwait(false);
                await store.DeleteFileContentAsync(file.RelativePath).ConfigureAwait(false);
            }

            await store.DeleteRepositoryAsync(repoId).ConfigureAwait(false);
        }
    }

    private static RepositoryRecord MapToRecord(Repository repo) =>
        new(
            repo.Id,
            SanitizePath(repo.RootPath),
            SanitizePath(Path.GetFileName(repo.RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))),
            repo.FileCount,
            repo.SymbolCount,
            DateTimeOffset.FromUnixTimeSeconds(repo.LastIndexed),
            null);

    private static string SanitizePath(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var chars = value.AsSpan();
        var result = new System.Text.StringBuilder(chars.Length);
        foreach (var ch in chars)
        {
            if (ch >= 0x20 && ch != 0x7F && !char.IsControl(ch))
            {
                result.Append(ch);
            }
        }

        return result.ToString();
    }
}

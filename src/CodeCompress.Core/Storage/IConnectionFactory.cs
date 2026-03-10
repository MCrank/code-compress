using Microsoft.Data.Sqlite;

namespace CodeCompress.Core.Storage;

public interface IConnectionFactory
{
    public Task<SqliteConnection> CreateConnectionAsync(string projectRootPath);
}

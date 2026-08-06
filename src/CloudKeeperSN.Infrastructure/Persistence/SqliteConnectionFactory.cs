using Microsoft.Data.Sqlite;

namespace CloudKeeperSN.Infrastructure.Persistence;

public sealed class SqliteConnectionFactory(SqliteOptions options)
{
    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(options.DatabasePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var connection = new SqliteConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}


using CloudKeeperSN.Application.Persistence;

namespace CloudKeeperSN.Infrastructure.Persistence;

public sealed class SqliteApplicationSettingRepository(
    IApplicationDatabase database,
    SqliteConnectionFactory connectionFactory) : IApplicationSettingRepository
{
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken);
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM application_settings WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken);
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO application_settings(key, value, updated_at_utc) VALUES ($key, $value, $updatedAt)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_at_utc = excluded.updated_at_utc
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}


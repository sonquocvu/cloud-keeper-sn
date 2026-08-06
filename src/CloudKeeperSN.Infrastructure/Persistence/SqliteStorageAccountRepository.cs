using CloudKeeperSN.Application.Persistence;
using CloudKeeperSN.Domain.Storage;

namespace CloudKeeperSN.Infrastructure.Persistence;

public sealed class SqliteStorageAccountRepository(
    IApplicationDatabase database,
    SqliteConnectionFactory connectionFactory) : IStorageAccountRepository
{
    public async Task UpsertAsync(StorageAccount account, CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken);
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO connected_accounts(id, provider_id, provider_account_id, display_name, is_connected, last_connected_at_utc)
            VALUES ($id, $providerId, $providerAccountId, $displayName, $isConnected, $lastConnectedAt)
            ON CONFLICT(id) DO UPDATE SET
                provider_id = excluded.provider_id,
                provider_account_id = excluded.provider_account_id,
                display_name = excluded.display_name,
                is_connected = excluded.is_connected,
                last_connected_at_utc = excluded.last_connected_at_utc
            """;
        command.Parameters.AddWithValue("$id", account.Id);
        command.Parameters.AddWithValue("$providerId", account.ProviderId);
        command.Parameters.AddWithValue("$providerAccountId", account.ProviderAccountId);
        command.Parameters.AddWithValue("$displayName", account.DisplayName);
        command.Parameters.AddWithValue("$isConnected", account.IsConnected);
        command.Parameters.AddWithValue("$lastConnectedAt", DbValue(account.LastConnectedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StorageAccount>> GetAllAsync(CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken);
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, provider_id, provider_account_id, display_name, is_connected, last_connected_at_utc FROM connected_accounts ORDER BY provider_id";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var accounts = new List<StorageAccount>();
        while (await reader.ReadAsync(cancellationToken))
        {
            accounts.Add(new StorageAccount(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetBoolean(4),
                reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5))));
        }
        return accounts;
    }

    public async Task RemoveAsync(string id, CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken);
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM connected_accounts WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static object DbValue(DateTimeOffset? value) => value is { } date ? date.ToString("O") : DBNull.Value;
}

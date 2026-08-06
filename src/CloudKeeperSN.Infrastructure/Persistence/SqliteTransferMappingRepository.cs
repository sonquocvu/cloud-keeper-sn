using CloudKeeperSN.Application.Persistence;
using CloudKeeperSN.Domain.Backup;

namespace CloudKeeperSN.Infrastructure.Persistence;

public sealed class SqliteTransferMappingRepository(
    IApplicationDatabase database,
    SqliteConnectionFactory connectionFactory) : ITransferMappingRepository
{
    public async Task<SourceDestinationMapping?> FindAsync(
        string sourceProviderAccountId,
        string sourceItemId,
        string destinationProviderAccountId,
        CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken);
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT destination_parent_item_id, destination_name, destination_item_id, source_fingerprint, updated_at_utc
            FROM source_destination_mappings
            WHERE source_provider_account_id = $sourceAccount AND source_item_id = $sourceItem
              AND destination_provider_account_id = $destinationAccount
            """;
        command.Parameters.AddWithValue("$sourceAccount", sourceProviderAccountId);
        command.Parameters.AddWithValue("$sourceItem", sourceItemId);
        command.Parameters.AddWithValue("$destinationAccount", destinationProviderAccountId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new SourceDestinationMapping(
            sourceProviderAccountId,
            sourceItemId,
            destinationProviderAccountId,
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3),
            DateTimeOffset.Parse(reader.GetString(4)));
    }

    public async Task UpsertAsync(SourceDestinationMapping mapping, CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken);
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO source_destination_mappings(
                source_provider_account_id, source_item_id, destination_provider_account_id,
                destination_parent_item_id, destination_name, destination_item_id, source_fingerprint, updated_at_utc)
            VALUES ($sourceAccount, $sourceItem, $destinationAccount, $destinationParent, $destinationName, $destinationItem, $fingerprint, $updatedAt)
            ON CONFLICT(source_provider_account_id, source_item_id, destination_provider_account_id) DO UPDATE SET
                destination_parent_item_id = excluded.destination_parent_item_id,
                destination_name = excluded.destination_name,
                destination_item_id = excluded.destination_item_id,
                source_fingerprint = excluded.source_fingerprint,
                updated_at_utc = excluded.updated_at_utc
            """;
        command.Parameters.AddWithValue("$sourceAccount", mapping.SourceProviderAccountId);
        command.Parameters.AddWithValue("$sourceItem", mapping.SourceItemId);
        command.Parameters.AddWithValue("$destinationAccount", mapping.DestinationProviderAccountId);
        command.Parameters.AddWithValue("$destinationParent", mapping.DestinationParentItemId);
        command.Parameters.AddWithValue("$destinationName", mapping.DestinationName);
        command.Parameters.AddWithValue("$destinationItem", mapping.DestinationItemId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$fingerprint", mapping.SourceFingerprint);
        command.Parameters.AddWithValue("$updatedAt", mapping.UpdatedAtUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}


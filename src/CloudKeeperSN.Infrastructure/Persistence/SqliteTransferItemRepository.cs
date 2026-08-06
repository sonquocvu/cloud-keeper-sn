using CloudKeeperSN.Application.Persistence;
using CloudKeeperSN.Domain.Transfers;
using Microsoft.Data.Sqlite;

namespace CloudKeeperSN.Infrastructure.Persistence;

public sealed class SqliteTransferItemRepository(
    IApplicationDatabase database,
    SqliteConnectionFactory connectionFactory) : ITransferItemRepository
{
    public async Task UpsertAsync(TransferItem item, CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken);
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO transfer_items(
                id, run_id, source_provider_account_id, source_item_id, source_parent_item_id, original_name,
                normalized_destination_name, relative_source_path, destination_item_id, mime_type, file_size,
                source_created_at_utc, source_modified_at_utc, source_checksum_algorithm, source_checksum,
                state, verification_level, last_error_category, retry_count, next_retry_at_utc, updated_at_utc)
            VALUES ($id, $runId, $sourceAccount, $sourceItem, $sourceParent, $originalName,
                $destinationName, $relativePath, $destinationItem, $mimeType, $fileSize,
                $createdAt, $modifiedAt, $checksumAlgorithm, $checksum,
                $state, $verification, $error, $retryCount, $nextRetryAt, $updatedAt)
            ON CONFLICT(id) DO UPDATE SET
                destination_item_id = excluded.destination_item_id,
                state = excluded.state,
                verification_level = excluded.verification_level,
                last_error_category = excluded.last_error_category,
                retry_count = excluded.retry_count,
                next_retry_at_utc = excluded.next_retry_at_utc,
                updated_at_utc = excluded.updated_at_utc
            """;
        AddParameters(command, item);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<TransferItem?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken);
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM transfer_items WHERE id = $id";
        command.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task<IReadOnlyList<TransferItem>> GetRecoverableAsync(CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken);
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM transfer_items WHERE state IN ('Waiting', 'Paused', 'RetryPending', 'Failed') ORDER BY updated_at_utc";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<TransferItem>();
        while (await reader.ReadAsync(cancellationToken)) items.Add(Read(reader));
        return items;
    }

    public async Task<int> RecoverInterruptedAsync(DateTimeOffset recoveredAtUtc, CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken);
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE transfer_items
            SET state = 'RetryPending', next_retry_at_utc = $recoveredAt, updated_at_utc = $recoveredAt
            WHERE state IN ('Downloading', 'Uploading', 'Verifying')
            """;
        command.Parameters.AddWithValue("$recoveredAt", recoveredAtUtc.ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string Columns = """
        id, run_id, source_provider_account_id, source_item_id, source_parent_item_id, original_name,
        normalized_destination_name, relative_source_path, destination_item_id, mime_type, file_size,
        source_created_at_utc, source_modified_at_utc, source_checksum_algorithm, source_checksum,
        state, verification_level, last_error_category, retry_count, next_retry_at_utc
        """;

    private static void AddParameters(SqliteCommand command, TransferItem item)
    {
        command.Parameters.AddWithValue("$id", item.Id.ToString());
        command.Parameters.AddWithValue("$runId", item.RunId.ToString());
        command.Parameters.AddWithValue("$sourceAccount", item.SourceProviderAccountId);
        command.Parameters.AddWithValue("$sourceItem", item.SourceItemId);
        command.Parameters.AddWithValue("$sourceParent", DbValue(item.SourceParentItemId));
        command.Parameters.AddWithValue("$originalName", item.OriginalName);
        command.Parameters.AddWithValue("$destinationName", item.NormalizedDestinationName);
        command.Parameters.AddWithValue("$relativePath", item.RelativeSourcePath);
        command.Parameters.AddWithValue("$destinationItem", DbValue(item.DestinationItemId));
        command.Parameters.AddWithValue("$mimeType", DbValue(item.MimeType));
        command.Parameters.AddWithValue("$fileSize", item.FileSize ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", DbValue(item.SourceCreatedAtUtc));
        command.Parameters.AddWithValue("$modifiedAt", DbValue(item.SourceModifiedAtUtc));
        command.Parameters.AddWithValue("$checksumAlgorithm", DbValue(item.SourceChecksumAlgorithm));
        command.Parameters.AddWithValue("$checksum", DbValue(item.SourceChecksum));
        command.Parameters.AddWithValue("$state", item.State.ToString());
        command.Parameters.AddWithValue("$verification", item.Verification?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$error", item.LastErrorCategory?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$retryCount", item.RetryCount);
        command.Parameters.AddWithValue("$nextRetryAt", DbValue(item.NextRetryAtUtc));
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
    }

    private static TransferItem Read(SqliteDataReader reader) => new()
    {
        Id = Guid.Parse(reader.GetString(0)),
        RunId = Guid.Parse(reader.GetString(1)),
        SourceProviderAccountId = reader.GetString(2),
        SourceItemId = reader.GetString(3),
        SourceParentItemId = NullableString(reader, 4),
        OriginalName = reader.GetString(5),
        NormalizedDestinationName = reader.GetString(6),
        RelativeSourcePath = reader.GetString(7),
        DestinationItemId = NullableString(reader, 8),
        MimeType = NullableString(reader, 9),
        FileSize = reader.IsDBNull(10) ? null : reader.GetInt64(10),
        SourceCreatedAtUtc = NullableDate(reader, 11),
        SourceModifiedAtUtc = NullableDate(reader, 12),
        SourceChecksumAlgorithm = NullableString(reader, 13),
        SourceChecksum = NullableString(reader, 14),
        State = Enum.Parse<TransferState>(reader.GetString(15)),
        Verification = reader.IsDBNull(16) ? null : Enum.Parse<VerificationLevel>(reader.GetString(16)),
        LastErrorCategory = reader.IsDBNull(17) ? null : Enum.Parse<TransferErrorCategory>(reader.GetString(17)),
        RetryCount = reader.GetInt32(18),
        NextRetryAtUtc = NullableDate(reader, 19)
    };

    private static string? NullableString(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
    private static DateTimeOffset? NullableDate(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : DateTimeOffset.Parse(reader.GetString(index));
    private static object DbValue(object? value) => value switch
    {
        null => DBNull.Value,
        DateTimeOffset date => date.ToString("O"),
        _ => value
    };
}


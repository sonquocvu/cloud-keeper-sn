using CloudKeeperSN.Application.Persistence;
using CloudKeeperSN.Domain.Scanning;
using Microsoft.Data.Sqlite;

namespace CloudKeeperSN.Infrastructure.Persistence;

public sealed class SqliteDriveInventoryRepository(
    IApplicationDatabase database,
    SqliteConnectionFactory connectionFactory) : IDriveInventoryRepository
{
    private const int HierarchyBatchSize = 500;

    public async Task RecoverInterruptedAsync(DateTimeOffset interruptedAtUtc, CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken);
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE drive_scan_runs
            SET status = $status, completed_at_utc = $completed, failure_category = 'ApplicationShutdown', is_complete = 0
            WHERE status = $scanning AND is_complete = 0;
            """;
        command.Parameters.AddWithValue("$status", DriveInventoryRunStatus.Interrupted.ToString());
        command.Parameters.AddWithValue("$completed", interruptedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$scanning", DriveInventoryRunStatus.Scanning.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task BeginAsync(DriveInventoryRun run, CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken);
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO drive_scan_runs(scan_id, provider_id, provider_account_id, started_at_utc, status, is_complete)
            VALUES($scan, $provider, $account, $started, $status, 0);
            """;
        command.Parameters.AddWithValue("$scan", run.ScanId.ToString());
        command.Parameters.AddWithValue("$provider", run.ProviderId);
        command.Parameters.AddWithValue("$account", run.ProviderAccountId);
        command.Parameters.AddWithValue("$started", run.StartedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$status", DriveInventoryRunStatus.Scanning.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AppendBatchAsync(Guid scanId, IReadOnlyList<DriveInventoryItem> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0) return;
        await database.InitializeAsync(cancellationToken);
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO drive_scan_items(scan_id, file_id, name, parent_id, display_path, mime_type, item_kind, location,
                    file_size, created_at_utc, modified_at_utc, md5_checksum, file_extension, shortcut_target_id,
                    shortcut_target_mime_type, is_shared, is_owned_by_user, backup_eligible, skip_reason)
                VALUES($scan, $file, $name, $parent, $path, $mime, $kind, $location, $size, $created, $modified,
                    $md5, $extension, $target, $targetMime, $shared, $owned, $eligible, $reason);
                """;
            AddItemParameters(command, scanId, item);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpdateHierarchyAsync(Guid scanId, DriveHierarchyResult hierarchy, CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken);
        foreach (var batch in hierarchy.Paths.Chunk(HierarchyBatchSize))
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            foreach (var entry in batch)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = "UPDATE drive_scan_items SET display_path = $path, location = $location WHERE scan_id = $scan AND file_id = $file";
                command.Parameters.AddWithValue("$path", entry.Value.Path);
                command.Parameters.AddWithValue("$location", entry.Value.Location.ToString());
                command.Parameters.AddWithValue("$scan", scanId.ToString());
                command.Parameters.AddWithValue("$file", entry.Key);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
    }

    public async Task CompleteAsync(DriveInventoryRun run, CancellationToken cancellationToken)
    {
        if (!run.IsComplete || run.Status != DriveInventoryRunStatus.Completed)
            throw new ArgumentException("Only complete inventory runs can be committed.", nameof(run));
        await database.InitializeAsync(cancellationToken);
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            UPDATE drive_scan_runs SET completed_at_utc=$completed, status=$status, total_items=$total,
                folder_count=$folders, file_count=$files, known_bytes=$bytes, unknown_size_count=$unknown,
                google_workspace_count=$native, shortcut_count=$shortcuts, unresolved_count=$unresolved,
                backup_eligible_count=$eligible, failure_category=NULL, is_complete=1,
                storage_limit_bytes=$limit, total_usage_bytes=$usage, drive_usage_bytes=$driveUsage, trash_usage_bytes=$trashUsage
            WHERE scan_id=$scan AND status=$scanning AND is_complete=0;
            """;
        command.Parameters.AddWithValue("$completed", run.CompletedAtUtc!.Value.ToString("O"));
        command.Parameters.AddWithValue("$status", run.Status.ToString());
        command.Parameters.AddWithValue("$total", run.TotalItems);
        command.Parameters.AddWithValue("$folders", run.FolderCount);
        command.Parameters.AddWithValue("$files", run.FileCount);
        command.Parameters.AddWithValue("$bytes", run.KnownBytes);
        command.Parameters.AddWithValue("$unknown", run.UnknownSizeCount);
        command.Parameters.AddWithValue("$native", run.GoogleWorkspaceFileCount);
        command.Parameters.AddWithValue("$shortcuts", run.ShortcutCount);
        command.Parameters.AddWithValue("$unresolved", run.UnresolvedCount);
        command.Parameters.AddWithValue("$eligible", run.BackupEligibleCount);
        command.Parameters.AddWithValue("$limit", Db(run.StorageInformation?.StorageLimitBytes));
        command.Parameters.AddWithValue("$usage", Db(run.StorageInformation?.TotalUsageBytes));
        command.Parameters.AddWithValue("$driveUsage", Db(run.StorageInformation?.DriveUsageBytes));
        command.Parameters.AddWithValue("$trashUsage", Db(run.StorageInformation?.TrashUsageBytes));
        command.Parameters.AddWithValue("$scan", run.ScanId.ToString());
        command.Parameters.AddWithValue("$scanning", DriveInventoryRunStatus.Scanning.ToString());
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("The staging Drive snapshot was not available for completion.");
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task MarkIncompleteAsync(Guid scanId, DriveInventoryRunStatus status, DateTimeOffset completedAtUtc, string? failureCategory, CancellationToken cancellationToken)
    {
        if (status == DriveInventoryRunStatus.Completed) throw new ArgumentOutOfRangeException(nameof(status));
        await database.InitializeAsync(cancellationToken);
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE drive_scan_runs SET status=$status, completed_at_utc=$completed, failure_category=$failure, is_complete=0
            WHERE scan_id=$scan AND is_complete=0;
            """;
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$completed", completedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$failure", Db(failureCategory));
        command.Parameters.AddWithValue("$scan", scanId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<DriveInventoryRun?> GetLatestSuccessfulAsync(string providerAccountId, CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken);
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT * FROM drive_scan_runs WHERE provider_account_id=$account AND status=$status AND is_complete=1
            ORDER BY completed_at_utc DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$account", providerAccountId);
        command.Parameters.AddWithValue("$status", DriveInventoryRunStatus.Completed.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRun(reader) : null;
    }

    public async Task<IReadOnlyList<DriveInventoryRun>> GetRecentAsync(int maximumCount, CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken);
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM drive_scan_runs ORDER BY started_at_utc DESC LIMIT $limit";
        command.Parameters.AddWithValue("$limit", Math.Clamp(maximumCount, 1, 500));
        var runs = new List<DriveInventoryRun>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) runs.Add(ReadRun(reader));
        return runs;
    }

    public async Task<IReadOnlyList<DriveInventoryItem>> GetItemsAsync(Guid scanId, int maximumCount, CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken);
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM drive_scan_items WHERE scan_id=$scan ORDER BY display_path LIMIT $limit";
        command.Parameters.AddWithValue("$scan", scanId.ToString());
        command.Parameters.AddWithValue("$limit", Math.Clamp(maximumCount, 1, 10_000));
        var items = new List<DriveInventoryItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) items.Add(ReadItem(reader));
        return items;
    }

    private static void AddItemParameters(SqliteCommand command, Guid scanId, DriveInventoryItem item)
    {
        command.Parameters.AddWithValue("$scan", scanId.ToString());
        command.Parameters.AddWithValue("$file", item.FileId);
        command.Parameters.AddWithValue("$name", item.Name);
        command.Parameters.AddWithValue("$parent", Db(item.ParentId));
        command.Parameters.AddWithValue("$path", item.DisplayPath);
        command.Parameters.AddWithValue("$mime", item.MimeType);
        command.Parameters.AddWithValue("$kind", item.Kind.ToString());
        command.Parameters.AddWithValue("$location", item.Location.ToString());
        command.Parameters.AddWithValue("$size", Db(item.Size));
        command.Parameters.AddWithValue("$created", Db(item.CreatedAtUtc?.ToString("O")));
        command.Parameters.AddWithValue("$modified", Db(item.ModifiedAtUtc?.ToString("O")));
        command.Parameters.AddWithValue("$md5", Db(item.Md5Checksum));
        command.Parameters.AddWithValue("$extension", Db(item.FileExtension));
        command.Parameters.AddWithValue("$target", Db(item.ShortcutTargetId));
        command.Parameters.AddWithValue("$targetMime", Db(item.ShortcutTargetMimeType));
        command.Parameters.AddWithValue("$shared", item.IsShared ? 1 : 0);
        command.Parameters.AddWithValue("$owned", item.IsOwnedByUser is null ? DBNull.Value : item.IsOwnedByUser.Value ? 1 : 0);
        command.Parameters.AddWithValue("$eligible", item.IsBackupEligible ? 1 : 0);
        command.Parameters.AddWithValue("$reason", Db(item.SkipReason));
    }

    private static DriveInventoryRun ReadRun(SqliteDataReader reader)
    {
        var storage = reader.IsDBNull(reader.GetOrdinal("storage_limit_bytes")) && reader.IsDBNull(reader.GetOrdinal("total_usage_bytes")) &&
                      reader.IsDBNull(reader.GetOrdinal("drive_usage_bytes")) && reader.IsDBNull(reader.GetOrdinal("trash_usage_bytes"))
            ? null
            : new DriveStorageInformation(NullableInt64(reader, "storage_limit_bytes"), NullableInt64(reader, "total_usage_bytes"),
                NullableInt64(reader, "drive_usage_bytes"), NullableInt64(reader, "trash_usage_bytes"));
        return new DriveInventoryRun(
            Guid.Parse(reader.GetString(reader.GetOrdinal("scan_id"))), reader.GetString(reader.GetOrdinal("provider_id")),
            reader.GetString(reader.GetOrdinal("provider_account_id")), DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("started_at_utc"))),
            NullableDate(reader, "completed_at_utc"), Enum.Parse<DriveInventoryRunStatus>(reader.GetString(reader.GetOrdinal("status"))),
            reader.GetInt32(reader.GetOrdinal("total_items")), reader.GetInt32(reader.GetOrdinal("folder_count")), reader.GetInt32(reader.GetOrdinal("file_count")),
            reader.GetInt64(reader.GetOrdinal("known_bytes")), reader.GetInt32(reader.GetOrdinal("unknown_size_count")),
            reader.GetInt32(reader.GetOrdinal("google_workspace_count")), reader.GetInt32(reader.GetOrdinal("shortcut_count")),
            reader.GetInt32(reader.GetOrdinal("unresolved_count")), reader.GetInt32(reader.GetOrdinal("backup_eligible_count")),
            NullableString(reader, "failure_category"), reader.GetInt32(reader.GetOrdinal("is_complete")) == 1, storage);
    }

    private static DriveInventoryItem ReadItem(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(reader.GetOrdinal("scan_id"))), reader.GetString(reader.GetOrdinal("file_id")),
        reader.GetString(reader.GetOrdinal("name")), NullableString(reader, "parent_id"), reader.GetString(reader.GetOrdinal("display_path")),
        reader.GetString(reader.GetOrdinal("mime_type")), Enum.Parse<DriveInventoryItemKind>(reader.GetString(reader.GetOrdinal("item_kind"))),
        Enum.Parse<DriveInventoryLocation>(reader.GetString(reader.GetOrdinal("location"))), NullableInt64(reader, "file_size"),
        NullableDate(reader, "created_at_utc"), NullableDate(reader, "modified_at_utc"), NullableString(reader, "md5_checksum"),
        NullableString(reader, "file_extension"), NullableString(reader, "shortcut_target_id"), NullableString(reader, "shortcut_target_mime_type"),
        reader.GetInt32(reader.GetOrdinal("is_shared")) == 1, NullableBool(reader, "is_owned_by_user"),
        reader.GetInt32(reader.GetOrdinal("backup_eligible")) == 1, NullableString(reader, "skip_reason"));

    private static object Db(object? value) => value ?? DBNull.Value;
    private static string? NullableString(SqliteDataReader reader, string name) => reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetString(reader.GetOrdinal(name));
    private static long? NullableInt64(SqliteDataReader reader, string name) => reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetInt64(reader.GetOrdinal(name));
    private static DateTimeOffset? NullableDate(SqliteDataReader reader, string name) => NullableString(reader, name) is { } value ? DateTimeOffset.Parse(value) : null;
    private static bool? NullableBool(SqliteDataReader reader, string name) => reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetInt32(reader.GetOrdinal(name)) == 1;
}

using CloudKeeperSN.Domain.Backup;
using CloudKeeperSN.Domain.Scanning;
using CloudKeeperSN.Domain.Planning;
using CloudKeeperSN.Domain.Transfers;
using CloudKeeperSN.Infrastructure.Persistence;

namespace CloudKeeperSN.Infrastructure.Tests;

public sealed class SqlitePersistenceTests : IAsyncDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "CloudKeeperSN.Tests", Guid.NewGuid().ToString("N"));
    private readonly SqliteApplicationDatabase _database;
    private readonly SqliteConnectionFactory _factory;

    public SqlitePersistenceTests()
    {
        _factory = new SqliteConnectionFactory(new SqliteOptions(Path.Combine(_directory, "test.db")));
        _database = new SqliteApplicationDatabase(_factory);
    }

    [Fact]
    public async Task AccountMigrationPersistsEmailAsNonSensitiveMetadata()
    {
        var repository = new SqliteStorageAccountRepository(_database, _factory);
        var account = new CloudKeeperSN.Domain.Storage.StorageAccount(
            "google:current", "google-drive", "permission-42", "Nguyễn An", true, DateTimeOffset.UtcNow, "an@example.test");

        await repository.UpsertAsync(account, CancellationToken.None);
        var restored = Assert.Single(await repository.GetAllAsync(CancellationToken.None));

        Assert.Equal(account.Email, restored.Email);
        Assert.Equal(account.ProviderAccountId, restored.ProviderAccountId);
    }

    [Fact]
    public async Task Mapping_UpsertPreservesStableDestinationIdentity()
    {
        var repository = new SqliteTransferMappingRepository(_database, _factory);
        var mapping = new SourceDestinationMapping("google-a", "source-42", "microsoft-a", "folder-9", "Báo cáo (CloudKeeperSN 2).pdf", "destination-7", "fingerprint", DateTimeOffset.UtcNow);

        await repository.UpsertAsync(mapping, CancellationToken.None);
        var restored = await repository.FindAsync("google-a", "source-42", "microsoft-a", CancellationToken.None);

        Assert.NotNull(restored);
        Assert.Equal(mapping.DestinationName, restored.DestinationName);
        Assert.Equal(mapping.DestinationItemId, restored.DestinationItemId);
    }

    [Fact]
    public async Task CrashRecovery_MovesInFlightItemToRetryPending()
    {
        await SeedBackupRunAsync();
        var repository = new SqliteTransferItemRepository(_database, _factory);
        var item = CreateTransferItem(TransferState.Uploading);
        await repository.UpsertAsync(item, CancellationToken.None);

        var recoveredCount = await repository.RecoverInterruptedAsync(DateTimeOffset.UtcNow, CancellationToken.None);
        var recovered = await repository.FindAsync(item.Id, CancellationToken.None);

        Assert.Equal(1, recoveredCount);
        Assert.Equal(TransferState.RetryPending, recovered!.State);
        Assert.NotNull(recovered.NextRetryAtUtc);
    }

    [Fact]
    public async Task ActivityRepository_RedactsTechnicalSecretsBeforePersisting()
    {
        var repository = new SqliteActivityEventRepository(_database, _factory);
        await repository.AddAsync(
            new CloudKeeperSN.Application.Persistence.ActivityEvent(Guid.NewGuid(), null, DateTimeOffset.UtcNow, "ProviderCall", "Yêu cầu thất bại.", "Authorization: Bearer super-secret"),
            CancellationToken.None);

        var events = await repository.GetRecentAsync(10, CancellationToken.None);

        Assert.Single(events);
        Assert.DoesNotContain("super-secret", events[0].TechnicalDetails);
    }

    [Fact]
    public async Task DriveInventory_CommitsItemsAndStorageOnlyWhenSnapshotCompletes()
    {
        var repository = new SqliteDriveInventoryRepository(_database, _factory);
        var staging = InventoryRun(Guid.NewGuid());
        var item = new DriveInventoryItem(staging.ScanId, "file-1", "Báo cáo.pdf", "root", "pending",
            "application/pdf", DriveInventoryItemKind.File, DriveInventoryLocation.MyDrive, 42,
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, "abc", "pdf", null, null,
            false, true, true, null);

        await repository.BeginAsync(staging, CancellationToken.None);
        await repository.AppendBatchAsync(staging.ScanId, [item], CancellationToken.None);
        await repository.UpdateHierarchyAsync(staging.ScanId,
            new DriveHierarchyResult(new Dictionary<string, (string Path, DriveInventoryLocation Location)>
            {
                [item.FileId] = ("Drive của tôi/Báo cáo.pdf", DriveInventoryLocation.MyDrive)
            }, 0), CancellationToken.None);
        var completed = staging with
        {
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Status = DriveInventoryRunStatus.Completed,
            TotalItems = 1,
            FileCount = 1,
            KnownBytes = 42,
            BackupEligibleCount = 1,
            IsComplete = true,
            StorageInformation = new DriveStorageInformation(1_000, 500, 400, 20)
        };
        await repository.CompleteAsync(completed, CancellationToken.None);

        var restored = await repository.GetLatestSuccessfulAsync("account", CancellationToken.None);
        var restoredItem = Assert.Single(await repository.GetItemsAsync(staging.ScanId, 10, CancellationToken.None));
        Assert.Equal(completed, restored);
        Assert.Equal("Drive của tôi/Báo cáo.pdf", restoredItem.DisplayPath);
        Assert.Equal(42, restoredItem.Size);
    }

    [Fact]
    public async Task DriveInventory_IncompleteRunDoesNotReplacePreviousSuccessfulSnapshot()
    {
        var repository = new SqliteDriveInventoryRepository(_database, _factory);
        var previous = InventoryRun(Guid.NewGuid()) with
        {
            CompletedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            Status = DriveInventoryRunStatus.Completed,
            TotalItems = 7,
            FileCount = 7,
            IsComplete = true
        };
        await repository.BeginAsync(previous with { Status = DriveInventoryRunStatus.Scanning, IsComplete = false }, CancellationToken.None);
        await repository.CompleteAsync(previous, CancellationToken.None);

        var failed = InventoryRun(Guid.NewGuid()) with { StartedAtUtc = DateTimeOffset.UtcNow };
        await repository.BeginAsync(failed, CancellationToken.None);
        await repository.MarkIncompleteAsync(failed.ScanId, DriveInventoryRunStatus.Failed, DateTimeOffset.UtcNow,
            "NetworkUnavailable", CancellationToken.None);

        var latest = await repository.GetLatestSuccessfulAsync("account", CancellationToken.None);
        var runs = await repository.GetRecentAsync(10, CancellationToken.None);
        Assert.Equal(previous.ScanId, latest!.ScanId);
        Assert.Contains(runs, run => run.ScanId == failed.ScanId && !run.IsComplete && run.Status == DriveInventoryRunStatus.Failed);
    }

    [Fact]
    public async Task DriveInventory_StartupRecoveryMarksOnlyStagingRunsInterrupted()
    {
        var repository = new SqliteDriveInventoryRepository(_database, _factory);
        var staging = InventoryRun(Guid.NewGuid());
        await repository.BeginAsync(staging, CancellationToken.None);

        await repository.RecoverInterruptedAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        var recovered = Assert.Single(await repository.GetRecentAsync(10, CancellationToken.None));
        Assert.Equal(DriveInventoryRunStatus.Interrupted, recovered.Status);
        Assert.False(recovered.IsComplete);
        Assert.Equal("ApplicationShutdown", recovered.FailureCategory);
    }

    [Fact]
    public async Task DriveInventorySchemaContainsMetadataButNoCredentialColumns()
    {
        await _database.InitializeAsync(CancellationToken.None);
        await using var connection = await _factory.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(drive_scan_items)";
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        while (await reader.ReadAsync(CancellationToken.None)) columns.Add(reader.GetString(1));

        Assert.Contains("file_id", columns);
        Assert.Contains("display_path", columns);
        Assert.DoesNotContain(columns, name => name.Contains("token", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(columns, name => name.Contains("secret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(columns, name => name.Contains("credential", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BackupSelectionPlan_RoundTripsRulesAndReplacesPriorEditAtomically()
    {
        var inventory = new SqliteDriveInventoryRepository(_database, _factory);
        var scan = InventoryRun(Guid.NewGuid());
        await inventory.BeginAsync(scan, CancellationToken.None);
        await inventory.CompleteAsync(scan with
        {
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Status = DriveInventoryRunStatus.Completed,
            IsComplete = true
        }, CancellationToken.None);
        var repository = new SqliteBackupSelectionPlanRepository(_database, _factory);
        var plan = new BackupSelectionPlan(Guid.NewGuid(), "account", "Tài liệu quan trọng", scan.ScanId,
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow,
            [new("folder", BackupSelectionRuleMode.Include, DriveInventoryItemKind.Folder, "Tài liệu")]);

        await repository.SaveAsync(plan, CancellationToken.None);
        var restored = await repository.GetByAccountAsync("account", CancellationToken.None);
        Assert.Equal(plan.PlanId, restored!.PlanId);
        Assert.Equal(plan.Name, restored.Name);
        Assert.Equal(plan.SourceScanId, restored.SourceScanId);
        Assert.Equal(plan.Rules, restored.Rules);

        var edited = plan with
        {
            Name = "Kế hoạch đã sửa",
            UpdatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(1),
            Rules = [new("file", BackupSelectionRuleMode.Exclude, DriveInventoryItemKind.File, "Riêng tư.txt")]
        };
        await repository.SaveAsync(edited, CancellationToken.None);
        var reopened = await repository.GetByAccountAsync("account", CancellationToken.None);

        Assert.Equal("Kế hoạch đã sửa", reopened!.Name);
        var rule = Assert.Single(reopened.Rules);
        Assert.Equal("file", rule.ItemId);
        Assert.Equal(BackupSelectionRuleMode.Exclude, rule.Mode);
    }

    private async Task SeedBackupRunAsync()
    {
        await _database.InitializeAsync(CancellationToken.None);
        await using var connection = await _factory.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO backup_definitions(id, name, source_provider_id, source_account_id, source_folder_id,
                destination_provider_id, destination_account_id, destination_folder_id, created_at_utc, updated_at_utc)
            VALUES ($definition, 'Test', 'google-drive', 'g', 'root', 'one-drive', 'm', 'root', $now, $now);
            INSERT INTO backup_runs(id, backup_definition_id, status, preview_was_shown, started_at_utc)
            VALUES ($run, $definition, 'Running', 1, $now);
            """;
        command.Parameters.AddWithValue("$definition", DefinitionId.ToString());
        command.Parameters.AddWithValue("$run", RunId.ToString());
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static readonly Guid DefinitionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RunId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static TransferItem CreateTransferItem(TransferState state) => new()
    {
        Id = Guid.NewGuid(),
        RunId = RunId,
        SourceProviderAccountId = "g",
        SourceItemId = "source",
        OriginalName = "file.txt",
        NormalizedDestinationName = "file.txt",
        RelativeSourcePath = "folder/file.txt",
        State = state,
        RetryCount = 0
    };

    private static DriveInventoryRun InventoryRun(Guid scanId) => new(
        scanId, "google-drive", "account", DateTimeOffset.UtcNow.AddMinutes(-2), null,
        DriveInventoryRunStatus.Scanning, 0, 0, 0, 0, 0, 0, 0, 0, 0, null, false, null);

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        return ValueTask.CompletedTask;
    }
}

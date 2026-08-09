using CloudKeeperSN.Domain.Backup;
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

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        return ValueTask.CompletedTask;
    }
}

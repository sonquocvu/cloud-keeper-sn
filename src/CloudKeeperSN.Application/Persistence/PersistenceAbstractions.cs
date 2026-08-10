using CloudKeeperSN.Domain.Backup;
using CloudKeeperSN.Domain.Storage;
using CloudKeeperSN.Domain.Scanning;
using CloudKeeperSN.Domain.Planning;
using CloudKeeperSN.Domain.Transfers;

namespace CloudKeeperSN.Application.Persistence;

public interface IApplicationDatabase
{
    Task InitializeAsync(CancellationToken cancellationToken);
}

public interface IStorageAccountRepository
{
    Task UpsertAsync(StorageAccount account, CancellationToken cancellationToken);
    Task<IReadOnlyList<StorageAccount>> GetAllAsync(CancellationToken cancellationToken);
    Task RemoveAsync(string id, CancellationToken cancellationToken);
}

public interface IDriveInventoryRepository
{
    Task RecoverInterruptedAsync(DateTimeOffset interruptedAtUtc, CancellationToken cancellationToken);
    Task BeginAsync(DriveInventoryRun run, CancellationToken cancellationToken);
    Task AppendBatchAsync(Guid scanId, IReadOnlyList<DriveInventoryItem> items, CancellationToken cancellationToken);
    Task UpdateHierarchyAsync(Guid scanId, DriveHierarchyResult hierarchy, CancellationToken cancellationToken);
    Task CompleteAsync(DriveInventoryRun completedRun, CancellationToken cancellationToken);
    Task MarkIncompleteAsync(Guid scanId, DriveInventoryRunStatus status, DateTimeOffset completedAtUtc, string? failureCategory, CancellationToken cancellationToken);
    Task<DriveInventoryRun?> GetLatestSuccessfulAsync(string providerAccountId, CancellationToken cancellationToken);
    Task<IReadOnlyList<DriveInventoryRun>> GetRecentAsync(int maximumCount, CancellationToken cancellationToken);
    Task<IReadOnlyList<DriveInventoryItem>> GetItemsAsync(Guid scanId, int maximumCount, CancellationToken cancellationToken);
    Task<IReadOnlyList<DriveInventoryItem>> GetAllItemsAsync(Guid scanId, CancellationToken cancellationToken);
}

public sealed class DriveInventoryPersistenceException : Exception
{
    public DriveInventoryPersistenceException(string message, Exception innerException) : base(message, innerException) { }
}

public interface IBackupSelectionPlanRepository
{
    Task<BackupSelectionPlan?> GetByAccountAsync(string providerAccountId, CancellationToken cancellationToken);
    Task SaveAsync(BackupSelectionPlan plan, CancellationToken cancellationToken);
}

public interface ITransferMappingRepository
{
    Task<SourceDestinationMapping?> FindAsync(
        string sourceProviderAccountId,
        string sourceItemId,
        string destinationProviderAccountId,
        CancellationToken cancellationToken);

    Task UpsertAsync(SourceDestinationMapping mapping, CancellationToken cancellationToken);
}

public interface ITransferItemRepository
{
    Task UpsertAsync(TransferItem item, CancellationToken cancellationToken);
    Task<TransferItem?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<TransferItem>> GetRecoverableAsync(CancellationToken cancellationToken);
    Task<int> RecoverInterruptedAsync(DateTimeOffset recoveredAtUtc, CancellationToken cancellationToken);
}

public sealed record ActivityEvent(
    Guid Id,
    Guid? RunId,
    DateTimeOffset OccurredAtUtc,
    string EventType,
    string VietnameseMessage,
    string? TechnicalDetails);

public interface IActivityEventRepository
{
    Task AddAsync(ActivityEvent activityEvent, CancellationToken cancellationToken);
    Task<IReadOnlyList<ActivityEvent>> GetRecentAsync(int maximumCount, CancellationToken cancellationToken);
}

public interface IApplicationSettingRepository
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken);
    Task SetAsync(string key, string value, CancellationToken cancellationToken);
}

public interface ICredentialProtector
{
    byte[] Protect(ReadOnlySpan<byte> plaintext, string purpose);
    byte[] Unprotect(ReadOnlySpan<byte> protectedData, string purpose);
}

public interface IProtectedCredentialStore
{
    Task<byte[]?> GetAsync(string providerId, string key, CancellationToken cancellationToken);
    Task StoreAsync(string providerId, string key, ReadOnlyMemory<byte> value, CancellationToken cancellationToken);
    Task DeleteAsync(string providerId, string key, CancellationToken cancellationToken);
    Task ClearProviderAsync(string providerId, CancellationToken cancellationToken);
}

public sealed class ProtectedCredentialException : Exception
{
    public ProtectedCredentialException(string message, Exception? innerException = null) : base(message, innerException) { }
}

public interface IProviderDiagnostics
{
    Task WriteAsync(string eventType, string vietnameseMessage, string? technicalDetails, CancellationToken cancellationToken);
}

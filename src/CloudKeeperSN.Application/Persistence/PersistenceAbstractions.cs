using CloudKeeperSN.Domain.Backup;
using CloudKeeperSN.Domain.Storage;
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


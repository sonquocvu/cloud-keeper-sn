using CloudKeeperSN.Domain.Storage;

namespace CloudKeeperSN.Application.Storage;

public interface IStorageProvider
{
    StorageProviderDescriptor Descriptor { get; }
    Task<StorageAccount?> GetConnectedAccountAsync(CancellationToken cancellationToken);
}

public interface IStorageBrowserCapability
{
    IAsyncEnumerable<StorageItem> GetChildrenAsync(
        string providerAccountId,
        string parentItemId,
        CancellationToken cancellationToken);
}

public sealed record StorageItemPage(IReadOnlyList<StorageItem> Items, string? ContinuationToken, bool IsIncomplete = false);

public interface IPagedStorageBrowserCapability : IStorageBrowserCapability
{
    Task<StorageItemPage> GetChildrenPageAsync(
        string providerAccountId,
        string parentItemId,
        string? continuationToken,
        CancellationToken cancellationToken);
}

public interface IStorageReadCapability
{
    Task<Stream> OpenReadAsync(
        string providerAccountId,
        string itemId,
        CancellationToken cancellationToken);
}

public interface IStorageNativeExportCapability
{
    Task<Stream> ExportAsync(
        string providerAccountId,
        string itemId,
        string destinationMimeType,
        CancellationToken cancellationToken);
}

public interface IStorageFolderWriteCapability
{
    Task<StorageItem> CreateFolderAsync(
        string providerAccountId,
        string parentItemId,
        string name,
        CancellationToken cancellationToken);
}

public interface IStorageWriteCapability
{
    Task<IStorageWriteSession> CreateWriteSessionAsync(
        string providerAccountId,
        string parentItemId,
        string name,
        long? expectedLength,
        CancellationToken cancellationToken);
}

public interface IStorageWriteSession : IAsyncDisposable
{
    string SessionId { get; }
    long BytesAccepted { get; }
    Task WriteAsync(ReadOnlyMemory<byte> chunk, CancellationToken cancellationToken);
    Task<StorageItem> CompleteAsync(CancellationToken cancellationToken);
    Task AbortAsync(CancellationToken cancellationToken);
}

public interface IProviderAuthenticationService
{
    string ProviderId { get; }
    bool IsConfigured { get; }
    string? ConfigurationMessage { get; }
    ProviderAuthenticationState State { get; }
    event Action<ProviderAuthenticationState>? StateChanged;
    event Action? ConfigurationChanged;
    Task<StorageAccount?> GetCachedAccountAsync(CancellationToken cancellationToken);
    Task<StorageAccount> ConnectAsync(CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);
    Task DisconnectLocalAsync(CancellationToken cancellationToken);
}

public enum ProviderAuthenticationStatus
{
    Disconnected,
    OpeningBrowser,
    CompletingConnection,
    Connected,
    ReauthenticationRequired,
    Cancelled,
    Failed,
    Disconnecting
}

public sealed record ProviderAuthenticationState(
    ProviderAuthenticationStatus Status,
    string VietnameseMessage,
    string? TechnicalCategory = null);

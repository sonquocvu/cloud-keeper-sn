using System.Runtime.CompilerServices;
using CloudKeeperSN.Application.Storage;
using CloudKeeperSN.Domain.Storage;

namespace CloudKeeperSN.Providers.GoogleDrive.Fakes;

public sealed class FakeGoogleDriveProvider : IStorageProvider, IStorageBrowserCapability, IStorageReadCapability, IStorageNativeExportCapability
{
    private readonly Dictionary<string, List<StorageItem>> _children = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _contents = new(StringComparer.Ordinal);
    private StorageAccount? _account;

    public StorageProviderDescriptor Descriptor { get; } = new(
        "google-drive",
        "Google Drive (mô phỏng)",
        new HashSet<StorageCapabilityKind>
        {
            StorageCapabilityKind.Browse,
            StorageCapabilityKind.Read,
            StorageCapabilityKind.ExportNativeFile,
            StorageCapabilityKind.ProviderChecksum
        });

    public void Connect(string providerAccountId = "fake-google-account", string displayName = "Tài khoản Google mẫu") =>
        _account = new StorageAccount("google:" + providerAccountId, Descriptor.ProviderId, providerAccountId, displayName, true, DateTimeOffset.UtcNow);

    public void Disconnect() => _account = null;

    public void AddItem(string parentItemId, StorageItem item, ReadOnlyMemory<byte> content = default)
    {
        if (!_children.TryGetValue(parentItemId, out var children))
        {
            children = [];
            _children[parentItemId] = children;
        }

        children.Add(item);
        if (item.Kind is StorageItemKind.File or StorageItemKind.ProviderNativeFile)
        {
            _contents[item.ItemId] = content.ToArray();
        }
    }

    public Task<StorageAccount?> GetConnectedAccountAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_account);
    }

    public async IAsyncEnumerable<StorageItem> GetChildrenAsync(
        string providerAccountId,
        string parentItemId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        EnsureConnected(providerAccountId);
        foreach (var item in _children.GetValueOrDefault(parentItemId, []))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
            await Task.Yield();
        }
    }

    public Task<Stream> OpenReadAsync(string providerAccountId, string itemId, CancellationToken cancellationToken)
    {
        EnsureConnected(providerAccountId);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_contents.TryGetValue(itemId, out var content)) throw new FileNotFoundException("Fake source item not found.", itemId);
        return Task.FromResult<Stream>(new MemoryStream(content, writable: false));
    }

    public Task<Stream> ExportAsync(string providerAccountId, string itemId, string destinationMimeType, CancellationToken cancellationToken) =>
        OpenReadAsync(providerAccountId, itemId, cancellationToken);

    private void EnsureConnected(string providerAccountId)
    {
        if (_account is null || !_account.IsConnected || _account.ProviderAccountId != providerAccountId)
        {
            throw new InvalidOperationException("Tài khoản Google Drive mô phỏng chưa được kết nối.");
        }
    }
}


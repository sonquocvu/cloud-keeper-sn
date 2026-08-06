using System.Runtime.CompilerServices;
using CloudKeeperSN.Application.Storage;
using CloudKeeperSN.Domain.Naming;
using CloudKeeperSN.Domain.Storage;

namespace CloudKeeperSN.Providers.OneDrive.Fakes;

public sealed class FakeOneDriveProvider : IStorageProvider, IStorageBrowserCapability, IStorageFolderWriteCapability, IStorageWriteCapability
{
    private readonly Dictionary<string, List<StorageItem>> _children = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _contents = new(StringComparer.Ordinal);
    private StorageAccount? _account;
    private int _nextId;

    public StorageProviderDescriptor Descriptor { get; } = new(
        "one-drive",
        "OneDrive (mô phỏng)",
        new HashSet<StorageCapabilityKind>
        {
            StorageCapabilityKind.Browse,
            StorageCapabilityKind.Write,
            StorageCapabilityKind.CreateFolder,
            StorageCapabilityKind.ResumableUpload,
            StorageCapabilityKind.ProviderChecksum
        });

    public void Connect(string providerAccountId = "fake-microsoft-account", string displayName = "Tài khoản Microsoft mẫu") =>
        _account = new StorageAccount("microsoft:" + providerAccountId, Descriptor.ProviderId, providerAccountId, displayName, true, DateTimeOffset.UtcNow);

    public void Disconnect() => _account = null;

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

    public Task<StorageItem> CreateFolderAsync(
        string providerAccountId,
        string parentItemId,
        string name,
        CancellationToken cancellationToken)
    {
        EnsureConnected(providerAccountId);
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = OneDriveNameNormalizer.Normalize(name);
        EnsureNameAvailable(parentItemId, normalized);
        var folder = CreateItem(providerAccountId, parentItemId, normalized, StorageItemKind.Folder, null, null);
        AddChild(parentItemId, folder);
        return Task.FromResult(folder);
    }

    public Task<IStorageWriteSession> CreateWriteSessionAsync(
        string providerAccountId,
        string parentItemId,
        string name,
        long? expectedLength,
        CancellationToken cancellationToken)
    {
        EnsureConnected(providerAccountId);
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = OneDriveNameNormalizer.Normalize(name);
        EnsureNameAvailable(parentItemId, normalized);
        IStorageWriteSession session = new FakeWriteSession(this, providerAccountId, parentItemId, normalized, expectedLength);
        return Task.FromResult(session);
    }

    public ReadOnlyMemory<byte> GetContent(string itemId) => _contents[itemId];

    private StorageItem CommitFile(string accountId, string parentId, string name, byte[] content)
    {
        EnsureNameAvailable(parentId, name);
        var item = CreateItem(accountId, parentId, name, StorageItemKind.File, "application/octet-stream", content.LongLength);
        AddChild(parentId, item);
        _contents[item.ItemId] = content;
        return item;
    }

    private StorageItem CreateItem(string accountId, string parentId, string name, StorageItemKind kind, string? mimeType, long? size) => new()
    {
        ProviderId = Descriptor.ProviderId,
        ProviderAccountId = accountId,
        ItemId = $"fake-onedrive-{Interlocked.Increment(ref _nextId)}",
        ParentItemId = parentId,
        Name = name,
        Kind = kind,
        MimeType = mimeType,
        Size = size,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        ModifiedAtUtc = DateTimeOffset.UtcNow
    };

    private void AddChild(string parentId, StorageItem item)
    {
        if (!_children.TryGetValue(parentId, out var children))
        {
            children = [];
            _children[parentId] = children;
        }
        children.Add(item);
    }

    private void EnsureNameAvailable(string parentItemId, string name)
    {
        if (_children.GetValueOrDefault(parentItemId, []).Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Đã có một mục khác dùng tên này. Trình cung cấp mô phỏng không ghi đè tệp.");
        }
    }

    private void EnsureConnected(string providerAccountId)
    {
        if (_account is null || !_account.IsConnected || _account.ProviderAccountId != providerAccountId)
        {
            throw new InvalidOperationException("Tài khoản OneDrive mô phỏng chưa được kết nối.");
        }
    }

    private sealed class FakeWriteSession(
        FakeOneDriveProvider owner,
        string accountId,
        string parentId,
        string name,
        long? expectedLength) : IStorageWriteSession
    {
        private readonly MemoryStream _buffer = new();
        private bool _finished;

        public string SessionId { get; } = Guid.NewGuid().ToString("N");
        public long BytesAccepted => _buffer.Length;

        public async Task WriteAsync(ReadOnlyMemory<byte> chunk, CancellationToken cancellationToken)
        {
            EnsureOpen();
            await _buffer.WriteAsync(chunk, cancellationToken);
        }

        public Task<StorageItem> CompleteAsync(CancellationToken cancellationToken)
        {
            EnsureOpen();
            cancellationToken.ThrowIfCancellationRequested();
            if (expectedLength is { } length && length != _buffer.Length)
            {
                throw new InvalidDataException("Dung lượng tải lên không khớp với dung lượng dự kiến.");
            }

            _finished = true;
            return Task.FromResult(owner.CommitFile(accountId, parentId, name, _buffer.ToArray()));
        }

        public Task AbortAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _finished = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _finished = true;
            return _buffer.DisposeAsync();
        }

        private void EnsureOpen()
        {
            if (_finished) throw new InvalidOperationException("Phiên tải lên đã kết thúc.");
        }
    }
}

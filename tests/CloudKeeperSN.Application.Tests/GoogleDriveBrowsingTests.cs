using CloudKeeperSN.Application.Persistence;
using CloudKeeperSN.Application.Storage;
using CloudKeeperSN.Domain.Scanning;
using CloudKeeperSN.Domain.Storage;
using CloudKeeperSN.Providers.GoogleDrive;
using CloudKeeperSN.Providers.GoogleDrive.Authentication;

namespace CloudKeeperSN.Application.Tests;

public sealed class GoogleDriveBrowsingTests
{
    [Theory]
    [InlineData("folder'id", "'folder\\'id' in parents and trashed = false")]
    [InlineData("folder\\id", "'folder\\\\id' in parents and trashed = false")]
    [InlineData("日本語-đặc-biệt", "'日本語-đặc-biệt' in parents and trashed = false")]
    public void ParentQueryEscapesOnlyLiteralSyntax(string folderId, string expected) =>
        Assert.Equal(expected, GoogleDriveQuery.ChildrenOf(folderId));

    [Fact]
    public async Task EnumeratesMultiplePagesIncludingAnEmptyIntermediatePage()
    {
        var session = new PagingSession(
            new GoogleDriveMetadataPage([Folder("one", "Một")], "next-1"),
            new GoogleDriveMetadataPage([], "next-2"),
            new GoogleDriveMetadataPage([Folder("two", "Hai")], null));
        await using var authentication = await CreateAuthenticationAsync(session);
        var provider = new GoogleDriveProvider(authentication, new NullDiagnostics());

        var results = await ReadAllAsync(provider.GetChildrenAsync("account", "root", CancellationToken.None));

        Assert.Equal(["one", "two"], results.Select(item => item.ItemId));
        Assert.Equal([null, "next-1", "next-2"], session.ReceivedTokens);
    }

    [Fact]
    public async Task RejectsRepeatedContinuationTokenWithoutLoopingForever()
    {
        var session = new PagingSession(
            new GoogleDriveMetadataPage([], "same"),
            new GoogleDriveMetadataPage([], "same"));
        await using var authentication = await CreateAuthenticationAsync(session);
        var provider = new GoogleDriveProvider(authentication, new NullDiagnostics());

        var failure = await Assert.ThrowsAsync<ProviderOperationException>(async () =>
            await ReadAllAsync(provider.GetChildrenAsync("account", "root", CancellationToken.None)));

        Assert.Equal(ProviderFailureCategory.InvalidProviderResponse, failure.Category);
        Assert.Equal(2, session.ReceivedTokens.Count);
    }

    [Fact]
    public async Task KeepsDuplicateNamesAsDistinctStableItems()
    {
        var session = new PagingSession(new GoogleDriveMetadataPage(
            [Folder("id-a", "Trùng tên"), Folder("id-b", "Trùng tên")], null));
        await using var authentication = await CreateAuthenticationAsync(session);
        var provider = new GoogleDriveProvider(authentication, new NullDiagnostics());

        var results = await ReadAllAsync(provider.GetChildrenAsync("account", "root", CancellationToken.None));

        Assert.Equal(2, results.Count);
        Assert.Equal(2, results.Select(item => item.ItemId).Distinct().Count());
    }

    [Fact]
    public async Task InventoryMapsKindsSharedMetadataAndExcludesTrashedItemsDefensively()
    {
        var session = new PagingSession
        {
            InventoryPage = new GoogleDriveMetadataPage([
                Metadata("file", "Dữ liệu_日本語.txt", "text/plain", size: null, checksum: null),
                Metadata("folder", "Tài liệu", GoogleDriveProvider.FolderMimeType),
                Metadata("native", "Kế hoạch", "application/vnd.google-apps.document"),
                Metadata("shortcut", "Lối tắt", "application/vnd.google-apps.shortcut", targetId: "file"),
                Metadata("shared", "Được chia sẻ.pdf", "application/pdf", size: 12, shared: true, owned: false),
                Metadata("trash", "Trong thùng rác.txt", "text/plain", trashed: true)
            ], "next")
        };
        await using var authentication = await CreateAuthenticationAsync(session);
        var provider = new GoogleDriveProvider(authentication, new NullDiagnostics());

        var result = await provider.GetInventoryPageAsync(Guid.NewGuid(), "account", null, CancellationToken.None);

        Assert.Equal(5, result.Items.Count);
        Assert.DoesNotContain(result.Items, item => item.FileId == "trash");
        Assert.Equal(DriveInventoryItemKind.File, result.Items.Single(item => item.FileId == "file").Kind);
        Assert.Equal(DriveInventoryItemKind.Folder, result.Items.Single(item => item.FileId == "folder").Kind);
        Assert.Equal(DriveInventoryItemKind.GoogleWorkspaceFile, result.Items.Single(item => item.FileId == "native").Kind);
        Assert.Equal(DriveInventoryItemKind.Shortcut, result.Items.Single(item => item.FileId == "shortcut").Kind);
        Assert.Equal(DriveInventoryLocation.Shared, result.Items.Single(item => item.FileId == "shared").Location);
        Assert.Equal("file", result.Items.Single(item => item.FileId == "shortcut").ShortcutTargetId);
        Assert.Equal("next", result.NextPageToken);
    }

    [Fact]
    public async Task InventoryReturnsStorageQuotaMetadata()
    {
        var session = new PagingSession { Storage = new GoogleDriveStorageInformation(1000, 500, 400, 20) };
        await using var authentication = await CreateAuthenticationAsync(session);
        var provider = new GoogleDriveProvider(authentication, new NullDiagnostics());

        var storage = await provider.GetStorageInformationAsync(CancellationToken.None);

        Assert.Equal(1000, storage.StorageLimitBytes);
        Assert.Equal(500, storage.TotalUsageBytes);
        Assert.Equal(400, storage.DriveUsageBytes);
        Assert.Equal(20, storage.TrashUsageBytes);
    }

    private static GoogleDriveItemMetadata Folder(string id, string name) => new(
        id, name, GoogleDriveProvider.FolderMimeType, ["root"], null, null, null, null, null, null, null, null);

    private static GoogleDriveItemMetadata Metadata(
        string id,
        string name,
        string mimeType,
        long? size = null,
        string? checksum = null,
        string? targetId = null,
        bool shared = false,
        bool? owned = true,
        bool trashed = false) => new(
            id, name, mimeType, ["root"], size, null, null, checksum, null, targetId, null, true,
            trashed, null, shared, owned);

    private static async Task<GoogleAuthenticationService> CreateAuthenticationAsync(IGoogleDriveSession session)
    {
        var service = new GoogleAuthenticationService(new SessionOAuthClient(session), new MemoryAccounts(), new NullDiagnostics());
        await service.ConnectAsync(CancellationToken.None);
        return service;
    }

    private static async Task<List<StorageItem>> ReadAllAsync(IAsyncEnumerable<StorageItem> source)
    {
        var results = new List<StorageItem>();
        await foreach (var item in source) results.Add(item);
        return results;
    }

    private sealed class PagingSession(params GoogleDriveMetadataPage[] pages) : IGoogleDriveSession
    {
        private int _index;
        public List<string?> ReceivedTokens { get; } = [];
        public GoogleDriveMetadataPage InventoryPage { get; set; } = new([], null);
        public GoogleDriveStorageInformation Storage { get; set; } = new(null, null, null, null);
        public Task<GoogleAccountProfile> GetAccountProfileAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new GoogleAccountProfile("account", "Tài khoản", "drive@example.test"));
        public Task<GoogleDriveStorageInformation> GetStorageInformationAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Storage);
        public Task VerifyReadOnlyAccessAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<GoogleDriveMetadataPage> GetInventoryPageAsync(string? pageToken, CancellationToken cancellationToken) =>
            Task.FromResult(InventoryPage);
        public Task<GoogleDriveMetadataPage> GetChildrenPageAsync(string parentItemId, string? pageToken, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReceivedTokens.Add(pageToken);
            return Task.FromResult(pages[_index++]);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SessionOAuthClient(IGoogleDriveSession session) : IGoogleOAuthClient
    {
        public event Action? ConfigurationChanged { add { } remove { } }
        public bool IsConfigured => true;
        public string? ConfigurationMessage => null;
        public Task<IGoogleDriveSession?> RestoreAsync(CancellationToken cancellationToken) => Task.FromResult<IGoogleDriveSession?>(session);
        public async Task<IGoogleDriveSession> AuthorizeAsync(
            Func<GoogleOAuthStage, CancellationToken, Task> reportStageAsync,
            CancellationToken cancellationToken)
        {
            await reportStageAsync(GoogleOAuthStage.AuthorizationStored, cancellationToken);
            return session;
        }
        public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ClearLocalAuthorizationAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class MemoryAccounts : IStorageAccountRepository
    {
        public Task UpsertAsync(StorageAccount account, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<StorageAccount>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<StorageAccount>>([]);
        public Task RemoveAsync(string id, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NullDiagnostics : IProviderDiagnostics
    {
        public Task WriteAsync(string eventType, string vietnameseMessage, string? technicalDetails, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

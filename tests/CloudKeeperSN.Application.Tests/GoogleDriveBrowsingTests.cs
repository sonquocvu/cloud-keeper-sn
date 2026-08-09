using CloudKeeperSN.Application.Persistence;
using CloudKeeperSN.Application.Storage;
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

    private static GoogleDriveItemMetadata Folder(string id, string name) => new(
        id, name, GoogleDriveProvider.FolderMimeType, ["root"], null, null, null, null, null, null, null, null);

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
        public Task<GoogleAccountProfile> GetAccountProfileAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new GoogleAccountProfile("account", "Tài khoản", "drive@example.test"));
        public Task VerifyReadOnlyAccessAsync(CancellationToken cancellationToken) => Task.CompletedTask;
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

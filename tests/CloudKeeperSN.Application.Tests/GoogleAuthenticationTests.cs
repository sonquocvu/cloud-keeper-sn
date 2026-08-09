using CloudKeeperSN.Application.Persistence;
using CloudKeeperSN.Application.Storage;
using CloudKeeperSN.Domain.Storage;
using CloudKeeperSN.Providers.GoogleDrive;
using CloudKeeperSN.Providers.GoogleDrive.Authentication;

namespace CloudKeeperSN.Application.Tests;

public sealed class GoogleAuthenticationTests
{
    [Fact]
    public async Task MissingConfigurationDoesNotEnableCachedConnection()
    {
        var oauth = new FakeGoogleOAuthClient { IsConfigured = false, ConfigurationMessage = "Thiếu cấu hình" };
        await using var service = CreateService(oauth, out _, out _);

        var account = await service.GetCachedAccountAsync(CancellationToken.None);

        Assert.Null(account);
        Assert.False(service.IsConfigured);
        Assert.Equal(ProviderAuthenticationStatus.Disconnected, service.State.Status);
    }

    [Fact]
    public async Task SuccessfulConnectionPersistsOnlyAccountMetadataAndReportsStates()
    {
        var oauth = new FakeGoogleOAuthClient { AuthorizedSession = new FakeGoogleDriveSession() };
        await using var service = CreateService(oauth, out var accounts, out var diagnostics);
        var states = new List<ProviderAuthenticationStatus>();
        service.StateChanged += state => states.Add(state.Status);

        var account = await service.ConnectAsync(CancellationToken.None);

        Assert.Equal("permission-42", account.ProviderAccountId);
        Assert.Equal("an@example.test", account.Email);
        Assert.Equal([ProviderAuthenticationStatus.OpeningBrowser, ProviderAuthenticationStatus.CompletingConnection, ProviderAuthenticationStatus.Connected], states);
        Assert.Single(accounts.Values);
        Assert.Contains(diagnostics.Events, entry => entry.EventType == "GoogleAuthenticationCompleted");
    }

    [Fact]
    public async Task ValidProtectedSessionIsRestoredAndIdentityIsConfirmed()
    {
        var oauth = new FakeGoogleOAuthClient { RestoredSession = new FakeGoogleDriveSession() };
        await using var service = CreateService(oauth, out var accounts, out var diagnostics);

        var account = await service.GetCachedAccountAsync(CancellationToken.None);

        Assert.NotNull(account);
        Assert.Equal("Nguyễn An", account.DisplayName);
        Assert.Equal("an@example.test", account.Email);
        Assert.Equal(ProviderAuthenticationStatus.Connected, service.State.Status);
        Assert.Single(accounts.Values);
        Assert.Contains(diagnostics.Events, entry => entry.EventType == "GoogleTokenRestored");
    }

    [Fact]
    public async Task ConcurrentInteractiveSignInIsRejected()
    {
        var oauth = new FakeGoogleOAuthClient { BlockAuthorization = true };
        await using var service = CreateService(oauth, out _, out _);
        var first = service.ConnectAsync(CancellationToken.None);
        await oauth.AuthorizationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConnectAsync(CancellationToken.None));

        oauth.ReleaseAuthorization.SetResult();
        await first;
    }

    [Fact]
    public async Task CancelledAuthorizationReturnsUsableCancelledState()
    {
        var oauth = new FakeGoogleOAuthClient { WaitForCancellation = true };
        await using var service = CreateService(oauth, out _, out _);
        using var cancellation = new CancellationTokenSource();
        var connect = service.ConnectAsync(cancellation.Token);
        await oauth.AuthorizationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connect);
        Assert.Equal(ProviderAuthenticationStatus.Cancelled, service.State.Status);
        Assert.Equal(1, oauth.DisconnectCalls);
    }

    [Fact]
    public async Task FailedProfileLookupClearsPartialAuthorizationAndAllowsRetry()
    {
        var session = new FakeGoogleDriveSession { ProfileException = new HttpRequestException("offline") };
        var oauth = new FakeGoogleOAuthClient { AuthorizedSession = session };
        await using var service = CreateService(oauth, out var accounts, out _);

        var failure = await Assert.ThrowsAsync<ProviderOperationException>(
            () => service.ConnectAsync(CancellationToken.None));

        Assert.Equal(ProviderFailureCategory.NetworkUnavailable, failure.Category);
        Assert.Equal(ProviderAuthenticationStatus.Failed, service.State.Status);
        Assert.Equal(1, oauth.DisconnectCalls);
        Assert.Empty(accounts.Values);

        session.ProfileException = null;
        var account = await service.ConnectAsync(CancellationToken.None);
        Assert.Equal("permission-42", account.ProviderAccountId);
        Assert.Equal(ProviderAuthenticationStatus.Connected, service.State.Status);
    }

    [Fact]
    public async Task ConsentDeniedIsRecoverableFailureAndDoesNotLeaveAuthorizationBehind()
    {
        var oauth = new FakeGoogleOAuthClient
        {
            AuthorizeException = new ProviderOperationException(
                ProviderFailureCategory.OAuthAccessDenied,
                "raw oauth details")
        };
        await using var service = CreateService(oauth, out var accounts, out _);

        var failure = await Assert.ThrowsAsync<ProviderOperationException>(
            () => service.ConnectAsync(CancellationToken.None));

        Assert.Equal(ProviderFailureCategory.OAuthAccessDenied, failure.Category);
        Assert.Equal(ProviderAuthenticationStatus.Failed, service.State.Status);
        Assert.Equal(1, oauth.DisconnectCalls);
        Assert.Empty(accounts.Values);
    }

    [Fact]
    public async Task RevokedCachedAuthorizationRequiresReauthenticationAndClearsLocalSession()
    {
        var oauth = new FakeGoogleOAuthClient
        {
            RestoreException = new ProviderOperationException(ProviderFailureCategory.AuthorizationRevoked, "revoked")
        };
        await using var service = CreateService(oauth, out _, out var diagnostics);

        var account = await service.GetCachedAccountAsync(CancellationToken.None);

        Assert.Null(account);
        Assert.Equal(ProviderAuthenticationStatus.ReauthenticationRequired, service.State.Status);
        Assert.Equal(1, oauth.DisconnectCalls);
        Assert.Contains(diagnostics.Events, entry => entry.EventType == "GoogleTokenRestoreFailed");
    }

    [Fact]
    public async Task DisconnectClearsCachedAuthorizationButKeepsNoCloudMutationCapability()
    {
        var oauth = new FakeGoogleOAuthClient { AuthorizedSession = new FakeGoogleDriveSession() };
        await using var service = CreateService(oauth, out var accounts, out _);
        await service.ConnectAsync(CancellationToken.None);

        await service.DisconnectAsync(CancellationToken.None);

        Assert.Empty(accounts.Values);
        Assert.Equal(1, oauth.DisconnectCalls);
        Assert.Equal(ProviderAuthenticationStatus.Disconnected, service.State.Status);
        var provider = new GoogleDriveProvider(service, new FakeProviderDiagnostics());
        Assert.DoesNotContain(StorageCapabilityKind.Write, provider.Descriptor.Capabilities);
        Assert.DoesNotContain(StorageCapabilityKind.CreateFolder, provider.Descriptor.Capabilities);
        Assert.DoesNotContain(StorageCapabilityKind.ExportNativeFile, provider.Descriptor.Capabilities);
    }

    [Fact]
    public async Task ConfigurationChangeDisconnectClearsOnlyLocalAuthorization()
    {
        var oauth = new FakeGoogleOAuthClient { AuthorizedSession = new FakeGoogleDriveSession() };
        await using var service = CreateService(oauth, out var accounts, out var diagnostics);
        await service.ConnectAsync(CancellationToken.None);

        await service.DisconnectLocalAsync(CancellationToken.None);

        Assert.Empty(accounts.Values);
        Assert.Equal(1, oauth.ClearLocalCalls);
        Assert.Equal(0, oauth.DisconnectCalls);
        Assert.Contains(diagnostics.Events, entry => entry.EventType == "GoogleAccountDisconnectedLocally");
    }

    private static GoogleAuthenticationService CreateService(
        FakeGoogleOAuthClient oauth,
        out MemoryAccountRepository accounts,
        out FakeProviderDiagnostics diagnostics)
    {
        accounts = new MemoryAccountRepository();
        diagnostics = new FakeProviderDiagnostics();
        return new GoogleAuthenticationService(oauth, accounts, diagnostics);
    }

    private sealed class FakeGoogleOAuthClient : IGoogleOAuthClient
    {
        public event Action? ConfigurationChanged { add { } remove { } }
        public bool IsConfigured { get; set; } = true;
        public string? ConfigurationMessage { get; set; }
        public IGoogleDriveSession? AuthorizedSession { get; set; } = new FakeGoogleDriveSession();
        public IGoogleDriveSession? RestoredSession { get; set; }
        public Exception? RestoreException { get; set; }
        public bool BlockAuthorization { get; set; }
        public bool WaitForCancellation { get; set; }
        public Exception? AuthorizeException { get; set; }
        public int DisconnectCalls { get; private set; }
        public int ClearLocalCalls { get; private set; }
        public TaskCompletionSource AuthorizationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseAuthorization { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IGoogleDriveSession?> RestoreAsync(CancellationToken cancellationToken) => RestoreException is null
            ? Task.FromResult(RestoredSession)
            : Task.FromException<IGoogleDriveSession?>(RestoreException);

        public async Task<IGoogleDriveSession> AuthorizeAsync(CancellationToken cancellationToken)
        {
            AuthorizationStarted.TrySetResult();
            if (WaitForCancellation) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            if (BlockAuthorization) await ReleaseAuthorization.Task.WaitAsync(cancellationToken);
            if (AuthorizeException is not null) throw AuthorizeException;
            return AuthorizedSession!;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DisconnectCalls++;
            return Task.CompletedTask;
        }

        public Task ClearLocalAuthorizationAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClearLocalCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeGoogleDriveSession : IGoogleDriveSession
    {
        public Exception? ProfileException { get; set; }
        public Task<GoogleAccountProfile> GetAccountProfileAsync(CancellationToken cancellationToken) => ProfileException is null
            ? Task.FromResult(new GoogleAccountProfile("permission-42", "Nguyễn An", "an@example.test"))
            : Task.FromException<GoogleAccountProfile>(ProfileException);
        public Task<GoogleDriveMetadataPage> GetChildrenPageAsync(string parentItemId, string? pageToken, CancellationToken cancellationToken) =>
            Task.FromResult(new GoogleDriveMetadataPage([], null));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class MemoryAccountRepository : IStorageAccountRepository
    {
        public Dictionary<string, StorageAccount> Values { get; } = [];
        public Task UpsertAsync(StorageAccount account, CancellationToken cancellationToken) { Values[account.Id] = account; return Task.CompletedTask; }
        public Task<IReadOnlyList<StorageAccount>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<StorageAccount>>(Values.Values.ToArray());
        public Task RemoveAsync(string id, CancellationToken cancellationToken) { Values.Remove(id); return Task.CompletedTask; }
    }

    private sealed class FakeProviderDiagnostics : IProviderDiagnostics
    {
        public List<(string EventType, string Message, string? Details)> Events { get; } = [];
        public Task WriteAsync(string eventType, string vietnameseMessage, string? technicalDetails, CancellationToken cancellationToken)
        {
            Events.Add((eventType, vietnameseMessage, technicalDetails));
            return Task.CompletedTask;
        }
    }
}

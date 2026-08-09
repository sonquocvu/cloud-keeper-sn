using CloudKeeperSN.Application.Persistence;
using CloudKeeperSN.Domain.Storage;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Services;

namespace CloudKeeperSN.Providers.GoogleDrive.Authentication;

public sealed class GoogleApisOAuthClient : IGoogleOAuthClient, IAsyncDisposable
{
    private const string UserKey = "current-windows-user";
    private const string CallbackCompleteHtml = "<html><head><title>CloudKeeperSN</title></head><body>Đăng nhập đã hoàn tất. Bạn có thể đóng cửa sổ này.</body></html>";
    private static readonly TimeSpan InteractiveAuthorizationTimeout = TimeSpan.FromMinutes(5);
    private readonly GoogleOAuthConfigurationManager _configurationManager;
    private readonly ProtectedGoogleDataStore _dataStore;
    private GoogleApisDriveSession? _activeSession;
    private int _disposed;

    public GoogleApisOAuthClient(GoogleOAuthConfigurationManager configurationManager, IProtectedCredentialStore credentials)
    {
        _configurationManager = configurationManager;
        _dataStore = new ProtectedGoogleDataStore(credentials);
        _configurationManager.Changed += ConfigurationManagerChanged;
    }

    public bool IsConfigured => _configurationManager.EffectiveConfiguration.IsConfigured;
    public string? ConfigurationMessage => _configurationManager.EffectiveConfiguration.ValidationMessage;
    public event Action? ConfigurationChanged;

    public async Task<IGoogleDriveSession?> RestoreAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var flow = CreateFlow();
        try
        {
            var token = await flow.LoadTokenAsync(UserKey, cancellationToken);
            if (token is null)
            {
                flow.Dispose();
                return null;
            }

            var credential = new UserCredential(flow, UserKey, token);
            _ = await credential.GetAccessTokenForRequestAsync(cancellationToken: cancellationToken);
            return ReplaceSession(new GoogleApisDriveSession(flow, credential));
        }
        catch
        {
            flow.Dispose();
            throw;
        }
    }

    public async Task<IGoogleDriveSession> AuthorizeAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var flow = CreateFlow();
        using var authorizationTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        authorizationTimeout.CancelAfter(InteractiveAuthorizationTimeout);
        try
        {
            var receiver = new StateValidatingCodeReceiver(new LocalServerCodeReceiver(
                CallbackCompleteHtml,
                LocalServerCodeReceiver.CallbackUriChooserStrategy.ForceLoopbackIp));
            var installedApp = new AuthorizationCodeInstalledApp(flow, receiver);
            var credential = await installedApp.AuthorizeAsync(UserKey, authorizationTimeout.Token);
            return ReplaceSession(new GoogleApisDriveSession(flow, credential));
        }
        catch (OperationCanceledException exception) when (
            authorizationTimeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            flow.Dispose();
            throw new TimeoutException("The Google OAuth callback did not complete within five minutes.", exception);
        }
        catch
        {
            flow.Dispose();
            throw;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        var session = Interlocked.Exchange(ref _activeSession, null);
        if (session is not null) await session.DisposeAsync();
        if (!IsConfigured)
        {
            await _dataStore.ClearAsync();
            return;
        }

        using var flow = CreateFlow();
        try
        {
            var token = await flow.LoadTokenAsync(UserKey, cancellationToken);
            var tokenToRevoke = token?.RefreshToken ?? token?.AccessToken;
            if (!string.IsNullOrWhiteSpace(tokenToRevoke))
                await flow.RevokeTokenAsync(UserKey, tokenToRevoke, cancellationToken);
        }
        finally
        {
            await _dataStore.ClearAsync();
        }
    }

    public async Task ClearLocalAuthorizationAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = Interlocked.Exchange(ref _activeSession, null);
        if (session is not null) await session.DisposeAsync();
        await _dataStore.ClearAsync();
    }

    private PkceGoogleAuthorizationCodeFlow CreateFlow() => new(new GoogleAuthorizationCodeFlow.Initializer
    {
        ClientSecrets = new ClientSecrets
        {
            ClientId = _configurationManager.EffectiveConfiguration.ClientId,
            ClientSecret = _configurationManager.EffectiveConfiguration.ClientSecret
        },
        Scopes = [GoogleOAuthConfiguration.ReadOnlyScope],
        DataStore = _dataStore,
        Prompt = "select_account"
    });

    private GoogleApisDriveSession ReplaceSession(GoogleApisDriveSession session)
    {
        var previous = Interlocked.Exchange(ref _activeSession, session);
        if (previous is not null) _ = previous.DisposeAsync();
        return session;
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
            throw new ProviderOperationException(ProviderFailureCategory.AuthenticationRequired, ConfigurationMessage!);
    }

    private void ConfigurationManagerChanged(GoogleOAuthConfigurationMetadata _) => ConfigurationChanged?.Invoke();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _configurationManager.Changed -= ConfigurationManagerChanged;
        var session = Interlocked.Exchange(ref _activeSession, null);
        if (session is not null) await session.DisposeAsync();
    }
}

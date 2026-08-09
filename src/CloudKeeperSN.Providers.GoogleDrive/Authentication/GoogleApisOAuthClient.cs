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
    private readonly GoogleOAuthConfiguration _configuration;
    private readonly ProtectedGoogleDataStore _dataStore;
    private GoogleApisDriveSession? _activeSession;
    private int _disposed;

    public GoogleApisOAuthClient(GoogleOAuthConfiguration configuration, IProtectedCredentialStore credentials)
    {
        _configuration = configuration;
        _dataStore = new ProtectedGoogleDataStore(credentials);
    }

    public bool IsConfigured => _configuration.IsConfigured;
    public string? ConfigurationMessage => _configuration.ValidationMessage;

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
        try
        {
            var installedApp = new AuthorizationCodeInstalledApp(flow, new LocalServerCodeReceiver());
            var credential = await installedApp.AuthorizeAsync(UserKey, cancellationToken);
            return ReplaceSession(new GoogleApisDriveSession(flow, credential));
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

    private PkceGoogleAuthorizationCodeFlow CreateFlow() => new(new GoogleAuthorizationCodeFlow.Initializer
    {
        ClientSecrets = new ClientSecrets
        {
            ClientId = _configuration.ClientId,
            ClientSecret = _configuration.ClientSecret
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

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        var session = Interlocked.Exchange(ref _activeSession, null);
        if (session is not null) await session.DisposeAsync();
    }
}

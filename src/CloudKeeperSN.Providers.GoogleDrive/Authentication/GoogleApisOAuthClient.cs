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
    private const string CallbackCompleteHtml = """
        <!doctype html><html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>CloudKeeperSN</title><style>body{font-family:Segoe UI,Arial,sans-serif;margin:0;background:#f4f7fb;color:#172033}main{max-width:560px;margin:12vh auto;padding:32px;border:1px solid #d8e0ec;border-radius:16px;background:white;box-shadow:0 12px 36px #1c31551a}h1{font-size:24px;margin-top:0}p{line-height:1.55;color:#475569}</style></head>
        <body><main><h1 id="title">Đã nhận phản hồi đăng nhập</h1><p id="message">CloudKeeperSN đang trao đổi và xác minh quyền truy cập. Bạn có thể đóng cửa sổ này và quay lại ứng dụng.</p></main>
        <script>(()=>{const p=new URLSearchParams(location.search),e=p.get('error'),c=p.has('code'),s=p.has('state'),t=document.getElementById('title'),m=document.getElementById('message');if(e){t.textContent=e==='access_denied'?'Đăng nhập đã bị hủy hoặc từ chối':'Google không thể hoàn tất yêu cầu';m.textContent='Bạn có thể đóng cửa sổ này và quay lại CloudKeeperSN để xem hướng dẫn thử lại.';}else if(!c||!s){t.textContent='Phản hồi đăng nhập không hợp lệ';m.textContent='CloudKeeperSN sẽ không sử dụng phản hồi này. Bạn có thể đóng cửa sổ và thử kết nối lại trong ứng dụng.';}})();</script></body></html>
        """;
    private static readonly TimeSpan CallbackTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TokenExchangeTimeout = TimeSpan.FromSeconds(45);
    private readonly GoogleOAuthConfigurationManager _configurationManager;
    private readonly ProtectedGoogleDataStore _dataStore;
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
            return new GoogleApisDriveSession(flow, credential);
        }
        catch
        {
            flow.Dispose();
            throw;
        }
    }

    public async Task<IGoogleDriveSession> AuthorizeAsync(
        Func<GoogleOAuthStage, CancellationToken, Task> reportStageAsync,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var flow = CreateFlow();
        try
        {
            var receiver = new StateValidatingCodeReceiver(new LocalServerCodeReceiver(
                CallbackCompleteHtml,
                LocalServerCodeReceiver.CallbackUriChooserStrategy.ForceLoopbackIp), reportStageAsync);

            using var callbackTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            callbackTimeout.CancelAfter(CallbackTimeout);
            AuthorizationCodeResponseUrl response;
            string codeVerifier;
            try
            {
                var request = flow.CreateAuthorizationCodeRequest(receiver.RedirectUri, out codeVerifier);
                response = await receiver.ReceiveCodeAsync(request, callbackTimeout.Token);
            }
            catch (OperationCanceledException exception) when (
                callbackTimeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("The Google OAuth callback did not arrive within five minutes.", exception);
            }

            if (!string.IsNullOrWhiteSpace(response.Error))
                throw new TokenResponseException(new TokenErrorResponse(response));
            if (string.IsNullOrWhiteSpace(response.Code))
                throw new InvalidDataException("The Google OAuth callback did not contain an authorization code.");

            await reportStageAsync(GoogleOAuthStage.ExchangingCode, cancellationToken);
            using var exchangeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            exchangeTimeout.CancelAfter(TokenExchangeTimeout);
            TokenResponse token;
            try
            {
                token = await flow.ExchangeCodeForTokenAsync(
                    UserKey,
                    response.Code,
                    codeVerifier,
                    receiver.RedirectUri,
                    exchangeTimeout.Token);
            }
            catch (OperationCanceledException exception) when (
                exchangeTimeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("The Google OAuth authorization-code exchange timed out.", exception);
            }

            if (string.IsNullOrWhiteSpace(token.AccessToken))
                throw new InvalidDataException("Google returned an unusable OAuth token response.");
            await reportStageAsync(GoogleOAuthStage.AuthorizationStored, cancellationToken);
            return new GoogleApisDriveSession(flow, new UserCredential(flow, UserKey, token));
        }
        catch
        {
            flow.Dispose();
            throw;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _dataStore.ClearAsync();
    }

    public async Task ClearLocalAuthorizationAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
        await Task.CompletedTask;
    }
}

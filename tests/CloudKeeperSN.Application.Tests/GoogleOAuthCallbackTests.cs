using CloudKeeperSN.Domain.Storage;
using CloudKeeperSN.Providers.GoogleDrive;
using CloudKeeperSN.Providers.GoogleDrive.Authentication;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;

namespace CloudKeeperSN.Application.Tests;

public sealed class GoogleOAuthCallbackTests
{
    [Fact]
    public async Task MatchingStateIsAccepted()
    {
        var stages = new List<GoogleOAuthStage>();
        var receiver = new StateValidatingCodeReceiver(
            new FakeCodeReceiver(echoState: true),
            (stage, _) => { stages.Add(stage); return Task.CompletedTask; });
        var request = new AuthorizationCodeRequestUrl(new Uri("https://accounts.google.com/o/oauth2/v2/auth"));

        var response = await receiver.ReceiveCodeAsync(request, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(request.State));
        Assert.Equal(request.State, response.State);
        Assert.Equal("sample-code", response.Code);
        Assert.Equal([
            GoogleOAuthStage.WaitingForCallback,
            GoogleOAuthStage.CallbackReceived,
            GoogleOAuthStage.StateValidated], stages);
    }

    [Fact]
    public async Task MismatchedStateIsRejectedAndClassifiedSafely()
    {
        var stages = new List<GoogleOAuthStage>();
        var receiver = new StateValidatingCodeReceiver(
            new FakeCodeReceiver(echoState: false),
            (stage, _) => { stages.Add(stage); return Task.CompletedTask; });
        var request = new AuthorizationCodeRequestUrl(new Uri("https://accounts.google.com/o/oauth2/v2/auth"));

        var exception = await Assert.ThrowsAsync<GoogleOAuthCallbackException>(
            () => receiver.ReceiveCodeAsync(request, CancellationToken.None));
        var mapped = GoogleProviderExceptionMapper.Map(exception);

        Assert.Equal(GoogleOAuthCallbackFailure.StateMismatch, exception.Failure);
        Assert.Equal(ProviderFailureCategory.OAuthStateMismatch, mapped.Category);
        Assert.DoesNotContain("sample-code", mapped.Message);
        Assert.Equal([
            GoogleOAuthStage.WaitingForCallback,
            GoogleOAuthStage.CallbackReceived], stages);
    }

    [Fact]
    public void OAuthProtocolErrorsHaveActionableCategories()
    {
        Assert.Equal(ProviderFailureCategory.OAuthAccessDenied, MapTokenError("access_denied"));
        Assert.Equal(ProviderFailureCategory.AuthorizationRevoked, MapTokenError("invalid_grant"));
        Assert.Equal(ProviderFailureCategory.OAuthInvalidClient, MapTokenError("invalid_client"));
        Assert.Equal(ProviderFailureCategory.OAuthRedirectMismatch, MapTokenError("redirect_uri_mismatch"));
    }

    [Fact]
    public void CallbackPortAndBrowserFailuresHaveDistinctCategories()
    {
        Assert.Equal(
            ProviderFailureCategory.OAuthCallbackUnavailable,
            GoogleProviderExceptionMapper.Map(new System.Net.HttpListenerException()).Category);
        Assert.Equal(
            ProviderFailureCategory.OAuthBrowserUnavailable,
            GoogleProviderExceptionMapper.Map(new NotSupportedException()).Category);
        Assert.Equal(
            ProviderFailureCategory.RequestTimedOut,
            GoogleProviderExceptionMapper.Map(new TimeoutException()).Category);
    }

    [Fact]
    public async Task CancellationIsForwardedToCallbackReceiver()
    {
        var inner = new BlockingCodeReceiver();
        var receiver = new StateValidatingCodeReceiver(inner);
        var request = new AuthorizationCodeRequestUrl(new Uri("https://accounts.google.com/o/oauth2/v2/auth"));
        using var cancellation = new CancellationTokenSource();

        var pending = receiver.ReceiveCodeAsync(request, cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.True(inner.CancellationObserved);
    }

    private static ProviderFailureCategory MapTokenError(string error)
    {
        var exception = new TokenResponseException(new TokenErrorResponse { Error = error });
        return GoogleProviderExceptionMapper.Map(exception).Category;
    }

    private sealed class FakeCodeReceiver(bool echoState) : ICodeReceiver
    {
        public string RedirectUri => "http://127.0.0.1:49152/authorize/";

        public Task<AuthorizationCodeResponseUrl> ReceiveCodeAsync(
            AuthorizationCodeRequestUrl url,
            CancellationToken taskCancellationToken)
        {
            var values = new Dictionary<string, string>
            {
                ["code"] = "sample-code",
                ["state"] = echoState ? url.State! : "unexpected-state"
            };
            return Task.FromResult(new AuthorizationCodeResponseUrl(values));
        }
    }

    private sealed class BlockingCodeReceiver : ICodeReceiver
    {
        public string RedirectUri => "http://127.0.0.1:49152/authorize/";
        public bool CancellationObserved { get; private set; }

        public async Task<AuthorizationCodeResponseUrl> ReceiveCodeAsync(
            AuthorizationCodeRequestUrl url,
            CancellationToken taskCancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, taskCancellationToken);
                throw new InvalidOperationException();
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }
        }
    }
}

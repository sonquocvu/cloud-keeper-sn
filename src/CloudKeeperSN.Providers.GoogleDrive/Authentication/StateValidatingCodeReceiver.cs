using System.Security.Cryptography;
using System.Text;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;

namespace CloudKeeperSN.Providers.GoogleDrive.Authentication;

public enum GoogleOAuthCallbackFailure
{
    StateMismatch
}

public sealed class GoogleOAuthCallbackException(
    GoogleOAuthCallbackFailure failure,
    string message) : Exception(message)
{
    public GoogleOAuthCallbackFailure Failure { get; } = failure;
}

/// <summary>
/// Adds an unpredictable OAuth state value and rejects callbacks that do not echo it.
/// </summary>
public sealed class StateValidatingCodeReceiver(ICodeReceiver inner) : ICodeReceiver
{
    public string RedirectUri => inner.RedirectUri;

    public async Task<AuthorizationCodeResponseUrl> ReceiveCodeAsync(
        AuthorizationCodeRequestUrl url,
        CancellationToken taskCancellationToken)
    {
        var expectedState = CreateState();
        url.State = expectedState;
        var response = await inner.ReceiveCodeAsync(url, taskCancellationToken);
        if (!FixedTimeEquals(expectedState, response.State))
        {
            throw new GoogleOAuthCallbackException(
                GoogleOAuthCallbackFailure.StateMismatch,
                "The OAuth callback state did not match the authorization request.");
        }

        return response;
    }

    private static string CreateState()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool FixedTimeEquals(string expected, string? actual)
    {
        if (actual is null) return false;
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        return expectedBytes.Length == actualBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}

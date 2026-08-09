using System.Net;
using System.Net.Sockets;
using CloudKeeperSN.Application.Persistence;
using CloudKeeperSN.Domain.Storage;
using Google;
using Google.Apis.Auth.OAuth2.Responses;

namespace CloudKeeperSN.Providers.GoogleDrive;

internal static class GoogleProviderExceptionMapper
{
    public static ProviderOperationException Map(Exception exception)
    {
        if (exception is ProviderOperationException mapped) return mapped;
        if (exception is ProtectedCredentialException)
            return Failure(ProviderFailureCategory.CredentialProtectionFailed, exception);
        if (exception is TokenResponseException tokenException)
        {
            var error = tokenException.Error?.Error;
            var category = string.Equals(error, "access_denied", StringComparison.OrdinalIgnoreCase)
                ? ProviderFailureCategory.AuthorizationCancelled
                : string.Equals(error, "invalid_grant", StringComparison.OrdinalIgnoreCase)
                    ? ProviderFailureCategory.AuthorizationRevoked
                    : ProviderFailureCategory.AuthenticationRequired;
            return Failure(category, exception);
        }
        if (exception is GoogleApiException googleException)
        {
            var category = googleException.HttpStatusCode switch
            {
                HttpStatusCode.Unauthorized => ProviderFailureCategory.AuthenticationRequired,
                HttpStatusCode.Forbidden => ProviderFailureCategory.PermissionDenied,
                HttpStatusCode.NotFound => ProviderFailureCategory.SourceFolderMissing,
                HttpStatusCode.RequestTimeout => ProviderFailureCategory.RequestTimedOut,
                HttpStatusCode.TooManyRequests => ProviderFailureCategory.ProviderThrottled,
                HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout => ProviderFailureCategory.ServiceUnavailable,
                _ => ProviderFailureCategory.UnknownProviderError
            };
            return Failure(category, exception);
        }
        if (exception is HttpRequestException { InnerException: SocketException } or HttpRequestException)
            return Failure(ProviderFailureCategory.NetworkUnavailable, exception);
        if (exception is TimeoutException or TaskCanceledException)
            return Failure(ProviderFailureCategory.RequestTimedOut, exception);
        if (exception is InvalidDataException)
            return Failure(ProviderFailureCategory.InvalidProviderResponse, exception);
        return Failure(ProviderFailureCategory.UnknownProviderError, exception);
    }

    private static ProviderOperationException Failure(ProviderFailureCategory category, Exception inner) =>
        new(category, ProviderFailureMessages.ToVietnamese(category), inner);
}

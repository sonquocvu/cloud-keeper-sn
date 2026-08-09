using System.Text.Json;
using CloudKeeperSN.Application.Persistence;
using Google.Apis.Util.Store;

namespace CloudKeeperSN.Providers.GoogleDrive.Authentication;

internal sealed class ProtectedGoogleDataStore(IProtectedCredentialStore credentials) : IDataStore
{
    private const string ProviderId = "google-drive";

    public Task ClearAsync() => credentials.ClearProviderAsync(ProviderId, CancellationToken.None);

    public Task DeleteAsync<T>(string key) => credentials.DeleteAsync(ProviderId, StorageKey<T>(key), CancellationToken.None);

    public async Task<T> GetAsync<T>(string key)
    {
        var bytes = await credentials.GetAsync(ProviderId, StorageKey<T>(key), CancellationToken.None);
        if (bytes is null) return default!;
        try
        {
            return JsonSerializer.Deserialize<T>(bytes) ?? throw new JsonException("The protected credential payload was empty.");
        }
        catch (JsonException exception)
        {
            throw new ProtectedCredentialException("Thông tin đăng nhập được bảo vệ không còn hợp lệ.", exception);
        }
    }

    public Task StoreAsync<T>(string key, T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        return credentials.StoreAsync(ProviderId, StorageKey<T>(key), bytes, CancellationToken.None);
    }

    private static string StorageKey<T>(string key) => $"{key}|{typeof(T).FullName}";
}

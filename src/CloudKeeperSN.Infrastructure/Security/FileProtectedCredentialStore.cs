using System.Security.Cryptography;
using System.Text;
using CloudKeeperSN.Application.Persistence;

namespace CloudKeeperSN.Infrastructure.Security;

public sealed class FileProtectedCredentialStore(ICredentialProtector protector, string rootDirectory) : IProtectedCredentialStore
{
    public async Task<byte[]?> GetAsync(string providerId, string key, CancellationToken cancellationToken)
    {
        var path = GetPath(providerId, key);
        if (!File.Exists(path)) return null;
        try
        {
            var protectedBytes = await File.ReadAllBytesAsync(path, cancellationToken);
            return protector.Unprotect(protectedBytes, Purpose(providerId, key));
        }
        catch (OperationCanceledException) { throw; }
        catch (ProtectedCredentialException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ProtectedCredentialException("Không thể đọc thông tin đăng nhập được bảo vệ.", exception);
        }
    }

    public async Task StoreAsync(string providerId, string key, ReadOnlyMemory<byte> value, CancellationToken cancellationToken)
    {
        var path = GetPath(providerId, key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        try
        {
            var protectedBytes = protector.Protect(value.Span, Purpose(providerId, key));
            await File.WriteAllBytesAsync(temporaryPath, protectedBytes, cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch (OperationCanceledException) { throw; }
        catch (ProtectedCredentialException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ProtectedCredentialException("Không thể lưu thông tin đăng nhập được bảo vệ.", exception);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public Task DeleteAsync(string providerId, string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetPath(providerId, key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public Task ClearProviderAsync(string providerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = ProviderDirectory(providerId);
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        return Task.CompletedTask;
    }

    private string GetPath(string providerId, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
        return Path.Combine(ProviderDirectory(providerId), hash + ".bin");
    }

    private string ProviderDirectory(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        if (providerId.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
            throw new ArgumentException("Provider ID contains unsupported characters.", nameof(providerId));
        return Path.Combine(rootDirectory, providerId);
    }

    private static string Purpose(string providerId, string key) => $"{providerId}|{Environment.UserName}|{key}";
}

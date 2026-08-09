using System.Security.Cryptography;
using System.Runtime.Versioning;
using System.Text;
using CloudKeeperSN.Application.Persistence;

namespace CloudKeeperSN.Infrastructure.Security;

public sealed class DpapiCredentialProtector : ICredentialProtector
{
    public byte[] Protect(ReadOnlySpan<byte> plaintext, string purpose)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows DPAPI is required for production credential protection.");
        try
        {
            return ProtectForCurrentWindowsUser(plaintext.ToArray(), Entropy(purpose));
        }
        catch (CryptographicException exception)
        {
            throw new ProtectedCredentialException("Không thể bảo vệ thông tin đăng nhập cục bộ.", exception);
        }
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedData, string purpose)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows DPAPI is required for production credential protection.");
        try
        {
            return UnprotectForCurrentWindowsUser(protectedData.ToArray(), Entropy(purpose));
        }
        catch (CryptographicException exception)
        {
            throw new ProtectedCredentialException("Không thể khôi phục thông tin đăng nhập cục bộ.", exception);
        }
    }

    private static byte[] Entropy(string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        return SHA256.HashData(Encoding.UTF8.GetBytes("CloudKeeperSN|credential|v1|" + purpose));
    }

    [SupportedOSPlatform("windows")]
    private static byte[] ProtectForCurrentWindowsUser(byte[] plaintext, byte[] entropy) =>
        ProtectedData.Protect(plaintext, entropy, DataProtectionScope.CurrentUser);

    [SupportedOSPlatform("windows")]
    private static byte[] UnprotectForCurrentWindowsUser(byte[] protectedData, byte[] entropy) =>
        ProtectedData.Unprotect(protectedData, entropy, DataProtectionScope.CurrentUser);
}

using System.Text;
using CloudKeeperSN.Application.Persistence;
using CloudKeeperSN.Infrastructure.Security;

namespace CloudKeeperSN.Infrastructure.Tests;

public sealed class ProtectedCredentialStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "CloudKeeperSN.Credentials.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task StoreRoundTripsThroughProtectorWithoutPlaintextOnDisk()
    {
        var store = new FileProtectedCredentialStore(new ReversingProtector(), _directory);
        var plaintext = Encoding.UTF8.GetBytes("refresh-token-super-secret");

        await store.StoreAsync("google-drive", "current", plaintext, CancellationToken.None);
        var restored = await store.GetAsync("google-drive", "current", CancellationToken.None);
        var diskBytes = await File.ReadAllBytesAsync(Directory.GetFiles(_directory, "*.bin", SearchOption.AllDirectories).Single());

        Assert.Equal(plaintext, restored);
        Assert.DoesNotContain("refresh-token-super-secret", Encoding.UTF8.GetString(diskBytes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedDecryptionIsReportedWithoutExposingPayload()
    {
        var writer = new FileProtectedCredentialStore(new ReversingProtector(), _directory);
        await writer.StoreAsync("google-drive", "current", Encoding.UTF8.GetBytes("sensitive-value"), CancellationToken.None);
        var reader = new FileProtectedCredentialStore(new FailingProtector(), _directory);

        var exception = await Assert.ThrowsAsync<ProtectedCredentialException>(() =>
            reader.GetAsync("google-drive", "current", CancellationToken.None));

        Assert.DoesNotContain("sensitive-value", exception.ToString(), StringComparison.Ordinal);
    }

    private sealed class ReversingProtector : ICredentialProtector
    {
        public byte[] Protect(ReadOnlySpan<byte> plaintext, string purpose) => plaintext.ToArray().Reverse().Append((byte)0xA5).ToArray();
        public byte[] Unprotect(ReadOnlySpan<byte> protectedData, string purpose) => protectedData[..^1].ToArray().Reverse().ToArray();
    }

    private sealed class FailingProtector : ICredentialProtector
    {
        public byte[] Protect(ReadOnlySpan<byte> plaintext, string purpose) => throw new NotSupportedException();
        public byte[] Unprotect(ReadOnlySpan<byte> protectedData, string purpose) => throw new ProtectedCredentialException("Không thể giải mã.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}

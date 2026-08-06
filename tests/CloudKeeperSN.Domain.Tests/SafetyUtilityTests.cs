using CloudKeeperSN.Domain.Diagnostics;
using CloudKeeperSN.Domain.Scanning;

namespace CloudKeeperSN.Domain.Tests;

public sealed class SafetyUtilityTests
{
    [Fact]
    public void Redactor_RemovesTokensAuthorizationCodesAndPasswords()
    {
        const string input = "Authorization: Bearer token123 access_token=abc&code=secret-code password=hunter2";

        var result = SensitiveDataRedactor.Redact(input);

        Assert.DoesNotContain("token123", result);
        Assert.DoesNotContain("abc", result);
        Assert.DoesNotContain("secret-code", result);
        Assert.DoesNotContain("hunter2", result);
        Assert.Contains("[ĐÃ ẨN]", result);
    }

    [Fact]
    public void CycleGuard_UsesProviderAccountAndItemIdentity()
    {
        var guard = new TraversalCycleGuard();

        Assert.True(guard.TryEnter("account-a", "folder-1"));
        Assert.False(guard.TryEnter("account-a", "folder-1"));
        Assert.True(guard.TryEnter("account-b", "folder-1"));
    }
}


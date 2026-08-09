using CloudKeeperSN.Domain.Diagnostics;
using CloudKeeperSN.Domain.Scanning;

namespace CloudKeeperSN.Domain.Tests;

public sealed class SafetyUtilityTests
{
    [Fact]
    public void Redactor_RemovesTokensAuthorizationCodesAndPasswords()
    {
        const string input = "Authorization: Bearer token123 access_token=abc&code=secret-code&state=oauth-state password=hunter2 code_verifier=pkce-secret-value";

        var result = SensitiveDataRedactor.Redact(input);

        Assert.DoesNotContain("token123", result);
        Assert.DoesNotContain("abc", result);
        Assert.DoesNotContain("secret-code", result);
        Assert.DoesNotContain("hunter2", result);
        Assert.DoesNotContain("oauth-state", result);
        Assert.DoesNotContain("pkce-secret-value", result);
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

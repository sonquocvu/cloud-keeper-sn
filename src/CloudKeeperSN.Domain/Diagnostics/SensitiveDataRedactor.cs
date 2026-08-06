using System.Text.RegularExpressions;

namespace CloudKeeperSN.Domain.Diagnostics;

public static partial class SensitiveDataRedactor
{
    private const string Replacement = "[ĐÃ ẨN]";

    public static string Redact(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var result = AuthorizationHeaderRegex().Replace(value, "$1" + Replacement);
        result = SensitiveKeyValueRegex().Replace(result, "$1" + Replacement);
        result = OAuthQueryRegex().Replace(result, "$1" + Replacement);
        return result;
    }

    [GeneratedRegex("(?i)(authorization\\s*[:=]\\s*)(?:bearer\\s+)?[^\\s,;]+")]
    private static partial Regex AuthorizationHeaderRegex();

    [GeneratedRegex("(?i)((?:access_token|refresh_token|client_secret|password)\\s*[:=]\\s*)[^\\s,;&]+")]
    private static partial Regex SensitiveKeyValueRegex();

    [GeneratedRegex("(?i)([?&](?:code|access_token|refresh_token|client_secret)=)[^&\\s]+")]
    private static partial Regex OAuthQueryRegex();
}

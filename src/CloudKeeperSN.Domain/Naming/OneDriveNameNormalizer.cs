using System.Security.Cryptography;
using System.Text;

namespace CloudKeeperSN.Domain.Naming;

public static class OneDriveNameNormalizer
{
    private static readonly HashSet<char> IllegalCharacters = ['"', '*', ':', '<', '>', '?', '/', '\\', '|'];
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        ".LOCK", "DESKTOP.INI"
    };

    public static string Normalize(string originalName, int maximumLength = 255)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalName);
        if (maximumLength < 16) throw new ArgumentOutOfRangeException(nameof(maximumLength));

        var sanitized = new string(originalName.Select(character =>
            char.IsControl(character) || IllegalCharacters.Contains(character) ? '_' : character).ToArray());
        sanitized = sanitized.Trim().TrimEnd('.');
        if (sanitized.Length == 0) sanitized = "_";

        var stem = Path.GetFileNameWithoutExtension(sanitized);
        if (ReservedNames.Contains(stem) || ReservedNames.Contains(sanitized))
        {
            sanitized = $"_{sanitized}";
        }

        return sanitized.Length <= maximumLength ? sanitized : Shorten(sanitized, maximumLength);
    }

    private static string Shorten(string name, int maximumLength)
    {
        var extension = Path.GetExtension(name);
        var stem = Path.GetFileNameWithoutExtension(name);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..8];
        var suffix = $"~{hash}{extension}";
        var stemLength = Math.Max(1, maximumLength - suffix.Length);
        return $"{stem[..Math.Min(stem.Length, stemLength)]}{suffix}";
    }
}


using CloudKeeperSN.Domain.Storage;

namespace CloudKeeperSN.Domain.Transfers;

public enum VerificationLevel
{
    VerifiedByStrongHash,
    VerifiedByProviderHash,
    VerifiedBySizeAndMetadata,
    UploadedButNotFullyVerified,
    VerificationFailed
}

public sealed record VerificationResult(
    VerificationLevel Level,
    string VietnameseExplanation,
    string? SourceEvidence = null,
    string? DestinationEvidence = null);

public static class ChecksumCompatibility
{
    private static readonly IReadOnlySet<string> StrongAlgorithms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "SHA-256", "SHA256", "SHA-512", "SHA512"
    };

    public static bool AreCompatible(ProviderChecksum source, ProviderChecksum destination) =>
        string.Equals(Normalize(source.Algorithm), Normalize(destination.Algorithm), StringComparison.OrdinalIgnoreCase);

    public static bool ValuesMatch(ProviderChecksum source, ProviderChecksum destination) =>
        AreCompatible(source, destination) &&
        string.Equals(source.Value, destination.Value, StringComparison.OrdinalIgnoreCase);

    public static bool IsStrong(ProviderChecksum checksum) => StrongAlgorithms.Contains(checksum.Algorithm);

    private static string Normalize(string algorithm) => algorithm.Replace("-", string.Empty, StringComparison.Ordinal).Trim();
}


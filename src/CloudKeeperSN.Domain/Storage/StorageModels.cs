using System.Collections.ObjectModel;

namespace CloudKeeperSN.Domain.Storage;

public enum StorageItemKind
{
    File,
    Folder,
    Shortcut,
    ProviderNativeFile
}

public enum StorageCapabilityKind
{
    Authenticate,
    Browse,
    ReadMetadata,
    Read,
    Write,
    CreateFolder,
    ResumableUpload,
    PlanNativeExport,
    ExportNativeFile,
    ProviderChecksum
}

public sealed record StorageAccount(
    string Id,
    string ProviderId,
    string ProviderAccountId,
    string DisplayName,
    bool IsConnected,
    DateTimeOffset? LastConnectedAtUtc,
    string? Email = null);

public sealed record ProviderChecksum(string Algorithm, string Value)
{
    public string NormalizedAlgorithm => Algorithm.Trim().ToUpperInvariant();
}

public sealed record StorageItem
{
    public required string ProviderId { get; init; }
    public required string ProviderAccountId { get; init; }
    public required string ItemId { get; init; }
    public string? ParentItemId { get; init; }
    public required string Name { get; init; }
    public required StorageItemKind Kind { get; init; }
    public string? MimeType { get; init; }
    public long? Size { get; init; }
    public DateTimeOffset? CreatedAtUtc { get; init; }
    public DateTimeOffset? ModifiedAtUtc { get; init; }
    public IReadOnlyList<ProviderChecksum> Checksums { get; init; } = [];
    public IReadOnlyDictionary<string, string> ProviderMetadata { get; init; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
}

public sealed record StorageProviderDescriptor(
    string ProviderId,
    string DisplayName,
    IReadOnlySet<StorageCapabilityKind> Capabilities)
{
    public bool Supports(StorageCapabilityKind capability) => Capabilities.Contains(capability);
}

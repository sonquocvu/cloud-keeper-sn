namespace CloudKeeperSN.Domain.Scanning;

public enum DriveInventoryRunStatus
{
    Scanning,
    Completed,
    Cancelled,
    Failed,
    RequiresReauthentication,
    Interrupted
}

public enum DriveInventoryItemKind
{
    File,
    Folder,
    GoogleWorkspaceFile,
    Shortcut
}

public enum DriveInventoryLocation
{
    MyDrive,
    Shared,
    Unresolved
}

public sealed record DriveStorageInformation(
    long? StorageLimitBytes,
    long? TotalUsageBytes,
    long? DriveUsageBytes,
    long? TrashUsageBytes);

public sealed record DriveInventoryItem(
    Guid ScanId,
    string FileId,
    string Name,
    string? ParentId,
    string DisplayPath,
    string MimeType,
    DriveInventoryItemKind Kind,
    DriveInventoryLocation Location,
    long? Size,
    DateTimeOffset? CreatedAtUtc,
    DateTimeOffset? ModifiedAtUtc,
    string? Md5Checksum,
    string? FileExtension,
    string? ShortcutTargetId,
    string? ShortcutTargetMimeType,
    bool IsShared,
    bool? IsOwnedByUser,
    bool IsBackupEligible,
    string? SkipReason);

public sealed record DriveInventoryRun(
    Guid ScanId,
    string ProviderId,
    string ProviderAccountId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DriveInventoryRunStatus Status,
    int TotalItems,
    int FolderCount,
    int FileCount,
    long KnownBytes,
    int UnknownSizeCount,
    int GoogleWorkspaceFileCount,
    int ShortcutCount,
    int UnresolvedCount,
    int BackupEligibleCount,
    string? FailureCategory,
    bool IsComplete,
    DriveStorageInformation? StorageInformation);

public sealed record DriveHierarchyNode(
    string FileId,
    string Name,
    string? ParentId,
    bool IsShared,
    bool? IsOwnedByUser);

public sealed record DriveHierarchyResult(
    IReadOnlyDictionary<string, (string Path, DriveInventoryLocation Location)> Paths,
    int UnresolvedCount);

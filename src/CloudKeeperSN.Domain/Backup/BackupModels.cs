using CloudKeeperSN.Domain.Storage;
using CloudKeeperSN.Domain.Transfers;

namespace CloudKeeperSN.Domain.Backup;

public sealed record BackupDefinition(
    Guid Id,
    string Name,
    string SourceProviderId,
    string SourceAccountId,
    string SourceFolderId,
    string DestinationProviderId,
    string DestinationAccountId,
    string DestinationFolderId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public enum BackupRunStatus
{
    Scanning,
    PreviewReady,
    Confirmed,
    Running,
    Paused,
    Completed,
    CompletedWithWarnings,
    Failed,
    Cancelled,
    Interrupted
}

public sealed record BackupRun(
    Guid Id,
    Guid BackupDefinitionId,
    BackupRunStatus Status,
    bool PreviewWasShown,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record SourceDestinationMapping(
    string SourceProviderAccountId,
    string SourceItemId,
    string DestinationProviderAccountId,
    string DestinationParentItemId,
    string DestinationName,
    string? DestinationItemId,
    string SourceFingerprint,
    DateTimeOffset UpdatedAtUtc);

public sealed record BackupPreview(
    Guid RunId,
    int FileCount,
    int FolderCount,
    long EstimatedSourceBytes,
    IReadOnlyList<PreviewNotice> Notices,
    IReadOnlyList<PlannedTransfer> Transfers);

public sealed record PreviewNotice(PreviewNoticeKind Kind, string VietnameseMessage, string? SourceItemId = null);

public enum PreviewNoticeKind
{
    Information,
    Warning,
    Conflict,
    Unsupported
}

public sealed record PlannedTransfer(
    string SourceItemId,
    StoragePath RelativeSourcePath,
    string DestinationName,
    TransferDecision Decision,
    string VietnameseReason);

public enum TransferDecision
{
    Copy,
    CreateFolder,
    SkipAlreadyTransferred,
    SkipUnsupported,
    RenameForConflict,
    Warn
}


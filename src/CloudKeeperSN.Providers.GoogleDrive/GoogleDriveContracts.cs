namespace CloudKeeperSN.Providers.GoogleDrive;

public sealed record GoogleAccountProfile(string AccountId, string DisplayName, string? EmailAddress);

public sealed record GoogleDriveStorageInformation(
    long? StorageLimitBytes,
    long? TotalUsageBytes,
    long? DriveUsageBytes,
    long? TrashUsageBytes);

public sealed record GoogleDriveItemMetadata(
    string Id,
    string Name,
    string MimeType,
    IReadOnlyList<string> ParentIds,
    long? Size,
    DateTimeOffset? CreatedAtUtc,
    DateTimeOffset? ModifiedAtUtc,
    string? Md5Checksum,
    long? Version,
    string? ShortcutTargetId,
    string? ShortcutTargetMimeType,
    bool? CanDownload,
    bool IsTrashed = false,
    string? FileExtension = null,
    bool IsShared = false,
    bool? IsOwnedByUser = null);

public sealed record GoogleDriveMetadataPage(
    IReadOnlyList<GoogleDriveItemMetadata> Items,
    string? NextPageToken,
    bool IsIncomplete = false);

public interface IGoogleDriveSession : IAsyncDisposable
{
    Task<GoogleAccountProfile> GetAccountProfileAsync(CancellationToken cancellationToken);
    Task<GoogleDriveStorageInformation> GetStorageInformationAsync(CancellationToken cancellationToken);
    Task VerifyReadOnlyAccessAsync(CancellationToken cancellationToken);
    Task<GoogleDriveMetadataPage> GetChildrenPageAsync(string parentItemId, string? pageToken, CancellationToken cancellationToken);
    Task<GoogleDriveMetadataPage> GetInventoryPageAsync(string? pageToken, CancellationToken cancellationToken);
}

public enum GoogleOAuthStage
{
    WaitingForCallback,
    CallbackReceived,
    StateValidated,
    ExchangingCode,
    AuthorizationStored
}

public interface IGoogleOAuthClient
{
    bool IsConfigured { get; }
    string? ConfigurationMessage { get; }
    event Action? ConfigurationChanged;
    Task<IGoogleDriveSession?> RestoreAsync(CancellationToken cancellationToken);
    Task<IGoogleDriveSession> AuthorizeAsync(
        Func<GoogleOAuthStage, CancellationToken, Task> reportStageAsync,
        CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);
    Task ClearLocalAuthorizationAsync(CancellationToken cancellationToken);
}

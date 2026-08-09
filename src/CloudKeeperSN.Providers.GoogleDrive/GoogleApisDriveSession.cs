using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Drive.v3;
using Google.Apis.Services;

namespace CloudKeeperSN.Providers.GoogleDrive;

internal sealed class GoogleApisDriveSession : IGoogleDriveSession
{
    private readonly IAuthorizationCodeFlow _flow;
    private readonly DriveService _service;
    private readonly GoogleRequestExecutor _requests;
    private readonly GoogleRetryAfterCapture _retryAfter = new();

    public GoogleApisDriveSession(IAuthorizationCodeFlow flow, UserCredential credential, GoogleRequestExecutor? requests = null)
    {
        _flow = flow;
        _requests = requests ?? new GoogleRequestExecutor();
        _service = new DriveService(new BaseClientService.Initializer
        {
            ApplicationName = "CloudKeeperSN",
            HttpClientInitializer = credential
        });
        _service.HttpClient.MessageHandler.AddUnsuccessfulResponseHandler(_retryAfter);
    }

    public async Task<GoogleAccountProfile> GetAccountProfileAsync(CancellationToken cancellationToken)
    {
        var request = _service.About.Get();
        request.Fields = "user(displayName,emailAddress,permissionId)";
        var about = await ExecuteAsync(token => request.ExecuteAsync(token), cancellationToken);
        var user = about.User ?? throw new InvalidDataException("Google Drive did not return the current user profile.");
        var accountId = user.PermissionId;
        if (string.IsNullOrWhiteSpace(accountId))
            throw new InvalidDataException("Google Drive did not return a stable account permission ID.");
        return new GoogleAccountProfile(accountId, user.DisplayName ?? user.EmailAddress ?? "Tài khoản Google", user.EmailAddress);
    }

    public async Task<GoogleDriveStorageInformation> GetStorageInformationAsync(CancellationToken cancellationToken)
    {
        var request = _service.About.Get();
        request.Fields = "storageQuota(limit,usage,usageInDrive,usageInDriveTrash)";
        var about = await ExecuteAsync(token => request.ExecuteAsync(token), cancellationToken);
        return new GoogleDriveStorageInformation(
            about.StorageQuota?.Limit,
            about.StorageQuota?.Usage,
            about.StorageQuota?.UsageInDrive,
            about.StorageQuota?.UsageInDriveTrash);
    }

    public async Task VerifyReadOnlyAccessAsync(CancellationToken cancellationToken)
    {
        var request = _service.Files.List();
        request.Spaces = "drive";
        request.Corpora = "user";
        request.PageSize = 1;
        request.Fields = "files(id)";
        _ = await ExecuteAsync(token => request.ExecuteAsync(token), cancellationToken);
    }

    public async Task<GoogleDriveMetadataPage> GetChildrenPageAsync(string parentItemId, string? pageToken, CancellationToken cancellationToken)
    {
        var request = _service.Files.List();
        request.Q = GoogleDriveQuery.ChildrenOf(parentItemId);
        request.Spaces = "drive";
        request.Corpora = "user";
        request.PageSize = 200;
        request.PageToken = pageToken;
        request.OrderBy = "folder,name_natural";
        request.Fields = "nextPageToken,incompleteSearch,files(id,name,mimeType,parents,size,createdTime,modifiedTime,md5Checksum,version,shortcutDetails(targetId,targetMimeType),capabilities(canDownload))";
        var page = await ExecuteAsync(token => request.ExecuteAsync(token), cancellationToken);
        var items = (page.Files ?? []).Select(Map).ToArray();
        return new GoogleDriveMetadataPage(items, page.NextPageToken, page.IncompleteSearch ?? false);
    }

    public async Task<GoogleDriveMetadataPage> GetInventoryPageAsync(string? pageToken, CancellationToken cancellationToken)
    {
        var request = _service.Files.List();
        request.Q = "trashed = false";
        request.Spaces = "drive";
        request.Corpora = "user";
        request.IncludeItemsFromAllDrives = false;
        request.SupportsAllDrives = false;
        request.PageSize = 1000;
        request.PageToken = pageToken;
        request.Fields = "nextPageToken,incompleteSearch,files(id,name,mimeType,parents,size,createdTime,modifiedTime,trashed,md5Checksum,fileExtension,shared,ownedByMe,shortcutDetails(targetId,targetMimeType))";
        var page = await ExecuteAsync(token => request.ExecuteAsync(token), cancellationToken);
        return new GoogleDriveMetadataPage((page.Files ?? []).Select(Map).ToArray(), page.NextPageToken, page.IncompleteSearch ?? false);
    }

    private Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        _retryAfter.Reset();
        return _requests.ExecuteAsync(operation, cancellationToken, _retryAfter.Consume);
    }

    private static GoogleDriveItemMetadata Map(Google.Apis.Drive.v3.Data.File item) => new(
        item.Id ?? throw new InvalidDataException("Google Drive item has no ID."),
        item.Name ?? "Mục không có tên",
        item.MimeType ?? "application/octet-stream",
        item.Parents?.ToArray() ?? [],
        item.Size,
        item.CreatedTimeDateTimeOffset,
        item.ModifiedTimeDateTimeOffset,
        item.Md5Checksum,
        item.Version,
        item.ShortcutDetails?.TargetId,
        item.ShortcutDetails?.TargetMimeType,
        item.Capabilities?.CanDownload,
        item.Trashed ?? false,
        item.FileExtension,
        item.Shared ?? false,
        item.OwnedByMe);

    public ValueTask DisposeAsync()
    {
        _service.Dispose();
        _flow.Dispose();
        return ValueTask.CompletedTask;
    }
}

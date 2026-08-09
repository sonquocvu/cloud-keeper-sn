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

    public GoogleApisDriveSession(IAuthorizationCodeFlow flow, UserCredential credential, GoogleRequestExecutor? requests = null)
    {
        _flow = flow;
        _requests = requests ?? new GoogleRequestExecutor();
        _service = new DriveService(new BaseClientService.Initializer
        {
            ApplicationName = "CloudKeeperSN",
            HttpClientInitializer = credential
        });
    }

    public async Task<GoogleAccountProfile> GetAccountProfileAsync(CancellationToken cancellationToken)
    {
        var request = _service.About.Get();
        request.Fields = "user(displayName,emailAddress,permissionId)";
        var about = await _requests.ExecuteAsync(token => request.ExecuteAsync(token), cancellationToken);
        var user = about.User ?? throw new InvalidDataException("Google Drive did not return the current user profile.");
        var accountId = user.PermissionId;
        if (string.IsNullOrWhiteSpace(accountId))
            throw new InvalidDataException("Google Drive did not return a stable account permission ID.");
        return new GoogleAccountProfile(accountId, user.DisplayName ?? user.EmailAddress ?? "Tài khoản Google", user.EmailAddress);
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
        var page = await _requests.ExecuteAsync(token => request.ExecuteAsync(token), cancellationToken);
        var items = (page.Files ?? []).Select(Map).ToArray();
        return new GoogleDriveMetadataPage(items, page.NextPageToken, page.IncompleteSearch ?? false);
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
        item.Capabilities?.CanDownload);

    public ValueTask DisposeAsync()
    {
        _service.Dispose();
        _flow.Dispose();
        return ValueTask.CompletedTask;
    }
}

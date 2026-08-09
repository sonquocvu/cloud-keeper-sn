using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using CloudKeeperSN.Application.Persistence;
using CloudKeeperSN.Application.Storage;
using CloudKeeperSN.Application.Scanning;
using CloudKeeperSN.Domain.Export;
using CloudKeeperSN.Domain.Scanning;
using CloudKeeperSN.Domain.Storage;
using CloudKeeperSN.Providers.GoogleDrive.Authentication;

namespace CloudKeeperSN.Providers.GoogleDrive;

public sealed class GoogleDriveProvider(
    GoogleAuthenticationService authentication,
    IProviderDiagnostics diagnostics) : IStorageProvider, IPagedStorageBrowserCapability, IDriveInventorySource
{
    public const string ProviderId = "google-drive";
    public const string RootFolderId = "root";
    public const string FolderMimeType = "application/vnd.google-apps.folder";

    public StorageProviderDescriptor Descriptor { get; } = new(
        ProviderId,
        "Google Drive",
        new HashSet<StorageCapabilityKind>
        {
            StorageCapabilityKind.Authenticate,
            StorageCapabilityKind.Browse,
            StorageCapabilityKind.ReadMetadata,
            StorageCapabilityKind.PlanNativeExport,
            StorageCapabilityKind.ProviderChecksum
        });

    public Task<StorageAccount?> GetConnectedAccountAsync(CancellationToken cancellationToken) =>
        authentication.GetCachedAccountAsync(cancellationToken);

    public async Task<StorageItemPage> GetChildrenPageAsync(
        string providerAccountId,
        string parentItemId,
        string? continuationToken,
        CancellationToken cancellationToken)
    {
        try
        {
            var session = await authentication.GetRequiredSessionAsync(cancellationToken);
            var account = authentication.CurrentAccount;
            if (account is null || !string.Equals(account.ProviderAccountId, providerAccountId, StringComparison.Ordinal))
                throw new ProviderOperationException(ProviderFailureCategory.AuthenticationRequired, ProviderFailureMessages.ToVietnamese(ProviderFailureCategory.AuthenticationRequired));
            var page = await session.GetChildrenPageAsync(parentItemId, continuationToken, cancellationToken);
            var items = page.Items.Select(item => Map(account.ProviderAccountId, item)).ToArray();
            await diagnostics.WriteAsync("GoogleFolderPageLoaded", "Đã tải một trang thư mục Google Drive.", $"items={items.Length}; hasNext={!string.IsNullOrWhiteSpace(page.NextPageToken)}", cancellationToken);
            return new StorageItemPage(items, page.NextPageToken, page.IsIncomplete);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            throw GoogleProviderExceptionMapper.Map(exception);
        }
    }

    public async IAsyncEnumerable<StorageItem> GetChildrenAsync(
        string providerAccountId,
        string parentItemId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var seenTokens = new HashSet<string>(StringComparer.Ordinal);
        string? token = null;
        var pageCount = 0;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++pageCount > 10_000)
                throw InvalidPagination("Google Drive returned too many continuation pages.");
            var page = await GetChildrenPageAsync(providerAccountId, parentItemId, token, cancellationToken);
            if (page.IsIncomplete)
                throw InvalidPagination("Google Drive marked the folder listing as incomplete.");
            foreach (var item in page.Items) yield return item;
            token = string.IsNullOrWhiteSpace(page.ContinuationToken) ? null : page.ContinuationToken;
            if (token is not null && !seenTokens.Add(token))
                throw InvalidPagination("Google Drive repeated a continuation token.");
        } while (token is not null);
    }

    public async Task<DriveStorageInformation> GetStorageInformationAsync(CancellationToken cancellationToken)
    {
        try
        {
            var session = await authentication.GetRequiredSessionAsync(cancellationToken);
            var storage = await session.GetStorageInformationAsync(cancellationToken);
            return new DriveStorageInformation(
                storage.StorageLimitBytes, storage.TotalUsageBytes, storage.DriveUsageBytes, storage.TrashUsageBytes);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { throw GoogleProviderExceptionMapper.Map(exception); }
    }

    public async Task<DriveInventoryPage> GetInventoryPageAsync(
        Guid scanId,
        string providerAccountId,
        string? pageToken,
        CancellationToken cancellationToken)
    {
        try
        {
            var session = await authentication.GetRequiredSessionAsync(cancellationToken);
            var account = authentication.CurrentAccount;
            if (account is null || !string.Equals(account.ProviderAccountId, providerAccountId, StringComparison.Ordinal))
                throw new ProviderOperationException(ProviderFailureCategory.AuthenticationRequired,
                    ProviderFailureMessages.ToVietnamese(ProviderFailureCategory.AuthenticationRequired));
            var page = await session.GetInventoryPageAsync(pageToken, cancellationToken);
            var items = page.Items.Where(item => !item.IsTrashed).Select(item => MapInventory(scanId, item)).ToArray();
            return new DriveInventoryPage(items, page.NextPageToken, page.IsIncomplete);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { throw GoogleProviderExceptionMapper.Map(exception); }
    }

    private static StorageItem Map(string accountId, GoogleDriveItemMetadata item)
    {
        var kind = item.MimeType switch
        {
            FolderMimeType => StorageItemKind.Folder,
            GoogleNativeExportPolicy.GoogleShortcut => StorageItemKind.Shortcut,
            _ when item.MimeType.StartsWith("application/vnd.google-apps.", StringComparison.Ordinal) => StorageItemKind.ProviderNativeFile,
            _ => StorageItemKind.File
        };
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["discoveredAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["parentIds"] = string.Join("|", item.ParentIds)
        };
        if (item.Version is { } version) metadata["version"] = version.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (item.ShortcutTargetId is { } targetId) metadata["shortcutTargetId"] = targetId;
        if (item.ShortcutTargetMimeType is { } targetMime) metadata["shortcutTargetMimeType"] = targetMime;
        if (item.CanDownload is { } canDownload) metadata["canDownload"] = canDownload.ToString();

        return new StorageItem
        {
            ProviderId = ProviderId,
            ProviderAccountId = accountId,
            ItemId = item.Id,
            ParentItemId = item.ParentIds.FirstOrDefault(),
            Name = item.Name,
            Kind = kind,
            MimeType = item.MimeType,
            Size = item.Size,
            CreatedAtUtc = item.CreatedAtUtc,
            ModifiedAtUtc = item.ModifiedAtUtc,
            Checksums = string.IsNullOrWhiteSpace(item.Md5Checksum) ? [] : [new ProviderChecksum("MD5", item.Md5Checksum)],
            ProviderMetadata = new ReadOnlyDictionary<string, string>(metadata)
        };
    }

    private static DriveInventoryItem MapInventory(Guid scanId, GoogleDriveItemMetadata item)
    {
        var kind = item.MimeType switch
        {
            FolderMimeType => DriveInventoryItemKind.Folder,
            GoogleNativeExportPolicy.GoogleShortcut => DriveInventoryItemKind.Shortcut,
            _ when item.MimeType.StartsWith("application/vnd.google-apps.", StringComparison.Ordinal) => DriveInventoryItemKind.GoogleWorkspaceFile,
            _ => DriveInventoryItemKind.File
        };
        var nativeDecision = kind == DriveInventoryItemKind.GoogleWorkspaceFile
            ? GoogleNativeExportPolicy.Decide(item.MimeType)
            : null;
        var eligible = kind == DriveInventoryItemKind.File || nativeDecision?.IsSupported == true;
        var skipReason = kind switch
        {
            DriveInventoryItemKind.Folder => "Thư mục được lưu để dựng cấu trúc; không phải nội dung tệp.",
            DriveInventoryItemKind.Shortcut => "Lối tắt được ghi nhận nhưng không tự động lần theo.",
            DriveInventoryItemKind.GoogleWorkspaceFile when nativeDecision?.IsSupported != true => nativeDecision?.VietnameseExplanation,
            _ => null
        };
        var location = item.IsShared || item.IsOwnedByUser == false
            ? DriveInventoryLocation.Shared
            : DriveInventoryLocation.MyDrive;
        return new DriveInventoryItem(
            scanId,
            item.Id,
            item.Name,
            item.ParentIds.FirstOrDefault(),
            $"Đang xác định/{item.Name.Replace('/', '／')}",
            item.MimeType,
            kind,
            location,
            item.Size,
            item.CreatedAtUtc,
            item.ModifiedAtUtc,
            item.Md5Checksum,
            item.FileExtension,
            item.ShortcutTargetId,
            item.ShortcutTargetMimeType,
            item.IsShared,
            item.IsOwnedByUser,
            eligible,
            skipReason);
    }

    private static ProviderOperationException InvalidPagination(string technicalMessage) => new(
        ProviderFailureCategory.InvalidProviderResponse,
        ProviderFailureMessages.ToVietnamese(ProviderFailureCategory.InvalidProviderResponse),
        new InvalidDataException(technicalMessage));
}

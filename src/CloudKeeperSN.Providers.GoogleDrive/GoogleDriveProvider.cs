using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using CloudKeeperSN.Application.Persistence;
using CloudKeeperSN.Application.Storage;
using CloudKeeperSN.Domain.Export;
using CloudKeeperSN.Domain.Storage;
using CloudKeeperSN.Providers.GoogleDrive.Authentication;

namespace CloudKeeperSN.Providers.GoogleDrive;

public sealed class GoogleDriveProvider(
    GoogleAuthenticationService authentication,
    IProviderDiagnostics diagnostics) : IStorageProvider, IPagedStorageBrowserCapability
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

    private static ProviderOperationException InvalidPagination(string technicalMessage) => new(
        ProviderFailureCategory.InvalidProviderResponse,
        ProviderFailureMessages.ToVietnamese(ProviderFailureCategory.InvalidProviderResponse),
        new InvalidDataException(technicalMessage));
}

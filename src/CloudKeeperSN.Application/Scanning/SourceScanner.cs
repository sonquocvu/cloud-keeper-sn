using CloudKeeperSN.Application.Storage;
using CloudKeeperSN.Domain.Export;
using CloudKeeperSN.Domain.Scanning;
using CloudKeeperSN.Domain.Storage;

namespace CloudKeeperSN.Application.Scanning;

public sealed record ScannedSourceItem(StorageItem Item, StoragePath RelativePath);

public sealed record SourceScanProgress(
    int DiscoveredItems,
    int FileCount,
    int FolderCount,
    int PendingFolderCount,
    string CurrentPath);

public sealed record SourceScanResult(
    IReadOnlyList<ScannedSourceItem> Items,
    int FileCount,
    int FolderCount,
    long EstimatedBytes,
    int UnknownSizeCount,
    int NativeFileCount,
    int UnsupportedNativeFileCount,
    int ShortcutCount,
    IReadOnlyList<string> VietnameseWarnings);

public sealed class SourceScanner(IStorageBrowserCapability browser)
{
    public async Task<SourceScanResult> ScanAsync(
        string providerAccountId,
        string sourceFolderId,
        CancellationToken cancellationToken,
        IProgress<SourceScanProgress>? progress = null)
    {
        var visited = new TraversalCycleGuard();
        var queue = new Queue<(string FolderId, StoragePath RelativePath)>();
        var results = new List<ScannedSourceItem>();
        var warnings = new List<string>();
        var fileCount = 0;
        var folderCount = 0;
        var unknownSizeCount = 0;
        var nativeFileCount = 0;
        var unsupportedNativeFileCount = 0;
        var shortcutCount = 0;
        long estimatedBytes = 0;

        visited.TryEnter(providerAccountId, sourceFolderId);
        queue.Enqueue((sourceFolderId, StoragePath.Root));

        while (queue.TryDequeue(out var current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new SourceScanProgress(results.Count, fileCount, folderCount, queue.Count + 1, current.RelativePath.ToString()));
            await foreach (var item in browser.GetChildrenAsync(providerAccountId, current.FolderId, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = current.RelativePath.Append(item.Name);

                if (item.Kind == StorageItemKind.Shortcut || item.MimeType == GoogleNativeExportPolicy.GoogleShortcut)
                {
                    results.Add(new ScannedSourceItem(item, relativePath));
                    shortcutCount++;
                    warnings.Add($"Đã bỏ qua lối tắt “{item.Name}” để tránh vòng lặp thư mục.");
                    progress?.Report(new SourceScanProgress(results.Count, fileCount, folderCount, queue.Count + 1, relativePath.ToString()));
                    continue;
                }

                results.Add(new ScannedSourceItem(item, relativePath));
                if (item.Kind == StorageItemKind.Folder)
                {
                    folderCount++;
                    if (visited.TryEnter(providerAccountId, item.ItemId))
                    {
                        queue.Enqueue((item.ItemId, relativePath));
                    }
                    else
                    {
                        warnings.Add($"Đã dừng duyệt “{item.Name}” vì phát hiện vòng lặp thư mục.");
                    }
                }
                else
                {
                    fileCount++;
                    if (item.Size is { } size) estimatedBytes = checked(estimatedBytes + size);
                    else unknownSizeCount++;

                    if (item.Kind == StorageItemKind.ProviderNativeFile)
                    {
                        nativeFileCount++;
                        if (string.IsNullOrWhiteSpace(item.MimeType) || !GoogleNativeExportPolicy.Decide(item.MimeType).IsSupported)
                            unsupportedNativeFileCount++;
                    }
                }
                progress?.Report(new SourceScanProgress(results.Count, fileCount, folderCount, queue.Count, relativePath.ToString()));
            }
        }

        return new SourceScanResult(
            results,
            fileCount,
            folderCount,
            estimatedBytes,
            unknownSizeCount,
            nativeFileCount,
            unsupportedNativeFileCount,
            shortcutCount,
            warnings);
    }
}

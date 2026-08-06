using CloudKeeperSN.Application.Storage;
using CloudKeeperSN.Domain.Export;
using CloudKeeperSN.Domain.Scanning;
using CloudKeeperSN.Domain.Storage;

namespace CloudKeeperSN.Application.Scanning;

public sealed record ScannedSourceItem(StorageItem Item, StoragePath RelativePath);

public sealed record SourceScanResult(
    IReadOnlyList<ScannedSourceItem> Items,
    int FileCount,
    int FolderCount,
    long EstimatedBytes,
    IReadOnlyList<string> VietnameseWarnings);

public sealed class SourceScanner(IStorageBrowserCapability browser)
{
    public async Task<SourceScanResult> ScanAsync(
        string providerAccountId,
        string sourceFolderId,
        CancellationToken cancellationToken)
    {
        var visited = new TraversalCycleGuard();
        var queue = new Queue<(string FolderId, StoragePath RelativePath)>();
        var results = new List<ScannedSourceItem>();
        var warnings = new List<string>();
        var fileCount = 0;
        var folderCount = 0;
        long estimatedBytes = 0;

        visited.TryEnter(providerAccountId, sourceFolderId);
        queue.Enqueue((sourceFolderId, StoragePath.Root));

        while (queue.TryDequeue(out var current))
        {
            await foreach (var item in browser.GetChildrenAsync(providerAccountId, current.FolderId, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = current.RelativePath.Append(item.Name);

                if (item.Kind == StorageItemKind.Shortcut || item.MimeType == GoogleNativeExportPolicy.GoogleShortcut)
                {
                    warnings.Add($"Đã bỏ qua lối tắt “{item.Name}” để tránh vòng lặp thư mục.");
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
                    estimatedBytes = checked(estimatedBytes + (item.Size ?? 0));
                }
            }
        }

        return new SourceScanResult(results, fileCount, folderCount, estimatedBytes, warnings);
    }
}


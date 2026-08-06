using CloudKeeperSN.App.ViewModels;
using CloudKeeperSN.Application.Scanning;
using CloudKeeperSN.Domain.Export;
using CloudKeeperSN.Domain.Naming;
using CloudKeeperSN.Domain.Storage;
using CloudKeeperSN.Providers.GoogleDrive.Fakes;

namespace CloudKeeperSN.App.Development;

public sealed class DemoBackupPlanner(FakeGoogleDriveProvider googleDrive)
{
    public async Task<BackupPreviewViewState> BuildAsync(FolderSelectionViewModel source, FolderSelectionViewModel destination, CancellationToken cancellationToken)
    {
        await Task.Delay(550, cancellationToken);
        var scan = await new SourceScanner(googleDrive).ScanAsync(source.AccountId, source.FolderId, cancellationToken);
        var duplicateOccurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var items = new List<PreviewItemViewModel>();
        var exportCount = 0;

        foreach (var scanned in scan.Items.Where(item => item.Item.Kind != StorageItemKind.Folder))
        {
            var item = scanned.Item;
            var relative = scanned.RelativePath.ToString();
            if (item.Kind == StorageItemKind.ProviderNativeFile)
            {
                var export = GoogleNativeExportPolicy.Decide(item.MimeType ?? string.Empty);
                if (!export.IsSupported)
                {
                    items.Add(new PreviewItemViewModel(item.ItemId, item.Name, relative, null, PreviewItemCategory.Unsupported, "Không được hỗ trợ", null, export.VietnameseExplanation));
                    continue;
                }
                exportCount++;
                var destinationName = item.Name + export.Extension;
                var category = item.ItemId == "g-slides" ? PreviewItemCategory.Warning : PreviewItemCategory.Copy;
                var explanation = category == PreviewItemCategory.Warning
                    ? $"{export.VietnameseExplanation} Tệp xuất sẽ được xác minh bằng thông tin tệp vì không có mã kiểm tra tương thích."
                    : export.VietnameseExplanation;
                items.Add(new PreviewItemViewModel(item.ItemId, item.Name, relative, null, category, $"Xuất thành {export.Extension}", destinationName, explanation));
                continue;
            }

            duplicateOccurrences.TryGetValue(item.Name, out var occurrence);
            occurrence++;
            duplicateOccurrences[item.Name] = occurrence;
            if (item.ItemId == "g-budget-a")
            {
                items.Add(new PreviewItemViewModel(item.ItemId, item.Name, relative, item.Size, PreviewItemCategory.Skip, "Bỏ qua", item.Name, "Tệp đã được sao lưu trước đó và không thay đổi."));
            }
            else if (occurrence > 1)
            {
                var safeName = new DeterministicConflictNamePolicy().CreateSafeName(item.Name, occurrence);
                items.Add(new PreviewItemViewModel(item.ItemId, item.Name, relative, item.Size, PreviewItemCategory.Conflict, "Đổi tên an toàn", safeName, "Tên đích đang được một mục khác sử dụng; tệp hiện có không bị ghi đè."));
            }
            else
            {
                items.Add(new PreviewItemViewModel(item.ItemId, item.Name, relative, item.Size, PreviewItemCategory.Copy, "Sao lưu", item.Name, "Tệp mới sẽ được sao lưu."));
            }
        }

        return BackupPreviewViewState.Create(source.DisplayPath, destination.DisplayPath, scan.FolderCount, items, exportCount, scan.VietnameseWarnings);
    }
}

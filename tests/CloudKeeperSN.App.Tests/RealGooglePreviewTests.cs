using CloudKeeperSN.App.Development;
using CloudKeeperSN.App.Services;
using CloudKeeperSN.App.ViewModels;
using CloudKeeperSN.Application.Storage;
using CloudKeeperSN.Domain.Export;
using CloudKeeperSN.Domain.Storage;

namespace CloudKeeperSN.App.Tests;

public sealed class RealGooglePreviewTests
{
    [Fact]
    public async Task ProductionModeScansMetadataPersistsCompleteSummaryAndKeepsTransferDisabled()
    {
        var environment = await UiTestEnvironment.CreateAsync();
        var provider = new ProductionGoogleProvider([
            Item("file", "Ảnh.jpg", StorageItemKind.File, "image/jpeg", 123),
            Item("doc", "Kế hoạch", StorageItemKind.ProviderNativeFile, GoogleNativeExportPolicy.GoogleDocument, null),
            Item("form", "Khảo sát", StorageItemKind.ProviderNativeFile, "application/vnd.google-apps.form", null),
            Item("shortcut", "Lối tắt", StorageItemKind.Shortcut, GoogleNativeExportPolicy.GoogleShortcut, null)]);
        environment.FolderPicker.Enqueue(new FolderSelection("google-drive", "real-account", "root", "Drive của tôi"));
        using var viewModel = Create(environment, provider);
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.SelectSourceCommand.Execute(null);
        await AsyncTest.UntilAsync(() => viewModel.SourceFolder is not null);
        viewModel.ScanCommand.Execute(null);
        await AsyncTest.UntilAsync(() => viewModel.Stage == BackupWorkflowStage.Preview);

        Assert.True(viewModel.IsProductionMode);
        Assert.Equal(3, viewModel.Preview!.FileCount);
        Assert.Equal(2, viewModel.Preview.UnknownSizeCount);
        Assert.Equal(1, viewModel.Preview.ExportCount);
        Assert.Equal(1, viewModel.Preview.UnsupportedCount);
        Assert.False(viewModel.StartBackupCommand.CanExecute(null));
        Assert.Contains("chưa có đích lưu trữ thực", viewModel.TransferAvailabilityMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(environment.SettingsRepository.Values.ContainsKey("backup.google.last-complete-scan-summary"));
        Assert.Equal("root", environment.SettingsRepository.Values["backup.google.source.folder"]);
    }

    [Fact]
    public async Task FailedProductionScanNeverPublishesPartialPreviewOrSummary()
    {
        var environment = await UiTestEnvironment.CreateAsync();
        var provider = new ProductionGoogleProvider(
            [Item("partial", "Một phần.txt", StorageItemKind.File, "text/plain", 12)],
            new ProviderOperationException(ProviderFailureCategory.PermissionDenied, "technical"));
        environment.FolderPicker.Enqueue(new FolderSelection("google-drive", "real-account", "root", "Drive của tôi"));
        using var viewModel = Create(environment, provider);
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.SelectSourceCommand.Execute(null);
        await AsyncTest.UntilAsync(() => viewModel.SourceFolder is not null);

        viewModel.ScanCommand.Execute(null);
        await AsyncTest.UntilAsync(() => !viewModel.IsScanning && viewModel.ScanErrorMessage is not null);

        Assert.Equal(BackupWorkflowStage.Setup, viewModel.Stage);
        Assert.Null(viewModel.Preview);
        Assert.DoesNotContain("backup.google.last-complete-scan-summary", environment.SettingsRepository.Values.Keys);
        Assert.Contains("không có quyền đọc", viewModel.ScanErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static BackupViewModel Create(UiTestEnvironment environment, IStorageProvider provider) => new(
        environment.DemoData,
        new DemoBackupPlanner(environment.GoogleDrive),
        new DemoTransferEngine(new DemoConfiguration(false, DemoScenarioKind.Standard), new ImmediateDelay()),
        environment.FolderPicker,
        environment.Dialogs,
        new DemoConfiguration(false, DemoScenarioKind.Standard),
        [provider],
        environment.SettingsRepository);

    private static StorageItem Item(string id, string name, StorageItemKind kind, string mimeType, long? size) => new()
    {
        ProviderId = "google-drive",
        ProviderAccountId = "real-account",
        ItemId = id,
        ParentItemId = "root",
        Name = name,
        Kind = kind,
        MimeType = mimeType,
        Size = size
    };

    private sealed class ProductionGoogleProvider(
        IReadOnlyList<StorageItem> items,
        Exception? terminalFailure = null) : IStorageProvider, IStorageBrowserCapability
    {
        public StorageProviderDescriptor Descriptor { get; } = new(
            "google-drive", "Google Drive", new HashSet<StorageCapabilityKind> { StorageCapabilityKind.Browse, StorageCapabilityKind.ReadMetadata });

        public Task<StorageAccount?> GetConnectedAccountAsync(CancellationToken cancellationToken) =>
            Task.FromResult<StorageAccount?>(new("google:current", "google-drive", "real-account", "Nguyễn An", true, DateTimeOffset.UtcNow, "an@example.test"));

        public async IAsyncEnumerable<StorageItem> GetChildrenAsync(
            string providerAccountId,
            string parentItemId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
                await Task.Yield();
            }
            if (terminalFailure is not null) throw terminalFailure;
        }
    }
}

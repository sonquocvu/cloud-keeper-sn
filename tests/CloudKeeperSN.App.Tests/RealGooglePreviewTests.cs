using CloudKeeperSN.App.Development;
using CloudKeeperSN.App.Services;
using CloudKeeperSN.App.ViewModels;
using CloudKeeperSN.Application.Scanning;
using CloudKeeperSN.Application.Storage;
using CloudKeeperSN.Domain.Scanning;
using CloudKeeperSN.Domain.Storage;

namespace CloudKeeperSN.App.Tests;

public sealed class RealGooglePreviewTests
{
    [Fact]
    public async Task ProductionModeScansWholeDriveAndPublishesSummaryWithoutTransfer()
    {
        var environment = await UiTestEnvironment.CreateAsync();
        var provider = new ProductionGoogleProvider();
        var scanner = new FakeInventoryScanner { Result = CompletedRun() };
        var dispatcher = new RecordingDispatcher();
        using var viewModel = Create(environment, provider, scanner, dispatcher);
        await viewModel.LoadAsync(CancellationToken.None);

        Assert.True(viewModel.ScanCommand.CanExecute(null));
        viewModel.ScanCommand.Execute(null);
        await AsyncTest.UntilAsync(() => viewModel.InventorySummary is not null && !viewModel.IsScanning);

        Assert.True(viewModel.IsProductionMode);
        Assert.Equal(3, viewModel.InventorySummary!.FileCount);
        Assert.Equal(2, viewModel.InventorySummary.UnknownSizeCount);
        Assert.Equal(1, viewModel.InventorySummary.GoogleWorkspaceFileCount);
        Assert.Equal("9.8 KB", viewModel.InventorySummary.StorageLimitLabel);
        Assert.Equal("2 KB", viewModel.InventorySummary.TotalUsageLabel);
        Assert.Equal("Đã quét Google Drive thành công.", viewModel.ScanSuccessMessage);
        Assert.False(viewModel.StartBackupCommand.CanExecute(null));
        Assert.Contains("chưa có đích lưu trữ thực", viewModel.TransferAvailabilityMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(dispatcher.InvocationCount > 0);
    }

    [Fact]
    public async Task FailedProductionScanPreservesPreviousSummaryAndEnablesRetry()
    {
        var environment = await UiTestEnvironment.CreateAsync();
        var provider = new ProductionGoogleProvider();
        var previous = CompletedRun();
        var scanner = new FakeInventoryScanner
        {
            Latest = previous,
            Exception = new ProviderOperationException(ProviderFailureCategory.NetworkUnavailable, "technical")
        };
        using var viewModel = Create(environment, provider, scanner, InlineUiDispatcher.Instance);
        await viewModel.LoadAsync(CancellationToken.None);
        Assert.NotNull(viewModel.InventorySummary);

        viewModel.ScanCommand.Execute(null);
        await AsyncTest.UntilAsync(() => !viewModel.IsScanning && viewModel.ScanErrorMessage is not null);

        Assert.Equal(previous.ScanId, scanner.Latest!.ScanId);
        Assert.Equal(previous.FileCount, viewModel.InventorySummary!.FileCount);
        Assert.True(viewModel.ScanCommand.CanExecute(null));
        Assert.Contains("mạng", viewModel.ScanErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ScanCommandIsDisabledWhenDisconnected()
    {
        var environment = await UiTestEnvironment.CreateAsync();
        using var viewModel = Create(environment, new ProductionGoogleProvider(connected: false), new FakeInventoryScanner(), InlineUiDispatcher.Instance);

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.False(viewModel.ScanCommand.CanExecute(null));
        Assert.Contains("kết nối Google Drive", viewModel.ValidationMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunningScanUpdatesProgressCommandsAndCanBeCancelledThenRetried()
    {
        var environment = await UiTestEnvironment.CreateAsync();
        var scanner = new FakeInventoryScanner { Block = true };
        using var viewModel = Create(environment, new ProductionGoogleProvider(), scanner, InlineUiDispatcher.Instance);
        var changedProperties = new List<string?>();
        var commandChanges = 0;
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);
        viewModel.ScanCommand.CanExecuteChanged += (_, _) => commandChanges++;
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.ScanCommand.Execute(null);
        await scanner.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await AsyncTest.UntilAsync(() => viewModel.IsScanning && viewModel.ScanProgressText.Contains("12"));

        Assert.False(viewModel.ScanCommand.CanExecute(null));
        Assert.True(viewModel.CancelScanCommand.CanExecute(null));
        viewModel.CancelScanCommand.Execute(null);
        await AsyncTest.UntilAsync(() => !viewModel.IsScanning && viewModel.ScanCommand.CanExecute(null));

        Assert.True(viewModel.ScanCommand.CanExecute(null));
        Assert.False(viewModel.CancelScanCommand.CanExecute(null));
        Assert.Contains("hủy", viewModel.ScanProgressText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(BackupViewModel.IsScanning), changedProperties);
        Assert.Contains(nameof(BackupViewModel.ScanProgressText), changedProperties);
        Assert.True(commandChanges > 0);
    }

    [Fact]
    public async Task DashboardRefreshesImmediatelyAfterSuccessfulProductionScan()
    {
        var environment = await UiTestEnvironment.CreateAsync();
        var scanner = new FakeInventoryScanner();
        using var viewModel = new DashboardViewModel(
            environment.DemoData, false, new ConnectedAuthentication(), scanner, InlineUiDispatcher.Instance);
        await viewModel.LoadAsync(CancellationToken.None);
        Assert.True(viewModel.IsEmpty);

        var completed = CompletedRun();
        scanner.PublishCompletion(completed);
        await AsyncTest.UntilAsync(() => viewModel.RecentRuns.Count == 1);

        Assert.False(viewModel.IsEmpty);
        Assert.Equal("Đã hoàn tất", viewModel.RecentRuns[0].Status.Text);
        Assert.Contains("3 tệp", viewModel.LastBackupCard.Detail);
    }

    private static BackupViewModel Create(UiTestEnvironment environment, IStorageProvider provider, IDriveInventoryScanner scanner, IUiDispatcher dispatcher) => new(
        environment.DemoData,
        new DemoBackupPlanner(environment.GoogleDrive),
        new DemoTransferEngine(new DemoConfiguration(false, DemoScenarioKind.Standard), new ImmediateDelay()),
        environment.FolderPicker,
        environment.Dialogs,
        new DemoConfiguration(false, DemoScenarioKind.Standard),
        [provider],
        scanner,
        dispatcher);

    private static DriveInventoryRun CompletedRun() => new(
        Guid.NewGuid(), "google-drive", "real-account", DateTimeOffset.UtcNow.AddSeconds(-2), DateTimeOffset.UtcNow,
        DriveInventoryRunStatus.Completed, 6, 1, 3, 123, 2, 1, 1, 1, 2, null, true,
        new DriveStorageInformation(10_000, 2_000, 1_500, 50));

    private sealed class ProductionGoogleProvider(bool connected = true) : IStorageProvider
    {
        public StorageProviderDescriptor Descriptor { get; } = new(
            "google-drive", "Google Drive", new HashSet<StorageCapabilityKind> { StorageCapabilityKind.ReadMetadata });
        public Task<StorageAccount?> GetConnectedAccountAsync(CancellationToken cancellationToken) => Task.FromResult<StorageAccount?>(
            connected ? new("google:current", "google-drive", "real-account", "Nguyễn An", true, DateTimeOffset.UtcNow, "an@example.test") : null);
    }

    private sealed class FakeInventoryScanner : IDriveInventoryScanner
    {
        public DriveInventoryRun? Result { get; set; }
        public DriveInventoryRun? Latest { get; set; }
        public Exception? Exception { get; set; }
        public bool Block { get; set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DriveInventoryScanState State { get; private set; } = new(DriveInventoryScanStatus.Idle, "Sẵn sàng");
        public event Action<DriveInventoryScanState>? StateChanged;
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<DriveInventoryRun?> GetLatestSuccessfulAsync(string providerAccountId, CancellationToken cancellationToken) => Task.FromResult(Latest);
        public Task<IReadOnlyList<DriveInventoryRun>> GetRecentAsync(int maximumCount, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DriveInventoryRun>>(Latest is null ? [] : [Latest]);
        public async Task<DriveInventoryRun> ScanAsync(CancellationToken cancellationToken)
        {
            Publish(new(DriveInventoryScanStatus.Scanning, "Đang xử lý 12 mục…", 12, 1, Guid.NewGuid()));
            Started.TrySetResult();
            try
            {
                if (Block) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                await Task.Yield();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Publish(new(DriveInventoryScanStatus.Cancelled,
                    "Đã hủy quá trình quét. Không có dữ liệu nào trên Google Drive bị thay đổi.", LastSuccessfulRun: Latest));
                throw;
            }
            if (Exception is not null)
            {
                Publish(new(DriveInventoryScanStatus.Failed, "Mất kết nối mạng trong khi quét. Kết quả trước vẫn được giữ.", FailureCategory: "NetworkUnavailable", LastSuccessfulRun: Latest));
                throw Exception;
            }
            Latest = Result ?? CompletedRun();
            Publish(new(DriveInventoryScanStatus.Completed, "Đã quét Google Drive thành công.", LastSuccessfulRun: Latest));
            return Latest;
        }
        private void Publish(DriveInventoryScanState state) { State = state; StateChanged?.Invoke(state); }
        public void PublishCompletion(DriveInventoryRun run)
        {
            Latest = run;
            Publish(new(DriveInventoryScanStatus.Completed, "Đã quét Google Drive thành công.", LastSuccessfulRun: run));
        }
    }

    private sealed class ConnectedAuthentication : IProviderAuthenticationService
    {
        private static readonly StorageAccount Account = new(
            "google:current", "google-drive", "real-account", "Nguyễn An", true, DateTimeOffset.UtcNow, "an@example.test");
        public string ProviderId => "google-drive";
        public bool IsConfigured => true;
        public string? ConfigurationMessage => null;
        public ProviderAuthenticationState State => new(ProviderAuthenticationStatus.Connected, "Đã kết nối", Account: Account);
        public event Action<ProviderAuthenticationState>? StateChanged { add { } remove { } }
        public event Action? ConfigurationChanged { add { } remove { } }
        public Task<StorageAccount?> GetCachedAccountAsync(CancellationToken cancellationToken) => Task.FromResult<StorageAccount?>(Account);
        public Task<StorageAccount> ConnectAsync(CancellationToken cancellationToken) => Task.FromResult(Account);
        public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DisconnectLocalAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingDispatcher : IUiDispatcher
    {
        public int InvocationCount { get; private set; }
        public void Invoke(Action action) { InvocationCount++; action(); }
    }
}

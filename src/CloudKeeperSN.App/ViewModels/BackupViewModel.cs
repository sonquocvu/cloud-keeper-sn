using System.Collections.ObjectModel;
using System.Windows.Input;
using CloudKeeperSN.App.Development;
using CloudKeeperSN.App.Presentation;
using CloudKeeperSN.App.Services;
using CloudKeeperSN.Application.Scanning;
using CloudKeeperSN.Application.Storage;
using CloudKeeperSN.Domain.Storage;
using CloudKeeperSN.Domain.Scanning;
using CloudKeeperSN.Domain.Transfers;

namespace CloudKeeperSN.App.ViewModels;

public enum BackupWorkflowStage { Setup, Preview, Running, Result }
public enum PreviewItemCategory { Copy, Skip, Warning, Conflict, Unsupported }

public sealed record FolderSelectionViewModel(string ProviderId, string AccountId, string FolderId, string DisplayPath);
public sealed record PreviewFilterOption(string Key, string Label, PreviewItemCategory? Category);
public sealed record PreviewItemViewModel(
    string ItemId,
    string OriginalName,
    string RelativePath,
    long? Size,
    PreviewItemCategory Category,
    string PlannedAction,
    string? DestinationName,
    string Reason)
{
    public string SizeLabel => Size is null ? "Không xác định" : DashboardViewModel.FormatBytes(Size.Value);
    public bool WasRenamed => !string.IsNullOrWhiteSpace(DestinationName) && !string.Equals(OriginalName, DestinationName, StringComparison.Ordinal);
    public StatusPresentation CategoryStatus => Category switch
    {
        PreviewItemCategory.Copy => new("Sẽ sao lưu", StatusTone.Success, "\uE73E"),
        PreviewItemCategory.Skip => new("Sẽ bỏ qua", StatusTone.Neutral, "\uE946"),
        PreviewItemCategory.Warning => new("Có cảnh báo", StatusTone.Warning, "\uE7BA"),
        PreviewItemCategory.Conflict => new("Xung đột tên", StatusTone.Warning, "\uE7BA"),
        _ => new("Không được hỗ trợ", StatusTone.Error, "\uEA39")
    };
}

public sealed record BackupPreviewViewState(
    string SourcePath,
    string DestinationPath,
    int FolderCount,
    int FileCount,
    long EstimatedBytes,
    int NewCount,
    int SkipCount,
    int ConflictCount,
    int ExportCount,
    int UnsupportedCount,
    int WarningCount,
    int UnknownSizeCount,
    IReadOnlyList<PreviewItemViewModel> Items,
    IReadOnlyList<string> Warnings)
{
    public static BackupPreviewViewState Create(
        string sourcePath,
        string destinationPath,
        int folderCount,
        IReadOnlyList<PreviewItemViewModel> items,
        int exportCount,
        IReadOnlyList<string> warnings) => new(
            sourcePath,
            destinationPath,
            folderCount,
            items.Count,
            items.Sum(item => item.Size ?? 0),
            items.Count(item => item.Category is PreviewItemCategory.Copy or PreviewItemCategory.Warning),
            items.Count(item => item.Category == PreviewItemCategory.Skip),
            items.Count(item => item.Category == PreviewItemCategory.Conflict),
            exportCount,
            items.Count(item => item.Category == PreviewItemCategory.Unsupported),
            warnings.Count + items.Count(item => item.Category is PreviewItemCategory.Warning or PreviewItemCategory.Unsupported),
            items.Count(item => item.Size is null),
            items,
            warnings);
}

public sealed record BackupResultViewModel(
    string Headline,
    StatusTone Tone,
    int CompletedCount,
    int SkippedCount,
    int WarningCount,
    int FailedCount,
    string TransferredCapacity,
    string TimeRange,
    StatusPresentation Verification,
    string DestinationPath,
    bool CanRetryFailed);

public sealed record DriveInventorySummaryViewModel(
    DateTimeOffset CompletedAt,
    int TotalItems,
    int FileCount,
    int FolderCount,
    long KnownBytes,
    int UnknownSizeCount,
    int GoogleWorkspaceFileCount,
    int ShortcutCount,
    int UnresolvedCount,
    int BackupEligibleCount,
    long? StorageLimitBytes,
    long? TotalUsageBytes,
    long? DriveUsageBytes,
    long? TrashUsageBytes)
{
    public string CompletedLabel => CompletedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    public string TotalItemsLabel => VietnameseNumberFormatter.FormatInteger(TotalItems);
    public string FileCountLabel => VietnameseNumberFormatter.FormatInteger(FileCount);
    public string FolderCountLabel => VietnameseNumberFormatter.FormatInteger(FolderCount);
    public string GoogleWorkspaceFileCountLabel => VietnameseNumberFormatter.FormatInteger(GoogleWorkspaceFileCount);
    public string UnknownSizeCountLabel => VietnameseNumberFormatter.FormatInteger(UnknownSizeCount);
    public string UnresolvedCountLabel => VietnameseNumberFormatter.FormatInteger(UnresolvedCount);
    public string BackupEligibleCountLabel => VietnameseNumberFormatter.FormatInteger(BackupEligibleCount);
    public string KnownBytesLabel => DashboardViewModel.FormatBytes(KnownBytes);
    public string StorageLimitLabel => FormatOptionalBytes(StorageLimitBytes);
    public string TotalUsageLabel => FormatOptionalBytes(TotalUsageBytes);
    public string DriveUsageLabel => FormatOptionalBytes(DriveUsageBytes);
    public string TrashUsageLabel => FormatOptionalBytes(TrashUsageBytes);
    public bool HasStorageInformation => StorageLimitBytes is >= 0 || TotalUsageBytes is >= 0 || DriveUsageBytes is >= 0 || TrashUsageBytes is >= 0;
    public bool HasStorageProgress => StorageLimitBytes is > 0 && TotalUsageBytes is >= 0;
    public double StorageUsagePercent => HasStorageProgress
        ? Math.Clamp(TotalUsageBytes!.Value * 100d / StorageLimitBytes!.Value, 0d, 100d)
        : 0d;
    public string StorageProgressLabel => HasStorageProgress
        ? $"{TotalUsageLabel} / {StorageLimitLabel} ({VietnameseNumberFormatter.FormatPercentage(StorageUsagePercent)})"
        : "Không xác định";
    public string StorageProgressAccessibleLabel => HasStorageProgress
        ? $"Đã sử dụng {TotalUsageLabel} trên {StorageLimitLabel}, {VietnameseNumberFormatter.FormatPercentage(StorageUsagePercent)}"
        : "Không xác định";

    private static string FormatOptionalBytes(long? value) => value is >= 0
        ? DashboardViewModel.FormatBytes(value.Value)
        : "Không xác định";
}

public sealed class BackupViewModel : PageViewModel, IDisposable
{
    private readonly DemoDataService _demoData;
    private readonly DemoBackupPlanner _planner;
    private readonly DemoTransferEngine _transferEngine;
    private readonly IFolderPickerService _folderPicker;
    private readonly IDialogService _dialogs;
    private readonly bool _isDemoMode;
    private readonly IStorageProvider? _realGoogleProvider;
    private readonly IDriveInventoryScanner? _inventoryScanner;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly List<PreviewItemViewModel> _allPreviewItems = [];
    private CancellationTokenSource? _runCancellation;
    private bool _googleConnected;
    private bool _oneDriveConnected;
    private FolderSelectionViewModel? _sourceFolder;
    private FolderSelectionViewModel? _destinationFolder;
    private BackupWorkflowStage _stage;
    private BackupPreviewViewState? _preview;
    private PreviewFilterOption _selectedFilter;
    private string _previewSearch = string.Empty;
    private bool _isScanning;
    private bool _isPaused;
    private double _overallProgress;
    private string _currentFile = "Đang chuẩn bị…";
    private string _currentOperation = "Đang chuẩn bị hàng đợi sao lưu";
    private string _fileProgress = "0 / 0 tệp";
    private string _byteProgress = "0 byte / 0 byte";
    private int _completedCount;
    private int _skippedCount;
    private int _warningCount;
    private int _failedCount;
    private int _retryCount;
    private BackupResultViewModel? _result;
    private string _accountDisplayName = "Google Drive";
    private string _scanProgressText = "Chưa bắt đầu quét";
    private string? _scanErrorMessage;
    private string? _scanSuccessMessage;
    private DriveInventorySummaryViewModel? _inventorySummary;

    public BackupViewModel(
        DemoDataService demoData,
        DemoBackupPlanner planner,
        DemoTransferEngine transferEngine,
        IFolderPickerService folderPicker,
        IDialogService dialogs)
        : this(demoData, planner, transferEngine, folderPicker, dialogs, true, null, null, null)
    {
    }

    public BackupViewModel(
        DemoDataService demoData,
        DemoBackupPlanner planner,
        DemoTransferEngine transferEngine,
        IFolderPickerService folderPicker,
        IDialogService dialogs,
        DemoConfiguration configuration,
        IEnumerable<IStorageProvider> providers,
        IDriveInventoryScanner? inventoryScanner = null,
        IUiDispatcher? uiDispatcher = null)
        : this(
            demoData,
            planner,
            transferEngine,
            folderPicker,
            dialogs,
            configuration.IsEnabled,
            providers.SingleOrDefault(provider => provider.Descriptor.ProviderId == "google-drive"),
            inventoryScanner,
            uiDispatcher)
    {
    }

    private BackupViewModel(
        DemoDataService demoData,
        DemoBackupPlanner planner,
        DemoTransferEngine transferEngine,
        IFolderPickerService folderPicker,
        IDialogService dialogs,
        bool isDemoMode,
        IStorageProvider? realGoogleProvider,
        IDriveInventoryScanner? inventoryScanner,
        IUiDispatcher? uiDispatcher)
        : base(
            "backup",
            isDemoMode ? "Sao lưu một chiều" : "Quét Google Drive chỉ đọc",
            isDemoMode ? "Google Drive là nguồn; OneDrive là nơi lưu bản sao." : "Quét và lập danh mục siêu dữ liệu cục bộ; chưa truyền nội dung tệp.")
    {
        _demoData = demoData;
        _planner = planner;
        _transferEngine = transferEngine;
        _folderPicker = folderPicker;
        _dialogs = dialogs;
        _isDemoMode = isDemoMode;
        _realGoogleProvider = realGoogleProvider;
        _inventoryScanner = inventoryScanner;
        _uiDispatcher = uiDispatcher ?? InlineUiDispatcher.Instance;
        if (_inventoryScanner is not null) _inventoryScanner.StateChanged += InventoryScannerStateChanged;
        PreviewFilters =
        [
            new("all", "Tất cả", null),
            new("copy", "Sẽ sao lưu", PreviewItemCategory.Copy),
            new("skip", "Sẽ bỏ qua", PreviewItemCategory.Skip),
            new("warning", "Có cảnh báo", PreviewItemCategory.Warning),
            new("conflict", "Xung đột tên", PreviewItemCategory.Conflict),
            new("unsupported", "Không được hỗ trợ", PreviewItemCategory.Unsupported)
        ];
        _selectedFilter = PreviewFilters[0];
        SelectSourceCommand = new AsyncRelayCommand(SelectSourceAsync, () => IsDemoMode && _googleConnected && Stage == BackupWorkflowStage.Setup);
        SelectDestinationCommand = new AsyncRelayCommand(SelectDestinationAsync, () => _oneDriveConnected && Stage == BackupWorkflowStage.Setup);
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => CanScan);
        StartBackupCommand = new AsyncRelayCommand(StartBackupAsync, () => IsDemoMode && Stage == BackupWorkflowStage.Preview && Preview is not null);
        CancelScanCommand = new RelayCommand(_ => ((AsyncRelayCommand)ScanCommand).Cancel(), _ => IsScanning);
        PauseCommand = new RelayCommand(_ => Pause(), _ => Stage == BackupWorkflowStage.Running && !IsPaused);
        ResumeCommand = new RelayCommand(_ => Resume(), _ => Stage == BackupWorkflowStage.Running && IsPaused);
        CancelCommand = new AsyncRelayCommand(CancelAsync, () => Stage == BackupWorkflowStage.Running);
        RetryFailedCommand = new AsyncRelayCommand(RetryFailedAsync, () => Stage == BackupWorkflowStage.Result && Result?.CanRetryFailed == true);
        NewBackupCommand = new RelayCommand(_ => Reset());
    }

    public event EventHandler? OpenHistoryRequested;
    public ObservableCollection<PreviewItemViewModel> VisiblePreviewItems { get; } = [];
    public IReadOnlyList<PreviewFilterOption> PreviewFilters { get; }
    public ICommand SelectSourceCommand { get; }
    public ICommand SelectDestinationCommand { get; }
    public ICommand ScanCommand { get; }
    public ICommand StartBackupCommand { get; }
    public ICommand CancelScanCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand ResumeCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand RetryFailedCommand { get; }
    public ICommand NewBackupCommand { get; }
    public ICommand OpenHistoryCommand => new RelayCommand(_ => OpenHistoryRequested?.Invoke(this, EventArgs.Empty));

    public BackupWorkflowStage Stage { get => _stage; private set { if (SetProperty(ref _stage, value)) NotifyState(); } }
    public bool IsSetup => Stage == BackupWorkflowStage.Setup;
    public bool IsPreview => Stage == BackupWorkflowStage.Preview;
    public bool IsRunning => Stage == BackupWorkflowStage.Running;
    public bool IsResult => Stage == BackupWorkflowStage.Result;
    public bool IsDemoMode => _isDemoMode;
    public bool IsProductionMode => !_isDemoMode;
    public string AccountDisplayName { get => _accountDisplayName; private set => SetProperty(ref _accountDisplayName, value); }
    public FolderSelectionViewModel? SourceFolder { get => _sourceFolder; private set { if (SetProperty(ref _sourceFolder, value)) NotifyState(); } }
    public FolderSelectionViewModel? DestinationFolder { get => _destinationFolder; private set { if (SetProperty(ref _destinationFolder, value)) NotifyState(); } }
    public string SourceFolderLabel => SourceFolder?.DisplayPath ?? "Chưa chọn thư mục nguồn";
    public string DestinationFolderLabel => DestinationFolder?.DisplayPath ?? "Chưa chọn thư mục đích";
    public bool CanScan => _googleConnected && SourceFolder is not null && !IsScanning && Stage == BackupWorkflowStage.Setup &&
        (IsProductionMode || (_oneDriveConnected && DestinationFolder is not null));
    public string ValidationMessage => !_googleConnected ? "Hãy kết nối Google Drive trên trang Tài khoản."
        : SourceFolder is null ? "Hãy chọn thư mục nguồn trên Google Drive."
        : IsProductionMode ? "Đã sẵn sàng tạo danh mục siêu dữ liệu chỉ đọc."
        : !_oneDriveConnected ? "Hãy kết nối OneDrive trên trang Tài khoản."
        : DestinationFolder is null ? "Hãy chọn thư mục đích trên OneDrive."
        : "Đã đủ thông tin để quét và xem trước.";
    public bool IsScanning { get => _isScanning; private set { if (SetProperty(ref _isScanning, value)) NotifyState(); } }
    public string ScanProgressText { get => _scanProgressText; private set => SetProperty(ref _scanProgressText, value); }
    public string? ScanErrorMessage { get => _scanErrorMessage; private set => SetProperty(ref _scanErrorMessage, value); }
    public string? ScanSuccessMessage { get => _scanSuccessMessage; private set => SetProperty(ref _scanSuccessMessage, value); }
    public DriveInventorySummaryViewModel? InventorySummary { get => _inventorySummary; private set { if (SetProperty(ref _inventorySummary, value)) OnPropertyChanged(nameof(HasInventorySummary)); } }
    public bool HasInventorySummary => InventorySummary is not null;
    public string ScanActionText => IsProductionMode ? "_Bắt đầu quét" : "_Quét và xem trước";
    public string TransferAvailabilityMessage => IsProductionMode
        ? "Chưa thể bắt đầu sao lưu: bản dựng này chưa có đích lưu trữ thực. Không có tệp nào được tải xuống, xuất hoặc truyền."
        : "Google Drive không bị thay đổi. OneDrive không bị xóa hoặc ghi đè theo mặc định.";
    public BackupPreviewViewState? Preview { get => _preview; private set => SetProperty(ref _preview, value); }
    public PreviewFilterOption SelectedFilter { get => _selectedFilter; set { if (SetProperty(ref _selectedFilter, value)) ApplyPreviewFilter(); } }
    public string PreviewSearch { get => _previewSearch; set { if (SetProperty(ref _previewSearch, value)) ApplyPreviewFilter(); } }
    public bool IsPaused { get => _isPaused; private set { if (SetProperty(ref _isPaused, value)) NotifyState(); } }
    public double OverallProgress { get => _overallProgress; private set => SetProperty(ref _overallProgress, value); }
    public string CurrentFile { get => _currentFile; private set => SetProperty(ref _currentFile, value); }
    public string CurrentOperation { get => _currentOperation; private set => SetProperty(ref _currentOperation, value); }
    public string FileProgress { get => _fileProgress; private set => SetProperty(ref _fileProgress, value); }
    public string ByteProgress { get => _byteProgress; private set => SetProperty(ref _byteProgress, value); }
    public int CompletedCount { get => _completedCount; private set => SetProperty(ref _completedCount, value); }
    public int SkippedCount { get => _skippedCount; private set => SetProperty(ref _skippedCount, value); }
    public int WarningCount { get => _warningCount; private set => SetProperty(ref _warningCount, value); }
    public int FailedCount { get => _failedCount; private set => SetProperty(ref _failedCount, value); }
    public int RetryCount { get => _retryCount; private set => SetProperty(ref _retryCount, value); }
    public BackupResultViewModel? Result { get => _result; private set { if (SetProperty(ref _result, value)) NotifyState(); } }

    public override async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (IsProductionMode)
        {
            var account = _realGoogleProvider is null ? null : await _realGoogleProvider.GetConnectedAccountAsync(cancellationToken);
            _googleConnected = account?.IsConnected == true;
            _oneDriveConnected = false;
            AccountDisplayName = account is null ? "Google Drive chưa kết nối" : account.Email ?? account.DisplayName;
            SourceFolder = account is null ? null : new FolderSelectionViewModel("google-drive", account.ProviderAccountId, "root", "Drive của tôi");
            if (_inventoryScanner is not null)
            {
                var latest = account is not null
                    ? await _inventoryScanner.GetLatestSuccessfulAsync(account.ProviderAccountId, cancellationToken)
                    : (await _inventoryScanner.GetRecentAsync(500, cancellationToken))
                        .Where(run => run.IsComplete && run.Status == DriveInventoryRunStatus.Completed && run.CompletedAtUtc.HasValue)
                        .OrderByDescending(run => run.CompletedAtUtc)
                        .FirstOrDefault();
                ApplyInventorySummary(latest);
            }
            RefreshIdleScanProgress();
            NotifyState();
            return;
        }
        var accounts = await _demoData.GetAccountsAsync(cancellationToken);
        _googleConnected = accounts.Any(account => account.ProviderId == "google-drive" && account.IsConnected);
        _oneDriveConnected = accounts.Any(account => account.ProviderId == "one-drive" && account.IsConnected);
        AccountDisplayName = accounts.FirstOrDefault(account => account.ProviderId == "google-drive")?.DisplayName ?? "Tài khoản trình diễn";
        NotifyState();
    }

    private async Task SelectSourceAsync(CancellationToken cancellationToken)
    {
        if (!IsDemoMode) return;
        var selected = await _folderPicker.PickAsync(new FolderPickerRequest("google-drive", DemoDataService.GoogleAccountId, "root", "Chọn thư mục nguồn trên Google Drive", false), cancellationToken);
        if (selected is not null)
            SourceFolder = new(selected.ProviderId, selected.AccountId, selected.FolderId, selected.DisplayPath);
    }

    private async Task SelectDestinationAsync(CancellationToken cancellationToken)
    {
        var selected = await _folderPicker.PickAsync(new FolderPickerRequest("one-drive", DemoDataService.MicrosoftAccountId, DemoDataService.OneDriveRootId, "Chọn nơi lưu bản sao trên OneDrive", true), cancellationToken);
        if (selected is not null) DestinationFolder = new(selected.ProviderId, selected.AccountId, selected.FolderId, selected.DisplayPath);
    }

    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        if (!CanScan || SourceFolder is null) return;
        IsScanning = true;
        ScanErrorMessage = null;
        ScanSuccessMessage = null;
        if (IsDemoMode) Preview = null;
        try
        {
            if (IsProductionMode)
            {
                if (_inventoryScanner is null) throw new InvalidOperationException("Dịch vụ quét Google Drive chưa sẵn sàng.");
                var scan = await _inventoryScanner.ScanAsync(cancellationToken);
                ApplyInventorySummary(scan);
                ScanProgressText = "Sẵn sàng quét lại";
                ScanSuccessMessage = "Đã quét Google Drive thành công.";
                return;
            }
            else
            {
                if (DestinationFolder is null) return;
                Preview = await _planner.BuildAsync(SourceFolder, DestinationFolder, cancellationToken);
            }
            _allPreviewItems.Clear();
            _allPreviewItems.AddRange(Preview.Items);
            SelectedFilter = PreviewFilters[0];
            PreviewSearch = string.Empty;
            ApplyPreviewFilter();
            Stage = BackupWorkflowStage.Preview;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ScanProgressText = IsProductionMode && _inventoryScanner?.State.Status == DriveInventoryScanStatus.Cancelled
                ? _inventoryScanner.State.VietnameseMessage
                : "Đã hủy quét. Kết quả chưa hoàn tất không được dùng làm bản xem trước.";
            throw;
        }
        catch (Exception exception)
        {
            ScanErrorMessage = IsProductionMode && _inventoryScanner is not null
                ? _inventoryScanner.State.VietnameseMessage
                : exception is ProviderOperationException failure
                    ? ProviderFailureMessages.ToVietnamese(failure.Category)
                    : "Không thể hoàn tất bản quét. Không có dữ liệu Google Drive nào bị thay đổi; vui lòng thử lại.";
            ScanProgressText = "Bản quét chưa hoàn tất.";
        }
        finally
        {
            IsScanning = false;
            RefreshIdleScanProgress();
        }
    }

    private void InventoryScannerStateChanged(DriveInventoryScanState state) =>
        _uiDispatcher.Invoke(() => ApplyInventoryScannerState(state));

    private void ApplyInventoryScannerState(DriveInventoryScanState state)
    {
        IsScanning = state.IsBusy;
        if (state.IsBusy) ScanProgressText = state.VietnameseMessage;
        if (state.Status == DriveInventoryScanStatus.Completed && state.LastSuccessfulRun is not null)
        {
            ApplyInventorySummary(state.LastSuccessfulRun);
            ScanSuccessMessage = "Đã quét Google Drive thành công.";
            ScanErrorMessage = null;
            RefreshIdleScanProgress();
        }
        else if (state.Status is DriveInventoryScanStatus.Failed or DriveInventoryScanStatus.RequiresReauthentication or DriveInventoryScanStatus.Cancelled)
        {
            ScanErrorMessage = state.VietnameseMessage;
            ScanSuccessMessage = null;
            RefreshIdleScanProgress();
        }
    }

    private void ApplyInventorySummary(DriveInventoryRun? run)
    {
        if (run?.CompletedAtUtc is not { } completed) return;
        InventorySummary = new DriveInventorySummaryViewModel(
            completed, run.TotalItems, run.FileCount, run.FolderCount, run.KnownBytes, run.UnknownSizeCount,
            run.GoogleWorkspaceFileCount, run.ShortcutCount, run.UnresolvedCount, run.BackupEligibleCount,
            run.StorageInformation?.StorageLimitBytes, run.StorageInformation?.TotalUsageBytes,
            run.StorageInformation?.DriveUsageBytes, run.StorageInformation?.TrashUsageBytes);
    }

    private void RefreshIdleScanProgress()
    {
        if (!IsProductionMode || IsScanning) return;
        ScanProgressText = _inventoryScanner?.State.Status switch
        {
            DriveInventoryScanStatus.Failed or DriveInventoryScanStatus.RequiresReauthentication or DriveInventoryScanStatus.Cancelled
                when HasInventorySummary => "Sẵn sàng thử lại. Kết quả quét thành công trước đó vẫn được giữ.",
            DriveInventoryScanStatus.Failed or DriveInventoryScanStatus.RequiresReauthentication or DriveInventoryScanStatus.Cancelled
                => "Lần quét chưa hoàn tất. Sẵn sàng thử lại.",
            _ when HasInventorySummary => "Sẵn sàng quét lại",
            _ => "Chưa bắt đầu quét"
        };
    }

    private async Task StartBackupAsync(CancellationToken cancellationToken)
    {
        if (Preview is null || SourceFolder is null || DestinationFolder is null) return;
        var transferCount = Preview.Items.Count(item => item.Category is PreviewItemCategory.Copy or PreviewItemCategory.Conflict);
        var confirmed = await _dialogs.ConfirmAsync(new ConfirmationRequest(
            "Bắt đầu sao lưu?",
            $"CloudKeeperSN sẽ sao lưu {transferCount} tệp ({DashboardViewModel.FormatBytes(Preview.EstimatedBytes)}) từ {SourceFolder.DisplayPath} đến {DestinationFolder.DisplayPath}.",
            "Bắt đầu sao lưu",
            SupportingText: "Google Drive sẽ không bị thay đổi. Các tệp hiện có trên OneDrive sẽ không bị xóa hoặc ghi đè theo mặc định."), cancellationToken);
        if (!confirmed) return;

        Stage = BackupWorkflowStage.Running;
        _demoData.Workspace.SetBackupRunning(true);
        _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var progress = new Progress<DemoTransferProgress>(UpdateProgress);
        DemoTransferResult transferResult;
        try
        {
            transferResult = await _transferEngine.RunAsync(Preview.Items, progress, _runCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            transferResult = new DemoTransferResult(DemoRunStatus.Cancelled, CompletedCount, SkippedCount, WarningCount, FailedCount, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, VerificationLevel.UploadedButNotFullyVerified);
        }
        finally
        {
            _runCancellation.Dispose();
            _runCancellation = null;
            _demoData.Workspace.SetBackupRunning(false);
        }
        Complete(transferResult);
    }

    private void UpdateProgress(DemoTransferProgress progress)
    {
        OverallProgress = progress.TotalItems == 0 ? 0 : progress.ProcessedItems * 100d / progress.TotalItems;
        CurrentFile = progress.CurrentFile;
        CurrentOperation = progress.CurrentOperation;
        FileProgress = $"{progress.ProcessedItems} / {progress.TotalItems} tệp";
        ByteProgress = $"{DashboardViewModel.FormatBytes(progress.TransferredBytes)} / {DashboardViewModel.FormatBytes(progress.TotalBytes)}";
        CompletedCount = progress.CompletedCount;
        SkippedCount = progress.SkippedCount;
        WarningCount = progress.WarningCount;
        FailedCount = progress.FailedCount;
        RetryCount = progress.RetryCount;
    }

    private void Pause() { _transferEngine.Pause(); IsPaused = true; CurrentOperation = "Đã tạm dừng nhận công việc mới"; }
    private void Resume() { _transferEngine.Resume(); IsPaused = false; CurrentOperation = "Đang tiếp tục sao lưu"; }

    private async Task CancelAsync(CancellationToken cancellationToken)
    {
        var confirmed = await _dialogs.ConfirmAsync(new ConfirmationRequest(
            "Hủy lần sao lưu này?",
            "Các tệp đã hoàn tất vẫn được giữ trên OneDrive. Dữ liệu nguồn trên Google Drive không thay đổi.",
            "Hủy sao lưu",
            IsDangerous: true,
            SupportingText: "Lần chạy sẽ xuất hiện là đã hủy trong lịch sử."), cancellationToken);
        if (confirmed) _runCancellation?.Cancel();
    }

    private Task RetryFailedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Result is null) return Task.CompletedTask;
        Result = Result with { Headline = "Sao lưu hoàn tất với một số cảnh báo", Tone = StatusTone.Warning, CompletedCount = Result.CompletedCount + Result.FailedCount, FailedCount = 0, CanRetryFailed = false, Verification = VietnamesePresentationMapper.Verification(VerificationLevel.VerifiedBySizeAndMetadata) };
        return Task.CompletedTask;
    }

    private void Complete(DemoTransferResult result)
    {
        var headline = result.Status switch
        {
            DemoRunStatus.Completed => "Sao lưu hoàn tất",
            DemoRunStatus.CompletedWithWarnings => "Sao lưu hoàn tất với một số cảnh báo",
            DemoRunStatus.Cancelled => "Đã hủy sao lưu",
            _ => "Sao lưu chưa hoàn tất"
        };
        var tone = result.Status switch { DemoRunStatus.Completed => StatusTone.Success, DemoRunStatus.CompletedWithWarnings => StatusTone.Warning, DemoRunStatus.Cancelled => StatusTone.Neutral, _ => StatusTone.Error };
        Result = new BackupResultViewModel(
            headline, tone, result.CompletedCount, result.SkippedCount, result.WarningCount, result.FailedCount,
            DashboardViewModel.FormatBytes(result.TransferredBytes),
            $"{result.StartedAt:HH:mm} – {result.CompletedAt:HH:mm, dd/MM/yyyy}",
            VietnamesePresentationMapper.Verification(result.Verification),
            DestinationFolder?.DisplayPath ?? "OneDrive",
            result.FailedCount > 0);
        var run = new DemoBackupRun(Guid.NewGuid(), "Sao lưu một chiều", SourceFolder?.DisplayPath ?? "Google Drive", DestinationFolder?.DisplayPath ?? "OneDrive", result.StartedAt, result.CompletedAt - result.StartedAt, result.Status, result.CompletedCount, result.SkippedCount, result.WarningCount, result.FailedCount, result.TransferredBytes, result.Verification, ["Đã bắt đầu quét thư mục nguồn", result.Status == DemoRunStatus.Cancelled ? "Đã hủy sao lưu theo yêu cầu" : "Đã hoàn tất xử lý hàng đợi"]);
        _demoData.Workspace.AddOrReplaceRun(run);
        Stage = BackupWorkflowStage.Result;
    }

    private void ApplyPreviewFilter()
    {
        var query = _allPreviewItems.AsEnumerable();
        if (SelectedFilter.Category is { } category) query = query.Where(item => item.Category == category);
        if (!string.IsNullOrWhiteSpace(PreviewSearch)) query = query.Where(item => item.OriginalName.Contains(PreviewSearch, StringComparison.CurrentCultureIgnoreCase) || item.RelativePath.Contains(PreviewSearch, StringComparison.CurrentCultureIgnoreCase));
        VisiblePreviewItems.Clear();
        foreach (var item in query) VisiblePreviewItems.Add(item);
    }

    private void Reset()
    {
        Stage = BackupWorkflowStage.Setup;
        Preview = null;
        Result = null;
        _allPreviewItems.Clear();
        VisiblePreviewItems.Clear();
        OverallProgress = 0;
        IsPaused = false;
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(IsSetup)); OnPropertyChanged(nameof(IsPreview)); OnPropertyChanged(nameof(IsRunning)); OnPropertyChanged(nameof(IsResult));
        OnPropertyChanged(nameof(SourceFolderLabel)); OnPropertyChanged(nameof(DestinationFolderLabel)); OnPropertyChanged(nameof(CanScan)); OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(TransferAvailabilityMessage));
        ((AsyncRelayCommand)SelectSourceCommand).NotifyCanExecuteChanged(); ((AsyncRelayCommand)SelectDestinationCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)ScanCommand).NotifyCanExecuteChanged(); ((AsyncRelayCommand)StartBackupCommand).NotifyCanExecuteChanged();
        ((RelayCommand)CancelScanCommand).RaiseCanExecuteChanged();
        ((RelayCommand)PauseCommand).RaiseCanExecuteChanged(); ((RelayCommand)ResumeCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)CancelCommand).NotifyCanExecuteChanged(); ((AsyncRelayCommand)RetryFailedCommand).NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_inventoryScanner is not null) _inventoryScanner.StateChanged -= InventoryScannerStateChanged;
        _runCancellation?.Cancel(); _runCancellation?.Dispose();
        foreach (var command in new[] { SelectSourceCommand, SelectDestinationCommand, ScanCommand, StartBackupCommand, CancelCommand, RetryFailedCommand }.OfType<AsyncRelayCommand>()) command.Dispose();
    }
}

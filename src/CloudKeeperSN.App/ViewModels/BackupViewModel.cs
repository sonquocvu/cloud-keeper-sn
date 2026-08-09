using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;
using CloudKeeperSN.App.Development;
using CloudKeeperSN.App.Presentation;
using CloudKeeperSN.App.Services;
using CloudKeeperSN.Application.Persistence;
using CloudKeeperSN.Application.Scanning;
using CloudKeeperSN.Application.Storage;
using CloudKeeperSN.Domain.Export;
using CloudKeeperSN.Domain.Storage;
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

public sealed class BackupViewModel : PageViewModel, IDisposable
{
    private readonly DemoDataService _demoData;
    private readonly DemoBackupPlanner _planner;
    private readonly DemoTransferEngine _transferEngine;
    private readonly IFolderPickerService _folderPicker;
    private readonly IDialogService _dialogs;
    private readonly bool _isDemoMode;
    private readonly IStorageProvider? _realGoogleProvider;
    private readonly IApplicationSettingRepository? _settings;
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
    private string? _realGoogleAccountId;
    private string _accountDisplayName = "Google Drive";
    private string _scanProgressText = "Chưa bắt đầu quét.";
    private string? _scanErrorMessage;

    public BackupViewModel(
        DemoDataService demoData,
        DemoBackupPlanner planner,
        DemoTransferEngine transferEngine,
        IFolderPickerService folderPicker,
        IDialogService dialogs)
        : this(demoData, planner, transferEngine, folderPicker, dialogs, true, null, null)
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
        IApplicationSettingRepository settings)
        : this(
            demoData,
            planner,
            transferEngine,
            folderPicker,
            dialogs,
            configuration.IsEnabled,
            providers.SingleOrDefault(provider => provider.Descriptor.ProviderId == "google-drive"),
            settings)
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
        IApplicationSettingRepository? settings)
        : base(
            "backup",
            isDemoMode ? "Sao lưu một chiều" : "Quét Google Drive chỉ đọc",
            isDemoMode ? "Google Drive là nguồn; OneDrive là nơi lưu bản sao." : "Duyệt, quét siêu dữ liệu và lập kế hoạch; chưa truyền nội dung tệp.")
    {
        _demoData = demoData;
        _planner = planner;
        _transferEngine = transferEngine;
        _folderPicker = folderPicker;
        _dialogs = dialogs;
        _isDemoMode = isDemoMode;
        _realGoogleProvider = realGoogleProvider;
        _settings = settings;
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
        SelectSourceCommand = new AsyncRelayCommand(SelectSourceAsync, () => _googleConnected && Stage == BackupWorkflowStage.Setup);
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
        : IsProductionMode ? "Đã sẵn sàng quét siêu dữ liệu và lập bản xem trước chỉ đọc."
        : !_oneDriveConnected ? "Hãy kết nối OneDrive trên trang Tài khoản."
        : DestinationFolder is null ? "Hãy chọn thư mục đích trên OneDrive."
        : "Đã đủ thông tin để quét và xem trước.";
    public bool IsScanning { get => _isScanning; private set { if (SetProperty(ref _isScanning, value)) NotifyState(); } }
    public string ScanProgressText { get => _scanProgressText; private set => SetProperty(ref _scanProgressText, value); }
    public string? ScanErrorMessage { get => _scanErrorMessage; private set => SetProperty(ref _scanErrorMessage, value); }
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
            _realGoogleAccountId = account?.ProviderAccountId;
            AccountDisplayName = account is null ? "Google Drive chưa kết nối" : account.Email ?? account.DisplayName;
            await RestoreRealSelectionAsync(cancellationToken);
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
        var accountId = IsDemoMode ? DemoDataService.GoogleAccountId : _realGoogleAccountId;
        if (string.IsNullOrWhiteSpace(accountId)) return;
        var selected = await _folderPicker.PickAsync(new FolderPickerRequest("google-drive", accountId, "root", "Chọn thư mục nguồn trên Google Drive", false), cancellationToken);
        if (selected is not null)
        {
            SourceFolder = new(selected.ProviderId, selected.AccountId, selected.FolderId, selected.DisplayPath);
            if (IsProductionMode) await PersistRealSelectionAsync(cancellationToken);
        }
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
        Preview = null;
        try
        {
            if (IsProductionMode)
            {
                var browser = _realGoogleProvider as IStorageBrowserCapability
                    ?? throw new InvalidOperationException("Google Drive không hỗ trợ duyệt thư mục.");
                var progress = new Progress<SourceScanProgress>(value =>
                    ScanProgressText = $"Đã phát hiện {value.DiscoveredItems:N0} mục; đang đọc {value.CurrentPath}.");
                var scan = await new SourceScanner(browser).ScanAsync(SourceFolder.AccountId, SourceFolder.FolderId, cancellationToken, progress);
                Preview = BuildRealPreview(SourceFolder.DisplayPath, scan);
                ScanProgressText = $"Hoàn tất: {scan.FileCount:N0} tệp, {scan.FolderCount:N0} thư mục.";
                await PersistScanSummaryAsync(scan, cancellationToken);
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
            ScanProgressText = "Đã hủy quét. Kết quả chưa hoàn tất không được dùng làm bản xem trước.";
            throw;
        }
        catch (Exception exception)
        {
            ScanErrorMessage = exception is ProviderOperationException failure
                ? ProviderFailureMessages.ToVietnamese(failure.Category)
                : "Không thể hoàn tất bản quét. Không có dữ liệu Google Drive nào bị thay đổi; vui lòng thử lại.";
            ScanProgressText = "Bản quét chưa hoàn tất.";
        }
        finally { IsScanning = false; }
    }

    private static BackupPreviewViewState BuildRealPreview(string sourcePath, SourceScanResult scan)
    {
        var items = new List<PreviewItemViewModel>();
        var exportCount = 0;
        foreach (var scanned in scan.Items.Where(scanned => scanned.Item.Kind != StorageItemKind.Folder))
        {
            var item = scanned.Item;
            var path = scanned.RelativePath.ToString();
            if (item.Kind == StorageItemKind.Shortcut || item.MimeType == GoogleNativeExportPolicy.GoogleShortcut)
            {
                items.Add(new(item.ItemId, item.Name, path, null, PreviewItemCategory.Skip, "Bỏ qua lối tắt", null,
                    "Lối tắt được bỏ qua để tránh vòng lặp; mục đích không được tự động lần theo."));
                continue;
            }
            if (item.Kind == StorageItemKind.ProviderNativeFile)
            {
                var decision = GoogleNativeExportPolicy.Decide(item.MimeType ?? string.Empty);
                if (!decision.IsSupported)
                {
                    items.Add(new(item.ItemId, item.Name, path, null, PreviewItemCategory.Unsupported, "Không hỗ trợ", null, decision.VietnameseExplanation));
                    continue;
                }
                exportCount++;
                items.Add(new(item.ItemId, item.Name, path, null, PreviewItemCategory.Warning, $"Dự kiến xuất {decision.Extension}", item.Name + decision.Extension,
                    decision.VietnameseExplanation + " Đây mới là kế hoạch; chưa có dữ liệu nào được xuất."));
                continue;
            }
            items.Add(new(item.ItemId, item.Name, path, item.Size, PreviewItemCategory.Copy, "Đủ điều kiện", item.Name,
                item.Size is null ? "Kích thước không được Google Drive cung cấp; chưa có nội dung nào được đọc." : "Đã đọc siêu dữ liệu; chưa có nội dung nào được tải xuống."));
        }

        return new BackupPreviewViewState(
            sourcePath,
            "Chưa có đích lưu trữ thực",
            scan.FolderCount,
            scan.FileCount,
            scan.EstimatedBytes,
            items.Count(item => item.Category is PreviewItemCategory.Copy or PreviewItemCategory.Warning),
            items.Count(item => item.Category == PreviewItemCategory.Skip),
            0,
            exportCount,
            items.Count(item => item.Category == PreviewItemCategory.Unsupported),
            scan.VietnameseWarnings.Count + items.Count(item => item.Category is PreviewItemCategory.Warning or PreviewItemCategory.Unsupported),
            scan.UnknownSizeCount,
            items,
            scan.VietnameseWarnings);
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

    private async Task RestoreRealSelectionAsync(CancellationToken cancellationToken)
    {
        if (_settings is null || string.IsNullOrWhiteSpace(_realGoogleAccountId)) return;
        var accountId = await _settings.GetAsync("backup.google.source.account", cancellationToken);
        var folderId = await _settings.GetAsync("backup.google.source.folder", cancellationToken);
        var displayPath = await _settings.GetAsync("backup.google.source.path", cancellationToken);
        if (string.Equals(accountId, _realGoogleAccountId, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(folderId) && !string.IsNullOrWhiteSpace(displayPath))
        {
            SourceFolder = new FolderSelectionViewModel("google-drive", accountId!, folderId, displayPath);
        }
    }

    private async Task PersistRealSelectionAsync(CancellationToken cancellationToken)
    {
        if (_settings is null || SourceFolder is null) return;
        await _settings.SetAsync("backup.google.source.account", SourceFolder.AccountId, cancellationToken);
        await _settings.SetAsync("backup.google.source.folder", SourceFolder.FolderId, cancellationToken);
        await _settings.SetAsync("backup.google.source.path", SourceFolder.DisplayPath, cancellationToken);
    }

    private Task PersistScanSummaryAsync(SourceScanResult scan, CancellationToken cancellationToken)
    {
        if (_settings is null) return Task.CompletedTask;
        var summary = JsonSerializer.Serialize(new
        {
            completedAtUtc = DateTimeOffset.UtcNow,
            scan.FileCount,
            scan.FolderCount,
            scan.EstimatedBytes,
            scan.UnknownSizeCount,
            scan.NativeFileCount,
            scan.UnsupportedNativeFileCount,
            scan.ShortcutCount,
            warningCount = scan.VietnameseWarnings.Count
        });
        return _settings.SetAsync("backup.google.last-complete-scan-summary", summary, cancellationToken);
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
        _runCancellation?.Cancel(); _runCancellation?.Dispose();
        foreach (var command in new[] { SelectSourceCommand, SelectDestinationCommand, ScanCommand, StartBackupCommand, CancelCommand, RetryFailedCommand }.OfType<AsyncRelayCommand>()) command.Dispose();
    }
}

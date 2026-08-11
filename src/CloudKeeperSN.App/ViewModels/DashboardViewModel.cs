using System.Collections.ObjectModel;
using System.Windows.Input;
using CloudKeeperSN.App.Development;
using CloudKeeperSN.App.Presentation;
using CloudKeeperSN.App.Services;
using CloudKeeperSN.Application.Scanning;
using CloudKeeperSN.Application.Storage;
using CloudKeeperSN.Domain.Scanning;

namespace CloudKeeperSN.App.ViewModels;

public sealed record SummaryCardViewModel(string Title, string Value, string Detail, string IconGlyph);

public sealed record RecentRunViewModel(
    Guid Id,
    string Name,
    string Route,
    string StartedAt,
    string FileSummary,
    string Capacity,
    StatusPresentation Status,
    StatusPresentation Verification);

public sealed class DashboardViewModel : PageViewModel, IDisposable
{
    private readonly DemoDataService _demoData;
    private readonly bool _isDemoMode;
    private readonly IProviderAuthenticationService? _authentication;
    private readonly IDriveInventoryScanner? _inventoryScanner;
    private readonly IUiDispatcher _dispatcher;
    private SummaryCardViewModel _googleCard = new("Google Drive", "Chưa kết nối", "Google Drive là nguồn", "\uEBD3");
    private SummaryCardViewModel _oneDriveCard = new("OneDrive", "Chưa tích hợp", "Chưa có đích lưu trữ thực", "\uE753");
    private SummaryCardViewModel _lastBackupCard = new("Lần quét gần nhất", "Chưa có", "Bắt đầu quét Google Drive", "\uE823");
    private SummaryCardViewModel _pendingCard = new("Mục cần kiểm tra", "0 mục", "Chưa có snapshot", "\uE7C1");
    private bool _isEmpty = true;
    private bool _disposed;
    private ICommand? _createBackupCommand;

    public DashboardViewModel(DemoDataService demoData)
        : this(demoData, true, null, null, null) { }

    public DashboardViewModel(
        DemoDataService demoData,
        bool isDemoMode,
        IProviderAuthenticationService? authentication,
        IDriveInventoryScanner? inventoryScanner,
        IUiDispatcher? dispatcher)
        : base("dashboard", "Tổng quan", isDemoMode ? "Trạng thái an toàn và hoạt động sao lưu gần đây." : "Tài khoản và danh mục Google Drive chỉ đọc gần nhất.")
    {
        _demoData = demoData;
        _isDemoMode = isDemoMode;
        _authentication = authentication;
        _inventoryScanner = inventoryScanner;
        _dispatcher = dispatcher ?? InlineUiDispatcher.Instance;
    }

    public event EventHandler? CreateBackupRequested;
    public ICommand CreateBackupCommand => _createBackupCommand ??= new RelayCommand(_ => RequestCreateBackup());
    public string HeaderTitle => _isDemoMode ? "Sao lưu an toàn, rõ ràng và có kiểm soát" : "Danh mục Google Drive an toàn và chỉ đọc";
    public string HeaderSubtitle => _isDemoMode ? "Google Drive là nguồn • OneDrive là nơi lưu bản sao • Không xóa dữ liệu nguồn" : "Theo dõi tài khoản, lần quét gần nhất và các mục cần kiểm tra • Không tải nội dung tệp";
    public string PrimaryActionText => _isDemoMode ? "_Tạo bản sao lưu" : "_Quét Google Drive";
    public SummaryCardViewModel GoogleCard { get => _googleCard; private set => SetProperty(ref _googleCard, value); }
    public SummaryCardViewModel OneDriveCard { get => _oneDriveCard; private set => SetProperty(ref _oneDriveCard, value); }
    public SummaryCardViewModel LastBackupCard { get => _lastBackupCard; private set => SetProperty(ref _lastBackupCard, value); }
    public SummaryCardViewModel PendingCard { get => _pendingCard; private set => SetProperty(ref _pendingCard, value); }
    public ObservableCollection<RecentRunViewModel> RecentRuns { get; } = [];
    public bool IsEmpty { get => _isEmpty; private set => SetProperty(ref _isEmpty, value); }

    public override async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (_isDemoMode)
        {
            _demoData.Workspace.Changed -= WorkspaceChanged;
            _demoData.Workspace.Changed += WorkspaceChanged;
            await RefreshDemoAsync(cancellationToken);
            return;
        }

        if (_inventoryScanner is not null)
        {
            _inventoryScanner.StateChanged -= InventoryStateChanged;
            _inventoryScanner.StateChanged += InventoryStateChanged;
        }
        await RefreshProductionAsync(cancellationToken);
    }

    private async Task RefreshProductionAsync(CancellationToken cancellationToken)
    {
        var account = _authentication?.State.Account;
        if (account is null && _authentication is not null)
            account = await _authentication.GetCachedAccountAsync(cancellationToken);
        GoogleCard = new("Google Drive", account is null ? "Chưa kết nối" : "Đã kết nối",
            account?.Email ?? account?.DisplayName ?? "Kết nối tài khoản để quét", "\uEBD3");
        OneDriveCard = new("OneDrive", "Chưa tích hợp", "Không có kết nối hoặc truyền dữ liệu thật", "\uE753");

        var latest = account is null || _inventoryScanner is null
            ? null
            : await _inventoryScanner.GetLatestSuccessfulAsync(account.ProviderAccountId, cancellationToken);
        LastBackupCard = latest?.CompletedAtUtc is { } completed
            ? new("Lần quét gần nhất", completed.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                $"{latest.FileCount:N0} tệp • {latest.FolderCount:N0} thư mục • {FormatBytes(latest.KnownBytes)}", "\uE823")
            : new("Lần quét gần nhất", "Chưa có", "Bắt đầu quét Google Drive", "\uE823");
        PendingCard = latest is null
            ? new("Mục cần kiểm tra", "0 mục", "Chưa có snapshot hoàn chỉnh", "\uE7C1")
            : new("Mục cần kiểm tra", $"{latest.UnresolvedCount:N0} mục", $"{latest.BackupEligibleCount:N0} mục đủ điều kiện trong tương lai", "\uE7C1");

        RecentRuns.Clear();
        if (_inventoryScanner is not null)
        {
            foreach (var run in (await _inventoryScanner.GetRecentAsync(5, cancellationToken)))
            {
                RecentRuns.Add(new RecentRunViewModel(run.ScanId, "Quét Google Drive", "Google Drive → Danh mục cục bộ",
                    run.StartedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                    $"{run.FileCount:N0} tệp • {run.FolderCount:N0} thư mục", FormatBytes(run.KnownBytes),
                    ScanStatus(run.Status), new("Chỉ siêu dữ liệu", StatusTone.Information, "\uE946")));
            }
        }
        IsEmpty = RecentRuns.Count == 0;
    }

    private async Task RefreshDemoAsync(CancellationToken cancellationToken)
    {
        var accounts = await _demoData.GetAccountsAsync(cancellationToken);
        var google = accounts.FirstOrDefault(account => account.ProviderId == "google-drive" && account.IsConnected);
        var microsoft = accounts.FirstOrDefault(account => account.ProviderId == "one-drive" && account.IsConnected);
        GoogleCard = new("Google Drive", google is null ? "Chưa kết nối" : "Đã kết nối", google?.DisplayName ?? "Google Drive là nguồn", "\uEBD3");
        OneDriveCard = new("OneDrive", microsoft is null ? "Chưa kết nối" : "Đã kết nối", microsoft?.DisplayName ?? "OneDrive là nơi lưu bản sao", "\uE753");
        var latest = _demoData.Workspace.Runs.FirstOrDefault();
        LastBackupCard = latest is null
            ? new("Lần sao lưu gần nhất", "Chưa có", "Tạo bản sao lưu đầu tiên", "\uE823")
            : new("Lần sao lưu gần nhất", latest.StartedAt.ToString("dd/MM/yyyy HH:mm"), $"{latest.CompletedFiles} tệp • {FormatBytes(latest.TransferredBytes)}", "\uE823");
        var pending = _demoData.Workspace.Runs.Sum(run => run.WarningCount + run.FailedCount);
        PendingCard = new("Đang chờ xử lý", $"{pending} mục", pending == 0 ? "Không có lỗi cần chú ý" : "Xem cảnh báo trong lịch sử", "\uE7C1");
        RecentRuns.Clear();
        foreach (var run in _demoData.Workspace.Runs.Take(5))
            RecentRuns.Add(new(run.Id, run.Name, $"{run.Source} → {run.Destination}", run.StartedAt.ToString("dd/MM/yyyy HH:mm"),
                $"{run.CompletedFiles} hoàn tất • {run.SkippedFiles} bỏ qua", FormatBytes(run.TransferredBytes),
                VietnamesePresentationMapper.RunStatus(run.Status), VietnamesePresentationMapper.Verification(run.Verification)));
        IsEmpty = RecentRuns.Count == 0;
    }

    private async void InventoryStateChanged(DriveInventoryScanState state)
    {
        if (_disposed || state.Status != DriveInventoryScanStatus.Completed) return;
        try { await _dispatcherInvokeAsync(() => RefreshProductionAsync(CancellationToken.None)); } catch { }
    }

    private Task _dispatcherInvokeAsync(Func<Task> action)
    {
        Task? pending = null;
        _dispatcher.Invoke(() => pending = action());
        return pending ?? Task.CompletedTask;
    }

    private async void WorkspaceChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        await RefreshDemoAsync(CancellationToken.None);
    }

    private static StatusPresentation ScanStatus(DriveInventoryRunStatus status) => status switch
    {
        DriveInventoryRunStatus.Completed => new("Đã hoàn tất", StatusTone.Success, "\uE73E"),
        DriveInventoryRunStatus.Cancelled => new("Đã hủy", StatusTone.Neutral, "\uE711"),
        DriveInventoryRunStatus.RequiresReauthentication => new("Cần đăng nhập lại", StatusTone.Warning, "\uE7BA"),
        DriveInventoryRunStatus.Scanning => new("Chưa hoàn tất", StatusTone.Information, "\uE895"),
        DriveInventoryRunStatus.Interrupted => new("Bị gián đoạn", StatusTone.Warning, "\uE7BA"),
        _ => new("Thất bại", StatusTone.Error, "\uEA39")
    };

    public void RequestCreateBackup() => CreateBackupRequested?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        _disposed = true;
        _demoData.Workspace.Changed -= WorkspaceChanged;
        if (_inventoryScanner is not null) _inventoryScanner.StateChanged -= InventoryStateChanged;
    }

    internal static string FormatBytes(long bytes)
        => VietnameseNumberFormatter.FormatBytes(bytes);
}

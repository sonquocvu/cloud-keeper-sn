using System.Collections.ObjectModel;
using System.Windows.Input;
using CloudKeeperSN.App.Development;
using CloudKeeperSN.App.Presentation;

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

public sealed class DashboardViewModel(DemoDataService demoData) : PageViewModel(
    "dashboard",
    "Tổng quan",
    "Trạng thái an toàn và hoạt động sao lưu gần đây."), IDisposable
{
    private SummaryCardViewModel _googleCard = new("Google Drive", "Chưa kết nối", "Google Drive là nguồn", "\uEBD3");
    private SummaryCardViewModel _oneDriveCard = new("OneDrive", "Chưa kết nối", "OneDrive là nơi lưu bản sao", "\uE753");
    private SummaryCardViewModel _lastBackupCard = new("Lần sao lưu gần nhất", "Chưa có", "Tạo bản sao lưu đầu tiên", "\uE823");
    private SummaryCardViewModel _pendingCard = new("Đang chờ xử lý", "0 mục", "Không có lỗi cần chú ý", "\uE7C1");
    private bool _isEmpty = true;
    private bool _disposed;
    private ICommand? _createBackupCommand;

    public event EventHandler? CreateBackupRequested;
    public ICommand CreateBackupCommand => _createBackupCommand ??= new RelayCommand(_ => RequestCreateBackup());

    public SummaryCardViewModel GoogleCard { get => _googleCard; private set => SetProperty(ref _googleCard, value); }
    public SummaryCardViewModel OneDriveCard { get => _oneDriveCard; private set => SetProperty(ref _oneDriveCard, value); }
    public SummaryCardViewModel LastBackupCard { get => _lastBackupCard; private set => SetProperty(ref _lastBackupCard, value); }
    public SummaryCardViewModel PendingCard { get => _pendingCard; private set => SetProperty(ref _pendingCard, value); }
    public ObservableCollection<RecentRunViewModel> RecentRuns { get; } = [];
    public bool IsEmpty { get => _isEmpty; private set => SetProperty(ref _isEmpty, value); }

    public override async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (demoData is null) return;
        demoData.Workspace.Changed -= WorkspaceChanged;
        demoData.Workspace.Changed += WorkspaceChanged;
        await RefreshAsync(cancellationToken);
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var accounts = await demoData.GetAccountsAsync(cancellationToken);
        var google = accounts.FirstOrDefault(account => account.ProviderId == "google-drive" && account.IsConnected);
        var microsoft = accounts.FirstOrDefault(account => account.ProviderId == "one-drive" && account.IsConnected);
        GoogleCard = new("Google Drive", google is null ? "Chưa kết nối" : "Đã kết nối", google?.DisplayName ?? "Google Drive là nguồn", "\uEBD3");
        OneDriveCard = new("OneDrive", microsoft is null ? "Chưa kết nối" : "Đã kết nối", microsoft?.DisplayName ?? "OneDrive là nơi lưu bản sao", "\uE753");

        var latest = demoData.Workspace.Runs.FirstOrDefault();
        LastBackupCard = latest is null
            ? new("Lần sao lưu gần nhất", "Chưa có", "Tạo bản sao lưu đầu tiên", "\uE823")
            : new("Lần sao lưu gần nhất", latest.StartedAt.ToString("dd/MM/yyyy HH:mm"), $"{latest.CompletedFiles} tệp • {FormatBytes(latest.TransferredBytes)}", "\uE823");
        var pending = demoData.Workspace.Runs.Sum(run => run.WarningCount + run.FailedCount);
        PendingCard = new("Đang chờ xử lý", $"{pending} mục", pending == 0 ? "Không có lỗi cần chú ý" : "Xem cảnh báo trong lịch sử", "\uE7C1");

        RecentRuns.Clear();
        foreach (var run in demoData.Workspace.Runs.Take(5))
        {
            RecentRuns.Add(new RecentRunViewModel(
                run.Id,
                run.Name,
                $"{run.Source} → {run.Destination}",
                run.StartedAt.ToString("dd/MM/yyyy HH:mm"),
                $"{run.CompletedFiles} hoàn tất • {run.SkippedFiles} bỏ qua",
                FormatBytes(run.TransferredBytes),
                VietnamesePresentationMapper.RunStatus(run.Status),
                VietnamesePresentationMapper.Verification(run.Verification)));
        }
        IsEmpty = RecentRuns.Count == 0;
    }

    private async void WorkspaceChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        await RefreshAsync(CancellationToken.None);
    }

    public void RequestCreateBackup() => CreateBackupRequested?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        _disposed = true;
        if (demoData is not null) demoData.Workspace.Changed -= WorkspaceChanged;
    }

    internal static string FormatBytes(long bytes)
    {
        string[] units = ["byte", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return unit == 0 ? $"{bytes} byte" : $"{value:0.#} {units[unit]}";
    }
}

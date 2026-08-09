using System.Collections.ObjectModel;
using System.Windows.Input;
using CloudKeeperSN.App.Development;
using CloudKeeperSN.App.Presentation;
using CloudKeeperSN.App.Services;
using CloudKeeperSN.Application.Scanning;
using CloudKeeperSN.Domain.Scanning;

namespace CloudKeeperSN.App.ViewModels;

public sealed record HistoryStatusFilter(string Label, string? StatusKey);
public sealed record HistoryDateFilter(string Label, int? Days);

public sealed record HistoryRunViewModel(
    Guid Id,
    string Name,
    DateTimeOffset StartedAtValue,
    string StatusKey,
    StatusPresentation Status,
    StatusPresentation Verification,
    string Route,
    string StartLabel,
    string DurationLabel,
    string FileSummary,
    string Capacity,
    IReadOnlyList<string> Timeline);

public sealed class HistoryViewModel : PageViewModel, IDisposable
{
    private readonly DemoDataService _demoData;
    private readonly IDiagnosticExportService _diagnosticExport;
    private readonly bool _isDemoMode;
    private readonly IDriveInventoryScanner? _inventoryScanner;
    private readonly IUiDispatcher _dispatcher;
    private readonly List<HistoryRunViewModel> _allRuns = [];
    private string _searchText = string.Empty;
    private HistoryStatusFilter _selectedStatusFilter;
    private HistoryDateFilter _selectedDateFilter;
    private HistoryRunViewModel? _selectedRun;
    private string _exportMessage = string.Empty;
    private bool _disposed;

    public HistoryViewModel(DemoDataService demoData, IDiagnosticExportService diagnosticExport)
        : this(demoData, diagnosticExport, true, null, null) { }

    public HistoryViewModel(DemoDataService demoData, IDiagnosticExportService diagnosticExport, bool isDemoMode,
        IDriveInventoryScanner? inventoryScanner, IUiDispatcher? dispatcher)
        : base("history", "Lịch sử", isDemoMode ? "Xem lại kết quả và các quyết định của từng lần sao lưu." : "Xem các lần quét danh mục Google Drive hoàn tất hoặc chưa hoàn tất.")
    {
        _demoData = demoData;
        _diagnosticExport = diagnosticExport;
        _isDemoMode = isDemoMode;
        _inventoryScanner = inventoryScanner;
        _dispatcher = dispatcher ?? InlineUiDispatcher.Instance;
        StatusFilters = [new("Tất cả trạng thái", null), new("Đã hoàn tất", "Completed"), new("Có cảnh báo", "CompletedWithWarnings"), new("Chưa hoàn tất", "Failed"), new("Đã hủy", "Cancelled")];
        DateFilters = [new("Mọi thời gian", null), new("7 ngày gần đây", 7), new("30 ngày gần đây", 30)];
        _selectedStatusFilter = StatusFilters[0];
        _selectedDateFilter = DateFilters[0];
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => _isDemoMode && _allRuns.Count > 0);
    }

    public ObservableCollection<HistoryRunViewModel> VisibleRuns { get; } = [];
    public IReadOnlyList<HistoryStatusFilter> StatusFilters { get; }
    public IReadOnlyList<HistoryDateFilter> DateFilters { get; }
    public ICommand ExportCommand { get; }
    public bool IsEmpty => VisibleRuns.Count == 0;
    public bool HasSelection => SelectedRun is not null;
    public string SearchText { get => _searchText; set { if (SetProperty(ref _searchText, value)) ApplyFilter(); } }
    public HistoryStatusFilter SelectedStatusFilter { get => _selectedStatusFilter; set { if (SetProperty(ref _selectedStatusFilter, value)) ApplyFilter(); } }
    public HistoryDateFilter SelectedDateFilter { get => _selectedDateFilter; set { if (SetProperty(ref _selectedDateFilter, value)) ApplyFilter(); } }
    public HistoryRunViewModel? SelectedRun { get => _selectedRun; set { if (SetProperty(ref _selectedRun, value)) OnPropertyChanged(nameof(HasSelection)); } }
    public string ExportMessage { get => _exportMessage; private set => SetProperty(ref _exportMessage, value); }

    public override async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (_isDemoMode)
        {
            _demoData.Workspace.Changed -= WorkspaceChanged;
            _demoData.Workspace.Changed += WorkspaceChanged;
            RefreshDemo();
            return;
        }
        if (_inventoryScanner is not null)
        {
            _inventoryScanner.StateChanged -= InventoryStateChanged;
            _inventoryScanner.StateChanged += InventoryStateChanged;
        }
        await RefreshInventoryAsync(cancellationToken);
    }

    private void RefreshDemo()
    {
        _allRuns.Clear();
        _allRuns.AddRange(_demoData.Workspace.Runs.OrderByDescending(run => run.StartedAt).Select(run => new HistoryRunViewModel(
            run.Id, run.Name, run.StartedAt, run.Status.ToString(), VietnamesePresentationMapper.RunStatus(run.Status),
            VietnamesePresentationMapper.Verification(run.Verification), $"{run.Source} → {run.Destination}", run.StartedAt.ToString("dd/MM/yyyy HH:mm"),
            run.Duration.TotalMinutes < 1 ? $"{run.Duration.TotalSeconds:0} giây" : $"{run.Duration.TotalMinutes:0} phút",
            $"{run.CompletedFiles} hoàn tất • {run.SkippedFiles} bỏ qua • {run.FailedCount} lỗi", DashboardViewModel.FormatBytes(run.TransferredBytes), run.Timeline)));
        ApplyFilter();
    }

    private async Task RefreshInventoryAsync(CancellationToken cancellationToken)
    {
        _allRuns.Clear();
        if (_inventoryScanner is not null)
        {
            foreach (var run in await _inventoryScanner.GetRecentAsync(200, cancellationToken))
            {
                var completed = run.CompletedAtUtc ?? DateTimeOffset.UtcNow;
                var duration = completed - run.StartedAtUtc;
                _allRuns.Add(new HistoryRunViewModel(run.ScanId, "Quét Google Drive", run.StartedAtUtc, run.Status.ToString(), ScanStatus(run.Status),
                    new("Chỉ siêu dữ liệu", StatusTone.Information, "\uE946"), "Google Drive → Danh mục SQLite cục bộ",
                    run.StartedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"), duration.TotalMinutes < 1 ? $"{duration.TotalSeconds:0} giây" : $"{duration.TotalMinutes:0} phút",
                    $"{run.FileCount:N0} tệp • {run.FolderCount:N0} thư mục • {run.UnresolvedCount:N0} cần kiểm tra", DashboardViewModel.FormatBytes(run.KnownBytes),
                    ["Đã bắt đầu quét metadata", run.IsComplete ? "Snapshot đã được công bố" : "Snapshot chưa hoàn tất; kết quả trước vẫn được giữ"]));
            }
        }
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = _allRuns.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(SearchText)) query = query.Where(run => run.Name.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) || run.Route.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase));
        if (SelectedStatusFilter.StatusKey is { } status) query = query.Where(run => string.Equals(run.StatusKey, status, StringComparison.Ordinal));
        if (SelectedDateFilter.Days is { } days) query = query.Where(run => run.StartedAtValue >= DateTimeOffset.Now.AddDays(-days));
        VisibleRuns.Clear();
        foreach (var run in query.OrderByDescending(run => run.StartedAtValue)) VisibleRuns.Add(run);
        SelectedRun = VisibleRuns.FirstOrDefault();
        OnPropertyChanged(nameof(IsEmpty));
        ((AsyncRelayCommand)ExportCommand).NotifyCanExecuteChanged();
    }

    private async Task ExportAsync(CancellationToken cancellationToken)
    {
        var path = await _diagnosticExport.ExportAsync(_demoData.Workspace.Runs, cancellationToken);
        ExportMessage = path is null ? string.Empty : $"Đã xuất thông tin chẩn đoán đến {path}";
    }

    private async void InventoryStateChanged(DriveInventoryScanState state)
    {
        if (_disposed || state.Status is not (DriveInventoryScanStatus.Completed or DriveInventoryScanStatus.Cancelled or DriveInventoryScanStatus.Failed or DriveInventoryScanStatus.RequiresReauthentication)) return;
        Task? refresh = null;
        _dispatcher.Invoke(() => refresh = RefreshInventoryAsync(CancellationToken.None));
        if (refresh is not null) try { await refresh; } catch { }
    }

    private static StatusPresentation ScanStatus(DriveInventoryRunStatus status) => status switch
    {
        DriveInventoryRunStatus.Completed => new("Đã hoàn tất", StatusTone.Success, "\uE73E"),
        DriveInventoryRunStatus.Cancelled => new("Đã hủy", StatusTone.Neutral, "\uE711"),
        DriveInventoryRunStatus.RequiresReauthentication => new("Cần đăng nhập lại", StatusTone.Warning, "\uE7BA"),
        DriveInventoryRunStatus.Interrupted => new("Bị gián đoạn", StatusTone.Warning, "\uE7BA"),
        DriveInventoryRunStatus.Scanning => new("Chưa hoàn tất", StatusTone.Information, "\uE895"),
        _ => new("Thất bại", StatusTone.Error, "\uEA39")
    };

    private void WorkspaceChanged(object? sender, EventArgs e) { if (!_disposed) RefreshDemo(); }

    public void Dispose()
    {
        _disposed = true;
        _demoData.Workspace.Changed -= WorkspaceChanged;
        if (_inventoryScanner is not null) _inventoryScanner.StateChanged -= InventoryStateChanged;
        ((AsyncRelayCommand)ExportCommand).Dispose();
    }
}

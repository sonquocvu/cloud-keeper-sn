using System.Collections.ObjectModel;
using System.Windows.Input;
using CloudKeeperSN.App.Development;
using CloudKeeperSN.App.Presentation;
using CloudKeeperSN.App.Services;

namespace CloudKeeperSN.App.ViewModels;

public sealed record HistoryStatusFilter(string Label, DemoRunStatus? Status);
public sealed record HistoryDateFilter(string Label, int? Days);

public sealed record HistoryRunViewModel(
    DemoBackupRun Source,
    StatusPresentation Status,
    StatusPresentation Verification,
    string Route,
    string StartLabel,
    string DurationLabel,
    string FileSummary,
    string Capacity);

public sealed class HistoryViewModel : PageViewModel, IDisposable
{
    private readonly DemoDataService _demoData;
    private readonly IDiagnosticExportService _diagnosticExport;
    private readonly List<HistoryRunViewModel> _allRuns = [];
    private string _searchText = string.Empty;
    private HistoryStatusFilter _selectedStatusFilter;
    private HistoryDateFilter _selectedDateFilter;
    private HistoryRunViewModel? _selectedRun;
    private string _exportMessage = string.Empty;
    private bool _disposed;

    public HistoryViewModel(DemoDataService demoData, IDiagnosticExportService diagnosticExport)
        : base("history", "Lịch sử", "Xem lại kết quả và các quyết định của từng lần sao lưu.")
    {
        _demoData = demoData;
        _diagnosticExport = diagnosticExport;
        StatusFilters =
        [
            new("Tất cả trạng thái", null), new("Đã hoàn tất", DemoRunStatus.Completed),
            new("Có cảnh báo", DemoRunStatus.CompletedWithWarnings), new("Chưa hoàn tất", DemoRunStatus.Failed),
            new("Đã hủy", DemoRunStatus.Cancelled)
        ];
        DateFilters = [new("Mọi thời gian", null), new("7 ngày gần đây", 7), new("30 ngày gần đây", 30)];
        _selectedStatusFilter = StatusFilters[0];
        _selectedDateFilter = DateFilters[0];
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => _allRuns.Count > 0);
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

    public override Task LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _demoData.Workspace.Changed -= WorkspaceChanged;
        _demoData.Workspace.Changed += WorkspaceChanged;
        Refresh();
        return Task.CompletedTask;
    }

    private void Refresh()
    {
        _allRuns.Clear();
        _allRuns.AddRange(_demoData.Workspace.Runs.OrderByDescending(run => run.StartedAt).Select(run => new HistoryRunViewModel(
            run,
            VietnamesePresentationMapper.RunStatus(run.Status),
            VietnamesePresentationMapper.Verification(run.Verification),
            $"{run.Source} → {run.Destination}",
            run.StartedAt.ToString("dd/MM/yyyy HH:mm"),
            run.Duration.TotalMinutes < 1 ? $"{run.Duration.TotalSeconds:0} giây" : $"{run.Duration.TotalMinutes:0} phút",
            $"{run.CompletedFiles} hoàn tất • {run.SkippedFiles} bỏ qua • {run.FailedCount} lỗi",
            DashboardViewModel.FormatBytes(run.TransferredBytes))));
        ApplyFilter();
        ((AsyncRelayCommand)ExportCommand).NotifyCanExecuteChanged();
    }

    private void ApplyFilter()
    {
        var query = _allRuns.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(SearchText)) query = query.Where(run => run.Source.Name.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) || run.Route.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase));
        if (SelectedStatusFilter.Status is { } status) query = query.Where(run => run.Source.Status == status);
        if (SelectedDateFilter.Days is { } days)
        {
            var threshold = DateTimeOffset.Now.AddDays(-days);
            query = query.Where(run => run.Source.StartedAt >= threshold);
        }
        VisibleRuns.Clear();
        foreach (var run in query.OrderByDescending(run => run.Source.StartedAt)) VisibleRuns.Add(run);
        SelectedRun = VisibleRuns.FirstOrDefault();
        OnPropertyChanged(nameof(IsEmpty));
    }

    private async Task ExportAsync(CancellationToken cancellationToken)
    {
        var path = await _diagnosticExport.ExportAsync(_demoData.Workspace.Runs, cancellationToken);
        ExportMessage = path is null ? string.Empty : $"Đã xuất thông tin chẩn đoán đến {path}";
    }

    private void WorkspaceChanged(object? sender, EventArgs e) { if (!_disposed) Refresh(); }

    public void Dispose()
    {
        _disposed = true;
        _demoData.Workspace.Changed -= WorkspaceChanged;
        ((AsyncRelayCommand)ExportCommand).Dispose();
    }
}


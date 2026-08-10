using System.Collections.ObjectModel;
using System.Windows.Input;
using CloudKeeperSN.App.Development;
using CloudKeeperSN.App.Services;
using CloudKeeperSN.Application.Planning;
using CloudKeeperSN.Application.Scanning;
using CloudKeeperSN.Application.Storage;
using CloudKeeperSN.Domain.Planning;
using CloudKeeperSN.Domain.Scanning;

namespace CloudKeeperSN.App.ViewModels;

public sealed record InventoryFilterOption(string Key, string Label);

public sealed class InventoryItemNodeViewModel : ObservableObject
{
    private readonly Action<InventoryItemNodeViewModel, bool>? _toggle;
    private bool _isChecked;
    private bool _suppressToggle;
    private string _selectionNote = "Chưa chọn";

    public InventoryItemNodeViewModel(DriveInventoryItem? item, string name, Action<InventoryItemNodeViewModel, bool>? toggle)
    {
        Item = item;
        Name = name;
        _toggle = toggle;
    }

    public DriveInventoryItem? Item { get; }
    public string Name { get; }
    public string ItemId => Item?.FileId ?? string.Empty;
    public string DisplayPath => Item?.DisplayPath ?? Name;
    public bool IsSelectable => Item is not null;
    public bool IsFolder => Item?.Kind == DriveInventoryItemKind.Folder;
    public bool RequiresReview { get; private set; }
    public string TypeLabel => Item?.Kind switch
    {
        DriveInventoryItemKind.Folder => "Thư mục",
        DriveInventoryItemKind.GoogleWorkspaceFile => "Tệp Google Workspace",
        DriveInventoryItemKind.Shortcut => "Lối tắt",
        _ => "Tệp"
    };
    public string SizeLabel => Item?.Size is { } size ? DashboardViewModel.FormatBytes(size) : IsFolder ? "—" : "Không xác định";
    public string EligibilityLabel => Item is null ? string.Empty : Item.IsBackupEligible ? "Có thể chọn cho sao lưu tương lai" : Item.SkipReason ?? "Cần kiểm tra";
    public string SelectionNote { get => _selectionNote; private set => SetProperty(ref _selectionNote, value); }
    public ObservableCollection<InventoryItemNodeViewModel> Children { get; } = [];

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (!SetProperty(ref _isChecked, value) || _suppressToggle || Item is null) return;
            _toggle?.Invoke(this, value);
        }
    }

    public void Apply(InventorySelectionState state, bool hasDirectRule)
    {
        _suppressToggle = true;
        IsChecked = state.IsCoveredByIncludeRule;
        _suppressToggle = false;
        RequiresReview = state.RequiresReview;
        SelectionNote = state.IsCoveredByIncludeRule
            ? hasDirectRule ? "Đã chọn trực tiếp" : "Được chọn từ thư mục cha"
            : hasDirectRule ? "Đã loại trừ" : state.AppliedRuleItemId is not null ? "Bị loại trừ từ thư mục cha" : "Chưa chọn";
        OnPropertyChanged(nameof(RequiresReview));
    }
}

public sealed class InventoryPlanViewModel : PageViewModel, IDisposable
{
    private readonly bool _isDemoMode;
    private readonly IProviderAuthenticationService? _authentication;
    private readonly IBackupSelectionPlanService _service;
    private readonly IDriveInventoryScanner? _scanner;
    private readonly IUiDispatcher _dispatcher;
    private readonly Dictionary<string, InventoryItemNodeViewModel> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BackupSelectionRule> _rules = new(StringComparer.Ordinal);
    private IReadOnlyList<DriveInventoryItem> _items = [];
    private BackupSelectionPlan? _plan;
    private DriveInventoryRun? _latestScan;
    private InventoryFilterOption _selectedFilter;
    private string _searchText = string.Empty;
    private string _planName = "Kế hoạch sao lưu Google Drive";
    private string _statusMessage = "Đang chờ dữ liệu danh mục.";
    private string _saveMessage = string.Empty;
    private string _reconciliationMessage = string.Empty;
    private bool _isLoading;
    private bool _hasSnapshot;
    private bool _hasUnsavedChanges;
    private int _selectedItemCount;
    private int _selectedFolderCount;
    private int _backupEligibleItemCount;
    private int _unknownSizeCount;
    private int _reviewItemCount;
    private int _selectedReviewItemCount;
    private int _missingRuleTargetCount;
    private long _knownBytes;
    private long _loadVersion;
    private bool _disposed;

    public InventoryPlanViewModel(
        DemoConfiguration configuration,
        IBackupSelectionPlanService service,
        IProviderAuthenticationService? authentication = null,
        IDriveInventoryScanner? scanner = null,
        IUiDispatcher? dispatcher = null)
        : base("inventory-plan", "Kế hoạch sao lưu", "Duyệt snapshot Google Drive và lưu lựa chọn cục bộ cho một lần sao lưu trong tương lai.")
    {
        _isDemoMode = configuration.IsEnabled;
        _service = service;
        _authentication = authentication;
        _scanner = scanner;
        _dispatcher = dispatcher ?? InlineUiDispatcher.Instance;
        Filters =
        [
            new("all", "Tất cả"), new("selected", "Đã chọn"), new("review", "Cần kiểm tra"),
            new("file", "Tệp thường"), new("folder", "Thư mục"), new("workspace", "Google Workspace"),
            new("shortcut", "Lối tắt"), new("shared", "Được chia sẻ")
        ];
        _selectedFilter = Filters[0];
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => CanSave);
        ClearSelectionCommand = new RelayCommand(_ => ClearSelection(), _ => HasSnapshot && _rules.Count > 0);
        RemoveMissingRulesCommand = new RelayCommand(_ => RemoveMissingRules(), _ => MissingRuleTargetCount > 0);
        if (_scanner is not null) _scanner.StateChanged += ScannerStateChanged;
    }

    public IReadOnlyList<InventoryFilterOption> Filters { get; }
    public ObservableCollection<InventoryItemNodeViewModel> TreeRoots { get; } = [];
    public ObservableCollection<InventoryItemNodeViewModel> SearchResults { get; } = [];
    public ICommand SaveCommand { get; }
    public ICommand ClearSelectionCommand { get; }
    public ICommand RemoveMissingRulesCommand { get; }
    public bool IsLoading { get => _isLoading; private set { if (SetProperty(ref _isLoading, value)) NotifyCommands(); } }
    public bool HasSnapshot { get => _hasSnapshot; private set { if (SetProperty(ref _hasSnapshot, value)) NotifyCommands(); } }
    public bool HasNoSnapshot => !HasSnapshot && !IsLoading;
    public bool HasUnsavedChanges { get => _hasUnsavedChanges; private set { if (SetProperty(ref _hasUnsavedChanges, value)) NotifyCommands(); } }
    public bool CanSave => HasSnapshot && !IsLoading && HasUnsavedChanges && !string.IsNullOrWhiteSpace(PlanName);
    public string PlanName { get => _planName; set { if (SetProperty(ref _planName, value)) HasUnsavedChanges = true; } }
    public string SearchText { get => _searchText; set { if (SetProperty(ref _searchText, value)) ApplyFilter(); } }
    public InventoryFilterOption SelectedFilter { get => _selectedFilter; set { if (SetProperty(ref _selectedFilter, value)) ApplyFilter(); } }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public string SaveMessage { get => _saveMessage; private set => SetProperty(ref _saveMessage, value); }
    public string ReconciliationMessage { get => _reconciliationMessage; private set { if (SetProperty(ref _reconciliationMessage, value)) OnPropertyChanged(nameof(HasReconciliationMessage)); } }
    public bool HasReconciliationMessage => !string.IsNullOrWhiteSpace(ReconciliationMessage);
    public int SelectedItemCount { get => _selectedItemCount; private set => SetProperty(ref _selectedItemCount, value); }
    public int SelectedFolderCount { get => _selectedFolderCount; private set => SetProperty(ref _selectedFolderCount, value); }
    public int BackupEligibleItemCount { get => _backupEligibleItemCount; private set => SetProperty(ref _backupEligibleItemCount, value); }
    public int UnknownSizeCount { get => _unknownSizeCount; private set => SetProperty(ref _unknownSizeCount, value); }
    public int ReviewItemCount { get => _reviewItemCount; private set => SetProperty(ref _reviewItemCount, value); }
    public int SelectedReviewItemCount { get => _selectedReviewItemCount; private set => SetProperty(ref _selectedReviewItemCount, value); }
    public int MissingRuleTargetCount { get => _missingRuleTargetCount; private set { if (SetProperty(ref _missingRuleTargetCount, value)) { OnPropertyChanged(nameof(HasMissingRules)); ((RelayCommand)RemoveMissingRulesCommand).RaiseCanExecuteChanged(); } } }
    public bool HasMissingRules => MissingRuleTargetCount > 0;
    public long KnownBytes { get => _knownBytes; private set { if (SetProperty(ref _knownBytes, value)) OnPropertyChanged(nameof(KnownBytesLabel)); } }
    public string KnownBytesLabel => DashboardViewModel.FormatBytes(KnownBytes);
    public string SnapshotLabel => _latestScan?.CompletedAtUtc?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "Chưa có snapshot";

    public override async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (HasSnapshot && HasUnsavedChanges)
        {
            StatusMessage = "Các thay đổi chưa lưu vẫn được giữ. Hãy lưu hoặc bỏ toàn bộ lựa chọn trước khi làm mới snapshot.";
            return;
        }
        var version = Interlocked.Increment(ref _loadVersion);
        IsLoading = true;
        SaveMessage = string.Empty;
        try
        {
            if (_isDemoMode)
            {
                ClearWorkspace("Kế hoạch cục bộ chỉ sử dụng snapshot Google Drive thật; chế độ trình diễn không tạo dữ liệu giả tại đây.");
                return;
            }
            var account = _authentication?.State.Account ?? (_authentication is null ? null : await _authentication.GetCachedAccountAsync(cancellationToken));
            if (account is not { IsConnected: true })
            {
                ClearWorkspace("Hãy kết nối Google Drive và hoàn tất một lần quét trước khi tạo kế hoạch.");
                return;
            }
            var workspace = await _service.LoadAsync(account.ProviderAccountId, cancellationToken);
            if (version != Interlocked.Read(ref _loadVersion)) return;
            if (workspace is null)
            {
                ClearWorkspace("Chưa có snapshot Google Drive hoàn chỉnh. Hãy mở trang Sao lưu và quét Google Drive trước.");
                return;
            }
            ApplyWorkspace(workspace);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            if (version == Interlocked.Read(ref _loadVersion))
                ClearWorkspace("Không thể mở danh mục hoặc kế hoạch cục bộ. Dữ liệu Google Drive không bị thay đổi; vui lòng thử lại.");
        }
        finally
        {
            if (version == Interlocked.Read(ref _loadVersion)) IsLoading = false;
            OnPropertyChanged(nameof(HasNoSnapshot));
        }
    }

    private void ApplyWorkspace(BackupPlanWorkspace workspace)
    {
        _latestScan = workspace.LatestScan;
        _plan = workspace.Plan;
        _items = workspace.InventoryItems;
        _rules.Clear();
        foreach (var rule in workspace.Plan.Rules) _rules[rule.ItemId] = rule;
        _planName = workspace.Plan.Name;
        OnPropertyChanged(nameof(PlanName));
        OnPropertyChanged(nameof(SnapshotLabel));
        HasSnapshot = true;
        HasUnsavedChanges = false;
        StatusMessage = $"Đang dùng snapshot hoàn tất gồm {workspace.LatestScan.TotalItems:N0} mục.";
        ReconciliationMessage = FormatReconciliation(workspace.Reconciliation);
        MissingRuleTargetCount = workspace.Reconciliation.MissingRuleTargetCount;
        BuildNodes();
        ApplyEvaluation(workspace.Evaluation);
        ApplyFilter();
    }

    private void BuildNodes()
    {
        _nodes.Clear();
        TreeRoots.Clear();
        foreach (var item in _items)
            _nodes[item.FileId] = new InventoryItemNodeViewModel(item, item.Name, ToggleSelection);
        var myDrive = new InventoryItemNodeViewModel(null, "Drive của tôi", null);
        var shared = new InventoryItemNodeViewModel(null, "Được chia sẻ", null);
        var unresolved = new InventoryItemNodeViewModel(null, "Không xác định được thư mục cha", null);
        foreach (var item in _items.OrderBy(item => item.DisplayPath, StringComparer.CurrentCultureIgnoreCase))
        {
            var node = _nodes[item.FileId];
            if (item.Location != DriveInventoryLocation.Unresolved && item.ParentId is { } parentId &&
                _nodes.TryGetValue(parentId, out var parent) && parent.IsFolder)
                parent.Children.Add(node);
            else
                (item.Location switch
                {
                    DriveInventoryLocation.Shared => shared,
                    DriveInventoryLocation.Unresolved => unresolved,
                    _ => myDrive
                }).Children.Add(node);
        }
        if (myDrive.Children.Count > 0) TreeRoots.Add(myDrive);
        if (shared.Children.Count > 0) TreeRoots.Add(shared);
        if (unresolved.Children.Count > 0) TreeRoots.Add(unresolved);
    }

    private void ToggleSelection(InventoryItemNodeViewModel node, bool include)
    {
        if (node.Item is null || _plan is null) return;
        if (node.IsFolder)
        {
            var descendants = DescendantIds(node.ItemId);
            foreach (var id in descendants) _rules.Remove(id);
        }
        _rules[node.ItemId] = new(node.ItemId,
            include ? BackupSelectionRuleMode.Include : BackupSelectionRuleMode.Exclude,
            node.Item.Kind, node.Item.Name);
        HasUnsavedChanges = true;
        SaveMessage = string.Empty;
        Reevaluate();
    }

    private IReadOnlySet<string> DescendantIds(string folderId)
    {
        var children = _items.Where(item => item.ParentId is not null)
            .GroupBy(item => item.ParentId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(item => item.FileId).ToArray(), StringComparer.Ordinal);
        var result = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(folderId);
        while (pending.Count > 0)
        {
            var parent = pending.Pop();
            if (!children.TryGetValue(parent, out var ids)) continue;
            foreach (var id in ids)
                if (result.Add(id)) pending.Push(id);
        }
        return result;
    }

    private void Reevaluate()
    {
        if (_plan is null) return;
        var draft = _plan with { Name = PlanName, Rules = _rules.Values.OrderBy(rule => rule.ItemId, StringComparer.Ordinal).ToArray() };
        ApplyEvaluation(_service.Evaluate(draft, _items));
        ApplyFilter();
    }

    private void ApplyEvaluation(BackupSelectionEvaluation evaluation)
    {
        foreach (var state in evaluation.Items.Values)
            if (_nodes.TryGetValue(state.Item.FileId, out var node)) node.Apply(state, _rules.ContainsKey(state.Item.FileId));
        SelectedItemCount = evaluation.Summary.SelectedItemCount;
        BackupEligibleItemCount = evaluation.Summary.BackupEligibleItemCount;
        SelectedFolderCount = evaluation.Summary.SelectedFolderCount;
        KnownBytes = evaluation.Summary.KnownBytes;
        UnknownSizeCount = evaluation.Summary.UnknownSizeCount;
        ReviewItemCount = evaluation.Summary.ReviewItemCount;
        SelectedReviewItemCount = evaluation.Summary.SelectedReviewItemCount;
    }

    private void ApplyFilter()
    {
        var query = _nodes.Values.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(SearchText))
            query = query.Where(node => node.Name.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase) ||
                node.DisplayPath.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase));
        query = SelectedFilter.Key switch
        {
            "selected" => query.Where(node => node.IsChecked),
            "review" => query.Where(node => node.RequiresReview),
            "file" => query.Where(node => node.Item?.Kind == DriveInventoryItemKind.File),
            "folder" => query.Where(node => node.Item?.Kind == DriveInventoryItemKind.Folder),
            "workspace" => query.Where(node => node.Item?.Kind == DriveInventoryItemKind.GoogleWorkspaceFile),
            "shortcut" => query.Where(node => node.Item?.Kind == DriveInventoryItemKind.Shortcut),
            "shared" => query.Where(node => node.Item?.Location == DriveInventoryLocation.Shared),
            _ => query
        };
        SearchResults.Clear();
        foreach (var node in query.OrderBy(node => node.DisplayPath, StringComparer.CurrentCultureIgnoreCase)) SearchResults.Add(node);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (_plan is null || _latestScan is null || !CanSave) return;
        var draft = _plan with
        {
            Name = PlanName.Trim(),
            Rules = _rules.Values.OrderBy(rule => rule.ItemId, StringComparer.Ordinal).ToArray()
        };
        try
        {
            _plan = await _service.SaveAsync(draft, _latestScan.ScanId, cancellationToken);
            HasUnsavedChanges = false;
            ReconciliationMessage = MissingRuleTargetCount == 0 ? string.Empty
                : $"Có {MissingRuleTargetCount:N0} quy tắc trỏ đến mục hiện không còn trong snapshot.";
            SaveMessage = "Đã lưu kế hoạch cục bộ. Chưa có tệp nào được tải xuống, xuất hoặc sao lưu.";
        }
        catch (BackupSelectionPlanSnapshotChangedException)
        {
            SaveMessage = "Có snapshot Google Drive mới hơn. Các thay đổi chưa lưu vẫn được giữ; hãy mở lại trang để đối chiếu trước khi lưu.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            SaveMessage = "Không thể lưu kế hoạch vào cơ sở dữ liệu cục bộ. Các thay đổi hiện tại vẫn được giữ; vui lòng thử lại.";
        }
    }

    private void ClearSelection()
    {
        _rules.Clear();
        MissingRuleTargetCount = 0;
        HasUnsavedChanges = true;
        SaveMessage = string.Empty;
        Reevaluate();
        ((RelayCommand)ClearSelectionCommand).RaiseCanExecuteChanged();
    }

    private void RemoveMissingRules()
    {
        var currentIds = _items.Select(item => item.FileId).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _rules.Keys.Where(id => !currentIds.Contains(id)).ToArray()) _rules.Remove(id);
        MissingRuleTargetCount = 0;
        ReconciliationMessage = string.Empty;
        HasUnsavedChanges = true;
        Reevaluate();
    }

    private void ClearWorkspace(string message)
    {
        _latestScan = null;
        _plan = null;
        _items = [];
        _nodes.Clear();
        _rules.Clear();
        TreeRoots.Clear();
        SearchResults.Clear();
        HasSnapshot = false;
        HasUnsavedChanges = false;
        StatusMessage = message;
        ReconciliationMessage = string.Empty;
        MissingRuleTargetCount = 0;
        OnPropertyChanged(nameof(SnapshotLabel));
    }

    private static string FormatReconciliation(BackupPlanReconciliation value)
    {
        if (!value.UsesNewerSnapshot) return value.MissingRuleTargetCount == 0
            ? string.Empty
            : $"Có {value.MissingRuleTargetCount:N0} quy tắc trỏ đến mục hiện không còn trong snapshot.";
        return $"Đang đối chiếu với snapshot mới: {value.NewlySelectedItemCount:N0} mục mới được chọn theo quy tắc thư mục; " +
               $"{value.MissingPreviouslySelectedItemCount:N0} mục đã chọn trước đây không còn; {value.MissingRuleTargetCount:N0} quy tắc cần kiểm tra.";
    }

    private async void ScannerStateChanged(DriveInventoryScanState state)
    {
        if (_disposed || state.Status != DriveInventoryScanStatus.Completed) return;
        if (HasUnsavedChanges)
        {
            _dispatcher.Invoke(() => ReconciliationMessage =
                "Có snapshot Google Drive mới hơn. Các thay đổi chưa lưu vẫn được giữ; hãy lưu hoặc mở lại trang để đối chiếu.");
            return;
        }
        Task? refresh = null;
        _dispatcher.Invoke(() => refresh = LoadAsync(CancellationToken.None));
        if (refresh is not null) try { await refresh; } catch { }
    }

    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(HasNoSnapshot));
        ((AsyncRelayCommand)SaveCommand).NotifyCanExecuteChanged();
        ((RelayCommand)ClearSelectionCommand).RaiseCanExecuteChanged();
    }

    public void Dispose()
    {
        _disposed = true;
        if (_scanner is not null) _scanner.StateChanged -= ScannerStateChanged;
        ((AsyncRelayCommand)SaveCommand).Dispose();
    }
}

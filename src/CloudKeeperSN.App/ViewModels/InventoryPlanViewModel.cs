using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows.Input;
using CloudKeeperSN.App.Development;
using CloudKeeperSN.App.Presentation;
using CloudKeeperSN.App.Services;
using CloudKeeperSN.Application.Planning;
using CloudKeeperSN.Application.Persistence;
using CloudKeeperSN.Application.Scanning;
using CloudKeeperSN.Application.Storage;
using CloudKeeperSN.Domain.Planning;
using CloudKeeperSN.Domain.Scanning;

namespace CloudKeeperSN.App.ViewModels;

public sealed record InventoryFilterOption(string Key, string Label);

public sealed class InventoryItemNodeViewModel : ObservableObject
{
    private readonly Action<InventoryItemNodeViewModel, bool>? _toggle;
    private readonly Action<InventoryItemNodeViewModel>? _expand;
    private bool? _isChecked;
    private bool _suppressToggle;
    private string _selectionNote = "Chưa chọn";
    private bool _isCurrentFolder;
    private bool _isExpanded;

    public InventoryItemNodeViewModel(
        DriveInventoryItem? item,
        string name,
        Action<InventoryItemNodeViewModel, bool>? toggle,
        string? iconGlyph = null,
        Action<InventoryItemNodeViewModel>? expand = null,
        DriveInventoryLocation? rootLocation = null,
        bool isPlaceholder = false)
    {
        Item = item;
        Name = name;
        _toggle = toggle;
        _expand = expand;
        RootLocation = rootLocation;
        IsPlaceholder = isPlaceholder;
        IconGlyph = iconGlyph ?? item?.Kind switch
        {
            DriveInventoryItemKind.Folder => "\uE8B7",
            DriveInventoryItemKind.Shortcut => "\uE753",
            _ => "\uE8A5"
        };
    }

    public DriveInventoryItem? Item { get; }
    public string Name { get; }
    public DriveInventoryLocation? RootLocation { get; }
    public bool IsPlaceholder { get; }
    public string ItemId => Item?.FileId ?? string.Empty;
    public string DisplayPath => Item?.DisplayPath ?? Name;
    public bool IsSelectable => Item is not null;
    public bool IsFolder => Item?.Kind == DriveInventoryItemKind.Folder;
    public bool CanBrowse => !IsPlaceholder && (Item is null || IsFolder);
    public bool ChildrenLoaded { get; set; }
    public string IconGlyph { get; }
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
    public bool IsCurrentFolder { get => _isCurrentFolder; set => SetProperty(ref _isCurrentFolder, value); }
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!SetProperty(ref _isExpanded, value) || !value) return;
            _expand?.Invoke(this);
        }
    }
    public StatusPresentation ItemStatus => Item switch
    {
        null => new(string.Empty, StatusTone.Neutral, "\uE946"),
        _ when RequiresReview && Item.Kind == DriveInventoryItemKind.Shortcut => new("Lối tắt — chưa thể sao lưu", StatusTone.Warning, "\uE7BA"),
        _ when RequiresReview => new("Cần kiểm tra", StatusTone.Warning, "\uE7BA"),
        _ when IsChecked == true => new("Đã chọn", StatusTone.Success, "\uE73E"),
        _ when Item.Size is null && Item.Kind != DriveInventoryItemKind.Folder => new("Chưa rõ dung lượng", StatusTone.Information, "\uE946"),
        _ when !Item.IsBackupEligible && Item.Kind != DriveInventoryItemKind.Folder => new("Không đủ điều kiện", StatusTone.Warning, "\uE7BA"),
        _ => new(string.Empty, StatusTone.Neutral, "\uE946")
    };
    public bool HasItemStatus => !string.IsNullOrWhiteSpace(ItemStatus.Text);
    public ObservableCollection<InventoryItemNodeViewModel> Children { get; } = [];

    public bool? IsChecked
    {
        get => _isChecked;
        set
        {
            if (!SetProperty(ref _isChecked, value)) return;
            OnPropertyChanged(nameof(ItemStatus));
            OnPropertyChanged(nameof(HasItemStatus));
            if (_suppressToggle || Item is null || value is null) return;
            _toggle?.Invoke(this, value.Value);
        }
    }

    public void Apply(InventorySelectionState state, bool hasDirectRule)
    {
        _suppressToggle = true;
        try { IsChecked = state.IsCoveredByIncludeRule; }
        finally { _suppressToggle = false; }
        RequiresReview = state.RequiresReview;
        SelectionNote = state.IsCoveredByIncludeRule
            ? hasDirectRule ? "Đã chọn trực tiếp" : "Được chọn từ thư mục cha"
            : hasDirectRule ? "Đã loại trừ" : state.AppliedRuleItemId is not null ? "Bị loại trừ từ thư mục cha" : "Chưa chọn";
        OnPropertyChanged(nameof(RequiresReview));
        OnPropertyChanged(nameof(ItemStatus));
        OnPropertyChanged(nameof(HasItemStatus));
    }

    public void ApplyCheckState(bool? value)
    {
        _suppressToggle = true;
        try { IsChecked = value; }
        finally { _suppressToggle = false; }
    }
}

public sealed class InventoryPlanViewModel : PageViewModel, IDisposable
{
    private readonly bool _isDemoMode;
    private readonly IProviderAuthenticationService? _authentication;
    private readonly IBackupSelectionPlanService _service;
    private readonly IDriveInventoryScanner? _scanner;
    private readonly IUiDispatcher _dispatcher;
    private readonly IDialogService? _dialogs;
    private readonly IProviderDiagnostics? _diagnostics;
    private readonly Dictionary<string, InventoryItemNodeViewModel> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DriveInventoryItem> _itemsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DriveInventoryItem[]> _childrenByParent = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DriveInventoryItem[]> _allChildrenByParent = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _resolvedParentEdges = new(StringComparer.Ordinal);
    private Dictionary<string, BackupSelectionRule> _rules = new(StringComparer.Ordinal);
    private Dictionary<string, BackupSelectionRule> _lastValidRules = new(StringComparer.Ordinal);
    private BackupSelectionEvaluation? _lastValidEvaluation;
    private bool _lastValidSelectionWasDirty;
    private IReadOnlyDictionary<string, InventorySelectionState> _evaluationStates =
        new Dictionary<string, InventorySelectionState>(StringComparer.Ordinal);
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
    private int _selectedFileCount;
    private int _selectedWorkspaceItemCount;
    private int _missingRuleTargetCount;
    private long _knownBytes;
    private long _loadVersion;
    private bool _disposed;
    private bool _isSaving;
    private bool _isSummaryUpdating;
    private InventoryItemNodeViewModel? _currentFolder;
    private string _accountIdentityLabel = "Google Drive chưa kết nối";
    private long _evaluationVersion;
    private CancellationTokenSource? _evaluationCancellation;
    private long _filterVersion;
    private CancellationTokenSource? _searchCancellation;
    private bool _isSearching;
    private string _searchErrorMessage = string.Empty;
    private string? _treeErrorMessage;

    public InventoryPlanViewModel(
        DemoConfiguration configuration,
        IBackupSelectionPlanService service,
        IProviderAuthenticationService? authentication = null,
        IDriveInventoryScanner? scanner = null,
        IUiDispatcher? dispatcher = null,
        IDialogService? dialogs = null,
        IProviderDiagnostics? diagnostics = null)
        : base("inventory-plan", "Kế hoạch sao lưu", "Chọn tệp và thư mục từ ảnh chụp Google Drive gần nhất.")
    {
        _isDemoMode = configuration.IsEnabled;
        _service = service;
        _authentication = authentication;
        _scanner = scanner;
        _dispatcher = dispatcher ?? InlineUiDispatcher.Instance;
        _dialogs = dialogs;
        _diagnostics = diagnostics;
        Filters =
        [
            new("all", "Tất cả"), new("selected", "Đã chọn"), new("review", "Cần kiểm tra"),
            new("file", "Tệp thường"), new("folder", "Thư mục"), new("workspace", "Google Workspace"),
            new("shortcut", "Lối tắt"), new("shared", "Được chia sẻ")
        ];
        _selectedFilter = Filters[0];
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => CanSave);
        ClearSelectionCommand = new AsyncRelayCommand(ClearSelectionAsync, () => HasSnapshot && _rules.Count > 0 && !IsSaving);
        ClearSearchCommand = new RelayCommand(_ => SearchText = string.Empty, _ => HasSearchText);
        BrowseFolderCommand = new RelayCommand(BrowseFolder, node => node is InventoryItemNodeViewModel { CanBrowse: true });
        RemoveMissingRulesCommand = new RelayCommand(_ => RemoveMissingRules(), _ => MissingRuleTargetCount > 0);
        if (_scanner is not null) _scanner.StateChanged += ScannerStateChanged;
    }

    public IReadOnlyList<InventoryFilterOption> Filters { get; }
    public ObservableCollection<InventoryItemNodeViewModel> TreeRoots { get; } = [];
    public ObservableCollection<InventoryItemNodeViewModel> SearchResults { get; } = [];
    public ICommand SaveCommand { get; }
    public ICommand ClearSelectionCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public ICommand BrowseFolderCommand { get; }
    public ICommand RemoveMissingRulesCommand { get; }
    public bool IsLoading { get => _isLoading; private set { if (SetProperty(ref _isLoading, value)) NotifyCommands(); } }
    public bool HasSnapshot { get => _hasSnapshot; private set { if (SetProperty(ref _hasSnapshot, value)) NotifyCommands(); } }
    public bool HasNoSnapshot => !HasSnapshot && !IsLoading;
    public bool HasUnsavedChanges { get => _hasUnsavedChanges; private set { if (SetProperty(ref _hasUnsavedChanges, value)) NotifyCommands(); } }
    public bool IsSaving { get => _isSaving; private set { if (SetProperty(ref _isSaving, value)) NotifyCommands(); } }
    public bool IsSummaryUpdating { get => _isSummaryUpdating; private set { if (SetProperty(ref _isSummaryUpdating, value)) NotifyCommands(); } }
    public bool CanSave => HasSnapshot && !IsLoading && !IsSaving && !IsSummaryUpdating && HasUnsavedChanges && !string.IsNullOrWhiteSpace(PlanName);
    public string PlanName { get => _planName; set { if (SetProperty(ref _planName, value)) HasUnsavedChanges = true; } }
    public string SearchText { get => _searchText; set { if (SetProperty(ref _searchText, value)) { OnPropertyChanged(nameof(HasSearchText)); OnPropertyChanged(nameof(CurrentLocationLabel)); OnPropertyChanged(nameof(EmptyResultsMessage)); ((RelayCommand)ClearSearchCommand).RaiseCanExecuteChanged(); if (HasSearchText) ScheduleSearch(); else ApplyFilter(); } } }
    public InventoryFilterOption SelectedFilter { get => _selectedFilter; set { if (SetProperty(ref _selectedFilter, value)) ApplyFilter(); } }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public string SaveMessage { get => _saveMessage; private set { if (SetProperty(ref _saveMessage, value)) OnPropertyChanged(nameof(SaveStateStatus)); } }
    public string ReconciliationMessage { get => _reconciliationMessage; private set { if (SetProperty(ref _reconciliationMessage, value)) OnPropertyChanged(nameof(HasReconciliationMessage)); } }
    public bool HasReconciliationMessage => !string.IsNullOrWhiteSpace(ReconciliationMessage);
    public int SelectedItemCount { get => _selectedItemCount; private set { if (SetProperty(ref _selectedItemCount, value)) OnPropertyChanged(nameof(SelectedItemCountLabel)); } }
    public int SelectedFolderCount { get => _selectedFolderCount; private set { if (SetProperty(ref _selectedFolderCount, value)) OnPropertyChanged(nameof(SelectedFolderCountLabel)); } }
    public int BackupEligibleItemCount { get => _backupEligibleItemCount; private set { if (SetProperty(ref _backupEligibleItemCount, value)) OnPropertyChanged(nameof(BackupEligibleItemCountLabel)); } }
    public int UnknownSizeCount { get => _unknownSizeCount; private set { if (SetProperty(ref _unknownSizeCount, value)) OnPropertyChanged(nameof(UnknownSizeCountLabel)); } }
    public int ReviewItemCount { get => _reviewItemCount; private set { if (SetProperty(ref _reviewItemCount, value)) OnPropertyChanged(nameof(ReviewItemCountLabel)); } }
    public int SelectedReviewItemCount { get => _selectedReviewItemCount; private set { if (SetProperty(ref _selectedReviewItemCount, value)) OnPropertyChanged(nameof(SelectedReviewItemCountLabel)); } }
    public int SelectedFileCount { get => _selectedFileCount; private set { if (SetProperty(ref _selectedFileCount, value)) OnPropertyChanged(nameof(SelectedFileCountLabel)); } }
    public int SelectedWorkspaceItemCount { get => _selectedWorkspaceItemCount; private set { if (SetProperty(ref _selectedWorkspaceItemCount, value)) OnPropertyChanged(nameof(SelectedWorkspaceItemCountLabel)); } }
    public int MissingRuleTargetCount { get => _missingRuleTargetCount; private set { if (SetProperty(ref _missingRuleTargetCount, value)) { OnPropertyChanged(nameof(HasMissingRules)); ((RelayCommand)RemoveMissingRulesCommand).RaiseCanExecuteChanged(); } } }
    public bool HasMissingRules => MissingRuleTargetCount > 0;
    public long KnownBytes { get => _knownBytes; private set { if (SetProperty(ref _knownBytes, value)) OnPropertyChanged(nameof(KnownBytesLabel)); } }
    public string KnownBytesLabel => DashboardViewModel.FormatBytes(KnownBytes);
    public string SnapshotLabel => _latestScan?.CompletedAtUtc?.ToLocalTime().ToString("dd/MM/yyyy 'lúc' HH:mm") ?? "Chưa có ảnh chụp";
    public string AccountIdentityLabel { get => _accountIdentityLabel; private set => SetProperty(ref _accountIdentityLabel, value); }
    public string SearchPlaceholder => "Tìm theo tên tệp hoặc thư mục…";
    public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);
    public bool HasSearchResults => SearchResults.Count > 0;
    public bool HasNoSearchResults => HasSnapshot && !IsSearching && !HasSearchResults;
    public bool IsSearching { get => _isSearching; private set { if (SetProperty(ref _isSearching, value)) OnPropertyChanged(nameof(HasNoSearchResults)); } }
    public string SearchErrorMessage { get => _searchErrorMessage; private set => SetProperty(ref _searchErrorMessage, value); }
    public string? TreeErrorMessage { get => _treeErrorMessage; private set => SetProperty(ref _treeErrorMessage, value); }
    public bool HasNoTreeRoots => HasSnapshot && TreeRoots.Count == 0;
    public string VisibleItemCountLabel => VietnameseNumberFormatter.FormatInteger(SearchResults.Count);
    public string CurrentLocationLabel => HasSearchText ? "Kết quả tìm kiếm" : CurrentFolder?.DisplayPath ?? "Drive của tôi";
    public string EmptyResultsMessage => HasSearchText ? "Không tìm thấy mục phù hợp." : "Không có mục nào trong thư mục này.";
    public InventoryItemNodeViewModel? CurrentFolder
    {
        get => _currentFolder;
        private set
        {
            if (ReferenceEquals(_currentFolder, value)) return;
            if (_currentFolder is not null) _currentFolder.IsCurrentFolder = false;
            _currentFolder = value;
            if (_currentFolder is not null) _currentFolder.IsCurrentFolder = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentLocationLabel));
        }
    }
    public string SelectedItemCountLabel => VietnameseNumberFormatter.FormatInteger(SelectedItemCount);
    public string SelectedFolderCountLabel => VietnameseNumberFormatter.FormatInteger(SelectedFolderCount);
    public string BackupEligibleItemCountLabel => VietnameseNumberFormatter.FormatInteger(BackupEligibleItemCount);
    public string UnknownSizeCountLabel => VietnameseNumberFormatter.FormatInteger(UnknownSizeCount);
    public string ReviewItemCountLabel => VietnameseNumberFormatter.FormatInteger(ReviewItemCount);
    public string SelectedReviewItemCountLabel => VietnameseNumberFormatter.FormatInteger(SelectedReviewItemCount);
    public string SelectedFileCountLabel => VietnameseNumberFormatter.FormatInteger(SelectedFileCount);
    public string SelectedWorkspaceItemCountLabel => VietnameseNumberFormatter.FormatInteger(SelectedWorkspaceItemCount);
    public string SaveActionText => IsSaving ? "Đang lưu…" : "_Lưu kế hoạch";
    public StatusPresentation SaveStateStatus => IsSaving
        ? new("Đang lưu…", StatusTone.Information, "\uE946")
        : SaveMessage.StartsWith("Không thể", StringComparison.Ordinal) || SaveMessage.StartsWith("Có snapshot", StringComparison.Ordinal)
            ? new("Không thể lưu", StatusTone.Error, "\uEA39")
            : HasUnsavedChanges
                ? new("Có thay đổi chưa được lưu", StatusTone.Warning, "\uE7BA")
                : new("Đã lưu", StatusTone.Success, "\uE73E");

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
            string? providerAccountId = account is { IsConnected: true } ? account.ProviderAccountId : null;
            if (providerAccountId is null && _scanner is not null)
            {
                var localScan = (await _scanner.GetRecentAsync(500, cancellationToken))
                    .Where(run => run.IsComplete && run.Status == DriveInventoryRunStatus.Completed && run.CompletedAtUtc.HasValue)
                    .OrderByDescending(run => run.CompletedAtUtc)
                    .FirstOrDefault();
                providerAccountId = localScan?.ProviderAccountId;
            }
            if (providerAccountId is null)
            {
                ClearWorkspace("Hãy kết nối Google Drive và hoàn tất một lần quét trước khi tạo kế hoạch.");
                return;
            }
            AccountIdentityLabel = account is { IsConnected: true }
                ? account.Email ?? account.DisplayName
                : "Google Drive đã ngắt kết nối • đang dùng ảnh chụp cục bộ";
            var workspace = await _service.LoadAsync(providerAccountId, cancellationToken);
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
        _lastValidRules = new Dictionary<string, BackupSelectionRule>(_rules, StringComparer.Ordinal);
        _lastValidEvaluation = workspace.Evaluation;
        _lastValidSelectionWasDirty = false;
        _planName = workspace.Plan.Name;
        OnPropertyChanged(nameof(PlanName));
        OnPropertyChanged(nameof(SnapshotLabel));
        HasSnapshot = true;
        HasUnsavedChanges = false;
        StatusMessage = $"Đang dùng ảnh chụp hoàn tất gồm {VietnameseNumberFormatter.FormatInteger(workspace.LatestScan.TotalItems)} mục.";
        ReconciliationMessage = FormatReconciliation(workspace.Reconciliation);
        MissingRuleTargetCount = workspace.Reconciliation.MissingRuleTargetCount;
        BuildNodes();
        CurrentFolder = TreeRoots.FirstOrDefault();
        ApplyEvaluation(workspace.Evaluation);
        ApplyFilter();
    }

    private void BuildNodes()
    {
        _nodes.Clear();
        _itemsById.Clear();
        _childrenByParent.Clear();
        _allChildrenByParent.Clear();
        _resolvedParentEdges.Clear();
        TreeRoots.Clear();
        foreach (var item in _items) _itemsById[item.FileId] = item;
        BuildResolvedParentEdges();
        foreach (var group in _items
                     .Where(item => !string.IsNullOrWhiteSpace(item.ParentId))
                     .GroupBy(item => item.ParentId!, StringComparer.Ordinal))
            _allChildrenByParent[group.Key] = group.ToArray();
        foreach (var group in _items
                     .Where(item => HasResolvedFolderParent(item))
                     .GroupBy(item => item.ParentId!, StringComparer.Ordinal))
            _childrenByParent[group.Key] = group.OrderBy(item => item.DisplayPath, StringComparer.CurrentCultureIgnoreCase).ToArray();

        AddRoot(DriveInventoryLocation.MyDrive, "Drive của tôi", "\uE8B7");
        AddRoot(DriveInventoryLocation.Shared, "Được chia sẻ", "\uE77B");
        AddRoot(DriveInventoryLocation.Unresolved, "Không xác định được thư mục cha", "\uE946");
        OnPropertyChanged(nameof(HasNoTreeRoots));
    }

    private void AddRoot(DriveInventoryLocation location, string name, string iconGlyph)
    {
        if (!RootItems(location).Any()) return;
        var root = new InventoryItemNodeViewModel(null, name, null, iconGlyph, ScheduleTreeExpansion, location);
        TreeRoots.Add(root);
        EnsureTreeChildren(root);
    }

    private InventoryItemNodeViewModel GetOrCreateNode(DriveInventoryItem item)
    {
        if (_nodes.TryGetValue(item.FileId, out var existing)) return existing;
        var node = new InventoryItemNodeViewModel(item, item.Name, ToggleSelection, expand: ScheduleTreeExpansion);
        _nodes[item.FileId] = node;
        if (_evaluationStates.TryGetValue(item.FileId, out var state))
            node.Apply(state, _rules.ContainsKey(item.FileId));
        if (item.Kind == DriveInventoryItemKind.Folder && HasFolderChildren(item.FileId))
            node.Children.Add(CreateLoadingPlaceholder());
        return node;
    }

    private static InventoryItemNodeViewModel CreateLoadingPlaceholder() =>
        new(null, "Đang tải cấu trúc thư mục…", null, "\uE946", isPlaceholder: true);

    private void EnsureTreeChildren(InventoryItemNodeViewModel node)
    {
        if (node.ChildrenLoaded || node.IsPlaceholder) return;
        var folders = DirectItems(node).Where(item => item.Kind == DriveInventoryItemKind.Folder);
        node.Children.Clear();
        foreach (var folder in folders.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
            node.Children.Add(GetOrCreateNode(folder));
        node.ChildrenLoaded = true;
    }

    private void ScheduleTreeExpansion(InventoryItemNodeViewModel node) =>
        _dispatcher.Post(() => TryEnsureTreeChildren(node));

    private bool TryEnsureTreeChildren(InventoryItemNodeViewModel node)
    {
        if (_disposed) return false;
        try
        {
            EnsureTreeChildren(node);
            TreeErrorMessage = null;
            return true;
        }
        catch
        {
            node.Children.Clear();
            node.Children.Add(new InventoryItemNodeViewModel(
                null, "Không thể tải cấu trúc thư mục. Vui lòng thử lại.", null, "\uE783", isPlaceholder: true));
            node.ChildrenLoaded = true;
            TreeErrorMessage = "Không thể tải cấu trúc thư mục. Kế hoạch đã lưu không bị thay đổi; vui lòng thử lại.";
            return false;
        }
    }

    private IEnumerable<DriveInventoryItem> DirectItems(InventoryItemNodeViewModel? node)
    {
        if (node?.Item is { } item)
            return _childrenByParent.TryGetValue(item.FileId, out var children) ? children : [];
        return node?.RootLocation is { } location ? RootItems(location) : [];
    }

    private IEnumerable<DriveInventoryItem> RootItems(DriveInventoryLocation location) =>
        _items.Where(item => item.Location == location && !HasResolvedFolderParent(item))
            .OrderBy(item => item.DisplayPath, StringComparer.CurrentCultureIgnoreCase);

    private bool HasResolvedFolderParent(DriveInventoryItem item) =>
        _resolvedParentEdges.TryGetValue(item.FileId, out var isResolved) && isResolved;

    private void BuildResolvedParentEdges()
    {
        var acyclic = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var start in _items)
        {
            if (acyclic.ContainsKey(start.FileId)) continue;
            var path = new List<string>();
            var positions = new Dictionary<string, int>(StringComparer.Ordinal);
            var current = start;
            var result = true;
            while (true)
            {
                if (acyclic.TryGetValue(current.FileId, out var cached))
                {
                    result = cached;
                    break;
                }
                if (positions.ContainsKey(current.FileId))
                {
                    result = false;
                    break;
                }
                positions[current.FileId] = path.Count;
                path.Add(current.FileId);
                if (string.IsNullOrWhiteSpace(current.ParentId) ||
                    string.Equals(current.ParentId, "root", StringComparison.Ordinal) ||
                    !_itemsById.TryGetValue(current.ParentId, out var parent) ||
                    parent.Kind != DriveInventoryItemKind.Folder)
                    break;
                current = parent;
            }
            foreach (var id in path) acyclic[id] = result;
        }

        foreach (var item in _items)
            _resolvedParentEdges[item.FileId] = item.Location != DriveInventoryLocation.Unresolved &&
                item.ParentId is { } parentId &&
                _itemsById.TryGetValue(parentId, out var parent) &&
                parent.Kind == DriveInventoryItemKind.Folder &&
                acyclic[item.FileId];
    }

    private bool HasFolderChildren(string folderId) =>
        _childrenByParent.TryGetValue(folderId, out var children) &&
        children.Any(item => item.Kind == DriveInventoryItemKind.Folder);

    private void ToggleSelection(InventoryItemNodeViewModel node, bool include)
    {
        if (node.Item is null || _plan is null) return;
        if (IsSaving)
        {
            if (_lastValidEvaluation is not null) ApplyEvaluation(_lastValidEvaluation);
            return;
        }
        var updatedRules = new Dictionary<string, BackupSelectionRule>(_rules, StringComparer.Ordinal);
        if (node.IsFolder)
        {
            var descendants = DescendantIds(node.ItemId);
            foreach (var id in descendants) updatedRules.Remove(id);
        }
        updatedRules[node.ItemId] = new(node.ItemId,
            include ? BackupSelectionRuleMode.Include : BackupSelectionRuleMode.Exclude,
            node.Item.Kind, node.Item.Name);
        _rules = updatedRules;
        HasUnsavedChanges = true;
        SaveMessage = string.Empty;
        Reevaluate(node.ItemId, include, node.ChildrenLoaded);
    }

    private IReadOnlySet<string> DescendantIds(string folderId)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(folderId);
        while (pending.Count > 0)
        {
            var parent = pending.Pop();
            if (!_allChildrenByParent.TryGetValue(parent, out var children)) continue;
            foreach (var child in children)
                if (result.Add(child.FileId)) pending.Push(child.FileId);
        }
        result.Remove(folderId);
        return result;
    }

    private void Reevaluate(string? changedItemId = null, bool? include = null, bool nodeLoaded = true)
    {
        if (_plan is null || _latestScan is null) return;
        var draft = _plan with { Name = PlanName, Rules = _rules.Values.OrderBy(rule => rule.ItemId, StringComparer.Ordinal).ToArray() };
        var items = _items;
        var version = Interlocked.Increment(ref _evaluationVersion);
        _evaluationCancellation?.Cancel();
        _evaluationCancellation?.Dispose();
        _evaluationCancellation = new CancellationTokenSource();
        IsSummaryUpdating = true;
        _ = ReevaluateAsync(
            draft,
            items,
            version,
            Guid.NewGuid().ToString("N"),
            changedItemId,
            include,
            nodeLoaded,
            _latestScan.ScanId,
            _plan.ProviderAccountId,
            _evaluationCancellation.Token);
    }

    private async Task ReevaluateAsync(
        BackupSelectionPlan draft,
        IReadOnlyList<DriveInventoryItem> items,
        long version,
        string correlationId,
        string? changedItemId,
        bool? include,
        bool nodeLoaded,
        Guid expectedScanId,
        string expectedAccountId,
        CancellationToken cancellationToken)
    {
        try
        {
            await WriteSelectionDiagnosticAsync(
                "FolderSelectionSummaryStarted",
                "Bắt đầu cập nhật tổng hợp lựa chọn thư mục.",
                $"operation={correlationId}; itemId={changedItemId ?? "none"}; action={SelectionAction(include)}; loaded={nodeLoaded}; rules={draft.Rules.Count}; generation={version}; scan={expectedScanId:N}")
                .ConfigureAwait(false);
            var evaluation = await _service.EvaluateAsync(draft, items, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var applied = false;
            _dispatcher.Invoke(() =>
            {
                if (!IsCurrentEvaluation(version, expectedScanId, expectedAccountId)) return;
                _lastValidRules = draft.Rules.ToDictionary(rule => rule.ItemId, StringComparer.Ordinal);
                _lastValidEvaluation = evaluation;
                _lastValidSelectionWasDirty = true;
                ApplyEvaluation(evaluation);
                ApplyFilter();
                IsSummaryUpdating = false;
                applied = true;
            });
            await WriteSelectionDiagnosticAsync(
                applied ? "FolderSelectionSummaryCompleted" : "FolderSelectionSummarySuperseded",
                applied ? "Đã cập nhật tổng hợp lựa chọn thư mục." : "Đã bỏ qua bản tổng hợp lựa chọn thư mục cũ.",
                $"operation={correlationId}; generation={version}; applied={applied}; selectedItems={evaluation.Summary.SelectedItemCount}; selectedFolders={evaluation.Summary.SelectedFolderCount}")
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await WriteSelectionDiagnosticAsync(
                "FolderSelectionSummaryCancelled",
                "Đã hủy bản tổng hợp lựa chọn thư mục cũ.",
                $"operation={correlationId}; generation={version}").ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _dispatcher.Invoke(() =>
            {
                if (!IsCurrentEvaluation(version, expectedScanId, expectedAccountId)) return;
                _rules = new Dictionary<string, BackupSelectionRule>(_lastValidRules, StringComparer.Ordinal);
                if (_lastValidEvaluation is not null)
                {
                    ApplyEvaluation(_lastValidEvaluation);
                    ApplyFilter();
                }
                HasUnsavedChanges = _lastValidSelectionWasDirty || IsPlanNameDirty();
                IsSummaryUpdating = false;
                SaveMessage = "Không thể cập nhật lựa chọn thư mục. Kế hoạch đã lưu vẫn được giữ nguyên. Vui lòng thử lại.";
            });
            await WriteSelectionDiagnosticAsync(
                "FolderSelectionSummaryFailed",
                "Không thể cập nhật tổng hợp lựa chọn thư mục.",
                $"operation={correlationId}; generation={version}; {SanitizeException(exception)}").ConfigureAwait(false);
        }
    }

    private bool IsCurrentEvaluation(long version, Guid expectedScanId, string expectedAccountId) =>
        !_disposed && version == Interlocked.Read(ref _evaluationVersion) &&
        _latestScan?.ScanId == expectedScanId &&
        string.Equals(_plan?.ProviderAccountId, expectedAccountId, StringComparison.Ordinal);

    private bool IsPlanNameDirty() => _plan is not null &&
        !string.Equals(PlanName.Trim(), _plan.Name, StringComparison.Ordinal);

    private static string SelectionAction(bool? include) => include switch
    {
        true => "include",
        false => "exclude",
        _ => "recalculate"
    };

    private async Task WriteSelectionDiagnosticAsync(string eventType, string message, string details)
    {
        if (_diagnostics is null) return;
        try
        {
            await Task.Run(() => _diagnostics.WriteAsync(eventType, message, details, CancellationToken.None))
                .ConfigureAwait(false);
        }
        catch
        {
            // Diagnostics must never replace the original selection outcome.
        }
    }

    private static string SanitizeException(Exception exception)
    {
        var stack = Regex.Replace(exception.StackTrace ?? string.Empty, @" in .*?:line \d+", string.Empty);
        return $"exception={exception.GetType().FullName}; stack={stack}";
    }

    private void ApplyEvaluation(BackupSelectionEvaluation evaluation)
    {
        _evaluationStates = evaluation.Items;
        foreach (var state in evaluation.Items.Values)
            if (_nodes.TryGetValue(state.Item.FileId, out var node)) node.Apply(state, _rules.ContainsKey(state.Item.FileId));
        ApplyTriStatePresentation();
        SelectedItemCount = evaluation.Summary.SelectedItemCount;
        BackupEligibleItemCount = evaluation.Summary.BackupEligibleItemCount;
        SelectedFolderCount = evaluation.Summary.SelectedFolderCount;
        KnownBytes = evaluation.Summary.KnownBytes;
        UnknownSizeCount = evaluation.Summary.UnknownSizeCount;
        ReviewItemCount = evaluation.Summary.ReviewItemCount;
        SelectedReviewItemCount = evaluation.Summary.SelectedReviewItemCount;
        SelectedFileCount = evaluation.Items.Values.Count(state => state.IsCoveredByIncludeRule && state.Item.Kind == DriveInventoryItemKind.File);
        SelectedWorkspaceItemCount = evaluation.Items.Values.Count(state => state.IsCoveredByIncludeRule && state.Item.Kind == DriveInventoryItemKind.GoogleWorkspaceFile);
    }

    private void ApplyFilter()
    {
        Interlocked.Increment(ref _filterVersion);
        _searchCancellation?.Cancel();
        IsSearching = false;
        SearchErrorMessage = string.Empty;
        PublishFilterResults(BuildFilterItems(SearchText, SelectedFilter.Key, CurrentFolder));
    }

    private void ScheduleSearch()
    {
        var version = Interlocked.Increment(ref _filterVersion);
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        IsSearching = true;
        SearchErrorMessage = string.Empty;
        _ = ApplySearchAsync(SearchText, SelectedFilter.Key, CurrentFolder, version, _searchCancellation.Token);
    }

    private async Task ApplySearchAsync(
        string searchText,
        string filterKey,
        InventoryItemNodeViewModel? currentFolder,
        long version,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(200, cancellationToken);
            var results = await Task.Run(() => BuildFilterItems(searchText, filterKey, currentFolder), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _dispatcher.Invoke(() =>
            {
                if (_disposed || version != Interlocked.Read(ref _filterVersion)) return;
                PublishFilterResults(results);
                IsSearching = false;
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch
        {
            _dispatcher.Invoke(() =>
            {
                if (_disposed || version != Interlocked.Read(ref _filterVersion)) return;
                SearchResults.Clear();
                SearchErrorMessage = "Không thể tìm kiếm danh mục. Kế hoạch đã lưu không bị thay đổi; vui lòng thử lại.";
                IsSearching = false;
                NotifyFilterPresentation();
            });
        }
    }

    private IReadOnlyList<DriveInventoryItem> BuildFilterItems(
        string searchText,
        string filterKey,
        InventoryItemNodeViewModel? currentFolder)
    {
        var hasSearch = !string.IsNullOrWhiteSpace(searchText);
        var query = hasSearch
            ? _items.AsEnumerable()
            : DirectItems(currentFolder);
        if (hasSearch)
            query = query.Where(item => item.Name.Contains(searchText.Trim(), StringComparison.CurrentCultureIgnoreCase) ||
                item.DisplayPath.Contains(searchText.Trim(), StringComparison.CurrentCultureIgnoreCase));
        query = filterKey switch
        {
            "selected" => query.Where(item => _evaluationStates.TryGetValue(item.FileId, out var state) && state.IsCoveredByIncludeRule),
            "review" => query.Where(item => _evaluationStates.TryGetValue(item.FileId, out var state) && state.RequiresReview),
            "file" => query.Where(item => item.Kind == DriveInventoryItemKind.File),
            "folder" => query.Where(item => item.Kind == DriveInventoryItemKind.Folder),
            "workspace" => query.Where(item => item.Kind == DriveInventoryItemKind.GoogleWorkspaceFile),
            "shortcut" => query.Where(item => item.Kind == DriveInventoryItemKind.Shortcut),
            "shared" => query.Where(item => item.Location == DriveInventoryLocation.Shared),
            _ => query
        };
        return query.OrderBy(item => item.DisplayPath, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private void PublishFilterResults(IEnumerable<DriveInventoryItem> results)
    {
        SearchResults.Clear();
        foreach (var item in results) SearchResults.Add(GetOrCreateNode(item));
        NotifyFilterPresentation();
    }

    private void NotifyFilterPresentation()
    {
        OnPropertyChanged(nameof(HasSearchResults));
        OnPropertyChanged(nameof(HasNoSearchResults));
        OnPropertyChanged(nameof(EmptyResultsMessage));
        OnPropertyChanged(nameof(VisibleItemCountLabel));
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (_plan is null || _latestScan is null || !CanSave) return;
        var draft = _plan with
        {
            Name = PlanName.Trim(),
            Rules = _rules.Values.OrderBy(rule => rule.ItemId, StringComparer.Ordinal).ToArray()
        };
        IsSaving = true;
        SaveMessage = string.Empty;
        try
        {
            _plan = await _service.SaveAsync(draft, _latestScan.ScanId, cancellationToken);
            _lastValidRules = new Dictionary<string, BackupSelectionRule>(_rules, StringComparer.Ordinal);
            _lastValidSelectionWasDirty = false;
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
        finally
        {
            IsSaving = false;
        }
    }

    private async Task ClearSelectionAsync(CancellationToken cancellationToken)
    {
        if (_rules.Count == 0) return;
        if (_dialogs is not null && (SelectedFolderCount > 0 || SelectedItemCount >= 10))
        {
            var confirmed = await _dialogs.ConfirmAsync(new ConfirmationRequest(
                "Bỏ toàn bộ lựa chọn?",
                $"Thao tác này sẽ bỏ {SelectedItemCountLabel} mục và {SelectedFolderCountLabel} thư mục khỏi kế hoạch cục bộ.",
                "Bỏ toàn bộ lựa chọn",
                IsDangerous: false,
                SupportingText: "Dữ liệu trên Google Drive không bị thay đổi. Bạn vẫn cần lưu kế hoạch để giữ thay đổi này."), cancellationToken);
            if (!confirmed) return;
        }
        _rules = new Dictionary<string, BackupSelectionRule>(StringComparer.Ordinal);
        MissingRuleTargetCount = 0;
        HasUnsavedChanges = true;
        SaveMessage = string.Empty;
        Reevaluate();
        ((AsyncRelayCommand)ClearSelectionCommand).NotifyCanExecuteChanged();
    }

    private void BrowseFolder(object? parameter)
    {
        if (parameter is not InventoryItemNodeViewModel { CanBrowse: true } node) return;
        if (!TryEnsureTreeChildren(node)) return;
        CurrentFolder = node;
        ApplyFilter();
    }

    private void ApplyTriStatePresentation()
    {
        var folderIds = _itemsById.Values
            .Where(item => item.Kind == DriveInventoryItemKind.Folder)
            .Select(item => item.FileId)
            .ToHashSet(StringComparer.Ordinal);
        var aggregate = folderIds.ToDictionary(
            id => id,
            id =>
            {
                var included = _evaluationStates.TryGetValue(id, out var state) && state.IsCoveredByIncludeRule;
                return (HasIncluded: included, HasExcluded: !included);
            },
            StringComparer.Ordinal);
        var remainingFolderChildren = folderIds.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);

        foreach (var parentId in folderIds)
        {
            if (!_allChildrenByParent.TryGetValue(parentId, out var children)) continue;
            foreach (var child in children)
            {
                if (child.Kind == DriveInventoryItemKind.Folder && folderIds.Contains(child.FileId))
                {
                    remainingFolderChildren[parentId]++;
                    continue;
                }

                var included = _evaluationStates.TryGetValue(child.FileId, out var state) && state.IsCoveredByIncludeRule;
                var current = aggregate[parentId];
                aggregate[parentId] = (current.HasIncluded || included, current.HasExcluded || !included);
            }
        }

        var pending = new Queue<string>(remainingFolderChildren.Where(pair => pair.Value == 0).Select(pair => pair.Key));
        var processed = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryDequeue(out var folderId))
        {
            if (!processed.Add(folderId)) continue;
            if (!_itemsById.TryGetValue(folderId, out var folder) || folder.ParentId is not { } parentId ||
                !folderIds.Contains(parentId)) continue;
            var parentAggregate = aggregate[parentId];
            var childAggregate = aggregate[folderId];
            aggregate[parentId] = (
                parentAggregate.HasIncluded || childAggregate.HasIncluded,
                parentAggregate.HasExcluded || childAggregate.HasExcluded);
            if (--remainingFolderChildren[parentId] == 0) pending.Enqueue(parentId);
        }

        foreach (var node in _nodes.Values.Where(node => node.IsFolder))
        {
            var value = aggregate[node.ItemId];
            node.ApplyCheckState(processed.Contains(node.ItemId)
                ? value.HasIncluded && value.HasExcluded ? null : value.HasIncluded
                : null);
        }
    }

    private void RemoveMissingRules()
    {
        var currentIds = _items.Select(item => item.FileId).ToHashSet(StringComparer.Ordinal);
        _rules = _rules
            .Where(pair => currentIds.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
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
        _itemsById.Clear();
        _childrenByParent.Clear();
        _allChildrenByParent.Clear();
        _resolvedParentEdges.Clear();
        _evaluationStates = new Dictionary<string, InventorySelectionState>(StringComparer.Ordinal);
        _rules.Clear();
        _lastValidRules.Clear();
        _lastValidEvaluation = null;
        _lastValidSelectionWasDirty = false;
        Interlocked.Increment(ref _evaluationVersion);
        _evaluationCancellation?.Cancel();
        IsSummaryUpdating = false;
        Interlocked.Increment(ref _filterVersion);
        _searchCancellation?.Cancel();
        IsSearching = false;
        SearchErrorMessage = string.Empty;
        TreeErrorMessage = null;
        TreeRoots.Clear();
        SearchResults.Clear();
        CurrentFolder = null;
        HasSnapshot = false;
        HasUnsavedChanges = false;
        StatusMessage = message;
        ReconciliationMessage = string.Empty;
        MissingRuleTargetCount = 0;
        OnPropertyChanged(nameof(SnapshotLabel));
        OnPropertyChanged(nameof(HasSearchResults));
        OnPropertyChanged(nameof(HasNoSearchResults));
        OnPropertyChanged(nameof(HasNoTreeRoots));
        OnPropertyChanged(nameof(VisibleItemCountLabel));
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
        OnPropertyChanged(nameof(SaveActionText));
        OnPropertyChanged(nameof(SaveStateStatus));
        ((AsyncRelayCommand)SaveCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)ClearSelectionCommand).NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        _disposed = true;
        _evaluationCancellation?.Cancel();
        _evaluationCancellation?.Dispose();
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        if (_scanner is not null) _scanner.StateChanged -= ScannerStateChanged;
        ((AsyncRelayCommand)SaveCommand).Dispose();
        ((AsyncRelayCommand)ClearSelectionCommand).Dispose();
    }
}

using CloudKeeperSN.App.Development;
using CloudKeeperSN.App.Services;
using CloudKeeperSN.App.ViewModels;
using CloudKeeperSN.Application.Planning;
using CloudKeeperSN.Application.Scanning;
using CloudKeeperSN.Application.Storage;
using CloudKeeperSN.Domain.Planning;
using CloudKeeperSN.Domain.Scanning;
using CloudKeeperSN.Domain.Storage;

namespace CloudKeeperSN.App.Tests;

public sealed class InventoryPlanViewModelTests
{
    [Fact]
    public async Task LoadsLatestSnapshotAndSupportsFolderIncludeWithDescendantExclude()
    {
        var workspace = Workspace();
        var service = new FakePlanService(workspace);
        using var viewModel = new InventoryPlanViewModel(
            new DemoConfiguration(false, DemoScenarioKind.Standard), service, new ConnectedAuthentication(), null, InlineUiDispatcher.Instance);

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.True(viewModel.HasSnapshot);
        Assert.Equal(2, viewModel.SearchResults.Count);
        var folder = viewModel.SearchResults.Single(node => node.ItemId == "folder");
        folder.IsChecked = true;
        await AsyncTest.UntilAsync(() => !viewModel.IsSummaryUpdating && viewModel.SelectedItemCount == 2);
        Assert.Equal(2, viewModel.SelectedItemCount);
        Assert.Equal(30, viewModel.KnownBytes);

        viewModel.BrowseFolderCommand.Execute(folder);
        Assert.Equal("Drive của tôi/Tài liệu", viewModel.CurrentLocationLabel);
        var excluded = viewModel.SearchResults.Single(node => node.ItemId == "two");
        excluded.IsChecked = false;
        await AsyncTest.UntilAsync(() => !viewModel.IsSummaryUpdating && viewModel.SelectedItemCount == 1);
        Assert.Equal(1, viewModel.SelectedItemCount);
        Assert.Equal(10, viewModel.KnownBytes);
        Assert.Null(folder.IsChecked);
        Assert.True(viewModel.CanSave);

        viewModel.SaveCommand.Execute(null);
        await AsyncTest.UntilAsync(() => service.SaveCount == 1 && !viewModel.HasUnsavedChanges);
        Assert.Contains(service.Saved!.Rules, rule => rule.ItemId == "folder" && rule.Mode == BackupSelectionRuleMode.Include);
        Assert.Contains(service.Saved.Rules, rule => rule.ItemId == "two" && rule.Mode == BackupSelectionRuleMode.Exclude);
        Assert.Contains("chưa có tệp nào", viewModel.SaveMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FolderTreeCreatesOnlyFolderNodesAndLoadsNestedChildrenOnExpansion()
    {
        var workspace = Workspace();
        var nested = Item(workspace.LatestScan.ScanId, "nested", "Dự án", "folder", DriveInventoryItemKind.Folder, false, null);
        var deepest = Item(workspace.LatestScan.ScanId, "deepest", "Năm 2026", "nested", DriveInventoryItemKind.Folder, false, null);
        var items = workspace.InventoryItems.Concat([nested, deepest]).ToArray();
        workspace = workspace with
        {
            InventoryItems = items,
            Evaluation = new BackupSelectionPlanner().Evaluate(workspace.Plan, items)
        };
        using var viewModel = Create(new FakePlanService(workspace));

        await viewModel.LoadAsync(CancellationToken.None);

        var root = viewModel.TreeRoots.Single(node => node.Name == "Drive của tôi");
        Assert.All(root.Children, node => Assert.True(node.IsFolder));
        var folder = Assert.Single(root.Children);
        Assert.True(Assert.Single(folder.Children).IsPlaceholder);

        folder.IsExpanded = true;

        var loadedNestedFolder = Assert.Single(folder.Children);
        Assert.False(loadedNestedFolder.IsPlaceholder);
        Assert.Equal("nested", loadedNestedFolder.ItemId);
        Assert.True(Assert.Single(loadedNestedFolder.Children).IsPlaceholder);
    }

    [Fact]
    public async Task FolderExpansionIsDeferredUntilAfterTheCurrentWpfTreeWalk()
    {
        var workspace = Workspace();
        var nested = Item(workspace.LatestScan.ScanId, "nested", "Dự án", "folder", DriveInventoryItemKind.Folder, false, null);
        var items = workspace.InventoryItems.Concat([nested]).ToArray();
        workspace = workspace with
        {
            InventoryItems = items,
            Evaluation = new BackupSelectionPlanner().Evaluate(workspace.Plan, items)
        };
        var dispatcher = new DeferredDispatcher();
        using var viewModel = new InventoryPlanViewModel(
            new DemoConfiguration(false, DemoScenarioKind.Standard), new FakePlanService(workspace),
            new ConnectedAuthentication(), null, dispatcher);
        await viewModel.LoadAsync(CancellationToken.None);
        var folder = viewModel.TreeRoots.Single(node => node.Name == "Drive của tôi").Children.Single();

        folder.IsExpanded = true;

        Assert.True(Assert.Single(folder.Children).IsPlaceholder);
        Assert.Equal(1, dispatcher.PostCount);
        dispatcher.RunPending();
        Assert.Equal("nested", Assert.Single(folder.Children).ItemId);
    }

    [Fact]
    public async Task CyclicFolderParentsArePresentedAsSafeRoots()
    {
        var workspace = Workspace();
        var first = Item(workspace.LatestScan.ScanId, "cycle-a", "Vòng A", "cycle-b", DriveInventoryItemKind.Folder, false, null);
        var second = Item(workspace.LatestScan.ScanId, "cycle-b", "Vòng B", "cycle-a", DriveInventoryItemKind.Folder, false, null);
        var items = workspace.InventoryItems.Concat([first, second]).ToArray();
        workspace = workspace with
        {
            InventoryItems = items,
            Evaluation = new BackupSelectionPlanner().Evaluate(workspace.Plan, items)
        };
        using var viewModel = Create(new FakePlanService(workspace));

        await viewModel.LoadAsync(CancellationToken.None);

        var root = viewModel.TreeRoots.Single(node => node.Name == "Drive của tôi");
        Assert.Contains(root.Children, node => node.ItemId == "cycle-a");
        Assert.Contains(root.Children, node => node.ItemId == "cycle-b");
        Assert.All(root.Children.Where(node => node.ItemId.StartsWith("cycle-", StringComparison.Ordinal)),
            node => Assert.Empty(node.Children));
    }

    [Fact]
    public async Task SearchReviewAndSelectedFiltersUseIndexedMetadataOnly()
    {
        var service = new FakePlanService(Workspace());
        using var viewModel = new InventoryPlanViewModel(
            new DemoConfiguration(false, DemoScenarioKind.Standard), service, new ConnectedAuthentication());
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.SearchText = "日本語";
        await AsyncTest.UntilAsync(() => !viewModel.IsSearching && viewModel.SearchResults.Count == 1);
        Assert.Equal("one", Assert.Single(viewModel.SearchResults).ItemId);
        viewModel.SearchText = string.Empty;
        viewModel.SelectedFilter = viewModel.Filters.Single(filter => filter.Key == "review");

        Assert.Equal("shortcut", Assert.Single(viewModel.SearchResults).ItemId);
        Assert.Equal(1, viewModel.ReviewItemCount);
    }

    [Fact]
    public async Task PagePresentationUsesCorrectSummarySemanticsAndVietnameseLabels()
    {
        using var viewModel = Create(new FakePlanService(Workspace()));
        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal("Drive của tôi", viewModel.CurrentLocationLabel);
        Assert.Equal("Tìm theo tên tệp hoặc thư mục…", viewModel.SearchPlaceholder);
        Assert.Equal("0", viewModel.SelectedItemCountLabel);
        Assert.Equal("0 byte", viewModel.KnownBytesLabel);
        Assert.Equal("0", viewModel.UnknownSizeCountLabel);
        Assert.Equal("0", viewModel.SelectedReviewItemCountLabel);
        Assert.Equal("1", viewModel.ReviewItemCountLabel);
        Assert.NotEqual(viewModel.BackupEligibleItemCount, viewModel.ReviewItemCount);
        Assert.Equal("Đã lưu", viewModel.SaveStateStatus.Text);
        Assert.Contains("account@example.test", viewModel.AccountIdentityLabel);
        Assert.Contains("lúc", viewModel.SnapshotLabel);

        var shortcut = viewModel.SearchResults.Single(node => node.ItemId == "shortcut");
        Assert.Equal("Không xác định", shortcut.SizeLabel);
        Assert.DoesNotContain("0 byte", shortcut.SizeLabel, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchContextPlaceholderClearCommandAndEmptyStateStayConsistent()
    {
        using var viewModel = Create(new FakePlanService(Workspace()));
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.SearchText = "日本語";
        await AsyncTest.UntilAsync(() => !viewModel.IsSearching && viewModel.SearchResults.Count == 1);
        Assert.True(viewModel.HasSearchText);
        Assert.Equal("Kết quả tìm kiếm", viewModel.CurrentLocationLabel);
        Assert.Equal("one", Assert.Single(viewModel.SearchResults).ItemId);
        Assert.True(viewModel.ClearSearchCommand.CanExecute(null));

        viewModel.SearchText = "không tồn tại";
        await AsyncTest.UntilAsync(() => !viewModel.IsSearching);
        Assert.True(viewModel.HasNoSearchResults);
        Assert.Equal("Không tìm thấy mục phù hợp.", viewModel.EmptyResultsMessage);

        viewModel.ClearSearchCommand.Execute(null);
        Assert.False(viewModel.HasSearchText);
        Assert.False(viewModel.ClearSearchCommand.CanExecute(null));
        Assert.Contains(nameof(InventoryPlanViewModel.CurrentLocationLabel), changed);
        Assert.Contains(nameof(InventoryPlanViewModel.HasSearchResults), changed);
    }

    [Fact]
    public async Task NewSearchQueryRejectsPendingOlderResults()
    {
        using var viewModel = Create(new FakePlanService(Workspace()));
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.SearchText = "日本語";
        viewModel.SearchText = "Hai";
        await AsyncTest.UntilAsync(() => !viewModel.IsSearching && viewModel.SearchResults.Count == 1);

        Assert.Equal("two", Assert.Single(viewModel.SearchResults).ItemId);
    }

    [Fact]
    public async Task SaveStateTracksDirtySavingSavedAndFailurePresentation()
    {
        var service = new BlockingSavePlanService(Workspace());
        using var viewModel = Create(service);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.PlanName = "Kế hoạch đã sửa";

        Assert.Equal("Có thay đổi chưa được lưu", viewModel.SaveStateStatus.Text);
        Assert.True(viewModel.SaveCommand.CanExecute(null));
        viewModel.SaveCommand.Execute(null);
        await service.SaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(viewModel.IsSaving);
        Assert.Equal("Đang lưu…", viewModel.SaveStateStatus.Text);
        Assert.False(viewModel.SaveCommand.CanExecute(null));

        service.CompleteSave();
        await AsyncTest.UntilAsync(() => !viewModel.IsSaving);
        Assert.Equal("Đã lưu", viewModel.SaveStateStatus.Text);
        Assert.Contains(nameof(InventoryPlanViewModel.SaveStateStatus), changed);
    }

    [Fact]
    public async Task SubstantialClearRequiresConfirmationAndPreservesRulesWhenDeclined()
    {
        var dialogs = new FakeDialogService { ConfirmationResult = false };
        using var viewModel = Create(new FakePlanService(Workspace()), dialogs);
        await viewModel.LoadAsync(CancellationToken.None);
        var folder = viewModel.SearchResults.Single(node => node.ItemId == "folder");
        folder.IsChecked = true;
        await AsyncTest.UntilAsync(() => !viewModel.IsSummaryUpdating && viewModel.SelectedItemCount == 2);

        viewModel.ClearSelectionCommand.Execute(null);
        await AsyncTest.UntilAsync(() => dialogs.Requests.Count == 1);

        Assert.Equal(2, viewModel.SelectedItemCount);
        Assert.Contains("Bỏ toàn bộ lựa chọn", dialogs.Requests[0].Title);
    }

    [Fact]
    public async Task OlderSelectionSummaryCannotReplaceNewerRules()
    {
        var service = new BlockingEvaluationPlanService(Workspace());
        using var viewModel = Create(service);
        await viewModel.LoadAsync(CancellationToken.None);
        var folder = viewModel.SearchResults.Single(node => node.ItemId == "folder");

        folder.IsChecked = true;
        await service.FirstEvaluationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        folder.IsChecked = false;
        await AsyncTest.UntilAsync(() => !viewModel.IsSummaryUpdating && viewModel.SelectedItemCount == 0);
        service.ReleaseFirstEvaluation();
        await service.FirstEvaluationCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(0, viewModel.SelectedItemCount);
        Assert.False(viewModel.IsSummaryUpdating);
    }

    [Fact]
    public async Task LoadFailureShowsSafeVietnameseErrorWithoutRawException()
    {
        using var viewModel = Create(new ThrowingPlanService());

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.True(viewModel.HasNoSnapshot);
        Assert.Contains("Không thể mở danh mục", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("SQL", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadingStateDisablesSaveUntilWorkspaceIsReady()
    {
        var service = new BlockingLoadPlanService(Workspace());
        using var viewModel = Create(service);

        var load = viewModel.LoadAsync(CancellationToken.None);
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(viewModel.IsLoading);
        Assert.False(viewModel.HasNoSnapshot);
        Assert.False(viewModel.SaveCommand.CanExecute(null));

        service.Complete();
        await load;
        Assert.False(viewModel.IsLoading);
        Assert.True(viewModel.HasSnapshot);
    }

    [Fact]
    public async Task NewerLoadWinsWhenAnOlderLoadCompletesLate()
    {
        var newer = Workspace();
        newer = newer with { Plan = newer.Plan with { Name = "Kế hoạch mới" } };
        var service = new SequencedLoadPlanService(Workspace(), newer);
        using var viewModel = Create(service);

        var firstLoad = viewModel.LoadAsync(CancellationToken.None);
        await service.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await viewModel.LoadAsync(CancellationToken.None);
        service.CompleteFirst();
        await firstLoad;

        Assert.Equal("Kế hoạch mới", viewModel.PlanName);
    }

    [Fact]
    public async Task DisconnectedAccountCanOpenLatestSuccessfulLocalSnapshot()
    {
        var workspace = Workspace();
        var scanner = new FakeInventoryScanner(workspace.LatestScan);
        using var viewModel = new InventoryPlanViewModel(
            new DemoConfiguration(false, DemoScenarioKind.Standard), new FakePlanService(workspace),
            new DisconnectedAuthentication(), scanner, InlineUiDispatcher.Instance);

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.True(viewModel.HasSnapshot);
        Assert.Contains("đã ngắt kết nối", viewModel.AccountIdentityLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(workspace.Plan.Name, viewModel.PlanName);
    }

    [Theory]
    [InlineData(DriveInventoryScanStatus.Failed)]
    [InlineData(DriveInventoryScanStatus.Cancelled)]
    public async Task FailedOrCancelledRetryDoesNotReplaceSuccessfulPlanSnapshot(DriveInventoryScanStatus status)
    {
        var workspace = Workspace();
        var scanner = new FakeInventoryScanner(workspace.LatestScan);
        using var viewModel = new InventoryPlanViewModel(
            new DemoConfiguration(false, DemoScenarioKind.Standard), new FakePlanService(workspace),
            new ConnectedAuthentication(), scanner, InlineUiDispatcher.Instance);
        await viewModel.LoadAsync(CancellationToken.None);

        scanner.Publish(new DriveInventoryScanState(status, "Lần quét mới chưa hoàn tất", LastSuccessfulRun: workspace.LatestScan));

        Assert.True(viewModel.HasSnapshot);
        Assert.Equal(workspace.Plan.Name, viewModel.PlanName);
        Assert.Equal(workspace.LatestScan.CompletedAtUtc?.ToLocalTime().ToString("dd/MM/yyyy 'lúc' HH:mm"), viewModel.SnapshotLabel);
    }

    [Fact]
    public async Task NewSnapshotReconciliationIsVisibleAndDoesNotClaimBackup()
    {
        var workspace = Workspace() with
        {
            Reconciliation = new BackupPlanReconciliation(true, 5, 2, 1)
        };
        using var viewModel = new InventoryPlanViewModel(
            new DemoConfiguration(false, DemoScenarioKind.Standard), new FakePlanService(workspace), new ConnectedAuthentication());

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.True(viewModel.HasReconciliationMessage);
        Assert.Contains("5", viewModel.ReconciliationMessage);
        Assert.Contains("2", viewModel.ReconciliationMessage);
        Assert.DoesNotContain("đã sao lưu", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingSnapshotShowsSafeEmptyState()
    {
        using var viewModel = new InventoryPlanViewModel(
            new DemoConfiguration(false, DemoScenarioKind.Standard), new FakePlanService(null), new ConnectedAuthentication());

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.False(viewModel.HasSnapshot);
        Assert.True(viewModel.HasNoSnapshot);
        Assert.False(viewModel.SaveCommand.CanExecute(null));
        Assert.Contains("quét Google Drive", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingRuleCanBeRemovedWithoutTouchingProviderData()
    {
        var workspace = Workspace();
        var plan = workspace.Plan with
        {
            Rules = [new BackupSelectionRule("missing", BackupSelectionRuleMode.Include, DriveInventoryItemKind.File, "Đã xóa.txt")]
        };
        workspace = workspace with
        {
            Plan = plan,
            Evaluation = new BackupSelectionPlanner().Evaluate(plan, workspace.InventoryItems),
            Reconciliation = new BackupPlanReconciliation(true, 0, 0, 1)
        };
        var service = new FakePlanService(workspace);
        using var viewModel = new InventoryPlanViewModel(
            new DemoConfiguration(false, DemoScenarioKind.Standard), service, new ConnectedAuthentication());
        await viewModel.LoadAsync(CancellationToken.None);

        Assert.True(viewModel.HasMissingRules);
        viewModel.RemoveMissingRulesCommand.Execute(null);

        Assert.False(viewModel.HasMissingRules);
        Assert.True(viewModel.HasUnsavedChanges);
    }

    private static InventoryPlanViewModel Create(IBackupSelectionPlanService service, IDialogService? dialogs = null) => new(
        new DemoConfiguration(false, DemoScenarioKind.Standard), service, new ConnectedAuthentication(), null,
        InlineUiDispatcher.Instance, dialogs);

    private static BackupPlanWorkspace Workspace()
    {
        var scanId = Guid.NewGuid();
        var run = new DriveInventoryRun(scanId, "google-drive", "account", DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow,
            DriveInventoryRunStatus.Completed, 4, 1, 2, 30, 1, 0, 1, 0, 2, null, true, null);
        var items = new[]
        {
            Item(scanId, "folder", "Tài liệu", "root", DriveInventoryItemKind.Folder, false, null),
            Item(scanId, "one", "日本語.txt", "folder", DriveInventoryItemKind.File, true, 10),
            Item(scanId, "two", "Hai.txt", "folder", DriveInventoryItemKind.File, true, 20),
            Item(scanId, "shortcut", "Lối tắt", "root", DriveInventoryItemKind.Shortcut, false, null)
        };
        var plan = new BackupSelectionPlan(Guid.NewGuid(), "account", "Kế hoạch", scanId,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, []);
        var evaluation = new BackupSelectionPlanner().Evaluate(plan, items);
        return new(run, plan, items, evaluation, new BackupPlanReconciliation(false, 0, 0, 0));
    }

    private static DriveInventoryItem Item(Guid scanId, string id, string name, string? parent,
        DriveInventoryItemKind kind, bool eligible, long? size) => new(
        scanId, id, name, parent, $"Drive của tôi/{name}", "text/plain", kind, DriveInventoryLocation.MyDrive,
        size, null, null, null, null, kind == DriveInventoryItemKind.Shortcut ? "one" : null, null,
        false, true, eligible, eligible || kind == DriveInventoryItemKind.Folder ? null : "Không tự động theo lối tắt");

    private sealed class FakePlanService(BackupPlanWorkspace? workspace) : IBackupSelectionPlanService
    {
        private readonly BackupSelectionPlanner _planner = new();
        public int SaveCount { get; private set; }
        public BackupSelectionPlan? Saved { get; private set; }
        public Task<BackupPlanWorkspace?> LoadAsync(string providerAccountId, CancellationToken cancellationToken) => Task.FromResult(workspace);
        public Task<BackupSelectionPlan> SaveAsync(BackupSelectionPlan plan, Guid latestScanId, CancellationToken cancellationToken)
        {
            SaveCount++;
            Saved = plan with { SourceScanId = latestScanId };
            return Task.FromResult(Saved);
        }
        public BackupSelectionEvaluation Evaluate(BackupSelectionPlan plan, IReadOnlyList<DriveInventoryItem> items) => _planner.Evaluate(plan, items);
    }

    private sealed class BlockingSavePlanService(BackupPlanWorkspace workspace) : IBackupSelectionPlanService
    {
        private readonly TaskCompletionSource<BackupSelectionPlan> _save = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<BackupPlanWorkspace?> LoadAsync(string providerAccountId, CancellationToken cancellationToken) => Task.FromResult<BackupPlanWorkspace?>(workspace);
        public Task<BackupSelectionPlan> SaveAsync(BackupSelectionPlan plan, Guid latestScanId, CancellationToken cancellationToken)
        {
            SaveStarted.TrySetResult();
            return _save.Task;
        }
        public void CompleteSave() => _save.TrySetResult(workspace.Plan);
        public BackupSelectionEvaluation Evaluate(BackupSelectionPlan plan, IReadOnlyList<DriveInventoryItem> items) => new BackupSelectionPlanner().Evaluate(plan, items);
    }

    private sealed class ThrowingPlanService : IBackupSelectionPlanService
    {
        public Task<BackupPlanWorkspace?> LoadAsync(string providerAccountId, CancellationToken cancellationToken) =>
            Task.FromException<BackupPlanWorkspace?>(new InvalidOperationException("SQL secret internal path"));
        public Task<BackupSelectionPlan> SaveAsync(BackupSelectionPlan plan, Guid latestScanId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public BackupSelectionEvaluation Evaluate(BackupSelectionPlan plan, IReadOnlyList<DriveInventoryItem> items) => throw new NotSupportedException();
    }

    private sealed class BlockingLoadPlanService(BackupPlanWorkspace workspace) : IBackupSelectionPlanService
    {
        private readonly TaskCompletionSource<BackupPlanWorkspace?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<BackupPlanWorkspace?> LoadAsync(string providerAccountId, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            return _completion.Task;
        }
        public void Complete() => _completion.TrySetResult(workspace);
        public Task<BackupSelectionPlan> SaveAsync(BackupSelectionPlan plan, Guid latestScanId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public BackupSelectionEvaluation Evaluate(BackupSelectionPlan plan, IReadOnlyList<DriveInventoryItem> items) =>
            new BackupSelectionPlanner().Evaluate(plan, items);
    }

    private sealed class DeferredDispatcher : IUiDispatcher
    {
        private readonly Queue<Action> _pending = new();
        public int PostCount { get; private set; }
        public void Invoke(Action action) => action();
        public void Post(Action action)
        {
            PostCount++;
            _pending.Enqueue(action);
        }
        public void RunPending()
        {
            while (_pending.TryDequeue(out var action)) action();
        }
    }

    private sealed class SequencedLoadPlanService(BackupPlanWorkspace older, BackupPlanWorkspace newer) : IBackupSelectionPlanService
    {
        private readonly TaskCompletionSource<BackupPlanWorkspace?> _first = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;
        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<BackupPlanWorkspace?> LoadAsync(string providerAccountId, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                FirstStarted.TrySetResult();
                return _first.Task;
            }
            return Task.FromResult<BackupPlanWorkspace?>(newer);
        }
        public void CompleteFirst() => _first.TrySetResult(older);
        public Task<BackupSelectionPlan> SaveAsync(BackupSelectionPlan plan, Guid latestScanId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public BackupSelectionEvaluation Evaluate(BackupSelectionPlan plan, IReadOnlyList<DriveInventoryItem> items) => new BackupSelectionPlanner().Evaluate(plan, items);
    }

    private sealed class BlockingEvaluationPlanService(BackupPlanWorkspace workspace) : IBackupSelectionPlanService
    {
        private readonly BackupSelectionPlanner _planner = new();
        private readonly ManualResetEventSlim _release = new(false);
        private int _evaluations;
        public TaskCompletionSource FirstEvaluationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FirstEvaluationCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<BackupPlanWorkspace?> LoadAsync(string providerAccountId, CancellationToken cancellationToken) => Task.FromResult<BackupPlanWorkspace?>(workspace);
        public Task<BackupSelectionPlan> SaveAsync(BackupSelectionPlan plan, Guid latestScanId, CancellationToken cancellationToken) => Task.FromResult(plan);
        public BackupSelectionEvaluation Evaluate(BackupSelectionPlan plan, IReadOnlyList<DriveInventoryItem> items)
        {
            var isFirst = Interlocked.Increment(ref _evaluations) == 1;
            if (isFirst)
            {
                FirstEvaluationStarted.TrySetResult();
                _release.Wait(TimeSpan.FromSeconds(2));
            }
            var evaluation = _planner.Evaluate(plan, items);
            if (isFirst) FirstEvaluationCompleted.TrySetResult();
            return evaluation;
        }
        public void ReleaseFirstEvaluation() => _release.Set();
    }

    private sealed class ConnectedAuthentication : IProviderAuthenticationService
    {
        private static readonly StorageAccount Account = new(
            "google:current", "google-drive", "account", "Nguyễn An", true, DateTimeOffset.UtcNow, "account@example.test");
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

    private sealed class DisconnectedAuthentication : IProviderAuthenticationService
    {
        public string ProviderId => "google-drive";
        public bool IsConfigured => true;
        public string? ConfigurationMessage => null;
        public ProviderAuthenticationState State => new(ProviderAuthenticationStatus.Disconnected, "Chưa kết nối");
        public event Action<ProviderAuthenticationState>? StateChanged { add { } remove { } }
        public event Action? ConfigurationChanged { add { } remove { } }
        public Task<StorageAccount?> GetCachedAccountAsync(CancellationToken cancellationToken) => Task.FromResult<StorageAccount?>(null);
        public Task<StorageAccount> ConnectAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DisconnectLocalAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeInventoryScanner(DriveInventoryRun latest) : IDriveInventoryScanner
    {
        public DriveInventoryScanState State { get; private set; } = new(DriveInventoryScanStatus.Idle, "Sẵn sàng", LastSuccessfulRun: latest);
        public event Action<DriveInventoryScanState>? StateChanged;
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<DriveInventoryRun?> GetLatestSuccessfulAsync(string providerAccountId, CancellationToken cancellationToken) => Task.FromResult<DriveInventoryRun?>(latest);
        public Task<IReadOnlyList<DriveInventoryRun>> GetRecentAsync(int maximumCount, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DriveInventoryRun>>([latest]);
        public Task<DriveInventoryRun> ScanAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public void Publish(DriveInventoryScanState state)
        {
            State = state;
            StateChanged?.Invoke(state);
        }
    }
}

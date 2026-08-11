using System.Diagnostics;
using CloudKeeperSN.App.Development;
using CloudKeeperSN.App.Services;
using CloudKeeperSN.App.ViewModels;
using CloudKeeperSN.Application.Planning;
using CloudKeeperSN.Application.Persistence;
using CloudKeeperSN.Application.Storage;
using CloudKeeperSN.Domain.Planning;
using CloudKeeperSN.Domain.Scanning;
using CloudKeeperSN.Domain.Storage;

namespace CloudKeeperSN.App.Tests;

public sealed class FolderSelectionRegressionTests
{
    [Fact]
    public async Task SelectingSeveralSiblingFoldersSequentiallyKeepsEveryRuleIndependent()
    {
        var items = Enumerable.Range(0, 8).Select(i => Item($"folder-{i}", "root", DriveInventoryItemKind.Folder)).ToArray();
        using var viewModel = Create(new PlanService(Workspace(items)));
        await viewModel.LoadAsync(CancellationToken.None);

        foreach (var node in viewModel.SearchResults.Take(6))
        {
            node.IsChecked = true;
            await AsyncTest.UntilAsync(() => !viewModel.IsSummaryUpdating);
        }

        Assert.Equal(6, viewModel.SelectedFolderCount);
        Assert.Equal(6, viewModel.SearchResults.Count(node => node.IsChecked == true));
    }

    [Fact]
    public async Task RapidSelectionAndDeselectionPublishesOnlyTheLatestIntent()
    {
        var service = new PlanService(Workspace([Item("folder", "root", DriveInventoryItemKind.Folder)]));
        using var viewModel = Create(service);
        await viewModel.LoadAsync(CancellationToken.None);
        var folder = Assert.Single(viewModel.SearchResults);

        for (var i = 0; i < 25; i++) folder.IsChecked = i % 2 == 0;
        folder.IsChecked = false;
        await AsyncTest.UntilAsync(() => !viewModel.IsSummaryUpdating);

        Assert.Equal(0, viewModel.SelectedFolderCount);
        Assert.False(folder.IsChecked);
    }

    [Fact]
    public async Task ParentIncludeAndChildExcludePreserveNearestRulePrecedence()
    {
        var items = new[]
        {
            Item("parent", "root", DriveInventoryItemKind.Folder),
            Item("child-a", "parent", DriveInventoryItemKind.File, eligible: true, size: 10),
            Item("child-b", "parent", DriveInventoryItemKind.File, eligible: true, size: 20)
        };
        using var viewModel = Create(new PlanService(Workspace(items)));
        await viewModel.LoadAsync(CancellationToken.None);
        var parent = Assert.Single(viewModel.SearchResults);
        parent.IsChecked = true;
        await AsyncTest.UntilAsync(() => !viewModel.IsSummaryUpdating);
        viewModel.BrowseFolderCommand.Execute(parent);

        viewModel.SearchResults.Single(node => node.ItemId == "child-b").IsChecked = false;
        await AsyncTest.UntilAsync(() => !viewModel.IsSummaryUpdating);

        Assert.Equal(1, viewModel.SelectedItemCount);
        Assert.Equal(10, viewModel.KnownBytes);
    }

    [Fact]
    public async Task ParentTriStateIsIndeterminateWhenDescendantsAreMixed()
    {
        var items = new[]
        {
            Item("parent", "root", DriveInventoryItemKind.Folder),
            Item("child", "parent", DriveInventoryItemKind.File, eligible: true, size: 10)
        };
        using var viewModel = Create(new PlanService(Workspace(items)));
        await viewModel.LoadAsync(CancellationToken.None);
        var parent = Assert.Single(viewModel.SearchResults);
        parent.IsChecked = true;
        await AsyncTest.UntilAsync(() => !viewModel.IsSummaryUpdating);
        viewModel.BrowseFolderCommand.Execute(parent);
        Assert.Single(viewModel.SearchResults).IsChecked = false;
        await AsyncTest.UntilAsync(() => !viewModel.IsSummaryUpdating);

        Assert.Null(parent.IsChecked);
    }

    [Fact]
    public async Task DeepHierarchyUsesIterativeTriStateWithoutStackOverflow()
    {
        const int depth = 6000;
        var items = Enumerable.Range(0, depth)
            .Select(i => Item($"folder-{i}", i == 0 ? "root" : $"folder-{i - 1}", DriveInventoryItemKind.Folder))
            .ToArray();
        using var viewModel = Create(new PlanService(Workspace(items)));
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.TreeRoots.Single().Children.Single().IsChecked = true;
        await AsyncTest.UntilAsync(() => !viewModel.IsSummaryUpdating, timeoutMilliseconds: 10000);

        Assert.Equal(depth, viewModel.SelectedFolderCount);
    }

    [Fact]
    public async Task CyclicParentsTerminateAndRemainDeterministicallyIndeterminate()
    {
        var items = new[]
        {
            Item("cycle-a", "cycle-b", DriveInventoryItemKind.Folder),
            Item("cycle-b", "cycle-a", DriveInventoryItemKind.Folder)
        };
        using var viewModel = Create(new PlanService(Workspace(items)));

        await viewModel.LoadAsync(CancellationToken.None);
        var nodes = viewModel.TreeRoots.Single().Children;
        nodes[0].IsChecked = true;
        await AsyncTest.UntilAsync(() => !viewModel.IsSummaryUpdating);

        Assert.All(nodes, node => Assert.Null(node.IsChecked));
    }

    [Fact]
    public async Task DuplicateNamesWithDifferentGoogleFileIdsRemainIndependent()
    {
        var items = new[]
        {
            Item("id-a", "root", DriveInventoryItemKind.Folder, name: "Trùng tên"),
            Item("id-b", "root", DriveInventoryItemKind.Folder, name: "Trùng tên")
        };
        using var viewModel = Create(new PlanService(Workspace(items)));
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.SearchResults.Single(node => node.ItemId == "id-a").IsChecked = true;
        await AsyncTest.UntilAsync(() => !viewModel.IsSummaryUpdating);

        Assert.True(viewModel.SearchResults.Single(node => node.ItemId == "id-a").IsChecked);
        Assert.False(viewModel.SearchResults.Single(node => node.ItemId == "id-b").IsChecked);
    }

    [Fact]
    public async Task UnloadedDescendantsAreEvaluatedFromInventoryRules()
    {
        var items = new[]
        {
            Item("root-folder", "root", DriveInventoryItemKind.Folder),
            Item("nested", "root-folder", DriveInventoryItemKind.Folder),
            Item("unloaded-file", "nested", DriveInventoryItemKind.File, eligible: true, size: 42)
        };
        using var viewModel = Create(new PlanService(Workspace(items)));
        await viewModel.LoadAsync(CancellationToken.None);
        var root = viewModel.TreeRoots.Single().Children.Single();
        Assert.True(root.Children.Single().IsPlaceholder);

        root.IsChecked = true;
        await AsyncTest.UntilAsync(() => !viewModel.IsSummaryUpdating);

        Assert.Equal(1, viewModel.SelectedItemCount);
        Assert.Equal(42, viewModel.KnownBytes);
    }

    [Fact]
    public async Task OlderSummaryCannotReplaceNewerSummary()
    {
        var service = new SupersedingPlanService(Workspace([Item("folder", "root", DriveInventoryItemKind.Folder)]));
        using var viewModel = Create(service);
        await viewModel.LoadAsync(CancellationToken.None);
        var folder = Assert.Single(viewModel.SearchResults);

        folder.IsChecked = true;
        await service.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        folder.IsChecked = false;
        await AsyncTest.UntilAsync(() => !viewModel.IsSummaryUpdating);
        service.ReleaseFirst();
        await service.FirstCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(0, viewModel.SelectedFolderCount);
        Assert.False(folder.IsChecked);
    }

    [Fact]
    public async Task ExpectedCancellationIsObservedAndDoesNotSurfaceAsFailure()
    {
        var service = new CancellationPlanService(Workspace([Item("folder", "root", DriveInventoryItemKind.Folder)]));
        using var viewModel = Create(service);
        await viewModel.LoadAsync(CancellationToken.None);
        var folder = Assert.Single(viewModel.SearchResults);

        folder.IsChecked = true;
        await service.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        folder.IsChecked = false;
        await service.FirstCancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await AsyncTest.UntilAsync(() => !viewModel.IsSummaryUpdating);

        Assert.DoesNotContain("Không thể", viewModel.SaveMessage, StringComparison.Ordinal);
        Assert.False(folder.IsChecked);
    }

    [Fact]
    public async Task BackgroundSummaryIsAppliedThroughDispatcher()
    {
        var dispatcher = new RecordingDispatcher();
        using var viewModel = Create(new PlanService(Workspace([Item("folder", "root", DriveInventoryItemKind.Folder)])), dispatcher);
        await viewModel.LoadAsync(CancellationToken.None);
        var before = dispatcher.InvokeCount;

        Assert.Single(viewModel.SearchResults).IsChecked = true;
        await AsyncTest.UntilAsync(() => !viewModel.IsSummaryUpdating);

        Assert.True(dispatcher.InvokeCount > before);
    }

    [Fact]
    public async Task FailedCalculationPreservesPreviousValidSummary()
    {
        var service = new FailingEvaluationPlanService(Workspace([Item("folder", "root", DriveInventoryItemKind.Folder)]));
        using var viewModel = Create(service);
        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Single(viewModel.SearchResults).IsChecked = true;
        await AsyncTest.UntilAsync(() => !viewModel.IsSummaryUpdating);

        Assert.Equal(0, viewModel.SelectedFolderCount);
        Assert.False(Assert.Single(viewModel.SearchResults).IsChecked);
        Assert.Contains("Kế hoạch đã lưu vẫn được giữ nguyên", viewModel.SaveMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedSelectionMutationPreservesSavedPlanRules()
    {
        var initialRule = new BackupSelectionRule("folder-a", BackupSelectionRuleMode.Include, DriveInventoryItemKind.Folder, "A");
        var workspace = Workspace(
            [Item("folder-a", "root", DriveInventoryItemKind.Folder), Item("folder-b", "root", DriveInventoryItemKind.Folder)],
            [initialRule]);
        var service = new FailingEvaluationPlanService(workspace);
        using var viewModel = Create(service);
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.SearchResults.Single(node => node.ItemId == "folder-b").IsChecked = true;
        await AsyncTest.UntilAsync(() => !viewModel.IsSummaryUpdating);

        Assert.True(viewModel.SearchResults.Single(node => node.ItemId == "folder-a").IsChecked);
        Assert.False(viewModel.SearchResults.Single(node => node.ItemId == "folder-b").IsChecked);
        Assert.False(viewModel.HasUnsavedChanges);
        Assert.Equal([initialRule], workspace.Plan.Rules);
    }

    [Fact]
    public async Task DirtyAndSaveCommandStatesWaitForValidatedSummary()
    {
        var service = new GatePlanService(Workspace([Item("folder", "root", DriveInventoryItemKind.Folder)]));
        using var viewModel = Create(service);
        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Single(viewModel.SearchResults).IsChecked = true;
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(viewModel.HasUnsavedChanges);
        Assert.False(viewModel.SaveCommand.CanExecute(null));
        service.Release();
        await AsyncTest.UntilAsync(() => !viewModel.IsSummaryUpdating);

        Assert.True(viewModel.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task RepeatedPropertyNotificationsDoNotReenterSelectionCommand()
    {
        var service = new PlanService(Workspace([Item("folder", "root", DriveInventoryItemKind.Folder)]));
        using var viewModel = Create(service);
        await viewModel.LoadAsync(CancellationToken.None);
        var folder = Assert.Single(viewModel.SearchResults);
        var checkedNotifications = 0;
        folder.PropertyChanged += (_, args) => checkedNotifications += args.PropertyName == nameof(folder.IsChecked) ? 1 : 0;

        folder.IsChecked = true;
        folder.IsChecked = true;
        await AsyncTest.UntilAsync(() => !viewModel.IsSummaryUpdating);

        Assert.Equal(1, service.EvaluationCount);
        Assert.Equal(1, checkedNotifications);
    }

    [Fact]
    public async Task SyntheticInventoryAbove4109ItemsCompletesManyRapidSelectionsWithinBound()
    {
        var folders = Enumerable.Range(0, 50).Select(i => Item($"folder-{i}", "root", DriveInventoryItemKind.Folder));
        var files = Enumerable.Range(0, 4200).Select(i => Item($"file-{i}", $"folder-{i % 50}", DriveInventoryItemKind.File, eligible: true, size: 1));
        using var viewModel = Create(new PlanService(Workspace(folders.Concat(files).ToArray())));
        var stopwatch = Stopwatch.StartNew();
        await viewModel.LoadAsync(CancellationToken.None);

        foreach (var folder in viewModel.SearchResults.ToArray()) folder.IsChecked = true;
        await AsyncTest.UntilAsync(() => !viewModel.IsSummaryUpdating && viewModel.SelectedItemCount == 4200, timeoutMilliseconds: 10000);
        stopwatch.Stop();

        Assert.Equal(4250, viewModel.SelectedItemCount + viewModel.SelectedFolderCount);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Selection took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task SharedAndUnresolvedParentClassificationsSelectSafely()
    {
        var items = new[]
        {
            Item("shared", "missing-shared", DriveInventoryItemKind.Folder, location: DriveInventoryLocation.Shared),
            Item("unresolved", "missing", DriveInventoryItemKind.Folder, location: DriveInventoryLocation.Unresolved)
        };
        using var viewModel = Create(new PlanService(Workspace(items)));
        await viewModel.LoadAsync(CancellationToken.None);

        foreach (var root in viewModel.TreeRoots)
        {
            root.Children.Single().IsChecked = true;
            await AsyncTest.UntilAsync(() => !viewModel.IsSummaryUpdating);
        }

        Assert.Equal(2, viewModel.SelectedFolderCount);
    }

    [Fact]
    public async Task UnknownSizeWorkspaceShortcutAndReviewCountsKeepTheirMeaning()
    {
        var items = new[]
        {
            Item("folder", "root", DriveInventoryItemKind.Folder),
            Item("unknown", "folder", DriveInventoryItemKind.File, eligible: true, size: null),
            Item("workspace", "folder", DriveInventoryItemKind.GoogleWorkspaceFile, eligible: true, size: null),
            Item("shortcut", "folder", DriveInventoryItemKind.Shortcut, eligible: false, size: null),
            Item("review", "folder", DriveInventoryItemKind.File, eligible: false, size: 5, location: DriveInventoryLocation.Unresolved)
        };
        using var viewModel = Create(new PlanService(Workspace(items)));
        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Single(viewModel.SearchResults).IsChecked = true;
        await AsyncTest.UntilAsync(() => !viewModel.IsSummaryUpdating);

        Assert.Equal(4, viewModel.SelectedItemCount);
        Assert.Equal(2, viewModel.UnknownSizeCount);
        Assert.Equal(1, viewModel.SelectedWorkspaceItemCount);
        Assert.Equal(2, viewModel.SelectedReviewItemCount);
    }

    [Fact]
    public async Task SelectionDiagnosticsUseIdsAndGenerationWithoutLoggingNames()
    {
        var diagnostics = new MemoryDiagnostics();
        using var viewModel = new InventoryPlanViewModel(
            new DemoConfiguration(false, DemoScenarioKind.Standard),
            new PlanService(Workspace([Item("safe-id", "root", DriveInventoryItemKind.Folder, name: "Personal folder name")])),
            new ConnectedAuthentication(),
            dispatcher: InlineUiDispatcher.Instance,
            diagnostics: diagnostics);
        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Single(viewModel.SearchResults).IsChecked = true;
        await AsyncTest.UntilAsync(() => diagnostics.Events.Any(item => item.EventType == "FolderSelectionSummaryCompleted"));

        var started = diagnostics.Events.Single(item => item.EventType == "FolderSelectionSummaryStarted");
        Assert.Contains("itemId=safe-id", started.Details, StringComparison.Ordinal);
        Assert.Contains("generation=", started.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("Personal folder name", started.Details, StringComparison.Ordinal);
    }

    private static InventoryPlanViewModel Create(IBackupSelectionPlanService service, IUiDispatcher? dispatcher = null) => new(
        new DemoConfiguration(false, DemoScenarioKind.Standard),
        service,
        new ConnectedAuthentication(),
        dispatcher: dispatcher ?? InlineUiDispatcher.Instance);

    private static BackupPlanWorkspace Workspace(
        IReadOnlyList<DriveInventoryItem> items,
        IReadOnlyList<BackupSelectionRule>? rules = null)
    {
        var scanId = items.Count == 0 ? Guid.NewGuid() : items[0].ScanId;
        var plan = new BackupSelectionPlan(Guid.NewGuid(), "account", "Kế hoạch", scanId,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, rules ?? []);
        var run = new DriveInventoryRun(scanId, "google-drive", "account", DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow,
            DriveInventoryRunStatus.Completed, items.Count,
            items.Count(item => item.Kind == DriveInventoryItemKind.Folder),
            items.Count(item => item.Kind != DriveInventoryItemKind.Folder),
            items.Sum(item => item.Size ?? 0),
            items.Count(item => item.Size is null && item.Kind != DriveInventoryItemKind.Folder),
            items.Count(item => item.Kind == DriveInventoryItemKind.GoogleWorkspaceFile),
            items.Count(item => item.Kind == DriveInventoryItemKind.Shortcut),
            items.Count(item => item.Location == DriveInventoryLocation.Unresolved),
            items.Count(item => item.IsBackupEligible), null, true, null);
        var evaluation = new BackupSelectionPlanner().Evaluate(plan, items);
        return new(run, plan, items, evaluation, new BackupPlanReconciliation(false, 0, 0, 0));
    }

    private static readonly Guid ScanId = Guid.NewGuid();

    private static DriveInventoryItem Item(
        string id,
        string? parentId,
        DriveInventoryItemKind kind,
        bool eligible = false,
        long? size = null,
        string? name = null,
        DriveInventoryLocation location = DriveInventoryLocation.MyDrive) => new(
        ScanId, id, name ?? id, parentId, $"/{id}", "application/octet-stream", kind, location, size,
        null, null, null, null, kind == DriveInventoryItemKind.Shortcut ? "target" : null, null,
        location == DriveInventoryLocation.Shared, true, eligible,
        eligible || kind == DriveInventoryItemKind.Folder ? null : "Cần kiểm tra");

    private class PlanService : IBackupSelectionPlanService
    {
        protected readonly BackupSelectionPlanner Planner = new();
        protected BackupPlanWorkspace Workspace { get; }
        private int _evaluationCount;
        public int EvaluationCount => _evaluationCount;
        public PlanService(BackupPlanWorkspace workspace) => Workspace = workspace;
        public Task<BackupPlanWorkspace?> LoadAsync(string providerAccountId, CancellationToken cancellationToken) =>
            Task.FromResult<BackupPlanWorkspace?>(Workspace);
        public Task<BackupSelectionPlan> SaveAsync(BackupSelectionPlan plan, Guid latestScanId, CancellationToken cancellationToken) =>
            Task.FromResult(plan with { SourceScanId = latestScanId });
        public virtual BackupSelectionEvaluation Evaluate(BackupSelectionPlan plan, IReadOnlyList<DriveInventoryItem> items)
        {
            Interlocked.Increment(ref _evaluationCount);
            return Planner.Evaluate(plan, items);
        }
        public virtual Task<BackupSelectionEvaluation> EvaluateAsync(
            BackupSelectionPlan plan,
            IReadOnlyList<DriveInventoryItem> items,
            CancellationToken cancellationToken) =>
            Task.Run(() => Evaluate(plan, items), cancellationToken);
    }

    private sealed class SupersedingPlanService(BackupPlanWorkspace workspace) : PlanService(workspace)
    {
        private readonly TaskCompletionSource<BackupSelectionEvaluation> _first = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;
        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FirstCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async Task<BackupSelectionEvaluation> EvaluateAsync(BackupSelectionPlan plan, IReadOnlyList<DriveInventoryItem> items, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) != 1) return Planner.Evaluate(plan, items);
            FirstStarted.TrySetResult();
            var result = await _first.Task;
            FirstCompleted.TrySetResult();
            return result;
        }
        public void ReleaseFirst() => _first.TrySetResult(Planner.Evaluate(
            Workspace.Plan with { Rules = [new("folder", BackupSelectionRuleMode.Include, DriveInventoryItemKind.Folder, "folder")] },
            Workspace.InventoryItems));
    }

    private sealed class CancellationPlanService(BackupPlanWorkspace workspace) : PlanService(workspace)
    {
        private int _calls;
        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FirstCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async Task<BackupSelectionEvaluation> EvaluateAsync(BackupSelectionPlan plan, IReadOnlyList<DriveInventoryItem> items, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) != 1) return Planner.Evaluate(plan, items);
            FirstStarted.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, cancellationToken); }
            catch (OperationCanceledException) { FirstCancelled.TrySetResult(); throw; }
            throw new UnreachableException();
        }
    }

    private sealed class FailingEvaluationPlanService(BackupPlanWorkspace workspace) : PlanService(workspace)
    {
        public override Task<BackupSelectionEvaluation> EvaluateAsync(BackupSelectionPlan plan, IReadOnlyList<DriveInventoryItem> items, CancellationToken cancellationToken) =>
            Task.FromException<BackupSelectionEvaluation>(new InvalidOperationException("synthetic selection failure"));
    }

    private sealed class GatePlanService(BackupPlanWorkspace workspace) : PlanService(workspace)
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async Task<BackupSelectionEvaluation> EvaluateAsync(BackupSelectionPlan plan, IReadOnlyList<DriveInventoryItem> items, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return Planner.Evaluate(plan, items);
        }
        public void Release() => _release.TrySetResult();
    }

    private sealed class RecordingDispatcher : IUiDispatcher
    {
        public int InvokeCount { get; private set; }
        public void Invoke(Action action)
        {
            InvokeCount++;
            action();
        }
    }

    private sealed class MemoryDiagnostics : IProviderDiagnostics
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<(string EventType, string? Details)> _events = new();
        public IReadOnlyList<(string EventType, string? Details)> Events => _events.ToArray();
        public Task WriteAsync(string eventType, string vietnameseMessage, string? technicalDetails, CancellationToken cancellationToken)
        {
            _events.Enqueue((eventType, technicalDetails));
            return Task.CompletedTask;
        }
    }

    private sealed class ConnectedAuthentication : IProviderAuthenticationService
    {
        private static readonly StorageAccount Account = new(
            "google:account", "google-drive", "account", "Test", true, DateTimeOffset.UtcNow, "test@example.invalid");
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
}

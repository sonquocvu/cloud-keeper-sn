using CloudKeeperSN.Application.Planning;
using CloudKeeperSN.Application.Persistence;
using CloudKeeperSN.Domain.Planning;
using CloudKeeperSN.Domain.Scanning;

namespace CloudKeeperSN.Application.Tests;

public sealed class BackupSelectionPlannerTests
{
    private readonly BackupSelectionPlanner _planner = new();

    [Fact]
    public void FolderIncludeSelectsEligibleDescendantsButLeavesUnsafeItemsForReview()
    {
        var scan = Guid.NewGuid();
        var items = new[]
        {
            Item(scan, "folder", "Tài liệu", "root", DriveInventoryItemKind.Folder, false),
            Item(scan, "file", "Báo cáo.pdf", "folder", DriveInventoryItemKind.File, true, 20),
            Item(scan, "native", "Kế hoạch", "folder", DriveInventoryItemKind.GoogleWorkspaceFile, true),
            Item(scan, "shortcut", "Lối tắt", "folder", DriveInventoryItemKind.Shortcut, false)
        };
        var plan = Plan(scan, new BackupSelectionRule("folder", BackupSelectionRuleMode.Include, DriveInventoryItemKind.Folder, "Tài liệu"));

        var result = _planner.Evaluate(plan, items);

        Assert.Equal(3, result.Summary.SelectedItemCount);
        Assert.Equal(2, result.Summary.BackupEligibleItemCount);
        Assert.Equal(1, result.Summary.SelectedFolderCount);
        Assert.Equal(20, result.Summary.KnownBytes);
        Assert.Equal(1, result.Summary.UnknownSizeCount);
        Assert.True(result.Items["shortcut"].IsCoveredByIncludeRule);
        Assert.False(result.Items["shortcut"].IsSelected);
        Assert.True(result.Items["shortcut"].RequiresReview);
    }

    [Fact]
    public void DescendantExcludeOverridesSelectedAncestorWithoutUsingNamesAsIdentity()
    {
        var scan = Guid.NewGuid();
        var items = new[]
        {
            Item(scan, "folder", "Trùng tên", "root", DriveInventoryItemKind.Folder, false),
            Item(scan, "excluded", "Trùng tên", "folder", DriveInventoryItemKind.File, true, 10),
            Item(scan, "included", "Trùng tên", "folder", DriveInventoryItemKind.File, true, 15)
        };
        var plan = Plan(scan,
            new BackupSelectionRule("folder", BackupSelectionRuleMode.Include, DriveInventoryItemKind.Folder, "Trùng tên"),
            new BackupSelectionRule("excluded", BackupSelectionRuleMode.Exclude, DriveInventoryItemKind.File, "Trùng tên"));

        var result = _planner.Evaluate(plan, items);

        Assert.False(result.Items["excluded"].IsSelected);
        Assert.True(result.Items["included"].IsSelected);
        Assert.Equal(15, result.Summary.KnownBytes);
    }

    [Fact]
    public void ParentCyclesTerminateAndOnlyDirectRulesApply()
    {
        var scan = Guid.NewGuid();
        var items = new[]
        {
            Item(scan, "a", "A", "b", DriveInventoryItemKind.Folder, false),
            Item(scan, "b", "B", "a", DriveInventoryItemKind.File, true, 1)
        };

        var result = _planner.Evaluate(Plan(scan, new BackupSelectionRule("a", BackupSelectionRuleMode.Include, DriveInventoryItemKind.Folder, "A")), items);

        Assert.True(result.Items["b"].IsSelected);
    }

    [Fact]
    public void NewSnapshotReportsAutomaticallyIncludedAndMissingItems()
    {
        var oldScan = Guid.NewGuid();
        var newScan = Guid.NewGuid();
        var plan = Plan(oldScan, new BackupSelectionRule("folder", BackupSelectionRuleMode.Include, DriveInventoryItemKind.Folder, "Tài liệu"));
        var baseline = new[]
        {
            Item(oldScan, "folder", "Tài liệu", "root", DriveInventoryItemKind.Folder, false),
            Item(oldScan, "removed", "Cũ.txt", "folder", DriveInventoryItemKind.File, true, 1)
        };
        var latest = new[]
        {
            Item(newScan, "folder", "Tài liệu mới", "root", DriveInventoryItemKind.Folder, false),
            Item(newScan, "new", "Mới.txt", "folder", DriveInventoryItemKind.File, true, 2)
        };

        var result = _planner.Reconcile(plan, newScan, latest, baseline);

        Assert.True(result.UsesNewerSnapshot);
        Assert.Equal(1, result.NewlySelectedItemCount);
        Assert.Equal(1, result.MissingPreviouslySelectedItemCount);
        Assert.Equal(0, result.MissingRuleTargetCount);
    }

    [Fact]
    public void StableFileIdSurvivesRenameAndMoveWithoutFalseReconciliationWarning()
    {
        var oldScan = Guid.NewGuid();
        var newScan = Guid.NewGuid();
        var plan = Plan(oldScan, new BackupSelectionRule("stable", BackupSelectionRuleMode.Include, DriveInventoryItemKind.File, "Tên cũ.txt"));
        var baseline = new[] { Item(oldScan, "stable", "Tên cũ.txt", "old-folder", DriveInventoryItemKind.File, true, 1) };
        var latest = new[] { Item(newScan, "stable", "Tên mới.txt", "new-folder", DriveInventoryItemKind.File, true, 1) };

        var result = _planner.Reconcile(plan, newScan, latest, baseline);

        Assert.Equal(0, result.NewlySelectedItemCount);
        Assert.Equal(0, result.MissingPreviouslySelectedItemCount);
        Assert.Equal(0, result.MissingRuleTargetCount);
    }

    private static BackupSelectionPlan Plan(Guid scanId, params BackupSelectionRule[] rules) => new(
        Guid.NewGuid(), "account", "Kế hoạch", scanId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, rules);

    private static DriveInventoryItem Item(Guid scanId, string id, string name, string? parent,
        DriveInventoryItemKind kind, bool eligible, long? size = null) => new(
        scanId, id, name, parent, $"Drive của tôi/{name}", kind == DriveInventoryItemKind.Folder ? "application/vnd.google-apps.folder" : "text/plain",
        kind, DriveInventoryLocation.MyDrive, size, null, null, null, null, null, null, false, true, eligible,
        eligible || kind == DriveInventoryItemKind.Folder ? null : "Chưa hỗ trợ");
}

public sealed class BackupSelectionPlanServiceTests
{
    [Fact]
    public async Task StoredPlanReopensAndReconcilesAgainstLatestCompleteSnapshot()
    {
        var oldScan = Run(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-5));
        var newScan = Run(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var inventory = new MemoryInventoryRepository { Latest = newScan };
        inventory.Items[oldScan.ScanId] =
        [
            Item(oldScan.ScanId, "folder", "Tài liệu", "root", DriveInventoryItemKind.Folder, false),
            Item(oldScan.ScanId, "old", "Cũ.txt", "folder", DriveInventoryItemKind.File, true)
        ];
        inventory.Items[newScan.ScanId] =
        [
            Item(newScan.ScanId, "folder", "Tài liệu", "root", DriveInventoryItemKind.Folder, false),
            Item(newScan.ScanId, "new", "Mới.txt", "folder", DriveInventoryItemKind.File, true)
        ];
        var plans = new MemoryPlanRepository
        {
            Plan = new BackupSelectionPlan(Guid.NewGuid(), "account", "Đã lưu", oldScan.ScanId,
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddMinutes(-5),
                [new("folder", BackupSelectionRuleMode.Include, DriveInventoryItemKind.Folder, "Tài liệu")])
        };
        var service = new BackupSelectionPlanService(inventory, plans, new BackupSelectionPlanner());

        var workspace = await service.LoadAsync("account", CancellationToken.None);

        Assert.NotNull(workspace);
        Assert.Equal("Đã lưu", workspace.Plan.Name);
        Assert.Equal(1, workspace.Evaluation.Summary.SelectedItemCount);
        Assert.True(workspace.Reconciliation.UsesNewerSnapshot);
        Assert.Equal(1, workspace.Reconciliation.NewlySelectedItemCount);
        Assert.Equal(1, workspace.Reconciliation.MissingPreviouslySelectedItemCount);
    }

    [Fact]
    public async Task SaveRejectsStaleSnapshotAndReopensSavedPlanFromRepository()
    {
        var current = Run(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var inventory = new MemoryInventoryRepository { Latest = current };
        inventory.Items[current.ScanId] = [];
        var plans = new MemoryPlanRepository();
        var service = new BackupSelectionPlanService(inventory, plans, new BackupSelectionPlanner());
        var draft = new BackupSelectionPlan(Guid.NewGuid(), "account", "Kế hoạch", Guid.NewGuid(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, []);

        await Assert.ThrowsAsync<BackupSelectionPlanSnapshotChangedException>(() =>
            service.SaveAsync(draft, draft.SourceScanId, CancellationToken.None));

        var saved = await service.SaveAsync(draft, current.ScanId, CancellationToken.None);
        var restartedService = new BackupSelectionPlanService(inventory, plans, new BackupSelectionPlanner());
        var reopened = await restartedService.LoadAsync("account", CancellationToken.None);
        Assert.Equal(saved.PlanId, reopened!.Plan.PlanId);
        Assert.Equal(current.ScanId, reopened.Plan.SourceScanId);
    }

    private static DriveInventoryRun Run(Guid id, DateTimeOffset completed) => new(
        id, "google-drive", "account", completed.AddMinutes(-1), completed, DriveInventoryRunStatus.Completed,
        0, 0, 0, 0, 0, 0, 0, 0, 0, null, true, null);

    private static DriveInventoryItem Item(Guid scanId, string id, string name, string? parent,
        DriveInventoryItemKind kind, bool eligible) => new(
        scanId, id, name, parent, $"Drive của tôi/{name}", "text/plain", kind, DriveInventoryLocation.MyDrive,
        eligible ? 1 : null, null, null, null, null, null, null, false, true, eligible, null);

    private sealed class MemoryPlanRepository : IBackupSelectionPlanRepository
    {
        public BackupSelectionPlan? Plan { get; set; }
        public Task<BackupSelectionPlan?> GetByAccountAsync(string providerAccountId, CancellationToken cancellationToken) => Task.FromResult(Plan);
        public Task SaveAsync(BackupSelectionPlan plan, CancellationToken cancellationToken) { Plan = plan; return Task.CompletedTask; }
    }

    private sealed class MemoryInventoryRepository : IDriveInventoryRepository
    {
        public DriveInventoryRun? Latest { get; set; }
        public Dictionary<Guid, IReadOnlyList<DriveInventoryItem>> Items { get; } = [];
        public Task RecoverInterruptedAsync(DateTimeOffset interruptedAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task BeginAsync(DriveInventoryRun run, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AppendBatchAsync(Guid scanId, IReadOnlyList<DriveInventoryItem> items, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpdateHierarchyAsync(Guid scanId, DriveHierarchyResult hierarchy, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CompleteAsync(DriveInventoryRun completedRun, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task MarkIncompleteAsync(Guid scanId, DriveInventoryRunStatus status, DateTimeOffset completedAtUtc, string? failureCategory, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<DriveInventoryRun?> GetLatestSuccessfulAsync(string providerAccountId, CancellationToken cancellationToken) => Task.FromResult(Latest);
        public Task<IReadOnlyList<DriveInventoryRun>> GetRecentAsync(int maximumCount, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DriveInventoryRun>>([]);
        public Task<IReadOnlyList<DriveInventoryItem>> GetItemsAsync(Guid scanId, int maximumCount, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DriveInventoryItem>>(Items.TryGetValue(scanId, out var items) ? items.Take(maximumCount).ToArray() : []);
        public Task<IReadOnlyList<DriveInventoryItem>> GetAllItemsAsync(Guid scanId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.TryGetValue(scanId, out var items) ? items : (IReadOnlyList<DriveInventoryItem>)[]);
    }
}

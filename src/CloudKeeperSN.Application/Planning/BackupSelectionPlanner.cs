using CloudKeeperSN.Application.Persistence;
using CloudKeeperSN.Domain.Planning;
using CloudKeeperSN.Domain.Scanning;

namespace CloudKeeperSN.Application.Planning;

public sealed record InventorySelectionState(
    DriveInventoryItem Item,
    bool IsCoveredByIncludeRule,
    bool IsSelected,
    bool RequiresReview,
    string? AppliedRuleItemId);

public sealed record BackupSelectionSummary(
    int SelectedItemCount,
    int BackupEligibleItemCount,
    int SelectedFolderCount,
    long KnownBytes,
    int UnknownSizeCount,
    int ReviewItemCount,
    int SelectedReviewItemCount);

public sealed record BackupPlanReconciliation(
    bool UsesNewerSnapshot,
    int NewlySelectedItemCount,
    int MissingPreviouslySelectedItemCount,
    int MissingRuleTargetCount)
{
    public bool RequiresAttention => NewlySelectedItemCount > 0 || MissingPreviouslySelectedItemCount > 0 || MissingRuleTargetCount > 0;
}

public sealed record BackupSelectionEvaluation(
    IReadOnlyDictionary<string, InventorySelectionState> Items,
    BackupSelectionSummary Summary);

public sealed record BackupPlanWorkspace(
    DriveInventoryRun LatestScan,
    BackupSelectionPlan Plan,
    IReadOnlyList<DriveInventoryItem> InventoryItems,
    BackupSelectionEvaluation Evaluation,
    BackupPlanReconciliation Reconciliation);

public sealed class BackupSelectionPlanner
{
    public BackupSelectionEvaluation Evaluate(BackupSelectionPlan plan, IReadOnlyList<DriveInventoryItem> items)
    {
        var byId = items.GroupBy(item => item.FileId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var rules = plan.Rules.GroupBy(rule => rule.ItemId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        var resolvedRules = new Dictionary<string, BackupSelectionRule?>(StringComparer.Ordinal);
        var states = new Dictionary<string, InventorySelectionState>(StringComparer.Ordinal);
        var selectedItems = 0;
        var eligibleItems = 0;
        var selectedFolders = 0;
        var unknownSizes = 0;
        var reviewItems = 0;
        var selectedReviewItems = 0;
        long knownBytes = 0;

        foreach (var item in byId.Values)
        {
            var applied = FindNearestRule(item, byId, rules, resolvedRules);
            var covered = applied?.Mode == BackupSelectionRuleMode.Include;
            var requiresReview = item.Location == DriveInventoryLocation.Unresolved ||
                (item.Kind != DriveInventoryItemKind.Folder && !item.IsBackupEligible);
            var selected = covered && item.IsBackupEligible && item.Kind != DriveInventoryItemKind.Folder;
            if (requiresReview) reviewItems++;
            if (covered && requiresReview) selectedReviewItems++;
            if (covered && item.Kind != DriveInventoryItemKind.Folder) selectedItems++;
            if (selected)
            {
                eligibleItems++;
                if (item.Size is { } size) knownBytes = checked(knownBytes + size);
                else unknownSizes++;
            }
            if (covered && item.Kind == DriveInventoryItemKind.Folder) selectedFolders++;
            states[item.FileId] = new(item, covered, selected, requiresReview, applied?.ItemId);
        }

        return new(states, new(selectedItems, eligibleItems, selectedFolders, knownBytes, unknownSizes, reviewItems, selectedReviewItems));
    }

    public BackupPlanReconciliation Reconcile(
        BackupSelectionPlan plan,
        Guid latestScanId,
        IReadOnlyList<DriveInventoryItem> latestItems,
        IReadOnlyList<DriveInventoryItem> baselineItems)
    {
        if (plan.SourceScanId == latestScanId)
            return new(false, 0, 0, plan.Rules.Count(rule => latestItems.All(item => item.FileId != rule.ItemId)));

        var latest = Evaluate(plan, latestItems).Items.Values.Where(state => state.IsSelected).Select(state => state.Item.FileId).ToHashSet(StringComparer.Ordinal);
        var baseline = Evaluate(plan, baselineItems).Items.Values.Where(state => state.IsSelected).Select(state => state.Item.FileId).ToHashSet(StringComparer.Ordinal);
        var latestIds = latestItems.Select(item => item.FileId).ToHashSet(StringComparer.Ordinal);
        return new(
            true,
            latest.Count(id => !baseline.Contains(id)),
            baseline.Count(id => !latest.Contains(id)),
            plan.Rules.Count(rule => !latestIds.Contains(rule.ItemId)));
    }

    private static BackupSelectionRule? FindNearestRule(
        DriveInventoryItem start,
        IReadOnlyDictionary<string, DriveInventoryItem> byId,
        IReadOnlyDictionary<string, BackupSelectionRule> rules,
        IDictionary<string, BackupSelectionRule?> resolvedRules)
    {
        if (resolvedRules.TryGetValue(start.FileId, out var cached)) return cached;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var chain = new List<string>();
        var current = start;
        BackupSelectionRule? result = null;
        while (visited.Add(current.FileId))
        {
            chain.Add(current.FileId);
            if (rules.TryGetValue(current.FileId, out var rule)) { result = rule; break; }
            if (resolvedRules.TryGetValue(current.FileId, out result)) break;
            if (string.IsNullOrWhiteSpace(current.ParentId) || string.Equals(current.ParentId, "root", StringComparison.Ordinal) ||
                !byId.TryGetValue(current.ParentId, out current!)) break;
        }
        foreach (var id in chain) resolvedRules[id] = result;
        return result;
    }
}

public interface IBackupSelectionPlanService
{
    Task<BackupPlanWorkspace?> LoadAsync(string providerAccountId, CancellationToken cancellationToken);
    Task<BackupSelectionPlan> SaveAsync(BackupSelectionPlan plan, Guid latestScanId, CancellationToken cancellationToken);
    BackupSelectionEvaluation Evaluate(BackupSelectionPlan plan, IReadOnlyList<DriveInventoryItem> items);
}

public sealed class BackupSelectionPlanSnapshotChangedException : Exception
{
    public BackupSelectionPlanSnapshotChangedException() : base("A newer complete Drive inventory snapshot is available.") { }
}

public sealed class BackupSelectionPlanService(
    IDriveInventoryRepository inventoryRepository,
    IBackupSelectionPlanRepository planRepository,
    BackupSelectionPlanner planner) : IBackupSelectionPlanService
{
    public async Task<BackupPlanWorkspace?> LoadAsync(string providerAccountId, CancellationToken cancellationToken)
    {
        var latest = await inventoryRepository.GetLatestSuccessfulAsync(providerAccountId, cancellationToken);
        if (latest is null) return null;
        var items = await inventoryRepository.GetAllItemsAsync(latest.ScanId, cancellationToken);
        var stored = await planRepository.GetByAccountAsync(providerAccountId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var plan = stored ?? new BackupSelectionPlan(Guid.NewGuid(), providerAccountId, "Kế hoạch sao lưu Google Drive",
            latest.ScanId, now, now, []);
        IReadOnlyList<DriveInventoryItem> baseline = [];
        if (stored is not null && stored.SourceScanId != latest.ScanId)
            baseline = await inventoryRepository.GetAllItemsAsync(stored.SourceScanId, cancellationToken);
        return new(latest, plan, items, planner.Evaluate(plan, items), planner.Reconcile(plan, latest.ScanId, items, baseline));
    }

    public async Task<BackupSelectionPlan> SaveAsync(BackupSelectionPlan plan, Guid latestScanId, CancellationToken cancellationToken)
    {
        var current = await inventoryRepository.GetLatestSuccessfulAsync(plan.ProviderAccountId, cancellationToken);
        if (current is null || current.ScanId != latestScanId)
            throw new BackupSelectionPlanSnapshotChangedException();
        var saved = plan with { SourceScanId = latestScanId, UpdatedAtUtc = DateTimeOffset.UtcNow };
        await planRepository.SaveAsync(saved, cancellationToken);
        return saved;
    }

    public BackupSelectionEvaluation Evaluate(BackupSelectionPlan plan, IReadOnlyList<DriveInventoryItem> items) =>
        planner.Evaluate(plan, items);
}

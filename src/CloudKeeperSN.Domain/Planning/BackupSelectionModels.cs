using CloudKeeperSN.Domain.Scanning;

namespace CloudKeeperSN.Domain.Planning;

public enum BackupSelectionRuleMode
{
    Include,
    Exclude
}

public sealed record BackupSelectionRule(
    string ItemId,
    BackupSelectionRuleMode Mode,
    DriveInventoryItemKind ItemKind,
    string LastKnownName);

public sealed record BackupSelectionPlan(
    Guid PlanId,
    string ProviderAccountId,
    string Name,
    Guid SourceScanId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<BackupSelectionRule> Rules);

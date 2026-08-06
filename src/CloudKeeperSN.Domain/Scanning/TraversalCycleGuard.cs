namespace CloudKeeperSN.Domain.Scanning;

public sealed class TraversalCycleGuard
{
    private readonly HashSet<(string AccountId, string ItemId)> _visited = [];

    public bool TryEnter(string providerAccountId, string itemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerAccountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        return _visited.Add((providerAccountId, itemId));
    }

    public int VisitedCount => _visited.Count;
}


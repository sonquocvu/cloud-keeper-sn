namespace CloudKeeperSN.Providers.GoogleDrive;

public static class GoogleDriveQuery
{
    public static string ChildrenOf(string parentItemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentItemId);
        return $"'{EscapeLiteral(parentItemId)}' in parents and trashed = false";
    }

    public static string EscapeLiteral(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "\\'", StringComparison.Ordinal);
}

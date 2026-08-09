using CloudKeeperSN.Domain.Scanning;

namespace CloudKeeperSN.Application.Scanning;

public sealed class DriveHierarchyBuilder
{
    public DriveHierarchyResult Build(IReadOnlyList<DriveHierarchyNode> nodes)
    {
        var byId = nodes.GroupBy(node => node.FileId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var resolved = new Dictionary<string, (string Path, DriveInventoryLocation Location)>(StringComparer.Ordinal);
        var unresolved = 0;

        foreach (var start in nodes)
        {
            if (resolved.ContainsKey(start.FileId)) continue;
            var chain = new List<DriveHierarchyNode>();
            var positions = new Dictionary<string, int>(StringComparer.Ordinal);
            var current = start;
            (string Path, DriveInventoryLocation Location)? anchor = null;
            var invalid = false;

            while (true)
            {
                if (resolved.TryGetValue(current.FileId, out var existing))
                {
                    anchor = existing;
                    break;
                }
                if (!positions.TryAdd(current.FileId, chain.Count))
                {
                    invalid = true;
                    break;
                }
                chain.Add(current);
                if (string.IsNullOrWhiteSpace(current.ParentId) || string.Equals(current.ParentId, "root", StringComparison.Ordinal))
                {
                    anchor = current.IsShared || current.IsOwnedByUser == false
                        ? ("Được chia sẻ", DriveInventoryLocation.Shared)
                        : ("Drive của tôi", DriveInventoryLocation.MyDrive);
                    break;
                }
                if (!byId.TryGetValue(current.ParentId, out current!))
                {
                    invalid = true;
                    break;
                }
            }

            if (invalid)
            {
                foreach (var node in chain)
                {
                    if (resolved.ContainsKey(node.FileId)) continue;
                    resolved[node.FileId] = ($"Không xác định được thư mục cha/{SafeName(node.Name)}", DriveInventoryLocation.Unresolved);
                    unresolved++;
                }
                continue;
            }

            var baseValue = anchor!.Value;
            for (var index = chain.Count - 1; index >= 0; index--)
            {
                var node = chain[index];
                baseValue = ($"{baseValue.Path}/{SafeName(node.Name)}", baseValue.Location);
                resolved[node.FileId] = baseValue;
            }
        }

        return new DriveHierarchyResult(resolved, unresolved);
    }

    private static string SafeName(string name) => string.IsNullOrWhiteSpace(name) ? "Mục không có tên" : name.Replace('/', '／');
}

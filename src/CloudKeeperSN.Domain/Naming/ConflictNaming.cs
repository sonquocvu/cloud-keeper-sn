namespace CloudKeeperSN.Domain.Naming;

public interface IConflictNamePolicy
{
    string CreateSafeName(string normalizedName, int occurrence);
}

public sealed class DeterministicConflictNamePolicy : IConflictNamePolicy
{
    public string CreateSafeName(string normalizedName, int occurrence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedName);
        if (occurrence < 2) throw new ArgumentOutOfRangeException(nameof(occurrence));

        var extension = Path.GetExtension(normalizedName);
        var stem = Path.GetFileNameWithoutExtension(normalizedName);
        return OneDriveNameNormalizer.Normalize($"{stem} (CloudKeeperSN {occurrence}){extension}");
    }
}


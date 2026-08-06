namespace CloudKeeperSN.Domain.Storage;

public sealed record StoragePath
{
    public static StoragePath Root { get; } = new([]);

    public IReadOnlyList<string> Segments { get; }

    public StoragePath(IEnumerable<string> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var materialized = segments.ToArray();
        if (materialized.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("A storage path cannot contain blank segments.", nameof(segments));
        }

        if (materialized.Any(segment => segment is "." or ".." || segment.Contains('/') || segment.Contains('\\')))
        {
            throw new ArgumentException("A storage path contains an invalid segment.", nameof(segments));
        }

        Segments = materialized;
    }

    public bool IsRoot => Segments.Count == 0;

    public StoragePath Append(string segment) => new(Segments.Append(segment));

    public StoragePath RelativeTo(StoragePath ancestor)
    {
        ArgumentNullException.ThrowIfNull(ancestor);
        if (ancestor.Segments.Count > Segments.Count ||
            !ancestor.Segments.SequenceEqual(Segments.Take(ancestor.Segments.Count), StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The supplied path is not an ancestor.");
        }

        return new StoragePath(Segments.Skip(ancestor.Segments.Count));
    }

    public override string ToString() => string.Join('/', Segments);
}


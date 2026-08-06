namespace CloudKeeperSN.Domain.Transfers;

public enum TransferState
{
    Discovered,
    Planned,
    Waiting,
    Downloading,
    Uploading,
    Verifying,
    Completed,
    Skipped,
    Paused,
    RetryPending,
    Failed,
    Cancelled
}

public sealed record TransferItem
{
    public required Guid Id { get; init; }
    public required Guid RunId { get; init; }
    public required string SourceProviderAccountId { get; init; }
    public required string SourceItemId { get; init; }
    public string? SourceParentItemId { get; init; }
    public required string OriginalName { get; init; }
    public required string NormalizedDestinationName { get; init; }
    public required string RelativeSourcePath { get; init; }
    public string? DestinationItemId { get; init; }
    public string? MimeType { get; init; }
    public long? FileSize { get; init; }
    public DateTimeOffset? SourceCreatedAtUtc { get; init; }
    public DateTimeOffset? SourceModifiedAtUtc { get; init; }
    public string? SourceChecksumAlgorithm { get; init; }
    public string? SourceChecksum { get; init; }
    public TransferState State { get; init; } = TransferState.Discovered;
    public VerificationLevel? Verification { get; init; }
    public TransferErrorCategory? LastErrorCategory { get; init; }
    public int RetryCount { get; init; }
    public DateTimeOffset? NextRetryAtUtc { get; init; }
}

public static class TransferStateMachine
{
    private static readonly IReadOnlyDictionary<TransferState, IReadOnlySet<TransferState>> AllowedTransitions =
        new Dictionary<TransferState, IReadOnlySet<TransferState>>
        {
            [TransferState.Discovered] = Set(TransferState.Planned, TransferState.Skipped, TransferState.Cancelled),
            [TransferState.Planned] = Set(TransferState.Waiting, TransferState.Skipped, TransferState.Cancelled),
            [TransferState.Waiting] = Set(TransferState.Downloading, TransferState.Paused, TransferState.Cancelled),
            [TransferState.Downloading] = Set(TransferState.Uploading, TransferState.RetryPending, TransferState.Failed, TransferState.Paused, TransferState.Cancelled),
            [TransferState.Uploading] = Set(TransferState.Verifying, TransferState.RetryPending, TransferState.Failed, TransferState.Paused, TransferState.Cancelled),
            [TransferState.Verifying] = Set(TransferState.Completed, TransferState.RetryPending, TransferState.Failed, TransferState.Paused, TransferState.Cancelled),
            [TransferState.Paused] = Set(TransferState.Waiting, TransferState.Cancelled),
            [TransferState.RetryPending] = Set(TransferState.Waiting, TransferState.Paused, TransferState.Failed, TransferState.Cancelled),
            [TransferState.Failed] = Set(TransferState.Waiting, TransferState.Cancelled),
            [TransferState.Completed] = Set(),
            [TransferState.Skipped] = Set(),
            [TransferState.Cancelled] = Set()
        };

    public static bool CanTransition(TransferState from, TransferState to) => AllowedTransitions[from].Contains(to);

    public static TransferState Transition(TransferState from, TransferState to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException($"Invalid transfer transition: {from} -> {to}.");
        }

        return to;
    }

    private static IReadOnlySet<TransferState> Set(params TransferState[] values) => new HashSet<TransferState>(values);
}


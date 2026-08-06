namespace CloudKeeperSN.Domain.Transfers;

public sealed record RetryDecision(bool ShouldRetry, TimeSpan Delay, int NextAttempt);

public sealed class RetryPolicy
{
    private readonly TimeSpan _baseDelay;
    private readonly TimeSpan _maximumDelay;
    private readonly int _maximumAttempts;

    public RetryPolicy(TimeSpan? baseDelay = null, TimeSpan? maximumDelay = null, int maximumAttempts = 6)
    {
        if (maximumAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        _baseDelay = baseDelay ?? TimeSpan.FromSeconds(2);
        _maximumDelay = maximumDelay ?? TimeSpan.FromMinutes(5);
        _maximumAttempts = maximumAttempts;
    }

    public RetryDecision Decide(int completedAttempts, TimeSpan? retryAfter = null, double jitterFraction = 0.5)
    {
        if (completedAttempts >= _maximumAttempts)
        {
            return new RetryDecision(false, TimeSpan.Zero, completedAttempts);
        }

        if (jitterFraction is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(jitterFraction));

        var exponentialMilliseconds = _baseDelay.TotalMilliseconds * Math.Pow(2, completedAttempts);
        var capped = Math.Min(exponentialMilliseconds, _maximumDelay.TotalMilliseconds);
        var jittered = TimeSpan.FromMilliseconds(capped * (0.5 + jitterFraction * 0.5));
        var delay = retryAfter is { } providerDelay && providerDelay > jittered ? providerDelay : jittered;
        return new RetryDecision(true, delay, completedAttempts + 1);
    }
}


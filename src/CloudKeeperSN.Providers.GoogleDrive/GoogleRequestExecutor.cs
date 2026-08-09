using CloudKeeperSN.Domain.Storage;
using CloudKeeperSN.Domain.Transfers;

namespace CloudKeeperSN.Providers.GoogleDrive;

public interface IGoogleRetryDelay
{
    Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class GoogleRetryDelay : IGoogleRetryDelay
{
    public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}

public sealed class GoogleRequestExecutor
{
    private readonly RetryPolicy _retryPolicy;
    private readonly IGoogleRetryDelay _delay;
    private readonly Func<double> _jitter;

    public GoogleRequestExecutor(
        IGoogleRetryDelay? delay = null,
        RetryPolicy? retryPolicy = null,
        Func<double>? jitter = null)
    {
        _delay = delay ?? new GoogleRetryDelay();
        _retryPolicy = retryPolicy ?? new RetryPolicy(maximumAttempts: 6);
        _jitter = jitter ?? Random.Shared.NextDouble;
    }

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        var completedAttempts = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await operation(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                var failure = GoogleProviderExceptionMapper.Map(exception);
                if (!IsTransient(failure.Category)) throw failure;
                var decision = _retryPolicy.Decide(completedAttempts, failure.RetryAfter, _jitter());
                if (!decision.ShouldRetry) throw failure;
                completedAttempts = decision.NextAttempt;
                await _delay.WaitAsync(decision.Delay, cancellationToken);
            }
        }
    }

    private static bool IsTransient(ProviderFailureCategory category) => category is
        ProviderFailureCategory.NetworkUnavailable or
        ProviderFailureCategory.RequestTimedOut or
        ProviderFailureCategory.ProviderThrottled or
        ProviderFailureCategory.ServiceUnavailable;
}

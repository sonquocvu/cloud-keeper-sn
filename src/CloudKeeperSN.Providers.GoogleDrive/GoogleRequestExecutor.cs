using CloudKeeperSN.Domain.Storage;
using CloudKeeperSN.Domain.Transfers;
using Google.Apis.Http;

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

    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken,
        Func<TimeSpan?>? retryAfterProvider = null)
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
                var retryAfter = failure.RetryAfter ?? retryAfterProvider?.Invoke();
                var decision = _retryPolicy.Decide(completedAttempts, retryAfter, _jitter());
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

internal sealed class GoogleRetryAfterCapture(Func<DateTimeOffset>? utcNow = null) : IHttpUnsuccessfulResponseHandler
{
    private readonly AsyncLocal<TimeSpan?> _captured = new();
    private readonly Func<DateTimeOffset> _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    public Task<bool> HandleResponseAsync(HandleUnsuccessfulResponseArgs args)
    {
        var retry = args.Response.Headers.RetryAfter;
        var value = retry?.Delta ?? (retry?.Date is { } date ? date - _utcNow() : null);
        _captured.Value = value > TimeSpan.Zero ? value : null;
        return Task.FromResult(false);
    }

    public void Reset() => _captured.Value = null;

    public TimeSpan? Consume()
    {
        var value = _captured.Value;
        _captured.Value = null;
        return value;
    }
}

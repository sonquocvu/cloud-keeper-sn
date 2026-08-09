using CloudKeeperSN.Domain.Storage;
using CloudKeeperSN.Domain.Transfers;
using CloudKeeperSN.Providers.GoogleDrive;
using Google.Apis.Http;
using System.Net;
using System.Net.Http.Headers;

namespace CloudKeeperSN.Application.Tests;

public sealed class GoogleRequestExecutorTests
{
    [Fact]
    public async Task RetriesTransientFailuresWithBoundedBackoff()
    {
        var delay = new CapturingDelay();
        var executor = new GoogleRequestExecutor(
            delay,
            new RetryPolicy(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(25), maximumAttempts: 3),
            () => 1);
        var attempts = 0;

        var result = await executor.ExecuteAsync<int>(_ =>
        {
            attempts++;
            return attempts < 3
                ? Task.FromException<int>(Failure(ProviderFailureCategory.ServiceUnavailable))
                : Task.FromResult(42);
        }, CancellationToken.None);

        Assert.Equal(42, result);
        Assert.Equal(3, attempts);
        Assert.Equal([TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20)], delay.Delays);
    }

    [Fact]
    public async Task DoesNotRetryPermissionFailure()
    {
        var delay = new CapturingDelay();
        var executor = new GoogleRequestExecutor(delay, jitter: () => 0);
        var attempts = 0;

        var failure = await Assert.ThrowsAsync<ProviderOperationException>(() => executor.ExecuteAsync<int>(_ =>
        {
            attempts++;
            return Task.FromException<int>(Failure(ProviderFailureCategory.PermissionDenied));
        }, CancellationToken.None));

        Assert.Equal(ProviderFailureCategory.PermissionDenied, failure.Category);
        Assert.Equal(1, attempts);
        Assert.Empty(delay.Delays);
    }

    [Fact]
    public async Task CancellationStopsDuringBackoff()
    {
        var delay = new BlockingDelay();
        var executor = new GoogleRequestExecutor(delay, jitter: () => 0);
        using var cancellation = new CancellationTokenSource();
        var operation = executor.ExecuteAsync<int>(
            _ => Task.FromException<int>(Failure(ProviderFailureCategory.NetworkUnavailable)),
            cancellation.Token);
        await delay.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
    }

    [Fact]
    public async Task RetryAfterOverridesCalculatedBackoffWhenProviderSuppliesIt()
    {
        var delay = new CapturingDelay();
        var executor = new GoogleRequestExecutor(
            delay,
            new RetryPolicy(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(30), maximumAttempts: 2),
            () => 0);
        var attempts = 0;

        var result = await executor.ExecuteAsync<int>(_ =>
        {
            attempts++;
            return attempts == 1
                ? Task.FromException<int>(new ProviderOperationException(
                    ProviderFailureCategory.ProviderThrottled, "throttled", retryAfter: TimeSpan.FromSeconds(7)))
                : Task.FromResult(42);
        }, CancellationToken.None);

        Assert.Equal(42, result);
        Assert.Equal([TimeSpan.FromSeconds(7)], delay.Delays);
    }

    [Fact]
    public async Task GoogleResponseCaptureReadsRetryAfterHeaderWithoutRetryingInsideSdk()
    {
        var now = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        var capture = new GoogleRetryAfterCapture(() => now);
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(now.AddSeconds(9));
        var args = new HandleUnsuccessfulResponseArgs
        {
            Response = response,
            Request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/drive/v3/files"),
            CancellationToken = CancellationToken.None,
            TotalTries = 1,
            CurrentFailedTry = 1
        };

        var handled = await capture.HandleResponseAsync(args);

        Assert.False(handled);
        Assert.Equal(TimeSpan.FromSeconds(9), capture.Consume());
        Assert.Null(capture.Consume());
        args.Request.Dispose();
    }

    private static ProviderOperationException Failure(ProviderFailureCategory category) =>
        new(category, ProviderFailureMessages.ToVietnamese(category));

    private sealed class CapturingDelay : IGoogleRetryDelay
    {
        public List<TimeSpan> Delays { get; } = [];
        public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Delays.Add(delay);
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingDelay : IGoogleRetryDelay
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}

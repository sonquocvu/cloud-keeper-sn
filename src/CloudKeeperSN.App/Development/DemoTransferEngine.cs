using CloudKeeperSN.App.ViewModels;
using CloudKeeperSN.Domain.Transfers;

namespace CloudKeeperSN.App.Development;

public interface IDemoDelay
{
    Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class DemoDelay : IDemoDelay
{
    public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}

public sealed record DemoTransferProgress(
    int ProcessedItems,
    int TotalItems,
    long TransferredBytes,
    long TotalBytes,
    string CurrentFile,
    string CurrentOperation,
    int CompletedCount,
    int SkippedCount,
    int WarningCount,
    int FailedCount,
    int RetryCount);

public sealed record DemoTransferResult(
    DemoRunStatus Status,
    int CompletedCount,
    int SkippedCount,
    int WarningCount,
    int FailedCount,
    long TransferredBytes,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    VerificationLevel Verification);

public sealed class DemoTransferEngine(DemoConfiguration configuration, IDemoDelay delay)
{
    private readonly object _pauseLock = new();
    private TaskCompletionSource _resumeSignal = CompletedSignal();

    public bool IsPaused { get; private set; }

    public void Pause()
    {
        lock (_pauseLock)
        {
            if (IsPaused) return;
            IsPaused = true;
            _resumeSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void Resume()
    {
        lock (_pauseLock)
        {
            if (!IsPaused) return;
            IsPaused = false;
            _resumeSignal.TrySetResult();
        }
    }

    public async Task<DemoTransferResult> RunAsync(
        IReadOnlyList<PreviewItemViewModel> items,
        IProgress<DemoTransferProgress> progress,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var totalBytes = items.Where(IsTransfer).Sum(item => item.Size ?? 0);
        var transferred = 0L;
        var completed = 0;
        var skipped = 0;
        var warnings = 0;
        var failed = 0;
        var retries = 0;
        var processed = 0;
        var itemDelay = configuration.Scenario == DemoScenarioKind.LongRunning ? TimeSpan.FromMilliseconds(900) : TimeSpan.FromMilliseconds(180);

        foreach (var item in items)
        {
            await WaitIfPausedAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (item.Category == PreviewItemCategory.Skip)
            {
                skipped++;
                processed++;
                Report(item.OriginalName, "Đang bỏ qua tệp không thay đổi");
                continue;
            }

            if (item.Category == PreviewItemCategory.Unsupported)
            {
                skipped++;
                if (configuration.Scenario != DemoScenarioKind.CompletedSuccessfully) warnings++;
                processed++;
                Report(item.OriginalName, "Đang ghi nhận mục không được hỗ trợ");
                continue;
            }

            if (item.Category == PreviewItemCategory.Warning && configuration.Scenario != DemoScenarioKind.CompletedSuccessfully)
            {
                warnings++;
            }

            if (item.ItemId == "g-doc" && configuration.Scenario is DemoScenarioKind.Standard or DemoScenarioKind.RetryAndFailure)
            {
                retries++;
                Report(item.OriginalName, "Kết nối bị gián đoạn; đang chờ thử lại an toàn");
                await delay.WaitAsync(itemDelay, cancellationToken);
                await WaitIfPausedAsync(cancellationToken);
            }

            Report(item.OriginalName, "Đang đọc từ Google Drive");
            await delay.WaitAsync(itemDelay, cancellationToken);
            await WaitIfPausedAsync(cancellationToken);
            Report(item.OriginalName, "Đang lưu bản sao lên OneDrive");
            await delay.WaitAsync(itemDelay, cancellationToken);

            if (configuration.Scenario == DemoScenarioKind.RetryAndFailure && item.ItemId == "g-photo")
            {
                failed++;
                processed++;
                Report(item.OriginalName, "Tệp chưa thể sao lưu sau khi thử lại");
                continue;
            }

            completed++;
            processed++;
            transferred += item.Size ?? 0;
            Report(item.OriginalName, "Đã sao lưu và xác minh tệp");
        }

        var status = failed > 0
            ? DemoRunStatus.Failed
            : warnings > 0 ? DemoRunStatus.CompletedWithWarnings : DemoRunStatus.Completed;
        var verification = failed > 0
            ? VerificationLevel.VerificationFailed
            : warnings > 0 ? VerificationLevel.VerifiedBySizeAndMetadata : VerificationLevel.VerifiedByProviderHash;
        return new DemoTransferResult(status, completed, skipped, warnings, failed, transferred, startedAt, DateTimeOffset.UtcNow, verification);

        void Report(string currentFile, string operation) => progress.Report(new DemoTransferProgress(
            processed, items.Count, transferred, totalBytes, currentFile, operation,
            completed, skipped, warnings, failed, retries));
    }

    private async Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        Task signal;
        lock (_pauseLock) signal = _resumeSignal.Task;
        await signal.WaitAsync(cancellationToken);
    }

    private static bool IsTransfer(PreviewItemViewModel item) => item.Category is PreviewItemCategory.Copy or PreviewItemCategory.Conflict or PreviewItemCategory.Warning;
    private static TaskCompletionSource CompletedSignal()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        signal.SetResult();
        return signal;
    }
}

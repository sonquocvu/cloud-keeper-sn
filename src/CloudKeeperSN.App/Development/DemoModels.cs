using CloudKeeperSN.Domain.Transfers;

namespace CloudKeeperSN.App.Development;

public enum DemoScenarioKind
{
    Disconnected,
    GoogleOnly,
    ConnectedEmpty,
    Standard,
    LongRunning,
    CompletedSuccessfully,
    CompletedWithWarnings,
    RetryAndFailure
}

public enum DemoRunStatus
{
    Running,
    Completed,
    CompletedWithWarnings,
    Failed,
    Cancelled
}

public sealed record DemoConfiguration(bool IsEnabled, DemoScenarioKind Scenario)
{
    public static DemoConfiguration FromEnvironment()
    {
        var enabledValue = Environment.GetEnvironmentVariable("CLOUDKEEPERSN_DEMO_MODE");
#if DEBUG
        var defaultEnabled = true;
#else
        var defaultEnabled = false;
#endif
        var enabled = bool.TryParse(enabledValue, out var parsedEnabled) ? parsedEnabled : defaultEnabled;
        var scenarioValue = Environment.GetEnvironmentVariable("CLOUDKEEPERSN_DEMO_SCENARIO");
        var scenario = Enum.TryParse<DemoScenarioKind>(scenarioValue, ignoreCase: true, out var parsedScenario)
            ? parsedScenario
            : DemoScenarioKind.Standard;
        return new DemoConfiguration(enabled, scenario);
    }
}

public sealed record DemoBackupRun(
    Guid Id,
    string Name,
    string Source,
    string Destination,
    DateTimeOffset StartedAt,
    TimeSpan Duration,
    DemoRunStatus Status,
    int CompletedFiles,
    int SkippedFiles,
    int WarningCount,
    int FailedCount,
    long TransferredBytes,
    VerificationLevel Verification,
    IReadOnlyList<string> Timeline);

public sealed class DemoWorkspace
{
    private readonly List<DemoBackupRun> _runs = [];
    public event EventHandler? Changed;
    public IReadOnlyList<DemoBackupRun> Runs => _runs;
    public bool IsBackupRunning { get; private set; }

    public void ReplaceRuns(IEnumerable<DemoBackupRun> runs)
    {
        _runs.Clear();
        _runs.AddRange(runs.OrderByDescending(run => run.StartedAt));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void AddOrReplaceRun(DemoBackupRun run)
    {
        _runs.RemoveAll(existing => existing.Id == run.Id);
        _runs.Add(run);
        _runs.Sort((left, right) => right.StartedAt.CompareTo(left.StartedAt));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetBackupRunning(bool value)
    {
        IsBackupRunning = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}


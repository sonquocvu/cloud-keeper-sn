using CloudKeeperSN.Application.Persistence;
using CloudKeeperSN.Application.Scanning;
using CloudKeeperSN.Application.Storage;
using CloudKeeperSN.Domain.Scanning;
using CloudKeeperSN.Domain.Storage;

namespace CloudKeeperSN.Application.Tests;

public sealed class DriveInventoryScannerTests
{
    [Fact]
    public async Task MultiplePagesArePersistedAndOnlyCompleteSnapshotIsPublished()
    {
        var source = new FakeSource(
            new DriveInventoryPage([Folder("folder", "Tài liệu", "root"), File("a", "Báo cáo.pdf", "folder", 10)], "next"),
            new DriveInventoryPage([
                File("b", "Báo cáo.pdf", "folder", 20),
                Native("native", "Kế hoạch", "root"),
                Shortcut("shortcut", "Lối tắt", "root"),
                File("unicode", "Dữ liệu_日本語.txt", "missing", null, shared: true)], null));
        var repository = new MemoryRepository();
        var scanner = Create(source, repository);
        var states = new List<DriveInventoryScanState>();
        scanner.StateChanged += states.Add;

        var result = await scanner.ScanAsync(CancellationToken.None);

        Assert.True(result.IsComplete);
        Assert.Equal(6, result.TotalItems);
        Assert.Equal(3, result.FileCount);
        Assert.Equal(1, result.FolderCount);
        Assert.Equal(1, result.GoogleWorkspaceFileCount);
        Assert.Equal(1, result.ShortcutCount);
        Assert.Equal(2, result.UnknownSizeCount);
        Assert.Equal(30, result.KnownBytes);
        Assert.Equal(2, repository.AppendCalls);
        Assert.Equal("Drive của tôi/Tài liệu/Báo cáo.pdf", repository.Items["a"].DisplayPath);
        Assert.Equal("Drive của tôi/Tài liệu/Báo cáo.pdf", repository.Items["b"].DisplayPath);
        Assert.StartsWith("Không xác định được thư mục cha/", repository.Items["unicode"].DisplayPath);
        Assert.Equal(DriveInventoryScanStatus.Completed, states[^1].Status);
        Assert.Contains(states, state => state.Status == DriveInventoryScanStatus.LoadingStorageInformation);
        Assert.Contains(states, state => state.Status == DriveInventoryScanStatus.BuildingHierarchy);
    }

    [Fact]
    public async Task EmptyDriveProducesValidCompleteSnapshot()
    {
        var scanner = Create(new FakeSource(new DriveInventoryPage([], null)), new MemoryRepository());

        var result = await scanner.ScanAsync(CancellationToken.None);

        Assert.True(result.IsComplete);
        Assert.Equal(0, result.TotalItems);
        Assert.Equal(DriveInventoryRunStatus.Completed, result.Status);
    }

    [Fact]
    public async Task PaginationFailurePreservesPreviousSuccessfulSnapshotAndAllowsRetry()
    {
        var previous = CompletedRun(Guid.NewGuid(), 9);
        var repository = new MemoryRepository { Latest = previous };
        var source = new FakeSource(new DriveInventoryPage([File("partial", "Một phần.txt", "root", 1)], "repeat"),
            new DriveInventoryPage([], "repeat"));
        var scanner = Create(source, repository);

        var failure = await Assert.ThrowsAsync<ProviderOperationException>(() => scanner.ScanAsync(CancellationToken.None));

        Assert.Equal(ProviderFailureCategory.InvalidProviderResponse, failure.Category);
        Assert.Equal(previous.ScanId, repository.Latest!.ScanId);
        Assert.Contains(repository.Runs.Values, run => run.Status == DriveInventoryRunStatus.Failed && !run.IsComplete);

        source.Reset(new DriveInventoryPage([], null));
        var retry = await scanner.ScanAsync(CancellationToken.None);
        Assert.True(retry.IsComplete);
    }

    [Fact]
    public async Task CancellationBetweenPagesMarksStagingRunIncompleteAndKeepsPreviousSnapshot()
    {
        var previous = CompletedRun(Guid.NewGuid(), 4);
        var repository = new MemoryRepository { Latest = previous };
        using var cancellation = new CancellationTokenSource();
        var source = new FakeSource(new DriveInventoryPage([File("one", "Một.txt", "root", 1)], "next"))
        {
            CancelAfterPage = cancellation
        };
        var scanner = Create(source, repository);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scanner.ScanAsync(cancellation.Token));

        Assert.Equal(DriveInventoryScanStatus.Cancelled, scanner.State.Status);
        Assert.Equal(previous.ScanId, repository.Latest!.ScanId);
        Assert.Contains(repository.Runs.Values, run => run.Status == DriveInventoryRunStatus.Cancelled && !run.IsComplete);
    }

    [Fact]
    public async Task ConcurrentScanIsRejectedWhileFirstScanRemainsCancellable()
    {
        var source = new FakeSource(new DriveInventoryPage([], null)) { Block = true };
        var scanner = Create(source, new MemoryRepository());
        using var cancellation = new CancellationTokenSource();
        var first = scanner.ScanAsync(cancellation.Token);
        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await Assert.ThrowsAsync<InvalidOperationException>(() => scanner.ScanAsync(CancellationToken.None));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
    }

    [Fact]
    public async Task RevokedSessionNeverCreatesSuccessfulSnapshot()
    {
        var authentication = new FakeAuthentication
        {
            Account = null,
            Exception = new ProviderOperationException(ProviderFailureCategory.AuthorizationRevoked, "revoked")
        };
        var repository = new MemoryRepository();
        var scanner = new DriveInventoryScanner(authentication, new FakeSource(new DriveInventoryPage([], null)), repository, new NullDiagnostics());

        var failure = await Assert.ThrowsAsync<ProviderOperationException>(() => scanner.ScanAsync(CancellationToken.None));

        Assert.Equal(ProviderFailureCategory.AuthorizationRevoked, failure.Category);
        Assert.Equal(DriveInventoryScanStatus.RequiresReauthentication, scanner.State.Status);
        Assert.Empty(repository.Runs);
    }

    [Fact]
    public async Task DatabaseFailureIsRecoverableAndKeepsPreviousSnapshot()
    {
        var previous = CompletedRun(Guid.NewGuid(), 5);
        var repository = new MemoryRepository { Latest = previous, FailAppend = true };
        var source = new FakeSource(new DriveInventoryPage([File("one", "Một.txt", "root", 1)], null));
        var scanner = Create(source, repository);

        await Assert.ThrowsAsync<ProviderOperationException>(() => scanner.ScanAsync(CancellationToken.None));

        Assert.Equal(DriveInventoryScanStatus.Failed, scanner.State.Status);
        Assert.Equal("LocalDatabaseWriteFailed", scanner.State.FailureCategory);
        Assert.Contains("cơ sở dữ liệu", scanner.State.VietnameseMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(previous.ScanId, repository.Latest!.ScanId);

        repository.FailAppend = false;
        source.Reset(new DriveInventoryPage([], null));
        var retry = await scanner.ScanAsync(CancellationToken.None);
        Assert.True(retry.IsComplete);
    }

    [Fact]
    public void HierarchyBuilderHandlesCyclesMissingParentsAndVeryDeepTreesIteratively()
    {
        var nodes = new List<DriveHierarchyNode>
        {
            new("cycle-a", "A", "cycle-b", false, true),
            new("cycle-b", "B", "cycle-a", false, true),
            new("orphan", "Mồ côi", "missing", false, true)
        };
        string parent = "root";
        for (var index = 0; index < 5_000; index++)
        {
            var id = $"deep-{index}";
            nodes.Add(new DriveHierarchyNode(id, $"Cấp {index}", parent, false, true));
            parent = id;
        }

        var result = new DriveHierarchyBuilder().Build(nodes);

        Assert.Equal(3, result.UnresolvedCount);
        Assert.StartsWith("Không xác định", result.Paths["cycle-a"].Path);
        Assert.StartsWith("Không xác định", result.Paths["orphan"].Path);
        Assert.EndsWith("Cấp 4999", result.Paths["deep-4999"].Path);
    }

    private static DriveInventoryScanner Create(FakeSource source, MemoryRepository repository) =>
        new(new FakeAuthentication(), source, repository, new NullDiagnostics());

    private static DriveInventoryRun CompletedRun(Guid id, int files) => new(
        id, "google-drive", "account", DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow,
        DriveInventoryRunStatus.Completed, files, 0, files, files, 0, 0, 0, 0, files, null, true, null);

    private static DriveInventoryItem File(string id, string name, string? parent, long? size, bool shared = false) => new(
        Guid.Empty, id, name, parent, "pending", "text/plain", DriveInventoryItemKind.File,
        shared ? DriveInventoryLocation.Shared : DriveInventoryLocation.MyDrive, size, null, null, null, "txt", null, null,
        shared, !shared, true, null);
    private static DriveInventoryItem Folder(string id, string name, string? parent) => new(
        Guid.Empty, id, name, parent, "pending", "application/vnd.google-apps.folder", DriveInventoryItemKind.Folder,
        DriveInventoryLocation.MyDrive, null, null, null, null, null, null, null, false, true, false, "folder");
    private static DriveInventoryItem Native(string id, string name, string? parent) => new(
        Guid.Empty, id, name, parent, "pending", "application/vnd.google-apps.document", DriveInventoryItemKind.GoogleWorkspaceFile,
        DriveInventoryLocation.MyDrive, null, null, null, null, null, null, null, false, true, true, null);
    private static DriveInventoryItem Shortcut(string id, string name, string? parent) => new(
        Guid.Empty, id, name, parent, "pending", "application/vnd.google-apps.shortcut", DriveInventoryItemKind.Shortcut,
        DriveInventoryLocation.MyDrive, null, null, null, null, null, "target", "text/plain", false, true, false, "shortcut");

    private sealed class FakeAuthentication : IProviderAuthenticationService
    {
        public StorageAccount? Account { get; set; } = new("google:current", "google-drive", "account", "Test", true, DateTimeOffset.UtcNow, "test@example.test");
        public Exception? Exception { get; set; }
        public string ProviderId => "google-drive";
        public bool IsConfigured => true;
        public string? ConfigurationMessage => null;
        public ProviderAuthenticationState State => new(Account is null ? ProviderAuthenticationStatus.Disconnected : ProviderAuthenticationStatus.Connected, "state", Account: Account);
        public event Action<ProviderAuthenticationState>? StateChanged { add { } remove { } }
        public event Action? ConfigurationChanged { add { } remove { } }
        public Task<StorageAccount?> GetCachedAccountAsync(CancellationToken cancellationToken) => Exception is null ? Task.FromResult(Account) : Task.FromException<StorageAccount?>(Exception);
        public Task<StorageAccount> ConnectAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DisconnectLocalAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeSource(params DriveInventoryPage[] pages) : IDriveInventorySource
    {
        private Queue<DriveInventoryPage> _pages = new(pages);
        public bool Block { get; set; }
        public CancellationTokenSource? CancelAfterPage { get; set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<DriveStorageInformation> GetStorageInformationAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new DriveStorageInformation(1000, 500, 400, 20));
        public async Task<DriveInventoryPage> GetInventoryPageAsync(Guid scanId, string providerAccountId, string? pageToken, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            if (Block) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_pages.Count == 0) throw new ProviderOperationException(ProviderFailureCategory.InvalidProviderResponse, "pagination");
            var page = _pages.Dequeue();
            CancelAfterPage?.Cancel();
            return page with { Items = page.Items.Select(item => item with { ScanId = scanId }).ToArray() };
        }
        public void Reset(params DriveInventoryPage[] replacement) { _pages = new Queue<DriveInventoryPage>(replacement); Block = false; CancelAfterPage = null; }
    }

    private sealed class MemoryRepository : IDriveInventoryRepository
    {
        public Dictionary<Guid, DriveInventoryRun> Runs { get; } = [];
        public Dictionary<string, DriveInventoryItem> Items { get; } = new(StringComparer.Ordinal);
        public DriveInventoryRun? Latest { get; set; }
        public int AppendCalls { get; private set; }
        public bool FailAppend { get; set; }
        public Task RecoverInterruptedAsync(DateTimeOffset interruptedAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task BeginAsync(DriveInventoryRun run, CancellationToken cancellationToken) { Runs[run.ScanId] = run; return Task.CompletedTask; }
        public Task AppendBatchAsync(Guid scanId, IReadOnlyList<DriveInventoryItem> items, CancellationToken cancellationToken)
        {
            if (FailAppend) throw new IOException("test database write failure");
            AppendCalls++;
            foreach (var item in items) Items[item.FileId] = item;
            return Task.CompletedTask;
        }
        public Task UpdateHierarchyAsync(Guid scanId, DriveHierarchyResult hierarchy, CancellationToken cancellationToken)
        {
            foreach (var entry in hierarchy.Paths)
                if (Items.TryGetValue(entry.Key, out var item)) Items[entry.Key] = item with { DisplayPath = entry.Value.Path, Location = entry.Value.Location };
            return Task.CompletedTask;
        }
        public Task CompleteAsync(DriveInventoryRun completedRun, CancellationToken cancellationToken) { Runs[completedRun.ScanId] = completedRun; Latest = completedRun; return Task.CompletedTask; }
        public Task MarkIncompleteAsync(Guid scanId, DriveInventoryRunStatus status, DateTimeOffset completedAtUtc, string? failureCategory, CancellationToken cancellationToken)
        {
            if (Runs.TryGetValue(scanId, out var run)) Runs[scanId] = run with { Status = status, CompletedAtUtc = completedAtUtc, FailureCategory = failureCategory, IsComplete = false };
            return Task.CompletedTask;
        }
        public Task<DriveInventoryRun?> GetLatestSuccessfulAsync(string providerAccountId, CancellationToken cancellationToken) => Task.FromResult(Latest);
        public Task<IReadOnlyList<DriveInventoryRun>> GetRecentAsync(int maximumCount, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DriveInventoryRun>>(Runs.Values.ToArray());
        public Task<IReadOnlyList<DriveInventoryItem>> GetItemsAsync(Guid scanId, int maximumCount, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DriveInventoryItem>>(Items.Values.ToArray());
        public Task<IReadOnlyList<DriveInventoryItem>> GetAllItemsAsync(Guid scanId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DriveInventoryItem>>(Items.Values.ToArray());
    }

    private sealed class NullDiagnostics : IProviderDiagnostics
    {
        public Task WriteAsync(string eventType, string vietnameseMessage, string? technicalDetails, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

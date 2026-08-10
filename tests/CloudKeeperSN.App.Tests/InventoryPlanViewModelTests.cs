using CloudKeeperSN.App.Development;
using CloudKeeperSN.App.Services;
using CloudKeeperSN.App.ViewModels;
using CloudKeeperSN.Application.Planning;
using CloudKeeperSN.Application.Storage;
using CloudKeeperSN.Domain.Planning;
using CloudKeeperSN.Domain.Scanning;
using CloudKeeperSN.Domain.Storage;

namespace CloudKeeperSN.App.Tests;

public sealed class InventoryPlanViewModelTests
{
    [Fact]
    public async Task LoadsLatestSnapshotAndSupportsFolderIncludeWithDescendantExclude()
    {
        var workspace = Workspace();
        var service = new FakePlanService(workspace);
        using var viewModel = new InventoryPlanViewModel(
            new DemoConfiguration(false, DemoScenarioKind.Standard), service, new ConnectedAuthentication(), null, InlineUiDispatcher.Instance);

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.True(viewModel.HasSnapshot);
        Assert.Equal(4, viewModel.SearchResults.Count);
        var folder = viewModel.SearchResults.Single(node => node.ItemId == "folder");
        folder.IsChecked = true;
        Assert.Equal(2, viewModel.SelectedItemCount);
        Assert.Equal(30, viewModel.KnownBytes);

        var excluded = viewModel.SearchResults.Single(node => node.ItemId == "two");
        excluded.IsChecked = false;
        Assert.Equal(1, viewModel.SelectedItemCount);
        Assert.Equal(10, viewModel.KnownBytes);
        Assert.True(viewModel.CanSave);

        viewModel.SaveCommand.Execute(null);
        await AsyncTest.UntilAsync(() => service.SaveCount == 1 && !viewModel.HasUnsavedChanges);
        Assert.Contains(service.Saved!.Rules, rule => rule.ItemId == "folder" && rule.Mode == BackupSelectionRuleMode.Include);
        Assert.Contains(service.Saved.Rules, rule => rule.ItemId == "two" && rule.Mode == BackupSelectionRuleMode.Exclude);
        Assert.Contains("chưa có tệp nào", viewModel.SaveMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchReviewAndSelectedFiltersUseIndexedMetadataOnly()
    {
        var service = new FakePlanService(Workspace());
        using var viewModel = new InventoryPlanViewModel(
            new DemoConfiguration(false, DemoScenarioKind.Standard), service, new ConnectedAuthentication());
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.SearchText = "日本語";
        Assert.Equal("one", Assert.Single(viewModel.SearchResults).ItemId);
        viewModel.SearchText = string.Empty;
        viewModel.SelectedFilter = viewModel.Filters.Single(filter => filter.Key == "review");

        Assert.Equal("shortcut", Assert.Single(viewModel.SearchResults).ItemId);
        Assert.Equal(1, viewModel.ReviewItemCount);
    }

    [Fact]
    public async Task NewSnapshotReconciliationIsVisibleAndDoesNotClaimBackup()
    {
        var workspace = Workspace() with
        {
            Reconciliation = new BackupPlanReconciliation(true, 5, 2, 1)
        };
        using var viewModel = new InventoryPlanViewModel(
            new DemoConfiguration(false, DemoScenarioKind.Standard), new FakePlanService(workspace), new ConnectedAuthentication());

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.True(viewModel.HasReconciliationMessage);
        Assert.Contains("5", viewModel.ReconciliationMessage);
        Assert.Contains("2", viewModel.ReconciliationMessage);
        Assert.DoesNotContain("đã sao lưu", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingSnapshotShowsSafeEmptyState()
    {
        using var viewModel = new InventoryPlanViewModel(
            new DemoConfiguration(false, DemoScenarioKind.Standard), new FakePlanService(null), new ConnectedAuthentication());

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.False(viewModel.HasSnapshot);
        Assert.True(viewModel.HasNoSnapshot);
        Assert.False(viewModel.SaveCommand.CanExecute(null));
        Assert.Contains("quét Google Drive", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingRuleCanBeRemovedWithoutTouchingProviderData()
    {
        var workspace = Workspace();
        var plan = workspace.Plan with
        {
            Rules = [new BackupSelectionRule("missing", BackupSelectionRuleMode.Include, DriveInventoryItemKind.File, "Đã xóa.txt")]
        };
        workspace = workspace with
        {
            Plan = plan,
            Evaluation = new BackupSelectionPlanner().Evaluate(plan, workspace.InventoryItems),
            Reconciliation = new BackupPlanReconciliation(true, 0, 0, 1)
        };
        var service = new FakePlanService(workspace);
        using var viewModel = new InventoryPlanViewModel(
            new DemoConfiguration(false, DemoScenarioKind.Standard), service, new ConnectedAuthentication());
        await viewModel.LoadAsync(CancellationToken.None);

        Assert.True(viewModel.HasMissingRules);
        viewModel.RemoveMissingRulesCommand.Execute(null);

        Assert.False(viewModel.HasMissingRules);
        Assert.True(viewModel.HasUnsavedChanges);
    }

    private static BackupPlanWorkspace Workspace()
    {
        var scanId = Guid.NewGuid();
        var run = new DriveInventoryRun(scanId, "google-drive", "account", DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow,
            DriveInventoryRunStatus.Completed, 4, 1, 2, 30, 1, 0, 1, 0, 2, null, true, null);
        var items = new[]
        {
            Item(scanId, "folder", "Tài liệu", "root", DriveInventoryItemKind.Folder, false, null),
            Item(scanId, "one", "日本語.txt", "folder", DriveInventoryItemKind.File, true, 10),
            Item(scanId, "two", "Hai.txt", "folder", DriveInventoryItemKind.File, true, 20),
            Item(scanId, "shortcut", "Lối tắt", "root", DriveInventoryItemKind.Shortcut, false, null)
        };
        var plan = new BackupSelectionPlan(Guid.NewGuid(), "account", "Kế hoạch", scanId,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, []);
        var evaluation = new BackupSelectionPlanner().Evaluate(plan, items);
        return new(run, plan, items, evaluation, new BackupPlanReconciliation(false, 0, 0, 0));
    }

    private static DriveInventoryItem Item(Guid scanId, string id, string name, string? parent,
        DriveInventoryItemKind kind, bool eligible, long? size) => new(
        scanId, id, name, parent, $"Drive của tôi/{name}", "text/plain", kind, DriveInventoryLocation.MyDrive,
        size, null, null, null, null, kind == DriveInventoryItemKind.Shortcut ? "one" : null, null,
        false, true, eligible, eligible || kind == DriveInventoryItemKind.Folder ? null : "Không tự động theo lối tắt");

    private sealed class FakePlanService(BackupPlanWorkspace? workspace) : IBackupSelectionPlanService
    {
        private readonly BackupSelectionPlanner _planner = new();
        public int SaveCount { get; private set; }
        public BackupSelectionPlan? Saved { get; private set; }
        public Task<BackupPlanWorkspace?> LoadAsync(string providerAccountId, CancellationToken cancellationToken) => Task.FromResult(workspace);
        public Task<BackupSelectionPlan> SaveAsync(BackupSelectionPlan plan, Guid latestScanId, CancellationToken cancellationToken)
        {
            SaveCount++;
            Saved = plan with { SourceScanId = latestScanId };
            return Task.FromResult(Saved);
        }
        public BackupSelectionEvaluation Evaluate(BackupSelectionPlan plan, IReadOnlyList<DriveInventoryItem> items) => _planner.Evaluate(plan, items);
    }

    private sealed class ConnectedAuthentication : IProviderAuthenticationService
    {
        private static readonly StorageAccount Account = new(
            "google:current", "google-drive", "account", "Nguyễn An", true, DateTimeOffset.UtcNow, "an@example.test");
        public string ProviderId => "google-drive";
        public bool IsConfigured => true;
        public string? ConfigurationMessage => null;
        public ProviderAuthenticationState State => new(ProviderAuthenticationStatus.Connected, "Đã kết nối", Account: Account);
        public event Action<ProviderAuthenticationState>? StateChanged { add { } remove { } }
        public event Action? ConfigurationChanged { add { } remove { } }
        public Task<StorageAccount?> GetCachedAccountAsync(CancellationToken cancellationToken) => Task.FromResult<StorageAccount?>(Account);
        public Task<StorageAccount> ConnectAsync(CancellationToken cancellationToken) => Task.FromResult(Account);
        public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DisconnectLocalAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

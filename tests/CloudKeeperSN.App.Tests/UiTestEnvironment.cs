using CloudKeeperSN.App.Development;
using CloudKeeperSN.App.ViewModels;
using CloudKeeperSN.App.Services;
using CloudKeeperSN.Providers.GoogleDrive.Fakes;
using CloudKeeperSN.Providers.OneDrive.Fakes;
using CloudKeeperSN.Application.Planning;
using CloudKeeperSN.Domain.Planning;
using CloudKeeperSN.Domain.Scanning;

namespace CloudKeeperSN.App.Tests;

internal sealed class UiTestEnvironment
{
    private UiTestEnvironment(DemoScenarioKind scenario, IDemoDelay delay)
    {
        AccountsRepository = new FakeStorageAccountRepository();
        SettingsRepository = new FakeSettingRepository();
        Configuration = new DemoConfiguration(true, scenario);
        Workspace = new DemoWorkspace();
        GoogleDrive = new FakeGoogleDriveProvider();
        OneDrive = new FakeOneDriveProvider();
        DemoData = new DemoDataService(Configuration, Workspace, GoogleDrive, OneDrive, AccountsRepository);
        Dialogs = new FakeDialogService();
        FolderPicker = new FakeFolderPickerService();
        DiagnosticExport = new FakeDiagnosticExportService();
        Theme = new FakeThemeService(SettingsRepository);
        LocalData = new FakeLocalDataService();
        Dashboard = new DashboardViewModel(DemoData);
        Accounts = new AccountsViewModel(DemoData, Dialogs);
        Backup = new BackupViewModel(DemoData, new DemoBackupPlanner(GoogleDrive), new DemoTransferEngine(Configuration, delay), FolderPicker, Dialogs);
        History = new HistoryViewModel(DemoData, DiagnosticExport);
        InventoryPlan = new InventoryPlanViewModel(Configuration, new EmptyPlanService());
        Settings = new SettingsViewModel(Theme, SettingsRepository, LocalData, DiagnosticExport, Workspace);
        Main = new MainWindowViewModel(Dashboard, Accounts, Backup, History, Settings, InventoryPlan) { IsDemoMode = true };
    }

    public FakeStorageAccountRepository AccountsRepository { get; }
    public FakeSettingRepository SettingsRepository { get; }
    public DemoConfiguration Configuration { get; }
    public DemoWorkspace Workspace { get; }
    public FakeGoogleDriveProvider GoogleDrive { get; }
    public FakeOneDriveProvider OneDrive { get; }
    public DemoDataService DemoData { get; }
    public FakeDialogService Dialogs { get; }
    public FakeFolderPickerService FolderPicker { get; }
    public FakeDiagnosticExportService DiagnosticExport { get; }
    public FakeThemeService Theme { get; }
    public FakeLocalDataService LocalData { get; }
    public DashboardViewModel Dashboard { get; }
    public AccountsViewModel Accounts { get; }
    public BackupViewModel Backup { get; }
    public HistoryViewModel History { get; }
    public InventoryPlanViewModel InventoryPlan { get; }
    public SettingsViewModel Settings { get; }
    public MainWindowViewModel Main { get; }

    public static async Task<UiTestEnvironment> CreateAsync(DemoScenarioKind scenario = DemoScenarioKind.Standard, IDemoDelay? delay = null)
    {
        var environment = new UiTestEnvironment(scenario, delay ?? new ImmediateDelay());
        await environment.DemoData.InitializeAsync(CancellationToken.None);
        await environment.Main.LoadAsync(CancellationToken.None);
        return environment;
    }

    public void QueueDefaultFolders()
    {
        FolderPicker.Enqueue(new FolderSelection("google-drive", DemoDataService.GoogleAccountId, DemoDataService.GoogleRootId, "Google Drive"));
        FolderPicker.Enqueue(new FolderSelection("one-drive", DemoDataService.MicrosoftAccountId, DemoDataService.OneDriveRootId, "OneDrive / CloudKeeperSN"));
    }

    private sealed class EmptyPlanService : IBackupSelectionPlanService
    {
        public Task<BackupPlanWorkspace?> LoadAsync(string providerAccountId, CancellationToken cancellationToken) => Task.FromResult<BackupPlanWorkspace?>(null);
        public Task<BackupSelectionPlan> SaveAsync(BackupSelectionPlan plan, Guid latestScanId, CancellationToken cancellationToken) => Task.FromResult(plan);
        public BackupSelectionEvaluation Evaluate(BackupSelectionPlan plan, IReadOnlyList<DriveInventoryItem> items) =>
            new(new Dictionary<string, InventorySelectionState>(), new BackupSelectionSummary(0, 0, 0, 0, 0, 0, 0));
    }
}

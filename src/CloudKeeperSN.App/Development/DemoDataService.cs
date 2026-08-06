using CloudKeeperSN.Application.Persistence;
using CloudKeeperSN.Domain.Export;
using CloudKeeperSN.Domain.Storage;
using CloudKeeperSN.Domain.Transfers;
using CloudKeeperSN.Providers.GoogleDrive.Fakes;
using CloudKeeperSN.Providers.OneDrive.Fakes;

namespace CloudKeeperSN.App.Development;

public sealed class DemoDataService(
    DemoConfiguration configuration,
    DemoWorkspace workspace,
    FakeGoogleDriveProvider googleDrive,
    FakeOneDriveProvider oneDrive,
    IStorageAccountRepository accounts)
{
    public const string GoogleAccountId = "demo-google";
    public const string MicrosoftAccountId = "demo-microsoft";
    public const string GoogleRootId = "google-root";
    public const string OneDriveRootId = "onedrive-root";

    private bool _seeded;

    public DemoConfiguration Configuration => configuration;
    public DemoWorkspace Workspace => workspace;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_seeded || !configuration.IsEnabled) return;
        _seeded = true;
        await accounts.RemoveAsync("google:" + GoogleAccountId, cancellationToken);
        await accounts.RemoveAsync("microsoft:" + MicrosoftAccountId, cancellationToken);

        if (configuration.Scenario != DemoScenarioKind.Disconnected)
        {
            await ConnectGoogleAsync(cancellationToken, delay: false);
        }

        if (configuration.Scenario is not (DemoScenarioKind.Disconnected or DemoScenarioKind.GoogleOnly))
        {
            await ConnectOneDriveAsync(cancellationToken, delay: false);
        }

        SeedProviderFolders();
        workspace.ReplaceRuns(CreateRuns(configuration.Scenario));
        workspace.SetBackupRunning(configuration.Scenario == DemoScenarioKind.LongRunning);
    }

    public async Task ConnectGoogleAsync(CancellationToken cancellationToken, bool delay = true)
    {
        EnsureDemoMode();
        if (delay) await Task.Delay(450, cancellationToken);
        googleDrive.Connect(GoogleAccountId, "Nguyễn Minh An");
        await accounts.UpsertAsync(
            new StorageAccount("google:" + GoogleAccountId, "google-drive", GoogleAccountId, "Nguyễn Minh An", true, DateTimeOffset.UtcNow),
            cancellationToken);
        workspace.SetBackupRunning(workspace.IsBackupRunning);
    }

    public async Task ConnectOneDriveAsync(CancellationToken cancellationToken, bool delay = true)
    {
        EnsureDemoMode();
        if (delay) await Task.Delay(450, cancellationToken);
        oneDrive.Connect(MicrosoftAccountId, "Nguyễn Minh An");
        await accounts.UpsertAsync(
            new StorageAccount("microsoft:" + MicrosoftAccountId, "one-drive", MicrosoftAccountId, "Nguyễn Minh An", true, DateTimeOffset.UtcNow),
            cancellationToken);
        workspace.SetBackupRunning(workspace.IsBackupRunning);
    }

    public async Task DisconnectAsync(string providerId, CancellationToken cancellationToken)
    {
        EnsureDemoMode();
        await Task.Delay(350, cancellationToken);
        if (providerId == "google-drive")
        {
            googleDrive.Disconnect();
            await accounts.RemoveAsync("google:" + GoogleAccountId, cancellationToken);
        }
        else if (providerId == "one-drive")
        {
            oneDrive.Disconnect();
            await accounts.RemoveAsync("microsoft:" + MicrosoftAccountId, cancellationToken);
        }
        workspace.SetBackupRunning(workspace.IsBackupRunning);
    }

    public Task<IReadOnlyList<StorageAccount>> GetAccountsAsync(CancellationToken cancellationToken) =>
        accounts.GetAllAsync(cancellationToken);

    private void EnsureDemoMode()
    {
        if (!configuration.IsEnabled)
        {
            throw new InvalidOperationException("Chế độ trình diễn chưa được bật.");
        }
    }

    private void SeedProviderFolders()
    {
        if (configuration.Scenario == DemoScenarioKind.ConnectedEmpty) return;

        googleDrive.AddItem(GoogleRootId, Folder("g-documents", GoogleRootId, "Tài liệu"));
        googleDrive.AddItem(GoogleRootId, Folder("g-photos", GoogleRootId, "Ảnh gia đình"));
        googleDrive.AddItem("g-documents", File("g-budget-a", "g-documents", "Ngân sách.xlsx", 82_400));
        googleDrive.AddItem("g-documents", File("g-budget-b", "g-documents", "Ngân sách.xlsx", 91_200));
        googleDrive.AddItem("g-documents", Native("g-doc", "g-documents", "Kế hoạch năm", GoogleNativeExportPolicy.GoogleDocument));
        googleDrive.AddItem("g-documents", Native("g-sheet", "g-documents", "Theo dõi chi tiêu", GoogleNativeExportPolicy.GoogleSpreadsheet));
        googleDrive.AddItem("g-documents", Native("g-slides", "g-documents", "Kỷ niệm", GoogleNativeExportPolicy.GooglePresentation));
        googleDrive.AddItem("g-documents", Native("g-form", "g-documents", "Biểu mẫu cũ", "application/vnd.google-apps.form"));
        googleDrive.AddItem("g-photos", File("g-photo", "g-photos", "Gia đình.jpg", 3_840_000));
    }

    private static IEnumerable<DemoBackupRun> CreateRuns(DemoScenarioKind scenario)
    {
        var now = new DateTimeOffset(2026, 8, 5, 9, 30, 0, TimeSpan.FromHours(7));
        if (scenario is DemoScenarioKind.Disconnected or DemoScenarioKind.GoogleOnly or DemoScenarioKind.ConnectedEmpty) return [];

        var completed = new DemoBackupRun(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            "Sao lưu Tài liệu",
            "Google Drive / Tài liệu",
            "OneDrive / CloudKeeperSN",
            now,
            TimeSpan.FromMinutes(8),
            DemoRunStatus.Completed,
            42,
            3,
            0,
            0,
            184_300_000,
            VerificationLevel.VerifiedByProviderHash,
            ["Đã bắt đầu quét thư mục nguồn", "Đã tạo thư mục trên OneDrive", "Đã sao lưu và xác minh 42 tệp"]);

        var warning = new DemoBackupRun(
            Guid.Parse("10000000-0000-0000-0000-000000000002"),
            "Sao lưu Ảnh gia đình",
            "Google Drive / Ảnh gia đình",
            "OneDrive / CloudKeeperSN / Ảnh",
            now.AddDays(-3),
            TimeSpan.FromMinutes(17),
            DemoRunStatus.CompletedWithWarnings,
            116,
            5,
            2,
            0,
            1_284_000_000,
            VerificationLevel.VerifiedBySizeAndMetadata,
            ["Đã bắt đầu quét thư mục nguồn", "Đã bỏ qua vì tệp không thay đổi", "Đã hoàn tất với 2 cảnh báo"]);

        return scenario switch
        {
            DemoScenarioKind.CompletedSuccessfully => [completed],
            DemoScenarioKind.CompletedWithWarnings => [warning],
            DemoScenarioKind.RetryAndFailure =>
            [
                warning with
                {
                    Id = Guid.Parse("10000000-0000-0000-0000-000000000003"),
                    Status = DemoRunStatus.Failed,
                    CompletedFiles = 18,
                    FailedCount = 1,
                    Verification = VerificationLevel.VerificationFailed,
                    Timeline = ["Đã bắt đầu quét thư mục nguồn", "Đang thử lại sau khi kết nối bị gián đoạn", "Một tệp chưa thể sao lưu"]
                },
                completed
            ],
            _ => [completed, warning]
        };
    }

    private static StorageItem Folder(string id, string parentId, string name) => new()
    {
        ProviderId = "google-drive",
        ProviderAccountId = GoogleAccountId,
        ItemId = id,
        ParentItemId = parentId,
        Name = name,
        Kind = StorageItemKind.Folder
    };

    private static StorageItem File(string id, string parentId, string name, long size) => new()
    {
        ProviderId = "google-drive",
        ProviderAccountId = GoogleAccountId,
        ItemId = id,
        ParentItemId = parentId,
        Name = name,
        Kind = StorageItemKind.File,
        Size = size,
        MimeType = "application/octet-stream"
    };

    private static StorageItem Native(string id, string parentId, string name, string mimeType) => new()
    {
        ProviderId = "google-drive",
        ProviderAccountId = GoogleAccountId,
        ItemId = id,
        ParentItemId = parentId,
        Name = name,
        Kind = StorageItemKind.ProviderNativeFile,
        MimeType = mimeType
    };
}


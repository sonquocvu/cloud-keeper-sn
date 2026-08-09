using System.Text;
using CloudKeeperSN.App.Development;
using CloudKeeperSN.App.Services;
using CloudKeeperSN.App.ViewModels;
using CloudKeeperSN.Application.Storage;
using CloudKeeperSN.Domain.Storage;
using CloudKeeperSN.Providers.GoogleDrive;
using CloudKeeperSN.Providers.GoogleDrive.Fakes;
using CloudKeeperSN.Providers.OneDrive.Fakes;

namespace CloudKeeperSN.App.Tests;

public sealed class GoogleOAuthSettingsTests
{
    [Fact]
    public async Task PickerCancellationDoesNotChangeConfigurationOrShowError()
    {
        var manager = new FakeManager();
        var picker = new FakePicker(null);
        using var viewModel = Create(manager, picker: picker);

        viewModel.ImportGoogleOAuthCommand.Execute(null);
        await AsyncTest.UntilAsync(() => picker.Calls == 1 && !((AsyncRelayCommand)viewModel.ImportGoogleOAuthCommand).IsRunning);

        Assert.Equal(GoogleOAuthConfigurationStatus.Missing, manager.Current.Status);
        Assert.Equal(0, manager.ValidateCalls);
        Assert.Empty(viewModel.GoogleOAuthErrorMessage);
    }

    [Fact]
    public async Task SuccessfulImportUpdatesSettingsImmediatelyWithoutExposingSecret()
    {
        var manager = new FakeManager();
        using var viewModel = Create(manager);

        viewModel.ImportGoogleOAuthCommand.Execute(null);
        await AsyncTest.UntilAsync(() => viewModel.GoogleOAuthMetadata?.Status == GoogleOAuthConfigurationStatus.Ready);

        Assert.Equal("Đã cấu hình", viewModel.GoogleOAuthStatus.Text);
        Assert.Equal("Đã nhập từ Cài đặt", viewModel.GoogleOAuthSource);
        Assert.Equal("1234••••••••.apps.googleusercontent.com", viewModel.GoogleOAuthMaskedClientId);
        Assert.Contains("Bạn có thể kết nối", viewModel.GoogleOAuthActionMessage);
        Assert.DoesNotContain("test-secret-value", PublicDisplayText(viewModel));
    }

    [Fact]
    public async Task InvalidImportKeepsPreviousConfiguration()
    {
        var manager = new FakeManager(ReadyMetadata) { ValidationFailure = new GoogleOAuthImportException(
            GoogleOAuthImportError.MalformedJson,
            "File đã chọn không phải JSON hợp lệ. Cấu hình hiện tại vẫn được giữ; hãy tải lại file OAuth Desktop.") };
        using var viewModel = Create(manager);

        viewModel.ImportGoogleOAuthCommand.Execute(null);
        await AsyncTest.UntilAsync(() => !string.IsNullOrEmpty(viewModel.GoogleOAuthErrorMessage));

        Assert.Equal(0, manager.ImportCalls);
        Assert.Equal(ReadyMetadata, manager.Current);
        Assert.Contains("vẫn được giữ", viewModel.GoogleOAuthErrorMessage);
    }

    [Fact]
    public async Task ReplacementRequiresConfirmationAndCancellationKeepsCurrentClient()
    {
        var manager = new FakeManager(ReadyMetadata);
        var dialogs = new FakeDialogService { ConfirmationResult = false };
        var authentication = new FakeAuthentication();
        using var viewModel = Create(manager, authentication: authentication, dialogs: dialogs);

        viewModel.ImportGoogleOAuthCommand.Execute(null);
        await AsyncTest.UntilAsync(() => dialogs.Requests.Count == 1 && !((AsyncRelayCommand)viewModel.ImportGoogleOAuthCommand).IsRunning);

        Assert.Equal(0, manager.ImportCalls);
        Assert.Equal(0, authentication.DisconnectCalls);
        Assert.Contains("Đã hủy thay đổi", viewModel.GoogleOAuthActionMessage);
    }

    [Fact]
    public async Task ApprovedReplacementDisconnectsOldClientBeforePersistingNewOne()
    {
        var sequence = new List<string>();
        var manager = new FakeManager(ReadyMetadata, sequence);
        var authentication = new FakeAuthentication(sequence);
        using var viewModel = Create(manager, authentication: authentication);

        viewModel.ImportGoogleOAuthCommand.Execute(null);
        await AsyncTest.UntilAsync(() => manager.ImportCalls == 1);

        Assert.Equal(["disconnect", "import"], sequence);
        Assert.Equal(1, authentication.DisconnectCalls);
        Assert.Equal(0, authentication.RemoteDisconnectCalls);
        Assert.Equal(GoogleOAuthConfigurationStatus.Ready, manager.Current.Status);
    }

    [Fact]
    public async Task SecurePersistenceFailureIsSafeAndRetryable()
    {
        var manager = new FakeManager { ImportFailure = new GoogleOAuthImportException(
            GoogleOAuthImportError.SecureStorageFailed,
            "Không thể lưu cấu hình OAuth bằng bộ nhớ bảo vệ. Cấu hình hiện tại vẫn được giữ; hãy thử lại.") };
        using var viewModel = Create(manager);

        viewModel.ImportGoogleOAuthCommand.Execute(null);
        await AsyncTest.UntilAsync(() => !string.IsNullOrEmpty(viewModel.GoogleOAuthErrorMessage));

        Assert.Equal(GoogleOAuthConfigurationStatus.Missing, manager.Current.Status);
        Assert.True(viewModel.ImportGoogleOAuthCommand.CanExecute(null));
        Assert.Contains("vẫn được giữ", viewModel.GoogleOAuthErrorMessage);
    }

    [Fact]
    public async Task RemovalRequiresConfirmationDisconnectsAndDisablesRemoval()
    {
        var sequence = new List<string>();
        var manager = new FakeManager(ReadyMetadata, sequence);
        var authentication = new FakeAuthentication(sequence);
        using var viewModel = Create(manager, authentication: authentication);

        viewModel.RemoveGoogleOAuthCommand.Execute(null);
        await AsyncTest.UntilAsync(() => manager.RemoveCalls == 1);

        Assert.Equal(["disconnect", "remove"], sequence);
        Assert.Equal(GoogleOAuthConfigurationStatus.Missing, manager.Current.Status);
        Assert.Equal(0, authentication.RemoteDisconnectCalls);
        Assert.False(viewModel.CanRemoveGoogleOAuth);
        Assert.Contains("ngắt kết nối cục bộ", viewModel.GoogleOAuthActionMessage);
    }

    [Fact]
    public async Task BusyAuthenticationPreventsConfigurationReplacement()
    {
        var manager = new FakeManager(ReadyMetadata);
        var authentication = new FakeAuthentication
        {
            State = new ProviderAuthenticationState(ProviderAuthenticationStatus.OpeningBrowser, "Đang mở trình duyệt")
        };
        var picker = new FakePicker("client.json");
        using var viewModel = Create(manager, authentication: authentication, picker: picker);

        viewModel.ImportGoogleOAuthCommand.Execute(null);
        await AsyncTest.UntilAsync(() => !string.IsNullOrEmpty(viewModel.GoogleOAuthErrorMessage));

        Assert.Equal(0, picker.Calls);
        Assert.Equal(0, manager.ImportCalls);
        Assert.Contains("Đăng nhập Google đang diễn ra", viewModel.GoogleOAuthErrorMessage);
    }

    [Fact]
    public async Task HelpActionShowsDesktopJsonInstructions()
    {
        var dialogs = new FakeDialogService();
        using var viewModel = Create(new FakeManager(), dialogs: dialogs);

        viewModel.ShowGoogleOAuthGuideCommand.Execute(null);
        await AsyncTest.UntilAsync(() => dialogs.InformationRequests.Count == 1);

        Assert.Contains("Desktop app", dialogs.InformationRequests[0].Message);
        Assert.Contains("Test users", dialogs.InformationRequests[0].Message);
        Assert.DoesNotContain("Client Secret", dialogs.InformationRequests[0].Message);
    }

    private static SettingsViewModel Create(
        FakeManager manager,
        FakeAuthentication? authentication = null,
        FakePicker? picker = null,
        FakeDialogService? dialogs = null)
    {
        var settings = new FakeSettingRepository();
        return new SettingsViewModel(
            new FakeThemeService(settings),
            settings,
            new FakeLocalDataService(),
            new FakeDiagnosticExportService(),
            new DemoWorkspace(),
            manager,
            authentication ?? new FakeAuthentication(),
            picker ?? new FakePicker("client.json"),
            dialogs ?? new FakeDialogService());
    }

    private static string PublicDisplayText(SettingsViewModel viewModel) => string.Join('|', new[]
    {
        viewModel.GoogleOAuthStatus.Text,
        viewModel.GoogleOAuthSource,
        viewModel.GoogleOAuthMaskedClientId,
        viewModel.GoogleOAuthImportedAt,
        viewModel.GoogleOAuthValidationMessage,
        viewModel.GoogleOAuthActionMessage,
        viewModel.GoogleOAuthErrorMessage
    });

    private static readonly GoogleOAuthConfigurationMetadata MissingMetadata = new(
        GoogleOAuthConfigurationStatus.Missing,
        "Chưa cấu hình",
        "Chưa cấu hình",
        string.Empty,
        null,
        "Chưa cấu hình OAuth.",
        false);

    private static readonly GoogleOAuthConfigurationMetadata ReadyMetadata = new(
        GoogleOAuthConfigurationStatus.Ready,
        "Đã cấu hình",
        "Đã nhập từ Cài đặt",
        "1234••••••••.apps.googleusercontent.com",
        new DateTimeOffset(2026, 8, 9, 10, 30, 0, TimeSpan.Zero),
        "OAuth đã sẵn sàng.",
        true);

    private sealed class FakePicker(string? path) : IGoogleOAuthFilePickerService
    {
        public int Calls { get; private set; }
        public Task<string?> PickAsync(CancellationToken cancellationToken) { Calls++; return Task.FromResult(path); }
    }

    private sealed class FakeManager : IGoogleOAuthConfigurationManager
    {
        private readonly List<string>? _sequence;
        public FakeManager(GoogleOAuthConfigurationMetadata? current = null, List<string>? sequence = null)
        {
            Current = current ?? MissingMetadata;
            _sequence = sequence;
        }
        public GoogleOAuthConfigurationMetadata Current { get; private set; }
        public int ValidateCalls { get; private set; }
        public int ImportCalls { get; private set; }
        public int RemoveCalls { get; private set; }
        public Exception? ValidationFailure { get; set; }
        public Exception? ImportFailure { get; set; }
        public event Action<GoogleOAuthConfigurationMetadata>? Changed;
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<GoogleOAuthImportCandidate> ValidateImportAsync(string sourcePath, CancellationToken cancellationToken)
        {
            ValidateCalls++;
            if (ValidationFailure is not null) return Task.FromException<GoogleOAuthImportCandidate>(ValidationFailure);
            const string json = """
                {"installed":{"client_id":"123456789-example.apps.googleusercontent.com","client_secret":"test-secret-value","auth_uri":"https://accounts.google.com/o/oauth2/auth","token_uri":"https://oauth2.googleapis.com/token","redirect_uris":["http://localhost"]}}
                """;
            return Task.FromResult(new GoogleDesktopOAuthJsonParser().Parse(Encoding.UTF8.GetBytes(json)));
        }
        public Task ImportAsync(GoogleOAuthImportCandidate candidate, CancellationToken cancellationToken)
        {
            ImportCalls++;
            if (ImportFailure is not null) return Task.FromException(ImportFailure);
            _sequence?.Add("import");
            Current = ReadyMetadata;
            Changed?.Invoke(Current);
            return Task.CompletedTask;
        }
        public Task RemoveImportedAsync(CancellationToken cancellationToken)
        {
            RemoveCalls++;
            _sequence?.Add("remove");
            Current = MissingMetadata;
            Changed?.Invoke(Current);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAuthentication(List<string>? sequence = null) : IProviderAuthenticationService
    {
        public int DisconnectCalls { get; private set; }
        public int RemoteDisconnectCalls { get; private set; }
        public string ProviderId => "google-drive";
        public bool IsConfigured => true;
        public string? ConfigurationMessage => "OAuth đã sẵn sàng";
        public ProviderAuthenticationState State { get; set; } = new(ProviderAuthenticationStatus.Disconnected, "Chưa kết nối");
        public event Action<ProviderAuthenticationState>? StateChanged { add { } remove { } }
        public event Action? ConfigurationChanged { add { } remove { } }
        public Task<StorageAccount?> GetCachedAccountAsync(CancellationToken cancellationToken) => Task.FromResult<StorageAccount?>(null);
        public Task<StorageAccount> ConnectAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            RemoteDisconnectCalls++;
            return Task.CompletedTask;
        }
        public Task DisconnectLocalAsync(CancellationToken cancellationToken)
        {
            DisconnectCalls++;
            sequence?.Add("disconnect");
            State = new ProviderAuthenticationState(ProviderAuthenticationStatus.Disconnected, "Chưa kết nối");
            return Task.CompletedTask;
        }
    }
}

using System.Reflection;
using System.Windows.Input;
using CloudKeeperSN.App.Services;
using CloudKeeperSN.App.Presentation;
using CloudKeeperSN.App.UI.Theming;
using CloudKeeperSN.Application.Persistence;
using CloudKeeperSN.App.Development;
using CloudKeeperSN.Application.Storage;
using CloudKeeperSN.Providers.GoogleDrive;

namespace CloudKeeperSN.App.ViewModels;

public sealed class SettingsViewModel : PageViewModel, IDisposable
{
    private readonly IThemeService _themeService;
    private readonly IApplicationSettingRepository _settings;
    private readonly ILocalDataService _localData;
    private readonly IDiagnosticExportService _diagnosticExport;
    private readonly DemoWorkspace _workspace;
    private readonly IGoogleOAuthConfigurationManager? _googleOAuth;
    private readonly IProviderAuthenticationService? _googleAuthentication;
    private readonly IGoogleOAuthFilePickerService? _googleOAuthFilePicker;
    private readonly IDialogService? _dialogs;
    private ThemeOption _selectedTheme;
    private string _concurrentTransfers = "2";
    private string _cacheLimitMiB = "1024";
    private string _retryAttempts = "6";
    private string _validationMessage = string.Empty;
    private string _actionMessage = string.Empty;
    private GoogleOAuthConfigurationMetadata? _googleOAuthMetadata;
    private string _googleOAuthActionMessage = string.Empty;
    private string _googleOAuthErrorMessage = string.Empty;

    public SettingsViewModel(
        IThemeService themeService,
        IApplicationSettingRepository settings,
        ILocalDataService localData,
        IDiagnosticExportService diagnosticExport,
        DemoWorkspace workspace,
        IGoogleOAuthConfigurationManager? googleOAuth = null,
        IProviderAuthenticationService? googleAuthentication = null,
        IGoogleOAuthFilePickerService? googleOAuthFilePicker = null,
        IDialogService? dialogs = null)
        : base("settings", "Cài đặt", "Điều chỉnh giao diện, truyền dữ liệu và dữ liệu cục bộ.")
    {
        _themeService = themeService;
        _settings = settings;
        _localData = localData;
        _diagnosticExport = diagnosticExport;
        _workspace = workspace;
        _googleOAuth = googleOAuth;
        _googleAuthentication = googleAuthentication;
        _googleOAuthFilePicker = googleOAuthFilePicker;
        _dialogs = dialogs;
        _googleOAuthMetadata = googleOAuth?.Current;
        if (_googleOAuth is not null) _googleOAuth.Changed += GoogleOAuthConfigurationChanged;
        ThemeOptions = [new(ThemeMode.System, "Theo cài đặt Windows"), new(ThemeMode.Light, "Giao diện sáng"), new(ThemeMode.Dark, "Giao diện tối")];
        _selectedTheme = ThemeOptions[0];
        SaveTransferSettingsCommand = new AsyncRelayCommand(SaveTransferSettingsAsync, () => string.IsNullOrEmpty(Validate()));
        ClearCacheCommand = new AsyncRelayCommand(ClearCacheAsync);
        ExportDiagnosticsCommand = new AsyncRelayCommand(ExportDiagnosticsAsync);
        ImportGoogleOAuthCommand = new AsyncRelayCommand(ImportGoogleOAuthAsync, () => IsGoogleOAuthAvailable);
        RemoveGoogleOAuthCommand = new AsyncRelayCommand(RemoveGoogleOAuthAsync, () => IsGoogleOAuthAvailable && CanRemoveGoogleOAuth);
        ShowGoogleOAuthGuideCommand = new AsyncRelayCommand(ShowGoogleOAuthGuideAsync, () => IsGoogleOAuthAvailable);
    }

    public IReadOnlyList<ThemeOption> ThemeOptions { get; }
    public ICommand SaveTransferSettingsCommand { get; }
    public ICommand ClearCacheCommand { get; }
    public ICommand ExportDiagnosticsCommand { get; }
    public ICommand ImportGoogleOAuthCommand { get; }
    public ICommand RemoveGoogleOAuthCommand { get; }
    public ICommand ShowGoogleOAuthGuideCommand { get; }
    public string DatabasePath => _localData.DatabasePath;
    public string LogPath => _localData.LogPath;
    public string CachePath => _localData.CachePath;
    public string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.2.0";
    public string BuildInformation => ".NET 10 • WPF • Windows 10/11";
    public bool IsGoogleOAuthAvailable => _googleOAuth is not null && _googleAuthentication is not null && _googleOAuthFilePicker is not null && _dialogs is not null;
    public GoogleOAuthConfigurationMetadata? GoogleOAuthMetadata { get => _googleOAuthMetadata; private set => SetProperty(ref _googleOAuthMetadata, value); }
    public StatusPresentation GoogleOAuthStatus => GoogleOAuthMetadata?.Status switch
    {
        GoogleOAuthConfigurationStatus.Ready => new("Đã cấu hình", StatusTone.Success, string.Empty),
        GoogleOAuthConfigurationStatus.Invalid => new("Cấu hình không hợp lệ", StatusTone.Error, string.Empty),
        _ => new("Chưa cấu hình", StatusTone.Neutral, string.Empty)
    };
    public string GoogleOAuthPrimaryActionText => GoogleOAuthMetadata?.Status == GoogleOAuthConfigurationStatus.Ready
        ? "Thay đổi file OAuth"
        : "Chọn file OAuth JSON";
    public bool HasGoogleOAuthMetadata => GoogleOAuthMetadata?.Status == GoogleOAuthConfigurationStatus.Ready;
    public bool CanRemoveGoogleOAuth => GoogleOAuthMetadata?.CanRemoveImportedConfiguration == true;
    public string GoogleOAuthApplicationType => "Ứng dụng máy tính";
    public string GoogleOAuthMaskedClientId => GoogleOAuthMetadata?.MaskedClientId ?? string.Empty;
    public string GoogleOAuthImportedAt => GoogleOAuthMetadata?.ImportedAtUtc is { } value
        ? value.ToLocalTime().ToString("dd/MM/yyyy HH:mm")
        : "Không áp dụng";
    public string GoogleOAuthSource => GoogleOAuthMetadata?.SourceLabel ?? "Chưa cấu hình";
    public string GoogleOAuthValidationMessage => GoogleOAuthMetadata?.ValidationMessage ?? string.Empty;
    public string GoogleOAuthActionMessage { get => _googleOAuthActionMessage; private set => SetProperty(ref _googleOAuthActionMessage, value); }
    public string GoogleOAuthErrorMessage { get => _googleOAuthErrorMessage; private set => SetProperty(ref _googleOAuthErrorMessage, value); }

    public ThemeOption SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (!SetProperty(ref _selectedTheme, value)) return;
            _ = ChangeThemeAsync(value, CancellationToken.None);
        }
    }

    public string ConcurrentTransfers { get => _concurrentTransfers; set { if (SetProperty(ref _concurrentTransfers, value)) ValidateAndNotify(); } }
    public string CacheLimitMiB { get => _cacheLimitMiB; set { if (SetProperty(ref _cacheLimitMiB, value)) ValidateAndNotify(); } }
    public string RetryAttempts { get => _retryAttempts; set { if (SetProperty(ref _retryAttempts, value)) ValidateAndNotify(); } }
    public string ValidationMessage { get => _validationMessage; private set => SetProperty(ref _validationMessage, value); }
    public string ActionMessage { get => _actionMessage; private set => SetProperty(ref _actionMessage, value); }

    public override async Task LoadAsync(CancellationToken cancellationToken)
    {
        SelectedTheme = ThemeOptions.First(option => option.Value == _themeService.CurrentMode);
        ConcurrentTransfers = await _settings.GetAsync("transfer.concurrent", cancellationToken) ?? "2";
        CacheLimitMiB = await _settings.GetAsync("transfer.cache-mib", cancellationToken) ?? "1024";
        RetryAttempts = await _settings.GetAsync("transfer.retry-attempts", cancellationToken) ?? "6";
        RefreshGoogleOAuthMetadata();
        ValidateAndNotify();
    }

    public async Task ChangeThemeAsync(ThemeOption option, CancellationToken cancellationToken)
    {
        try
        {
            await _themeService.ApplyAsync(option.Value, cancellationToken);
            ActionMessage = $"Đã áp dụng {option.VietnameseLabel.ToLowerInvariant()}.";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ActionMessage = "Không thể đổi giao diện lúc này. Cài đặt trước đó vẫn được giữ lại.";
        }
    }

    public string Validate()
    {
        if (!int.TryParse(ConcurrentTransfers, out var concurrent) || concurrent is < 1 or > 8) return "Số tệp truyền đồng thời phải từ 1 đến 8.";
        if (!int.TryParse(CacheLimitMiB, out var cache) || cache is < 128 or > 8192) return "Giới hạn lưu tạm phải từ 128 đến 8192 MB.";
        if (!int.TryParse(RetryAttempts, out var retries) || retries is < 1 or > 10) return "Số lần thử lại phải từ 1 đến 10.";
        return string.Empty;
    }

    private async Task SaveTransferSettingsAsync(CancellationToken cancellationToken)
    {
        var validation = Validate();
        if (!string.IsNullOrEmpty(validation)) { ValidationMessage = validation; return; }
        await _settings.SetAsync("transfer.concurrent", ConcurrentTransfers, cancellationToken);
        await _settings.SetAsync("transfer.cache-mib", CacheLimitMiB, cancellationToken);
        await _settings.SetAsync("transfer.retry-attempts", RetryAttempts, cancellationToken);
        ActionMessage = "Đã lưu cài đặt truyền dữ liệu.";
    }

    private async Task ClearCacheAsync(CancellationToken cancellationToken)
    {
        await _localData.ClearCacheAsync(cancellationToken);
        ActionMessage = "Đã dọn bộ nhớ tạm của CloudKeeperSN. Dữ liệu đám mây không bị thay đổi.";
    }

    private async Task ExportDiagnosticsAsync(CancellationToken cancellationToken)
    {
        var path = await _diagnosticExport.ExportAsync(_workspace.Runs, cancellationToken);
        ActionMessage = path is null ? string.Empty : $"Đã xuất thông tin chẩn đoán đến {path}";
    }

    private async Task ImportGoogleOAuthAsync(CancellationToken cancellationToken)
    {
        if (!IsGoogleOAuthAvailable) return;
        GoogleOAuthActionMessage = string.Empty;
        GoogleOAuthErrorMessage = string.Empty;
        try
        {
            if (IsAuthenticationBusy())
            {
                GoogleOAuthErrorMessage = "Đăng nhập Google đang diễn ra. Cấu hình hiện tại vẫn được giữ; hãy hủy hoặc hoàn tất đăng nhập trước khi thay đổi OAuth.";
                return;
            }

            var path = await _googleOAuthFilePicker!.PickAsync(cancellationToken);
            if (path is null) return;
            var candidate = await _googleOAuth!.ValidateImportAsync(path, cancellationToken);

            var replacing = GoogleOAuthMetadata?.Status is GoogleOAuthConfigurationStatus.Ready or GoogleOAuthConfigurationStatus.Invalid;
            if (replacing)
            {
                var confirmed = await _dialogs!.ConfirmAsync(new ConfirmationRequest(
                    "Thay đổi cấu hình Google OAuth?",
                    "Cấu hình mới sẽ thay thế cấu hình hiện tại. Tài khoản Google Drive đang kết nối sẽ bị ngắt cục bộ và authorization cache của OAuth client trước sẽ bị xóa.",
                    "_Thay đổi file OAuth",
                    SupportingText: "Không có dữ liệu Google Drive nào bị thay đổi. Bạn có thể kết nối lại bằng cấu hình mới."), cancellationToken);
                if (!confirmed)
                {
                    GoogleOAuthActionMessage = "Đã hủy thay đổi cấu hình. Cấu hình OAuth hiện tại vẫn được giữ.";
                    return;
                }
                await DisconnectForConfigurationChangeAsync(cancellationToken);
            }

            await _googleOAuth.ImportAsync(candidate, cancellationToken);
            GoogleOAuthActionMessage = "Đã nhập cấu hình Google OAuth. Bạn có thể kết nối tài khoản Google Drive ngay bây giờ.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (GoogleOAuthImportException exception)
        {
            GoogleOAuthErrorMessage = exception.Message;
        }
        catch (Exception)
        {
            GoogleOAuthErrorMessage = "Không thể nhập cấu hình Google OAuth. Cấu hình hiện tại vẫn được giữ; hãy kiểm tra file rồi thử lại.";
        }
    }

    private async Task RemoveGoogleOAuthAsync(CancellationToken cancellationToken)
    {
        if (!IsGoogleOAuthAvailable || !CanRemoveGoogleOAuth) return;
        GoogleOAuthActionMessage = string.Empty;
        GoogleOAuthErrorMessage = string.Empty;
        try
        {
            if (IsAuthenticationBusy())
            {
                GoogleOAuthErrorMessage = "Đăng nhập Google đang diễn ra. Hãy hủy hoặc hoàn tất đăng nhập trước khi xóa cấu hình OAuth.";
                return;
            }
            var confirmed = await _dialogs!.ConfirmAsync(new ConfirmationRequest(
                "Xóa cấu hình Google OAuth?",
                "Bạn có muốn xóa cấu hình Google OAuth khỏi máy này không? Tài khoản Google Drive đang kết nối sẽ bị ngắt khỏi CloudKeeperSN, nhưng dữ liệu trên Google Drive sẽ không bị thay đổi.",
                "_Xóa cấu hình",
                IsDangerous: true,
                SupportingText: "Authorization cache cục bộ sẽ bị xóa. Lịch sử quét và sao lưu vẫn được giữ lại."), cancellationToken);
            if (!confirmed) return;

            await DisconnectForConfigurationChangeAsync(cancellationToken);
            await _googleOAuth!.RemoveImportedAsync(cancellationToken);
            GoogleOAuthActionMessage = GoogleOAuthMetadata?.Status == GoogleOAuthConfigurationStatus.Ready
                ? "Đã xóa cấu hình đã nhập. Cấu hình môi trường phát triển hiện đang được sử dụng."
                : "Đã xóa cấu hình Google OAuth khỏi máy này và ngắt kết nối cục bộ.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (GoogleOAuthImportException exception)
        {
            GoogleOAuthErrorMessage = exception.Message;
        }
        catch (Exception)
        {
            GoogleOAuthErrorMessage = "Không thể xóa cấu hình Google OAuth. Cấu hình hiện tại chưa thay đổi; hãy thử lại.";
        }
    }

    private async Task ShowGoogleOAuthGuideAsync(CancellationToken cancellationToken)
    {
        if (!IsGoogleOAuthAvailable) return;
        await _dialogs!.ShowInformationAsync(
            "Hướng dẫn lấy file OAuth",
            "1. Mở Google Cloud Console và tạo hoặc chọn project.\n" +
            "2. Bật Google Drive API.\n" +
            "3. Cấu hình OAuth consent screen và audience.\n" +
            "4. Nếu ứng dụng đang Testing, thêm tài khoản Google vào Test users.\n" +
            "5. Tạo OAuth Client ID với loại Desktop app.\n" +
            "6. Tải file OAuth JSON.\n" +
            "7. Trở lại CloudKeeperSN > Cài đặt > Kết nối Google Drive.\n" +
            "8. Chọn file OAuth JSON vừa tải.\n" +
            "9. Mở Tài khoản và nhấn Kết nối Google Drive.",
            cancellationToken);
    }

    private async Task DisconnectForConfigurationChangeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _googleAuthentication!.DisconnectLocalAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            // GoogleAuthenticationService always clears the local account/token in its finally block.
            // Configuration changes intentionally do not revoke remote Google access automatically.
        }
    }

    private bool IsAuthenticationBusy() => _googleAuthentication?.State.Status is
        ProviderAuthenticationStatus.OpeningBrowser or
        ProviderAuthenticationStatus.CompletingConnection or
        ProviderAuthenticationStatus.Disconnecting;

    private void GoogleOAuthConfigurationChanged(GoogleOAuthConfigurationMetadata metadata)
    {
        GoogleOAuthMetadata = metadata;
        NotifyGoogleOAuthProperties();
    }

    private void RefreshGoogleOAuthMetadata()
    {
        if (_googleOAuth is not null) GoogleOAuthMetadata = _googleOAuth.Current;
        NotifyGoogleOAuthProperties();
    }

    private void NotifyGoogleOAuthProperties()
    {
        OnPropertyChanged(nameof(GoogleOAuthStatus));
        OnPropertyChanged(nameof(GoogleOAuthPrimaryActionText));
        OnPropertyChanged(nameof(HasGoogleOAuthMetadata));
        OnPropertyChanged(nameof(CanRemoveGoogleOAuth));
        OnPropertyChanged(nameof(GoogleOAuthMaskedClientId));
        OnPropertyChanged(nameof(GoogleOAuthImportedAt));
        OnPropertyChanged(nameof(GoogleOAuthSource));
        OnPropertyChanged(nameof(GoogleOAuthValidationMessage));
        ((AsyncRelayCommand)ImportGoogleOAuthCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)RemoveGoogleOAuthCommand).NotifyCanExecuteChanged();
    }

    private void ValidateAndNotify()
    {
        ValidationMessage = Validate();
        ((AsyncRelayCommand)SaveTransferSettingsCommand).NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_googleOAuth is not null) _googleOAuth.Changed -= GoogleOAuthConfigurationChanged;
        ((AsyncRelayCommand)SaveTransferSettingsCommand).Dispose();
        ((AsyncRelayCommand)ClearCacheCommand).Dispose();
        ((AsyncRelayCommand)ExportDiagnosticsCommand).Dispose();
        ((AsyncRelayCommand)ImportGoogleOAuthCommand).Dispose();
        ((AsyncRelayCommand)RemoveGoogleOAuthCommand).Dispose();
        ((AsyncRelayCommand)ShowGoogleOAuthGuideCommand).Dispose();
    }
}

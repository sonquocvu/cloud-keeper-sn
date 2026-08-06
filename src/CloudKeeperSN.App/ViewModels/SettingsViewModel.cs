using System.Reflection;
using System.Windows.Input;
using CloudKeeperSN.App.Services;
using CloudKeeperSN.App.UI.Theming;
using CloudKeeperSN.Application.Persistence;
using CloudKeeperSN.App.Development;

namespace CloudKeeperSN.App.ViewModels;

public sealed class SettingsViewModel : PageViewModel, IDisposable
{
    private readonly IThemeService _themeService;
    private readonly IApplicationSettingRepository _settings;
    private readonly ILocalDataService _localData;
    private readonly IDiagnosticExportService _diagnosticExport;
    private readonly DemoWorkspace _workspace;
    private ThemeOption _selectedTheme;
    private string _concurrentTransfers = "2";
    private string _cacheLimitMiB = "1024";
    private string _retryAttempts = "6";
    private string _validationMessage = string.Empty;
    private string _actionMessage = string.Empty;

    public SettingsViewModel(
        IThemeService themeService,
        IApplicationSettingRepository settings,
        ILocalDataService localData,
        IDiagnosticExportService diagnosticExport,
        DemoWorkspace workspace)
        : base("settings", "Cài đặt", "Điều chỉnh giao diện, truyền dữ liệu và dữ liệu cục bộ.")
    {
        _themeService = themeService;
        _settings = settings;
        _localData = localData;
        _diagnosticExport = diagnosticExport;
        _workspace = workspace;
        ThemeOptions = [new(ThemeMode.System, "Theo cài đặt Windows"), new(ThemeMode.Light, "Giao diện sáng"), new(ThemeMode.Dark, "Giao diện tối")];
        _selectedTheme = ThemeOptions[0];
        SaveTransferSettingsCommand = new AsyncRelayCommand(SaveTransferSettingsAsync, () => string.IsNullOrEmpty(Validate()));
        ClearCacheCommand = new AsyncRelayCommand(ClearCacheAsync);
        ExportDiagnosticsCommand = new AsyncRelayCommand(ExportDiagnosticsAsync);
    }

    public IReadOnlyList<ThemeOption> ThemeOptions { get; }
    public ICommand SaveTransferSettingsCommand { get; }
    public ICommand ClearCacheCommand { get; }
    public ICommand ExportDiagnosticsCommand { get; }
    public string DatabasePath => _localData.DatabasePath;
    public string LogPath => _localData.LogPath;
    public string CachePath => _localData.CachePath;
    public string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.2.0";
    public string BuildInformation => ".NET 10 • WPF • Windows 10/11";

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

    private void ValidateAndNotify()
    {
        ValidationMessage = Validate();
        ((AsyncRelayCommand)SaveTransferSettingsCommand).NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        ((AsyncRelayCommand)SaveTransferSettingsCommand).Dispose();
        ((AsyncRelayCommand)ClearCacheCommand).Dispose();
        ((AsyncRelayCommand)ExportDiagnosticsCommand).Dispose();
    }
}

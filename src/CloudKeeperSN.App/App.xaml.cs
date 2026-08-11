using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using CloudKeeperSN.Application.Persistence;
using CloudKeeperSN.Application.Recovery;
using CloudKeeperSN.Application.Storage;
using CloudKeeperSN.Application.Scanning;
using CloudKeeperSN.Application.Planning;
using CloudKeeperSN.App.ViewModels;
using CloudKeeperSN.App.UI.Theming;
using CloudKeeperSN.App.UI.Windowing;
using CloudKeeperSN.App.Development;
using CloudKeeperSN.App.Services;
using CloudKeeperSN.Infrastructure;
using CloudKeeperSN.Providers.GoogleDrive.Fakes;
using CloudKeeperSN.Providers.GoogleDrive;
using CloudKeeperSN.Providers.GoogleDrive.Authentication;
using CloudKeeperSN.Providers.OneDrive.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace CloudKeeperSN.App;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;
    private IProviderDiagnostics? _exceptionDiagnostics;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var localData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CloudKeeperSN");
        Directory.CreateDirectory(localData);

        var services = new ServiceCollection();
        services.AddCloudKeeperInfrastructure(Path.Combine(localData, "cloudkeeper.db"));
        var demoConfiguration = DemoConfiguration.FromEnvironment();
        services.AddSingleton(demoConfiguration);
        services.AddSingleton<DemoWorkspace>();
        services.AddSingleton<FakeGoogleDriveProvider>();
        services.AddSingleton<FakeOneDriveProvider>();
        if (demoConfiguration.IsEnabled)
        {
            services.AddSingleton<IStorageProvider>(provider => provider.GetRequiredService<FakeGoogleDriveProvider>());
            services.AddSingleton<IStorageProvider>(provider => provider.GetRequiredService<FakeOneDriveProvider>());
        }
        else
        {
            services.AddSingleton<IGoogleOAuthEnvironment, SystemGoogleOAuthEnvironment>();
            services.AddSingleton<IGoogleOAuthImportFileReader, GoogleOAuthImportFileReader>();
            services.AddSingleton<IGoogleOAuthClock, SystemGoogleOAuthClock>();
            services.AddSingleton<GoogleOAuthConfigurationManager>();
            services.AddSingleton<IGoogleOAuthConfigurationManager>(provider => provider.GetRequiredService<GoogleOAuthConfigurationManager>());
            services.AddSingleton<IGoogleOAuthClient, GoogleApisOAuthClient>();
            services.AddSingleton<GoogleAuthenticationService>();
            services.AddSingleton<IProviderAuthenticationService>(provider => provider.GetRequiredService<GoogleAuthenticationService>());
            services.AddSingleton<GoogleDriveProvider>();
            services.AddSingleton<IStorageProvider>(provider => provider.GetRequiredService<GoogleDriveProvider>());
            services.AddSingleton<IDriveInventorySource>(provider => provider.GetRequiredService<GoogleDriveProvider>());
            services.AddSingleton<DriveInventoryScanner>();
            services.AddSingleton<IDriveInventoryScanner>(provider => provider.GetRequiredService<DriveInventoryScanner>());
        }
        services.AddSingleton<DemoDataService>();
        services.AddSingleton<DemoBackupPlanner>();
        services.AddSingleton<IDemoDelay, DemoDelay>();
        services.AddSingleton<DemoTransferEngine>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IUiDispatcher>(_ => new WpfUiDispatcher(Dispatcher));
        services.AddSingleton<IGoogleOAuthFilePickerService, GoogleOAuthFilePickerService>();
        services.AddSingleton<IFolderPickerService, FolderPickerService>();
        services.AddSingleton<IDiagnosticExportService, DiagnosticExportService>();
        services.AddSingleton<ILocalDataService, LocalDataService>();
        services.AddSingleton<TransferRecoveryService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IWindowPlacementService, WindowPlacementService>();
        services.AddSingleton<BackupSelectionPlanner>();
        services.AddSingleton<IBackupSelectionPlanService, BackupSelectionPlanService>();
        services.AddSingleton(provider => new DashboardViewModel(
            provider.GetRequiredService<DemoDataService>(),
            demoConfiguration.IsEnabled,
            demoConfiguration.IsEnabled ? null : provider.GetRequiredService<IProviderAuthenticationService>(),
            demoConfiguration.IsEnabled ? null : provider.GetRequiredService<IDriveInventoryScanner>(),
            provider.GetRequiredService<IUiDispatcher>()));
        services.AddSingleton(provider => new AccountsViewModel(
            provider.GetRequiredService<DemoDataService>(),
            provider.GetRequiredService<IDialogService>(),
            demoConfiguration.IsEnabled ? null : provider.GetRequiredService<IProviderAuthenticationService>(),
            provider.GetRequiredService<IUiDispatcher>()));
        services.AddSingleton(provider => new BackupViewModel(
            provider.GetRequiredService<DemoDataService>(),
            provider.GetRequiredService<DemoBackupPlanner>(),
            provider.GetRequiredService<DemoTransferEngine>(),
            provider.GetRequiredService<IFolderPickerService>(),
            provider.GetRequiredService<IDialogService>(),
            demoConfiguration,
            provider.GetServices<IStorageProvider>(),
            demoConfiguration.IsEnabled ? null : provider.GetRequiredService<IDriveInventoryScanner>(),
            provider.GetRequiredService<IUiDispatcher>()));
        services.AddSingleton(provider => new HistoryViewModel(
            provider.GetRequiredService<DemoDataService>(),
            provider.GetRequiredService<IDiagnosticExportService>(),
            demoConfiguration.IsEnabled,
            demoConfiguration.IsEnabled ? null : provider.GetRequiredService<IDriveInventoryScanner>(),
            provider.GetRequiredService<IUiDispatcher>()));
        services.AddSingleton(provider => new InventoryPlanViewModel(
            demoConfiguration,
            provider.GetRequiredService<IBackupSelectionPlanService>(),
            demoConfiguration.IsEnabled ? null : provider.GetRequiredService<IProviderAuthenticationService>(),
            demoConfiguration.IsEnabled ? null : provider.GetRequiredService<IDriveInventoryScanner>(),
            provider.GetRequiredService<IUiDispatcher>(),
            provider.GetRequiredService<IDialogService>(),
            provider.GetRequiredService<IProviderDiagnostics>()));
        services.AddSingleton(provider => new SettingsViewModel(
            provider.GetRequiredService<IThemeService>(),
            provider.GetRequiredService<IApplicationSettingRepository>(),
            provider.GetRequiredService<ILocalDataService>(),
            provider.GetRequiredService<IDiagnosticExportService>(),
            provider.GetRequiredService<DemoWorkspace>(),
            demoConfiguration.IsEnabled ? null : provider.GetRequiredService<IGoogleOAuthConfigurationManager>(),
            demoConfiguration.IsEnabled ? null : provider.GetRequiredService<IProviderAuthenticationService>(),
            provider.GetRequiredService<IGoogleOAuthFilePickerService>(),
            provider.GetRequiredService<IDialogService>()));
        services.AddSingleton(provider => new MainWindowViewModel(
            provider.GetRequiredService<DashboardViewModel>(),
            provider.GetRequiredService<AccountsViewModel>(),
            provider.GetRequiredService<BackupViewModel>(),
            provider.GetRequiredService<HistoryViewModel>(),
            provider.GetRequiredService<SettingsViewModel>(),
            provider.GetRequiredService<InventoryPlanViewModel>())
        {
            IsDemoMode = provider.GetRequiredService<DemoConfiguration>().IsEnabled
        });
        services.AddSingleton<MainWindow>();
        _serviceProvider = services.BuildServiceProvider(validateScopes: true);
        RegisterExceptionDiagnostics(_serviceProvider.GetRequiredService<IProviderDiagnostics>());

        try
        {
            await _serviceProvider.GetRequiredService<IApplicationDatabase>().InitializeAsync(CancellationToken.None);
            if (!demoConfiguration.IsEnabled)
            {
                await _serviceProvider.GetRequiredService<IGoogleOAuthConfigurationManager>()
                    .InitializeAsync(CancellationToken.None);
                await _serviceProvider.GetRequiredService<IDriveInventoryScanner>()
                    .InitializeAsync(CancellationToken.None);
            }
            await _serviceProvider.GetRequiredService<TransferRecoveryService>().RecoverAsync(CancellationToken.None);
            await _serviceProvider.GetRequiredService<IThemeService>().InitializeAsync(CancellationToken.None);
            await _serviceProvider.GetRequiredService<DemoDataService>().InitializeAsync(CancellationToken.None);
            var window = _serviceProvider.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
            await window.ViewModel.LoadAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            MessageBox.Show(
                "Không thể khởi tạo dữ liệu cục bộ. Vui lòng kiểm tra quyền ghi trong thư mục dữ liệu ứng dụng rồi thử lại.",
                "CloudKeeperSN",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        UnregisterExceptionDiagnostics();
        _serviceProvider?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnExit(e);
    }

    private void RegisterExceptionDiagnostics(IProviderDiagnostics diagnostics)
    {
        _exceptionDiagnostics = diagnostics;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    private void UnregisterExceptionDiagnostics()
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        _exceptionDiagnostics = null;
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e) =>
        RecordUnhandledException("UnhandledUiException", "Ứng dụng gặp lỗi giao diện không thể khôi phục.", e.Exception);

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e) =>
        RecordUnhandledException("UnobservedTaskException", "Một tác vụ nền đã kết thúc với lỗi chưa được xử lý.", e.Exception);

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            RecordUnhandledException("UnhandledBackgroundException", "Ứng dụng gặp lỗi nền không thể khôi phục.", exception);
    }

    private void RecordUnhandledException(string eventType, string message, Exception exception)
    {
        if (_exceptionDiagnostics is null) return;
        var stack = Regex.Replace(exception.StackTrace ?? string.Empty, @" in .*?:line \d+", string.Empty);
        var details = $"exception={exception.GetType().FullName}; stack={stack}";
        try
        {
            Task.Run(() => _exceptionDiagnostics.WriteAsync(eventType, message, details, CancellationToken.None))
                .Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // The original unhandled exception remains authoritative and is not suppressed.
        }
    }
}

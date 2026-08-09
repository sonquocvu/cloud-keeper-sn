using System.IO;
using System.Windows;
using CloudKeeperSN.Application.Persistence;
using CloudKeeperSN.Application.Recovery;
using CloudKeeperSN.Application.Storage;
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
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton(provider => new AccountsViewModel(
            provider.GetRequiredService<DemoDataService>(),
            provider.GetRequiredService<IDialogService>(),
            demoConfiguration.IsEnabled ? null : provider.GetRequiredService<IProviderAuthenticationService>(),
            provider.GetRequiredService<IUiDispatcher>()));
        services.AddSingleton<BackupViewModel>();
        services.AddSingleton<HistoryViewModel>();
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
            provider.GetRequiredService<SettingsViewModel>())
        {
            IsDemoMode = provider.GetRequiredService<DemoConfiguration>().IsEnabled
        });
        services.AddSingleton<MainWindow>();
        _serviceProvider = services.BuildServiceProvider(validateScopes: true);

        try
        {
            await _serviceProvider.GetRequiredService<IApplicationDatabase>().InitializeAsync(CancellationToken.None);
            if (!demoConfiguration.IsEnabled)
            {
                await _serviceProvider.GetRequiredService<IGoogleOAuthConfigurationManager>()
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
        _serviceProvider?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnExit(e);
    }
}

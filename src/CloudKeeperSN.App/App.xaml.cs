using System.IO;
using System.Windows;
using CloudKeeperSN.Application.Persistence;
using CloudKeeperSN.Application.Recovery;
using CloudKeeperSN.Application.Storage;
using CloudKeeperSN.App.ViewModels;
using CloudKeeperSN.Infrastructure;
using CloudKeeperSN.Providers.GoogleDrive.Fakes;
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
        services.AddSingleton<FakeGoogleDriveProvider>();
        services.AddSingleton<FakeOneDriveProvider>();
        services.AddSingleton<IStorageProvider>(provider => provider.GetRequiredService<FakeGoogleDriveProvider>());
        services.AddSingleton<IStorageProvider>(provider => provider.GetRequiredService<FakeOneDriveProvider>());
        services.AddSingleton<TransferRecoveryService>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
        _serviceProvider = services.BuildServiceProvider(validateScopes: true);

        try
        {
            await _serviceProvider.GetRequiredService<IApplicationDatabase>().InitializeAsync(CancellationToken.None);
            await _serviceProvider.GetRequiredService<TransferRecoveryService>().RecoverAsync(CancellationToken.None);
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
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}


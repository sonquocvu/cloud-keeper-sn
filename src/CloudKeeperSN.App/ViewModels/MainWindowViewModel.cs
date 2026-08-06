using System.Collections.ObjectModel;
using System.Windows.Input;
using CloudKeeperSN.Application.Persistence;
using CloudKeeperSN.Application.Storage;

namespace CloudKeeperSN.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly IStorageAccountRepository _accountRepository;
    private readonly IReadOnlyList<IStorageProvider> _providers;
    private string _selectedSection = "dashboard";
    private string _googleStatus = "Chưa kết nối";
    private string _oneDriveStatus = "Chưa kết nối";
    private string _statusMessage = "Sẵn sàng thiết lập sao lưu an toàn.";

    public MainWindowViewModel(IStorageAccountRepository accountRepository, IEnumerable<IStorageProvider> providers)
    {
        _accountRepository = accountRepository;
        _providers = providers.ToArray();
        NavigateCommand = new RelayCommand(parameter => SelectedSection = parameter as string ?? "dashboard");
    }

    public ICommand NavigateCommand { get; }
    public ObservableCollection<string> RecentActivities { get; } = [];

    public string SelectedSection
    {
        get => _selectedSection;
        private set
        {
            if (!SetProperty(ref _selectedSection, value)) return;
            OnPropertyChanged(nameof(IsDashboard));
            OnPropertyChanged(nameof(IsAccounts));
            OnPropertyChanged(nameof(IsBackup));
            OnPropertyChanged(nameof(IsHistory));
            OnPropertyChanged(nameof(IsSettings));
        }
    }

    public bool IsDashboard => SelectedSection == "dashboard";
    public bool IsAccounts => SelectedSection == "accounts";
    public bool IsBackup => SelectedSection == "backup";
    public bool IsHistory => SelectedSection == "history";
    public bool IsSettings => SelectedSection == "settings";

    public string GoogleStatus { get => _googleStatus; private set => SetProperty(ref _googleStatus, value); }
    public string OneDriveStatus { get => _oneDriveStatus; private set => SetProperty(ref _oneDriveStatus, value); }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public string LastBackup => "Chưa có lần sao lưu nào";
    public string TotalFiles => "0";
    public string PendingErrors => "0";
    public string TransferredSize => "0 byte";

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var persistedAccounts = await _accountRepository.GetAllAsync(cancellationToken);
        GoogleStatus = persistedAccounts.Any(account => account.ProviderId == "google-drive" && account.IsConnected)
            ? "Đã kết nối"
            : "Chưa kết nối";
        OneDriveStatus = persistedAccounts.Any(account => account.ProviderId == "one-drive" && account.IsConnected)
            ? "Đã kết nối"
            : "Chưa kết nối";

        var availableProviders = string.Join(", ", _providers.Select(provider => provider.Descriptor.DisplayName));
        StatusMessage = $"Nền tảng cục bộ đã sẵn sàng. Trình cung cấp thử nghiệm: {availableProviders}.";
    }
}


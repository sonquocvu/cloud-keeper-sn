using System.Windows.Input;
using CloudKeeperSN.App.Development;
using CloudKeeperSN.App.Presentation;
using CloudKeeperSN.App.Services;
using CloudKeeperSN.Domain.Storage;

namespace CloudKeeperSN.App.ViewModels;

public enum AccountConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    ReauthenticationRequired,
    Error,
    Disconnecting
}

public sealed class ProviderAccountCardViewModel : ObservableObject, IDisposable
{
    private readonly DemoDataService _demoData;
    private readonly IDialogService _dialogs;
    private AccountConnectionState _state;
    private string? _accountName;
    private string? _email;
    private string? _errorMessage;

    public ProviderAccountCardViewModel(
        string providerId,
        string providerName,
        string description,
        string connectText,
        string demoEmail,
        DemoDataService demoData,
        IDialogService dialogs)
    {
        ProviderId = providerId;
        ProviderName = providerName;
        Description = description;
        ConnectText = connectText;
        DemoEmail = demoEmail;
        _demoData = demoData;
        _dialogs = dialogs;
        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => IsDemoEnabled && State is AccountConnectionState.Disconnected or AccountConnectionState.Error or AccountConnectionState.ReauthenticationRequired);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsDemoEnabled && State == AccountConnectionState.Connected);
    }

    public string ProviderId { get; }
    public string ProviderName { get; }
    public string Description { get; }
    public string ConnectText { get; }
    public string DemoEmail { get; }
    public bool IsDemoEnabled => _demoData.Configuration.IsEnabled;
    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }

    public AccountConnectionState State
    {
        get => _state;
        private set
        {
            if (!SetProperty(ref _state, value)) return;
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(PrimaryActionText));
            NotifyCommands();
        }
    }

    public string? AccountName { get => _accountName; private set => SetProperty(ref _accountName, value); }
    public string? Email { get => _email; private set => SetProperty(ref _email, value); }
    public string? ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }
    public bool IsConnected => State == AccountConnectionState.Connected;
    public bool IsBusy => State is AccountConnectionState.Connecting or AccountConnectionState.Disconnecting;
    public string PrimaryActionText => State == AccountConnectionState.ReauthenticationRequired ? "Đăng nhập lại" : ConnectText;
    public StatusPresentation Status => State switch
    {
        AccountConnectionState.Connecting => new("Đang kết nối", StatusTone.Information, "\uE895"),
        AccountConnectionState.Connected => new("Đã kết nối", StatusTone.Success, "\uE73E"),
        AccountConnectionState.ReauthenticationRequired => new("Cần đăng nhập lại", StatusTone.Warning, "\uE7BA"),
        AccountConnectionState.Error => new("Kết nối gặp lỗi", StatusTone.Error, "\uEA39"),
        AccountConnectionState.Disconnecting => new("Đang ngắt kết nối", StatusTone.Information, "\uE895"),
        _ => new("Chưa kết nối", StatusTone.Neutral, "\uE946")
    };

    public void Apply(StorageAccount? account)
    {
        State = account is { IsConnected: true } ? AccountConnectionState.Connected : AccountConnectionState.Disconnected;
        AccountName = account?.DisplayName;
        Email = account is null ? null : DemoEmail;
        ErrorMessage = null;
    }

    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        State = AccountConnectionState.Connecting;
        ErrorMessage = null;
        try
        {
            if (ProviderId == "google-drive") await _demoData.ConnectGoogleAsync(cancellationToken);
            else await _demoData.ConnectOneDriveAsync(cancellationToken);
            Apply((await _demoData.GetAccountsAsync(cancellationToken)).FirstOrDefault(account => account.ProviderId == ProviderId));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            State = AccountConnectionState.Error;
            ErrorMessage = "Không thể hoàn tất kết nối thử nghiệm. Dữ liệu đám mây không bị thay đổi. Vui lòng thử lại.";
        }
    }

    private async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        var confirmed = await _dialogs.ConfirmAsync(
            new ConfirmationRequest(
                "Ngắt kết nối tài khoản?",
                $"CloudKeeperSN sẽ ngắt kết nối {ProviderName} trên thiết bị này.",
                "Ngắt kết nối",
                IsDangerous: true,
                SupportingText: "Tài khoản và dữ liệu trên đám mây không bị thay đổi. Thông tin đăng nhập lưu cục bộ sẽ được xóa; lịch sử sao lưu vẫn được giữ lại."),
            cancellationToken);
        if (!confirmed) return;

        State = AccountConnectionState.Disconnecting;
        await _demoData.DisconnectAsync(ProviderId, cancellationToken);
        Apply(null);
    }

    private void NotifyCommands()
    {
        ((AsyncRelayCommand)ConnectCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)DisconnectCommand).NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        ((AsyncRelayCommand)ConnectCommand).Dispose();
        ((AsyncRelayCommand)DisconnectCommand).Dispose();
    }
}

public sealed class AccountsViewModel : PageViewModel, IDisposable
{
    private readonly DemoDataService _demoData;
    private string _pageMessage = "Kết nối được mô phỏng cục bộ; không có thông tin đăng nhập thật nào được sử dụng.";

    public AccountsViewModel(DemoDataService demoData, IDialogService dialogs)
        : base("accounts", "Tài khoản", "Quản lý kết nối Google Drive và OneDrive.")
    {
        _demoData = demoData;
        GoogleDrive = new ProviderAccountCardViewModel(
            "google-drive", "Google Drive", "Quyền chỉ đọc. Không xóa dữ liệu nguồn.", "Kết nối Google Drive", "minh.an@example.test", demoData, dialogs);
        OneDrive = new ProviderAccountCardViewModel(
            "one-drive", "OneDrive", "Chỉ dùng làm nơi lưu bản sao. Không ghi đè theo mặc định.", "Kết nối OneDrive", "minh.an@outlook.example", demoData, dialogs);
    }

    public ProviderAccountCardViewModel GoogleDrive { get; }
    public ProviderAccountCardViewModel OneDrive { get; }
    public bool IsDemoEnabled => _demoData.Configuration.IsEnabled;
    public string PageMessage { get => _pageMessage; private set => SetProperty(ref _pageMessage, value); }

    public override async Task LoadAsync(CancellationToken cancellationToken)
    {
        var accounts = await _demoData.GetAccountsAsync(cancellationToken);
        GoogleDrive.Apply(accounts.FirstOrDefault(account => account.ProviderId == "google-drive"));
        OneDrive.Apply(accounts.FirstOrDefault(account => account.ProviderId == "one-drive"));
        PageMessage = IsDemoEnabled
            ? "Đây là kết nối trình diễn cục bộ. Không có cửa sổ đăng nhập hoặc dịch vụ đám mây thật nào được gọi."
            : "Kết nối thật chưa được triển khai. Bật chế độ trình diễn dành cho nhà phát triển để khám phá giao diện an toàn.";
    }

    public void Dispose()
    {
        GoogleDrive.Dispose();
        OneDrive.Dispose();
    }
}


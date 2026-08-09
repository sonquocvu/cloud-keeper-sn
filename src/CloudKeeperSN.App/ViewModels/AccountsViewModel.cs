using System.Windows.Input;
using CloudKeeperSN.App.Development;
using CloudKeeperSN.App.Presentation;
using CloudKeeperSN.App.Services;
using CloudKeeperSN.Application.Storage;
using CloudKeeperSN.Domain.Storage;

namespace CloudKeeperSN.App.ViewModels;

public enum AccountConnectionState
{
    Disconnected,
    OpeningBrowser,
    CompletingConnection,
    Connected,
    ReauthenticationRequired,
    Cancelled,
    Error,
    Disconnecting
}

public sealed class ProviderAccountCardViewModel : ObservableObject, IDisposable
{
    private readonly IDialogService _dialogs;
    private readonly Func<CancellationToken, Task<StorageAccount?>> _load;
    private readonly Func<CancellationToken, Task<StorageAccount>> _connect;
    private readonly Func<CancellationToken, Task> _disconnect;
    private readonly IProviderAuthenticationService? _authentication;
    private AccountConnectionState _state;
    private string? _accountName;
    private string? _email;
    private string? _errorMessage;
    private bool _isProviderEnabled;
    private string? _disabledExplanation;
    private string? _configurationStatusMessage;

    public ProviderAccountCardViewModel(
        string providerId,
        string providerName,
        string description,
        string connectText,
        bool isEnabled,
        string? disabledExplanation,
        IDialogService dialogs,
        Func<CancellationToken, Task<StorageAccount?>> load,
        Func<CancellationToken, Task<StorageAccount>> connect,
        Func<CancellationToken, Task> disconnect,
        IProviderAuthenticationService? authentication = null)
    {
        ProviderId = providerId;
        ProviderName = providerName;
        Description = description;
        ConnectText = connectText;
        _isProviderEnabled = isEnabled;
        _disabledExplanation = isEnabled ? null : disabledExplanation;
        _configurationStatusMessage = isEnabled ? disabledExplanation : null;
        _dialogs = dialogs;
        _load = load;
        _connect = connect;
        _disconnect = disconnect;
        _authentication = authentication;
        if (_authentication is not null)
        {
            _authentication.StateChanged += AuthenticationStateChanged;
            _authentication.ConfigurationChanged += AuthenticationConfigurationChanged;
        }
        ConnectCommand = new AsyncRelayCommand(ConnectAsync, CanConnect);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsProviderEnabled && State == AccountConnectionState.Connected);
        CancelConnectCommand = new RelayCommand(_ => ((AsyncRelayCommand)ConnectCommand).Cancel(), _ => CanCancelConnection);
    }

    public string ProviderId { get; }
    public string ProviderName { get; }
    public string Description { get; }
    public string ConnectText { get; }
    public bool IsProviderEnabled { get => _isProviderEnabled; private set => SetProperty(ref _isProviderEnabled, value); }
    public string? DisabledExplanation { get => _disabledExplanation; private set => SetProperty(ref _disabledExplanation, value); }
    public string? ConfigurationStatusMessage { get => _configurationStatusMessage; private set => SetProperty(ref _configurationStatusMessage, value); }
    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand CancelConnectCommand { get; }

    public AccountConnectionState State
    {
        get => _state;
        private set
        {
            if (!SetProperty(ref _state, value)) return;
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(CanCancelConnection));
            OnPropertyChanged(nameof(PrimaryActionText));
            NotifyCommands();
        }
    }

    public string? AccountName { get => _accountName; private set => SetProperty(ref _accountName, value); }
    public string? Email { get => _email; private set => SetProperty(ref _email, value); }
    public string? ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }
    public bool IsConnected => State == AccountConnectionState.Connected;
    public bool IsBusy => State is AccountConnectionState.OpeningBrowser or AccountConnectionState.CompletingConnection or AccountConnectionState.Disconnecting;
    public bool CanCancelConnection => State is AccountConnectionState.OpeningBrowser or AccountConnectionState.CompletingConnection;
    public string PrimaryActionText => State == AccountConnectionState.ReauthenticationRequired ? "Đăng nhập lại" : ConnectText;
    public StatusPresentation Status => State switch
    {
        AccountConnectionState.OpeningBrowser => new("Đang mở trình duyệt để đăng nhập", StatusTone.Information, "\uE895"),
        AccountConnectionState.CompletingConnection => new("Đang hoàn tất kết nối", StatusTone.Information, "\uE895"),
        AccountConnectionState.Connected => new("Đã kết nối", StatusTone.Success, "\uE73E"),
        AccountConnectionState.ReauthenticationRequired => new("Cần đăng nhập lại", StatusTone.Warning, "\uE7BA"),
        AccountConnectionState.Cancelled => new("Đã hủy đăng nhập", StatusTone.Neutral, "\uE711"),
        AccountConnectionState.Error => new("Không thể kết nối", StatusTone.Error, "\uEA39"),
        AccountConnectionState.Disconnecting => new("Đang ngắt kết nối", StatusTone.Information, "\uE895"),
        _ => new("Chưa kết nối", StatusTone.Neutral, "\uE946")
    };

    public async Task LoadAsync(CancellationToken cancellationToken) => Apply(await _load(cancellationToken));

    public void Apply(StorageAccount? account)
    {
        State = account is { IsConnected: true } ? AccountConnectionState.Connected : AccountConnectionState.Disconnected;
        AccountName = account?.DisplayName;
        Email = account?.Email;
        ErrorMessage = IsProviderEnabled ? null : DisabledExplanation;
    }

    private bool CanConnect() => IsProviderEnabled && State is AccountConnectionState.Disconnected or AccountConnectionState.Cancelled or AccountConnectionState.Error or AccountConnectionState.ReauthenticationRequired;

    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        State = _authentication is null ? AccountConnectionState.CompletingConnection : AccountConnectionState.OpeningBrowser;
        ErrorMessage = null;
        try
        {
            Apply(await _connect(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            State = AccountConnectionState.Cancelled;
            ErrorMessage = "Đã hủy đăng nhập. Không có dữ liệu đám mây nào bị thay đổi.";
            throw;
        }
        catch (ProviderOperationException exception)
        {
            State = exception.Category is ProviderFailureCategory.AuthenticationRequired or ProviderFailureCategory.AuthorizationRevoked
                ? AccountConnectionState.ReauthenticationRequired
                : exception.Category == ProviderFailureCategory.AuthorizationCancelled
                    ? AccountConnectionState.Cancelled
                    : AccountConnectionState.Error;
            ErrorMessage = ProviderFailureMessages.ToVietnamese(exception.Category);
        }
        catch (Exception)
        {
            State = AccountConnectionState.Error;
            ErrorMessage = "Không thể hoàn tất kết nối. Dữ liệu đám mây không bị thay đổi. Vui lòng thử lại.";
        }
    }

    private async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        var confirmed = await _dialogs.ConfirmAsync(
            new ConfirmationRequest(
                "Ngắt kết nối tài khoản?",
                $"CloudKeeperSN sẽ xóa thông tin đăng nhập {ProviderName} của tài khoản {Email ?? AccountName ?? "đang kết nối"} được lưu cục bộ trên thiết bị này.",
                "_Ngắt kết nối",
                IsDangerous: true,
                SupportingText: "Dữ liệu Google Drive sẽ không bị thay đổi. Lịch sử hiện có vẫn được giữ lại và bạn có thể kết nối lại sau."),
            cancellationToken);
        if (!confirmed) return;

        State = AccountConnectionState.Disconnecting;
        try
        {
            await _disconnect(cancellationToken);
            Apply(null);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            State = AccountConnectionState.Error;
            ErrorMessage = "Đã xóa phiên cục bộ nhưng không thể xác nhận thu hồi với Google. Dữ liệu Drive không bị thay đổi; bạn có thể thu hồi quyền trong Tài khoản Google.";
        }
    }

    private void AuthenticationStateChanged(ProviderAuthenticationState state)
    {
        State = state.Status switch
        {
            ProviderAuthenticationStatus.OpeningBrowser => AccountConnectionState.OpeningBrowser,
            ProviderAuthenticationStatus.CompletingConnection => AccountConnectionState.CompletingConnection,
            ProviderAuthenticationStatus.Connected => AccountConnectionState.Connected,
            ProviderAuthenticationStatus.ReauthenticationRequired => AccountConnectionState.ReauthenticationRequired,
            ProviderAuthenticationStatus.Cancelled => AccountConnectionState.Cancelled,
            ProviderAuthenticationStatus.Failed => AccountConnectionState.Error,
            ProviderAuthenticationStatus.Disconnecting => AccountConnectionState.Disconnecting,
            _ => AccountConnectionState.Disconnected
        };
        if (state.Status is ProviderAuthenticationStatus.Failed or ProviderAuthenticationStatus.ReauthenticationRequired or ProviderAuthenticationStatus.Cancelled)
            ErrorMessage = state.VietnameseMessage;
        if (state.Status == ProviderAuthenticationStatus.Disconnected)
        {
            AccountName = null;
            Email = null;
        }
    }

    private void AuthenticationConfigurationChanged()
    {
        if (_authentication is null) return;
        IsProviderEnabled = _authentication.IsConfigured;
        DisabledExplanation = IsProviderEnabled ? null : _authentication.ConfigurationMessage;
        ConfigurationStatusMessage = IsProviderEnabled ? _authentication.ConfigurationMessage : null;
        if (!IsProviderEnabled)
        {
            AccountName = null;
            Email = null;
            State = AccountConnectionState.Disconnected;
            ErrorMessage = DisabledExplanation;
        }
        else if (!IsConnected)
        {
            State = AccountConnectionState.Disconnected;
            ErrorMessage = null;
        }
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        ((AsyncRelayCommand)ConnectCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)DisconnectCommand).NotifyCanExecuteChanged();
        ((RelayCommand)CancelConnectCommand).RaiseCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_authentication is not null)
        {
            _authentication.StateChanged -= AuthenticationStateChanged;
            _authentication.ConfigurationChanged -= AuthenticationConfigurationChanged;
        }
        ((AsyncRelayCommand)ConnectCommand).Dispose();
        ((AsyncRelayCommand)DisconnectCommand).Dispose();
    }
}

public sealed class AccountsViewModel : PageViewModel, IDisposable
{
    private readonly DemoDataService _demoData;
    private string _pageMessage;

    public AccountsViewModel(DemoDataService demoData, IDialogService dialogs, IProviderAuthenticationService? googleAuthentication = null)
        : base(
            "accounts",
            "Tài khoản",
            demoData.Configuration.IsEnabled ? "Quản lý kết nối Google Drive và OneDrive trình diễn." : "Kết nối Google Drive chỉ đọc; OneDrive thật chưa được tích hợp.")
    {
        _demoData = demoData;
        IsDemoEnabled = demoData.Configuration.IsEnabled;
        _pageMessage = IsDemoEnabled
            ? "Đây là kết nối trình diễn cục bộ. Không có cửa sổ đăng nhập hoặc dịch vụ đám mây thật nào được gọi."
            : "Google Drive sử dụng trình duyệt hệ thống và chỉ yêu cầu quyền đọc. OneDrive thật chưa được tích hợp.";

        GoogleDrive = IsDemoEnabled
            ? CreateDemoGoogle(demoData, dialogs)
            : CreateRealGoogle(googleAuthentication ?? throw new InvalidOperationException("Real Google authentication service is required."), dialogs);
        OneDrive = IsDemoEnabled
            ? CreateDemoOneDrive(demoData, dialogs)
            : CreateUnavailableOneDrive(dialogs);
    }

    public ProviderAccountCardViewModel GoogleDrive { get; }
    public ProviderAccountCardViewModel OneDrive { get; }
    public bool IsDemoEnabled { get; }
    public string PageMessage { get => _pageMessage; private set => SetProperty(ref _pageMessage, value); }

    public override async Task LoadAsync(CancellationToken cancellationToken)
    {
        await GoogleDrive.LoadAsync(cancellationToken);
        await OneDrive.LoadAsync(cancellationToken);
    }

    private static ProviderAccountCardViewModel CreateDemoGoogle(DemoDataService demo, IDialogService dialogs) => new(
        "google-drive", "Google Drive", "Quyền chỉ đọc. Không xóa dữ liệu nguồn.", "_Kết nối Google Drive", true, null, dialogs,
        async token => (await demo.GetAccountsAsync(token)).FirstOrDefault(account => account.ProviderId == "google-drive"),
        async token => { await demo.ConnectGoogleAsync(token); return (await demo.GetAccountsAsync(token)).First(account => account.ProviderId == "google-drive"); },
        token => demo.DisconnectAsync("google-drive", token));

    private static ProviderAccountCardViewModel CreateDemoOneDrive(DemoDataService demo, IDialogService dialogs) => new(
        "one-drive", "OneDrive", "Chỉ dùng làm nơi lưu bản sao. Không ghi đè theo mặc định.", "_Kết nối OneDrive", true, null, dialogs,
        async token => (await demo.GetAccountsAsync(token)).FirstOrDefault(account => account.ProviderId == "one-drive"),
        async token => { await demo.ConnectOneDriveAsync(token); return (await demo.GetAccountsAsync(token)).First(account => account.ProviderId == "one-drive"); },
        token => demo.DisconnectAsync("one-drive", token));

    private static ProviderAccountCardViewModel CreateRealGoogle(IProviderAuthenticationService authentication, IDialogService dialogs) => new(
        "google-drive", "Google Drive", "Chỉ đọc dữ liệu và siêu dữ liệu. Không thể sửa hoặc xóa dữ liệu nguồn.", "_Kết nối Google Drive",
        authentication.IsConfigured, authentication.ConfigurationMessage, dialogs,
        authentication.GetCachedAccountAsync, authentication.ConnectAsync, authentication.DisconnectAsync, authentication);

    private static ProviderAccountCardViewModel CreateUnavailableOneDrive(IDialogService dialogs) => new(
        "one-drive", "OneDrive", "Tích hợp OneDrive thật chưa có trong phiên bản này.", "Kết nối OneDrive", false,
        "OneDrive thật sẽ được bổ sung ở bước phát triển tiếp theo; không có đích giả nào được dùng trong chế độ thật.", dialogs,
        _ => Task.FromResult<StorageAccount?>(null),
        _ => throw new NotSupportedException(),
        _ => Task.CompletedTask);

    public void Dispose()
    {
        GoogleDrive.Dispose();
        OneDrive.Dispose();
    }
}

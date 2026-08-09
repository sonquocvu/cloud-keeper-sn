using CloudKeeperSN.App.ViewModels;
using CloudKeeperSN.Application.Storage;
using CloudKeeperSN.Domain.Storage;

namespace CloudKeeperSN.App.Tests;

public sealed class ProviderAccountCardCommandTests
{
    [Fact]
    public void MissingConfigurationDisablesConnectAndShowsReason()
    {
        using var card = CreateCard(
            configured: false,
            configurationMessage: "Thiếu CLOUDKEEPERSN_GOOGLE_CLIENT_ID");

        card.Apply(null);

        Assert.False(card.ConnectCommand.CanExecute(null));
        Assert.Equal("Thiếu CLOUDKEEPERSN_GOOGLE_CLIENT_ID", card.ErrorMessage);
        Assert.Null(card.ConfigurationStatusMessage);
    }

    [Fact]
    public void InvalidConfigurationDisablesConnectAndShowsReason()
    {
        using var card = CreateCard(
            configured: false,
            configurationMessage: "Cấu hình Google OAuth không hợp lệ: ClientId không đúng định dạng.");

        card.Apply(null);

        Assert.False(card.ConnectCommand.CanExecute(null));
        Assert.Contains("không hợp lệ", card.ErrorMessage);
    }

    [Fact]
    public void ReadyConfigurationEnablesConnectAndShowsSafeSourceStatus()
    {
        using var card = CreateCard(
            configured: true,
            configurationMessage: "OAuth đã sẵn sàng (nguồn: biến môi trường). Chưa kết nối tài khoản.");

        card.Apply(null);

        Assert.True(card.ConnectCommand.CanExecute(null));
        Assert.Null(card.ErrorMessage);
        Assert.Contains("OAuth đã sẵn sàng", card.ConfigurationStatusMessage);
    }

    [Fact]
    public async Task FailedConnectionReturnsCommandToRetryableState()
    {
        using var card = CreateCard(
            configured: true,
            connect: _ => Task.FromException<StorageAccount>(new ProviderOperationException(
                ProviderFailureCategory.OAuthInvalidClient,
                "technical value that must not be displayed")));
        card.Apply(null);

        card.ConnectCommand.Execute(null);
        await AsyncTest.UntilAsync(() => card.State == AccountConnectionState.Error &&
                                           card.ConnectCommand.CanExecute(null));

        Assert.Contains("OAuth client", card.ErrorMessage);
    }

    [Fact]
    public async Task CancelledConnectionReturnsCommandToRetryableState()
    {
        using var card = CreateCard(
            configured: true,
            connect: async token =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new InvalidOperationException();
            });
        card.Apply(null);

        card.ConnectCommand.Execute(null);
        await AsyncTest.UntilAsync(() => card.CanCancelConnection);
        Assert.False(card.ConnectCommand.CanExecute(null));
        card.CancelConnectCommand.Execute(null);
        await AsyncTest.UntilAsync(() => card.State == AccountConnectionState.Cancelled &&
                                           card.ConnectCommand.CanExecute(null));

        Assert.Contains("hủy đăng nhập", card.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeConfigurationChangeImmediatelyUpdatesConnectCommand()
    {
        var authentication = new FakeAuthentication(false, "Chưa cấu hình OAuth.");
        using var card = new ProviderAccountCardViewModel(
            "google-drive", "Google Drive", "Chỉ đọc", "Kết nối Google Drive",
            false, authentication.ConfigurationMessage, new FakeDialogService(),
            _ => Task.FromResult<StorageAccount?>(null),
            _ => Task.FromResult(Account),
            _ => Task.CompletedTask,
            authentication);
        card.Apply(null);
        Assert.False(card.ConnectCommand.CanExecute(null));

        authentication.SetConfiguration(true, "OAuth đã sẵn sàng (nguồn: Đã nhập từ Cài đặt). Chưa kết nối tài khoản.");

        Assert.True(card.ConnectCommand.CanExecute(null));
        Assert.Null(card.ErrorMessage);
        Assert.Contains("OAuth đã sẵn sàng", card.ConfigurationStatusMessage);

        authentication.SetConfiguration(false, "Chưa cấu hình OAuth.");
        Assert.False(card.ConnectCommand.CanExecute(null));
        Assert.Equal("Chưa cấu hình OAuth.", card.ErrorMessage);
    }

    private static ProviderAccountCardViewModel CreateCard(
        bool configured,
        string? configurationMessage = null,
        Func<CancellationToken, Task<StorageAccount>>? connect = null)
    {
        var authentication = new FakeAuthentication(configured, configurationMessage);
        return new ProviderAccountCardViewModel(
            "google-drive",
            "Google Drive",
            "Chỉ đọc",
            "Kết nối Google Drive",
            configured,
            configurationMessage,
            new FakeDialogService(),
            _ => Task.FromResult<StorageAccount?>(null),
            connect ?? (_ => Task.FromResult(Account)),
            _ => Task.CompletedTask,
            authentication);
    }

    private static readonly StorageAccount Account = new(
        "google:test", "google-drive", "test", "Test User", true, DateTimeOffset.UtcNow, "test@example.test");

    private sealed class FakeAuthentication : IProviderAuthenticationService
    {
        private bool _configured;
        private string? _configurationMessage;
        private event Action? ConfigurationChangedHandlers;

        public FakeAuthentication(bool configured, string? configurationMessage)
        {
            _configured = configured;
            _configurationMessage = configurationMessage;
        }

        public string ProviderId => "google-drive";
        public bool IsConfigured => _configured;
        public string? ConfigurationMessage => _configurationMessage;
        public ProviderAuthenticationState State { get; private set; } =
            new(ProviderAuthenticationStatus.Disconnected, "Chưa kết nối");
        public event Action<ProviderAuthenticationState>? StateChanged { add { } remove { } }
        public event Action? ConfigurationChanged { add => ConfigurationChangedHandlers += value; remove => ConfigurationChangedHandlers -= value; }
        public Task<StorageAccount?> GetCachedAccountAsync(CancellationToken cancellationToken) => Task.FromResult<StorageAccount?>(null);
        public Task<StorageAccount> ConnectAsync(CancellationToken cancellationToken) => Task.FromResult(Account);
        public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DisconnectLocalAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void SetConfiguration(bool configured, string message)
        {
            _configured = configured;
            _configurationMessage = message;
            ConfigurationChangedHandlers?.Invoke();
        }
    }
}

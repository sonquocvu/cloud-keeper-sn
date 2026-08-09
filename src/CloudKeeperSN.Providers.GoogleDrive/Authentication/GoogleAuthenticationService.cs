using CloudKeeperSN.Application.Persistence;
using CloudKeeperSN.Application.Storage;
using CloudKeeperSN.Domain.Storage;

namespace CloudKeeperSN.Providers.GoogleDrive.Authentication;

public sealed class GoogleAuthenticationService(
    IGoogleOAuthClient oauthClient,
    IStorageAccountRepository accounts,
    IProviderDiagnostics diagnostics) : IProviderAuthenticationService, IAsyncDisposable
{
    private const string Provider = "google-drive";
    private readonly SemaphoreSlim _interactiveGate = new(1, 1);
    private IGoogleDriveSession? _session;
    private StorageAccount? _account;
    private int _disposed;

    public string ProviderId => Provider;
    public bool IsConfigured => oauthClient.IsConfigured;
    public string? ConfigurationMessage => oauthClient.ConfigurationMessage;
    public ProviderAuthenticationState State { get; private set; } = new(ProviderAuthenticationStatus.Disconnected, "Chưa kết nối");
    public event Action<ProviderAuthenticationState>? StateChanged;
    public StorageAccount? CurrentAccount => _account;

    public async Task<StorageAccount?> GetCachedAccountAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            SetState(ProviderAuthenticationStatus.Disconnected, ConfigurationMessage!);
            return null;
        }

        try
        {
            var restored = await oauthClient.RestoreAsync(cancellationToken);
            if (restored is null)
            {
                SetState(ProviderAuthenticationStatus.Disconnected, "Chưa kết nối");
                return null;
            }

            return await CompleteSessionAsync(restored, "GoogleTokenRestored", cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            var failure = GoogleProviderExceptionMapper.Map(exception);
            await SafeClearAsync(cancellationToken);
            SetState(ProviderAuthenticationStatus.ReauthenticationRequired, ProviderFailureMessages.ToVietnamese(failure.Category), failure.Category.ToString());
            await WriteDiagnosticAsync("GoogleTokenRestoreFailed", "Không thể khôi phục phiên Google Drive; cần đăng nhập lại.", failure, cancellationToken);
            return null;
        }
    }

    public async Task<StorageAccount> ConnectAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            throw new ProviderOperationException(ProviderFailureCategory.AuthenticationRequired, ConfigurationMessage!);
        if (!await _interactiveGate.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("Một thao tác đăng nhập Google Drive khác đang diễn ra.");

        try
        {
            SetState(ProviderAuthenticationStatus.OpeningBrowser, "Đang mở trình duyệt để đăng nhập");
            await diagnostics.WriteAsync("GoogleAuthenticationStarted", "Đã bắt đầu đăng nhập Google Drive bằng trình duyệt hệ thống.", null, cancellationToken);
            var session = await oauthClient.AuthorizeAsync(cancellationToken);
            SetState(ProviderAuthenticationStatus.CompletingConnection, "Đang hoàn tất kết nối");
            return await CompleteSessionAsync(session, "GoogleAuthenticationCompleted", cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetState(ProviderAuthenticationStatus.Cancelled, "Đã hủy đăng nhập");
            await diagnostics.WriteAsync("GoogleAuthenticationCancelled", "Đã hủy đăng nhập Google Drive.", null, CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            var failure = GoogleProviderExceptionMapper.Map(exception);
            var status = failure.Category is ProviderFailureCategory.AuthorizationCancelled
                ? ProviderAuthenticationStatus.Cancelled
                : failure.Category is ProviderFailureCategory.AuthorizationRevoked or ProviderFailureCategory.AuthenticationRequired
                    ? ProviderAuthenticationStatus.ReauthenticationRequired
                    : ProviderAuthenticationStatus.Failed;
            SetState(status, ProviderFailureMessages.ToVietnamese(failure.Category), failure.Category.ToString());
            await WriteDiagnosticAsync("GoogleAuthenticationFailed", "Không thể hoàn tất đăng nhập Google Drive.", failure, CancellationToken.None);
            throw failure;
        }
        finally
        {
            _interactiveGate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        SetState(ProviderAuthenticationStatus.Disconnecting, "Đang ngắt kết nối");
        try
        {
            await oauthClient.DisconnectAsync(cancellationToken);
        }
        finally
        {
            _session = null;
            _account = null;
            await accounts.RemoveAsync(AccountRecordId, CancellationToken.None);
            SetState(ProviderAuthenticationStatus.Disconnected, "Chưa kết nối");
            await diagnostics.WriteAsync("GoogleAccountDisconnected", "Đã xóa thông tin đăng nhập Google Drive lưu cục bộ; dữ liệu đám mây không bị thay đổi.", null, CancellationToken.None);
        }
    }

    public async Task<IGoogleDriveSession> GetRequiredSessionAsync(CancellationToken cancellationToken)
    {
        if (_session is not null) return _session;
        _ = await GetCachedAccountAsync(cancellationToken);
        return _session ?? throw new ProviderOperationException(
            ProviderFailureCategory.AuthenticationRequired,
            ProviderFailureMessages.ToVietnamese(ProviderFailureCategory.AuthenticationRequired));
    }

    private async Task<StorageAccount> CompleteSessionAsync(IGoogleDriveSession session, string eventType, CancellationToken cancellationToken)
    {
        var profile = await session.GetAccountProfileAsync(cancellationToken);
        _session = session;
        _account = new StorageAccount(AccountRecordId, Provider, profile.AccountId, profile.DisplayName, true, DateTimeOffset.UtcNow, profile.EmailAddress);
        await accounts.UpsertAsync(_account, cancellationToken);
        SetState(ProviderAuthenticationStatus.Connected, "Đã kết nối");
        await diagnostics.WriteAsync(eventType, "Đã kết nối Google Drive với quyền chỉ đọc.", "scope=drive.readonly", cancellationToken);
        return _account;
    }

    private async Task SafeClearAsync(CancellationToken cancellationToken)
    {
        try { await oauthClient.DisconnectAsync(cancellationToken); }
        catch { }
        _session = null;
        _account = null;
        await accounts.RemoveAsync(AccountRecordId, CancellationToken.None);
    }

    private Task WriteDiagnosticAsync(string eventType, string message, ProviderOperationException failure, CancellationToken cancellationToken) =>
        diagnostics.WriteAsync(eventType, message, $"category={failure.Category}; exception={failure.InnerException?.GetType().Name ?? failure.GetType().Name}", cancellationToken);

    private void SetState(ProviderAuthenticationStatus status, string message, string? category = null)
    {
        State = new ProviderAuthenticationState(status, message, category);
        StateChanged?.Invoke(State);
    }

    private const string AccountRecordId = "google:current";

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _interactiveGate.Dispose();
        _session = null;
        if (oauthClient is IAsyncDisposable disposable) await disposable.DisposeAsync();
    }
}

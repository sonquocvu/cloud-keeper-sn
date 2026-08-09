using System.Diagnostics;
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
    private const string AccountRecordId = "google:current";
    private readonly SemaphoreSlim _interactiveGate = new(1, 1);
    private readonly SemaphoreSlim _publicationGate = new(1, 1);
    private IGoogleDriveSession? _session;
    private StorageAccount? _account;
    private long _operationVersion;
    private int _disposed;

    public string ProviderId => Provider;
    public bool IsConfigured => oauthClient.IsConfigured;
    public string? ConfigurationMessage => oauthClient.ConfigurationMessage;
    public ProviderAuthenticationState State { get; private set; } =
        new(ProviderAuthenticationStatus.Disconnected, "Chưa kết nối");
    public event Action<ProviderAuthenticationState>? StateChanged;
    public event Action? ConfigurationChanged
    {
        add => oauthClient.ConfigurationChanged += value;
        remove => oauthClient.ConfigurationChanged -= value;
    }
    public StorageAccount? CurrentAccount => _account;

    public async Task<StorageAccount?> GetCachedAccountAsync(CancellationToken cancellationToken)
    {
        var operationVersion = Interlocked.Read(ref _operationVersion);
        var attemptId = Guid.NewGuid();
        var elapsed = Stopwatch.StartNew();
        if (!IsConfigured)
        {
            SetState(ProviderAuthenticationStatus.Disconnected, ConfigurationMessage!);
            return null;
        }

        IGoogleDriveSession? restored = null;
        try
        {
            restored = await oauthClient.RestoreAsync(cancellationToken);
            if (restored is null)
            {
                if (IsCurrent(operationVersion))
                    SetState(ProviderAuthenticationStatus.Disconnected, "Chưa kết nối");
                return CurrentAccount;
            }

            if (!IsCurrent(operationVersion))
            {
                await SafeDisposeSessionAsync(restored);
                return CurrentAccount;
            }

            var account = await CompleteSessionAsync(
                restored,
                operationVersion,
                attemptId,
                elapsed,
                "GoogleTokenRestored",
                cancellationToken);
            restored = null;
            return account;
        }
        catch (StaleAuthenticationOperationException)
        {
            if (restored is not null) await SafeDisposeSessionAsync(restored);
            return CurrentAccount;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            if (restored is not null) await SafeDisposeSessionAsync(restored);
            if (!IsCurrent(operationVersion)) return CurrentAccount;
            var failure = GoogleProviderExceptionMapper.Map(exception);
            await SafeClearAsync(cancellationToken);
            SetState(ProviderAuthenticationStatus.ReauthenticationRequired,
                ProviderFailureMessages.ToVietnamese(failure.Category), failure.Category.ToString());
            await SafeDiagnosticAsync("GoogleTokenRestoreFailed",
                "Không thể khôi phục phiên Google Drive; cần đăng nhập lại.",
                DiagnosticDetails(attemptId, "RestoreFailed", elapsed, failure), cancellationToken);
            return null;
        }
    }

    public async Task<StorageAccount> ConnectAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            throw new ProviderOperationException(ProviderFailureCategory.AuthenticationRequired, ConfigurationMessage!);
        if (!await _interactiveGate.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("Một thao tác đăng nhập Google Drive khác đang diễn ra.");

        var operationVersion = Interlocked.Increment(ref _operationVersion);
        var attemptId = Guid.NewGuid();
        var elapsed = Stopwatch.StartNew();
        IGoogleDriveSession? pendingSession = null;
        try
        {
            SetState(ProviderAuthenticationStatus.OpeningBrowser,
                "Đang mở trình duyệt để đăng nhập", attemptId: attemptId);
            await SafeDiagnosticAsync("GoogleAuthenticationStarted",
                "Đã bắt đầu đăng nhập Google Drive bằng trình duyệt hệ thống.",
                DiagnosticDetails(attemptId, "OpeningBrowser", elapsed), cancellationToken);

            pendingSession = await oauthClient.AuthorizeAsync(
                (stage, token) => ReportOAuthStageAsync(stage, attemptId, elapsed, operationVersion, token),
                cancellationToken);

            var account = await CompleteSessionAsync(
                pendingSession,
                operationVersion,
                attemptId,
                elapsed,
                "GoogleAuthenticationCompleted",
                cancellationToken);
            pendingSession = null;
            return account;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (pendingSession is not null) await SafeDisposeSessionAsync(pendingSession);
            await SafeClearAsync(CancellationToken.None);
            SetState(ProviderAuthenticationStatus.Cancelled, "Đã hủy đăng nhập", attemptId: attemptId);
            await SafeDiagnosticAsync("GoogleAuthenticationCancelled", "Đã hủy đăng nhập Google Drive.",
                DiagnosticDetails(attemptId, "Cancelled", elapsed), CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            if (pendingSession is not null) await SafeDisposeSessionAsync(pendingSession);
            var failure = GoogleProviderExceptionMapper.Map(exception);
            await SafeClearAsync(CancellationToken.None);
            var status = failure.Category is ProviderFailureCategory.AuthorizationCancelled
                ? ProviderAuthenticationStatus.Cancelled
                : failure.Category is ProviderFailureCategory.AuthorizationRevoked or ProviderFailureCategory.AuthenticationRequired
                    ? ProviderAuthenticationStatus.ReauthenticationRequired
                    : ProviderAuthenticationStatus.Failed;
            SetState(status, ProviderFailureMessages.ToVietnamese(failure.Category), failure.Category.ToString(), attemptId: attemptId);
            await SafeDiagnosticAsync("GoogleAuthenticationFailed", "Không thể hoàn tất đăng nhập Google Drive.",
                DiagnosticDetails(attemptId, "Failed", elapsed, failure), CancellationToken.None);
            throw failure;
        }
        finally
        {
            _interactiveGate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _operationVersion);
        SetState(ProviderAuthenticationStatus.Disconnecting, "Đang ngắt kết nối");
        try
        {
            await SafeDisposeCurrentSessionAsync();
            await oauthClient.DisconnectAsync(cancellationToken);
        }
        finally
        {
            _account = null;
            await accounts.RemoveAsync(AccountRecordId, CancellationToken.None);
            SetState(ProviderAuthenticationStatus.Disconnected, "Chưa kết nối");
            await SafeDiagnosticAsync("GoogleAccountDisconnected",
                "Đã xóa thông tin đăng nhập Google Drive lưu cục bộ; dữ liệu đám mây không bị thay đổi.", null,
                CancellationToken.None);
        }
    }

    public async Task DisconnectLocalAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _operationVersion);
        SetState(ProviderAuthenticationStatus.Disconnecting, "Đang ngắt kết nối cục bộ");
        try
        {
            await SafeDisposeCurrentSessionAsync();
            await oauthClient.ClearLocalAuthorizationAsync(cancellationToken);
        }
        finally
        {
            _account = null;
            await accounts.RemoveAsync(AccountRecordId, CancellationToken.None);
            SetState(ProviderAuthenticationStatus.Disconnected, "Chưa kết nối");
            await SafeDiagnosticAsync("GoogleAccountDisconnectedLocally",
                "Đã xóa authorization cache Google Drive cục bộ để thay đổi cấu hình OAuth; quyền trên Google và dữ liệu đám mây không bị thay đổi.",
                null, CancellationToken.None);
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

    private async Task<StorageAccount> CompleteSessionAsync(
        IGoogleDriveSession session,
        long operationVersion,
        Guid attemptId,
        Stopwatch elapsed,
        string completionEventType,
        CancellationToken cancellationToken)
    {
        EnsureCurrent(operationVersion);
        SetState(ProviderAuthenticationStatus.LoadingAccount, "Đang tải thông tin tài khoản", attemptId: attemptId);
        var profile = await session.GetAccountProfileAsync(cancellationToken);
        await SafeDiagnosticAsync("GoogleAccountIdentityRetrieved", "Đã tải thông tin định danh tài khoản Google.",
            DiagnosticDetails(attemptId, "LoadingAccount", elapsed), cancellationToken);

        EnsureCurrent(operationVersion);
        SetState(ProviderAuthenticationStatus.VerifyingDrive, "Đang xác minh quyền đọc Google Drive", attemptId: attemptId);
        await session.VerifyReadOnlyAccessAsync(cancellationToken);
        await SafeDiagnosticAsync("GoogleDriveReadOnlyVerified", "Đã xác minh truy cập siêu dữ liệu Google Drive chỉ đọc.",
            DiagnosticDetails(attemptId, "VerifyingDrive", elapsed), cancellationToken);

        EnsureCurrent(operationVersion);
        var account = new StorageAccount(
            AccountRecordId, Provider, profile.AccountId, profile.DisplayName, true, DateTimeOffset.UtcNow, profile.EmailAddress);
        await _publicationGate.WaitAsync(cancellationToken);
        try
        {
            EnsureCurrent(operationVersion);
            await accounts.UpsertAsync(account, cancellationToken);
            EnsureCurrent(operationVersion);

            var previous = Interlocked.Exchange(ref _session, session);
            _account = account;
            if (previous is not null && !ReferenceEquals(previous, session)) await SafeDisposeSessionAsync(previous);
            SetState(ProviderAuthenticationStatus.Connected, "Đã kết nối", account: account, attemptId: attemptId);
        }
        finally
        {
            _publicationGate.Release();
        }
        await SafeDiagnosticAsync("GoogleAccountStatePublished", "Đã công bố trạng thái tài khoản Google Drive đã kết nối.",
            DiagnosticDetails(attemptId, "Connected", elapsed), cancellationToken);
        await SafeDiagnosticAsync(completionEventType, "Đã kết nối Google Drive với quyền chỉ đọc.",
            DiagnosticDetails(attemptId, "Completed", elapsed, extra: "scope=drive.readonly"), cancellationToken);
        return account;
    }

    private async Task ReportOAuthStageAsync(
        GoogleOAuthStage stage,
        Guid attemptId,
        Stopwatch elapsed,
        long operationVersion,
        CancellationToken cancellationToken)
    {
        EnsureCurrent(operationVersion);
        var (status, message, eventType, diagnosticMessage) = stage switch
        {
            GoogleOAuthStage.WaitingForCallback => (ProviderAuthenticationStatus.WaitingForCallback,
                "Đang chờ phản hồi đăng nhập từ trình duyệt", "GoogleOAuthBrowserLaunched",
                "Đã chuyển yêu cầu đăng nhập sang trình duyệt hệ thống và đang chờ callback."),
            GoogleOAuthStage.CallbackReceived => (ProviderAuthenticationStatus.WaitingForCallback,
                "Đã nhận phản hồi, đang kiểm tra tính hợp lệ", "GoogleOAuthCallbackReceived",
                "Đã nhận callback OAuth trên loopback listener."),
            GoogleOAuthStage.StateValidated => (ProviderAuthenticationStatus.WaitingForCallback,
                "Phản hồi hợp lệ, đang chuẩn bị trao đổi mã", "GoogleOAuthStateValidated",
                "Đã xác minh state của callback OAuth."),
            GoogleOAuthStage.ExchangingCode => (ProviderAuthenticationStatus.ExchangingCode,
                "Đang trao đổi mã xác thực", "GoogleOAuthCodeExchangeStarted",
                "Đã bắt đầu trao đổi authorization code."),
            GoogleOAuthStage.AuthorizationStored => (ProviderAuthenticationStatus.ExchangingCode,
                "Đã lưu quyền an toàn, đang xác minh tài khoản", "GoogleOAuthAuthorizationStored",
                "Đã hoàn tất trao đổi mã và lưu authorization bằng kho thông tin được bảo vệ."),
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null)
        };
        SetState(status, message, attemptId: attemptId);
        await SafeDiagnosticAsync(eventType, diagnosticMessage,
            DiagnosticDetails(attemptId, stage.ToString(), elapsed), cancellationToken);
    }

    private async Task SafeClearAsync(CancellationToken cancellationToken)
    {
        await SafeDisposeCurrentSessionAsync();
        try { await oauthClient.ClearLocalAuthorizationAsync(cancellationToken); }
        catch { }
        _account = null;
        try { await accounts.RemoveAsync(AccountRecordId, CancellationToken.None); }
        catch { }
    }

    private async Task SafeDisposeCurrentSessionAsync()
    {
        var session = Interlocked.Exchange(ref _session, null);
        if (session is not null) await SafeDisposeSessionAsync(session);
    }

    private static async Task SafeDisposeSessionAsync(IGoogleDriveSession session)
    {
        try { await session.DisposeAsync(); }
        catch { }
    }

    private async Task SafeDiagnosticAsync(
        string eventType,
        string message,
        string? details,
        CancellationToken cancellationToken)
    {
        try { await diagnostics.WriteAsync(eventType, message, details, cancellationToken); }
        catch { }
    }

    private static string DiagnosticDetails(
        Guid attemptId,
        string stage,
        Stopwatch elapsed,
        ProviderOperationException? failure = null,
        string? extra = null)
    {
        var details = $"attempt={attemptId:N}; stage={stage}; elapsedMs={elapsed.ElapsedMilliseconds}";
        if (failure is not null)
            details += $"; category={failure.Category}; exception={failure.InnerException?.GetType().Name ?? failure.GetType().Name}";
        if (!string.IsNullOrWhiteSpace(extra)) details += $"; {extra}";
        return details;
    }

    private bool IsCurrent(long operationVersion) => Interlocked.Read(ref _operationVersion) == operationVersion;

    private void EnsureCurrent(long operationVersion)
    {
        if (!IsCurrent(operationVersion)) throw new StaleAuthenticationOperationException();
    }

    private void SetState(
        ProviderAuthenticationStatus status,
        string message,
        string? category = null,
        StorageAccount? account = null,
        Guid? attemptId = null)
    {
        State = new ProviderAuthenticationState(status, message, category, account, attemptId);
        var handlers = StateChanged;
        if (handlers is null) return;
        foreach (Action<ProviderAuthenticationState> handler in handlers.GetInvocationList())
        {
            try { handler(State); }
            catch { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await SafeDisposeCurrentSessionAsync();
        _interactiveGate.Dispose();
        _publicationGate.Dispose();
        if (oauthClient is IAsyncDisposable disposable) await disposable.DisposeAsync();
    }

    private sealed class StaleAuthenticationOperationException : OperationCanceledException;
}

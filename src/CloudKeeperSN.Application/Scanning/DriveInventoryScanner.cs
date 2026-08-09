using System.Diagnostics;
using CloudKeeperSN.Application.Persistence;
using CloudKeeperSN.Application.Storage;
using CloudKeeperSN.Domain.Scanning;
using CloudKeeperSN.Domain.Storage;

namespace CloudKeeperSN.Application.Scanning;

public sealed record DriveInventoryPage(IReadOnlyList<DriveInventoryItem> Items, string? NextPageToken, bool IsIncomplete = false);

public interface IDriveInventorySource
{
    Task<DriveStorageInformation> GetStorageInformationAsync(CancellationToken cancellationToken);
    Task<DriveInventoryPage> GetInventoryPageAsync(Guid scanId, string providerAccountId, string? pageToken, CancellationToken cancellationToken);
}

public enum DriveInventoryScanStatus
{
    Idle,
    ValidatingSession,
    LoadingStorageInformation,
    Scanning,
    BuildingHierarchy,
    SavingSnapshot,
    Completed,
    Cancelled,
    Failed,
    RequiresReauthentication
}

public sealed record DriveInventoryScanState(
    DriveInventoryScanStatus Status,
    string VietnameseMessage,
    int ProcessedItems = 0,
    int PageNumber = 0,
    Guid? ScanId = null,
    DriveInventoryRun? LastSuccessfulRun = null,
    string? FailureCategory = null)
{
    public bool IsBusy => Status is DriveInventoryScanStatus.ValidatingSession or
        DriveInventoryScanStatus.LoadingStorageInformation or DriveInventoryScanStatus.Scanning or
        DriveInventoryScanStatus.BuildingHierarchy or DriveInventoryScanStatus.SavingSnapshot;
}

public interface IDriveInventoryScanner
{
    DriveInventoryScanState State { get; }
    event Action<DriveInventoryScanState>? StateChanged;
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<DriveInventoryRun?> GetLatestSuccessfulAsync(string providerAccountId, CancellationToken cancellationToken);
    Task<IReadOnlyList<DriveInventoryRun>> GetRecentAsync(int maximumCount, CancellationToken cancellationToken);
    Task<DriveInventoryRun> ScanAsync(CancellationToken cancellationToken);
}

public sealed class DriveInventoryScanner(
    IProviderAuthenticationService authentication,
    IDriveInventorySource source,
    IDriveInventoryRepository repository,
    IProviderDiagnostics diagnostics) : IDriveInventoryScanner
{
    private const int MaximumPages = 100_000;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private long _operationVersion;

    public DriveInventoryScanState State { get; private set; } = new(DriveInventoryScanStatus.Idle, "Sẵn sàng quét Google Drive");
    public event Action<DriveInventoryScanState>? StateChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken) =>
        await repository.RecoverInterruptedAsync(DateTimeOffset.UtcNow, cancellationToken);

    public Task<DriveInventoryRun?> GetLatestSuccessfulAsync(string providerAccountId, CancellationToken cancellationToken) =>
        repository.GetLatestSuccessfulAsync(providerAccountId, cancellationToken);

    public Task<IReadOnlyList<DriveInventoryRun>> GetRecentAsync(int maximumCount, CancellationToken cancellationToken) =>
        repository.GetRecentAsync(maximumCount, cancellationToken);

    public async Task<DriveInventoryRun> ScanAsync(CancellationToken cancellationToken)
    {
        if (!await _scanGate.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("Một lần quét Google Drive khác đang diễn ra.");

        var version = Interlocked.Increment(ref _operationVersion);
        var scanId = Guid.NewGuid();
        var elapsed = Stopwatch.StartNew();
        DriveInventoryRun? stagingRun = null;
        try
        {
            Publish(new(DriveInventoryScanStatus.ValidatingSession, "Đang kiểm tra tài khoản…", ScanId: scanId));
            var account = authentication.State.Account ?? await authentication.GetCachedAccountAsync(cancellationToken);
            if (account is not { IsConnected: true })
                throw new ProviderOperationException(ProviderFailureCategory.AuthenticationRequired,
                    ProviderFailureMessages.ToVietnamese(ProviderFailureCategory.AuthenticationRequired));
            EnsureCurrent(version);
            await SafeDiagnosticAsync("GoogleDriveScanSessionValidated", "Đã xác minh phiên Google Drive cho lần quét.",
                Details(scanId, "ValidatingSession", elapsed), cancellationToken);

            stagingRun = new DriveInventoryRun(scanId, "google-drive", account.ProviderAccountId, DateTimeOffset.UtcNow, null,
                DriveInventoryRunStatus.Scanning, 0, 0, 0, 0, 0, 0, 0, 0, 0, null, false, null);
            await PersistAsync(() => repository.BeginAsync(stagingRun, cancellationToken));
            await SafeDiagnosticAsync("GoogleDriveScanStarted", "Đã bắt đầu quét danh mục Google Drive chỉ đọc.",
                Details(scanId, "Started", elapsed), cancellationToken);

            Publish(new(DriveInventoryScanStatus.LoadingStorageInformation, "Đang đọc thông tin dung lượng…", ScanId: scanId));
            var storage = await source.GetStorageInformationAsync(cancellationToken);
            await SafeDiagnosticAsync("GoogleDriveStorageLoaded", "Đã tải thông tin dung lượng Google Drive.",
                Details(scanId, "LoadingStorageInformation", elapsed), cancellationToken);

            var nodes = new List<DriveHierarchyNode>();
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            var seenTokens = new HashSet<string>(StringComparer.Ordinal);
            var pageNumber = 0;
            var processed = 0;
            var folders = 0;
            var files = 0;
            var nativeFiles = 0;
            var shortcuts = 0;
            var unknownSizes = 0;
            var eligible = 0;
            long knownBytes = 0;
            string? token = null;

            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureCurrent(version);
                if (++pageNumber > MaximumPages) throw InvalidPagination("too-many-pages");
                Publish(new(DriveInventoryScanStatus.Scanning,
                    processed == 0 ? "Đang quét Google Drive…" : $"Đang xử lý {processed:N0} mục…",
                    processed, pageNumber, scanId));
                var page = await source.GetInventoryPageAsync(scanId, account.ProviderAccountId, token, cancellationToken);
                if (page.IsIncomplete) throw InvalidPagination("incomplete-search");
                var uniqueItems = page.Items.Where(item => seenIds.Add(item.FileId)).ToArray();
                await PersistAsync(() => repository.AppendBatchAsync(scanId, uniqueItems, cancellationToken));
                await SafeDiagnosticAsync("GoogleDriveInventoryBatchPersisted", "Đã lưu một lô siêu dữ liệu Google Drive vào snapshot tạm.",
                    Details(scanId, "Scanning", elapsed, $"page={pageNumber}; batch={uniqueItems.Length}"), cancellationToken);
                foreach (var item in uniqueItems)
                {
                    nodes.Add(new DriveHierarchyNode(item.FileId, item.Name, item.ParentId, item.IsShared, item.IsOwnedByUser));
                    processed++;
                    switch (item.Kind)
                    {
                        case DriveInventoryItemKind.Folder: folders++; break;
                        case DriveInventoryItemKind.GoogleWorkspaceFile: nativeFiles++; break;
                        case DriveInventoryItemKind.Shortcut: shortcuts++; break;
                        default: files++; break;
                    }
                    if (item.Kind is DriveInventoryItemKind.File or DriveInventoryItemKind.GoogleWorkspaceFile)
                    {
                        if (item.Size is { } size) knownBytes = checked(knownBytes + size);
                        else unknownSizes++;
                    }
                    if (item.IsBackupEligible) eligible++;
                }
                await SafeDiagnosticAsync("GoogleDriveMetadataPageRetrieved", "Đã tải và lưu một trang siêu dữ liệu Google Drive.",
                    Details(scanId, "Scanning", elapsed, $"page={pageNumber}; batch={uniqueItems.Length}; processed={processed}"), cancellationToken);
                token = string.IsNullOrWhiteSpace(page.NextPageToken) ? null : page.NextPageToken;
                if (token is not null && !seenTokens.Add(token)) throw InvalidPagination("repeated-page-token");
            } while (token is not null);

            Publish(new(DriveInventoryScanStatus.BuildingHierarchy, "Đang xây dựng cấu trúc thư mục…", processed, pageNumber, scanId));
            var hierarchy = new DriveHierarchyBuilder().Build(nodes);
            await PersistAsync(() => repository.UpdateHierarchyAsync(scanId, hierarchy, cancellationToken));
            await SafeDiagnosticAsync("GoogleDriveHierarchyCompleted", "Đã xây dựng cấu trúc thư mục an toàn.",
                Details(scanId, "BuildingHierarchy", elapsed, $"unresolved={hierarchy.UnresolvedCount}"), cancellationToken);

            Publish(new(DriveInventoryScanStatus.SavingSnapshot, "Đang lưu kết quả quét…", processed, pageNumber, scanId));
            var completed = stagingRun with
            {
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Status = DriveInventoryRunStatus.Completed,
                TotalItems = processed,
                FolderCount = folders,
                FileCount = files,
                KnownBytes = knownBytes,
                UnknownSizeCount = unknownSizes,
                GoogleWorkspaceFileCount = nativeFiles,
                ShortcutCount = shortcuts,
                UnresolvedCount = hierarchy.UnresolvedCount,
                BackupEligibleCount = eligible,
                IsComplete = true,
                StorageInformation = storage
            };
            EnsureCurrent(version);
            await PersistAsync(() => repository.CompleteAsync(completed, cancellationToken));
            Publish(new(DriveInventoryScanStatus.Completed, "Đã quét Google Drive thành công.", processed, pageNumber, scanId, completed));
            await SafeDiagnosticAsync("GoogleDriveSnapshotCommitted", "Đã hoàn tất và công bố snapshot Google Drive.",
                Details(scanId, "Completed", elapsed, $"items={processed}"), cancellationToken);
            return completed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (stagingRun is not null)
                await SafeMarkIncompleteAsync(scanId, DriveInventoryRunStatus.Cancelled, "UserCancelled");
            var previous = stagingRun is null ? null : await SafeGetLatestSuccessfulAsync(stagingRun.ProviderAccountId);
            Publish(new(DriveInventoryScanStatus.Cancelled,
                "Đã hủy quá trình quét. Kết quả quét thành công trước đó vẫn được giữ nguyên.", ScanId: scanId, LastSuccessfulRun: previous));
            await SafeDiagnosticAsync("GoogleDriveScanCancelled", "Đã hủy quét Google Drive; snapshot trước không thay đổi.",
                Details(scanId, "Cancelled", elapsed), CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            var persistenceFailure = exception is DriveInventoryPersistenceException;
            var hierarchyFailure = State.Status == DriveInventoryScanStatus.BuildingHierarchy && exception is not ProviderOperationException;
            var failure = exception as ProviderOperationException ??
                new ProviderOperationException(ProviderFailureCategory.UnknownProviderError,
                    ProviderFailureMessages.ToVietnamese(ProviderFailureCategory.UnknownProviderError), exception);
            var requiresAuthentication = failure.Category is ProviderFailureCategory.AuthenticationRequired or ProviderFailureCategory.AuthorizationRevoked;
            var safeFailureCategory = persistenceFailure ? "LocalDatabaseWriteFailed"
                : hierarchyFailure ? "HierarchyReconstructionFailed"
                : failure.Category.ToString();
            if (stagingRun is not null)
                await SafeMarkIncompleteAsync(scanId,
                    requiresAuthentication ? DriveInventoryRunStatus.RequiresReauthentication : DriveInventoryRunStatus.Failed,
                    safeFailureCategory);
            var previous = stagingRun is null ? null : await SafeGetLatestSuccessfulAsync(stagingRun.ProviderAccountId);
            var status = requiresAuthentication ? DriveInventoryScanStatus.RequiresReauthentication : DriveInventoryScanStatus.Failed;
            var message = persistenceFailure
                ? "Không thể lưu dữ liệu quét vào cơ sở dữ liệu cục bộ. Kết quả quét thành công trước đó vẫn được giữ; vui lòng kiểm tra quyền ghi và dung lượng đĩa rồi thử lại."
                : hierarchyFailure
                    ? "Không thể xây dựng cấu trúc thư mục Google Drive. Kết quả quét thành công trước đó vẫn được giữ; vui lòng thử lại."
                : FailureMessage(failure.Category);
            Publish(new(status, message, ScanId: scanId, LastSuccessfulRun: previous, FailureCategory: safeFailureCategory));
            await SafeDiagnosticAsync(requiresAuthentication ? "GoogleDriveScanReauthenticationRequired" : "GoogleDriveScanFailed",
                "Không thể hoàn tất quét Google Drive; snapshot trước không thay đổi.",
                Details(scanId, status.ToString(), elapsed, $"category={safeFailureCategory}; exception={failure.InnerException?.GetType().Name ?? failure.GetType().Name}"),
                CancellationToken.None);
            throw failure;
        }
        finally
        {
            _scanGate.Release();
        }
    }

    private void Publish(DriveInventoryScanState state)
    {
        State = state;
        var handlers = StateChanged;
        if (handlers is null) return;
        foreach (Action<DriveInventoryScanState> handler in handlers.GetInvocationList())
        {
            try { handler(state); } catch { }
        }
    }

    private async Task SafeMarkIncompleteAsync(Guid scanId, DriveInventoryRunStatus status, string category)
    {
        try { await repository.MarkIncompleteAsync(scanId, status, DateTimeOffset.UtcNow, category, CancellationToken.None); }
        catch { }
    }

    private async Task<DriveInventoryRun?> SafeGetLatestSuccessfulAsync(string providerAccountId)
    {
        try { return await repository.GetLatestSuccessfulAsync(providerAccountId, CancellationToken.None); }
        catch { return null; }
    }

    private static async Task PersistAsync(Func<Task> operation)
    {
        try { await operation(); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            throw new DriveInventoryPersistenceException("The local Drive inventory snapshot could not be persisted.", exception);
        }
    }

    private async Task SafeDiagnosticAsync(string eventType, string message, string? details, CancellationToken cancellationToken)
    {
        try { await diagnostics.WriteAsync(eventType, message, details, cancellationToken); } catch { }
    }

    private static string Details(Guid scanId, string stage, Stopwatch elapsed, string? extra = null) =>
        $"scan={scanId:N}; stage={stage}; elapsedMs={elapsed.ElapsedMilliseconds}" + (string.IsNullOrWhiteSpace(extra) ? string.Empty : $"; {extra}");

    private static ProviderOperationException InvalidPagination(string reason) => new(
        ProviderFailureCategory.InvalidProviderResponse,
        ProviderFailureMessages.ToVietnamese(ProviderFailureCategory.InvalidProviderResponse),
        new InvalidDataException(reason));

    private static string FailureMessage(ProviderFailureCategory category) => category switch
    {
        ProviderFailureCategory.AuthenticationRequired or ProviderFailureCategory.AuthorizationRevoked =>
            "Không thể quét Google Drive vì phiên đăng nhập đã hết hiệu lực. Vui lòng đăng nhập lại. Kết quả quét trước vẫn được giữ.",
        ProviderFailureCategory.NetworkUnavailable =>
            "Mất kết nối mạng trong khi quét. Kết quả quét thành công trước đó vẫn được giữ nguyên. Vui lòng kiểm tra mạng rồi thử lại.",
        ProviderFailureCategory.ProviderThrottled =>
            "Google Drive đang giới hạn yêu cầu. Kết quả quét trước vẫn được giữ; vui lòng thử lại sau.",
        ProviderFailureCategory.ServiceUnavailable =>
            "Google Drive tạm thời không khả dụng. Kết quả quét trước vẫn được giữ; vui lòng thử lại.",
        ProviderFailureCategory.RequestTimedOut =>
            "Yêu cầu Google Drive đã hết thời gian chờ. Kết quả quét trước vẫn được giữ; vui lòng thử lại.",
        ProviderFailureCategory.InvalidProviderResponse =>
            "Google Drive trả về dữ liệu hoặc phân trang không hợp lệ. Kết quả quét trước vẫn được giữ; vui lòng thử lại.",
        _ => "Không thể hoàn tất quét Google Drive. Kết quả quét thành công trước đó vẫn được giữ; vui lòng thử lại."
    };

    private bool IsCurrent(long version) => Interlocked.Read(ref _operationVersion) == version;
    private void EnsureCurrent(long version)
    {
        if (!IsCurrent(version)) throw new OperationCanceledException("The scan was superseded.");
    }
}

using CloudKeeperSN.App.Development;
using CloudKeeperSN.App.Services;
using CloudKeeperSN.App.UI.Theming;
using CloudKeeperSN.Application.Persistence;
using CloudKeeperSN.Domain.Backup;
using CloudKeeperSN.Domain.Storage;
using CloudKeeperSN.Domain.Transfers;

namespace CloudKeeperSN.App.Tests;

internal sealed class FakeStorageAccountRepository : IStorageAccountRepository
{
    private readonly Dictionary<string, StorageAccount> _accounts = new(StringComparer.Ordinal);
    public Task UpsertAsync(StorageAccount account, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); _accounts[account.Id] = account; return Task.CompletedTask; }
    public Task<IReadOnlyList<StorageAccount>> GetAllAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyList<StorageAccount>>(_accounts.Values.ToArray()); }
    public Task RemoveAsync(string id, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); _accounts.Remove(id); return Task.CompletedTask; }
}

internal sealed class FakeSettingRepository : IApplicationSettingRepository
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
    public Task<string?> GetAsync(string key, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Values.GetValueOrDefault(key)); }
    public Task SetAsync(string key, string value, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); Values[key] = value; return Task.CompletedTask; }
}

internal sealed class FakeThemeService(FakeSettingRepository settings) : IThemeService
{
    public ThemeMode CurrentMode { get; private set; } = ThemeMode.System;
    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public async Task ApplyAsync(ThemeMode mode, CancellationToken cancellationToken) { CurrentMode = mode; await settings.SetAsync("ui.theme", mode.ToString(), cancellationToken); }
}

internal sealed class FakeDialogService : IDialogService
{
    public bool ConfirmationResult { get; set; } = true;
    public List<ConfirmationRequest> Requests { get; } = [];
    public Task<bool> ConfirmAsync(ConfirmationRequest request, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); Requests.Add(request); return Task.FromResult(ConfirmationResult); }
    public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class FakeFolderPickerService : IFolderPickerService
{
    private readonly Queue<FolderSelection?> _selections = new();
    public void Enqueue(FolderSelection? selection) => _selections.Enqueue(selection);
    public Task<FolderSelection?> PickAsync(FolderPickerRequest request, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(_selections.Count == 0 ? null : _selections.Dequeue()); }
}

internal sealed class FakeDiagnosticExportService : IDiagnosticExportService
{
    public int Calls { get; private set; }
    public Task<string?> ExportAsync(IReadOnlyList<DemoBackupRun> runs, CancellationToken cancellationToken) { Calls++; return Task.FromResult<string?>("C:\\demo\\diagnostic.json"); }
}

internal sealed class FakeLocalDataService : ILocalDataService
{
    public string DatabasePath => "C:\\demo\\cloudkeeper.db";
    public string LogPath => "C:\\demo\\Logs";
    public string CachePath => "C:\\demo\\Cache";
    public int ClearCalls { get; private set; }
    public Task ClearCacheAsync(CancellationToken cancellationToken) { ClearCalls++; return Task.CompletedTask; }
}

internal sealed class ImmediateDelay : IDemoDelay
{
    public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.CompletedTask; }
}

internal sealed class UnusedTransferItemRepository : ITransferItemRepository
{
    public Task UpsertAsync(TransferItem item, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<TransferItem?> FindAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<IReadOnlyList<TransferItem>> GetRecoverableAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<int> RecoverInterruptedAsync(DateTimeOffset recoveredAtUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
}

internal static class AsyncTest
{
    public static async Task UntilAsync(Func<bool> condition, int timeoutMilliseconds = 4000)
    {
        var started = Environment.TickCount64;
        while (!condition())
        {
            if (Environment.TickCount64 - started > timeoutMilliseconds) throw new TimeoutException("The expected view-model state was not reached.");
            await Task.Delay(10);
        }
    }
}


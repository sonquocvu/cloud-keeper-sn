using CloudKeeperSN.App.Services;
using CloudKeeperSN.App.ViewModels;
using CloudKeeperSN.Application.Storage;
using CloudKeeperSN.Domain.Storage;

namespace CloudKeeperSN.App.Tests;

public sealed class FolderPickerViewModelTests
{
    [Fact]
    public async Task LoadsFoldersOnlyAndPreservesDuplicateNamesById()
    {
        var browser = new BrowserStub([
            Item("folder-a", "Trùng tên", StorageItemKind.Folder),
            Item("file", "Không hiển thị.txt", StorageItemKind.File),
            Item("folder-b", "Trùng tên", StorageItemKind.Folder)]);
        using var viewModel = Create(browser);

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal("Drive của tôi", viewModel.CurrentPath);
        Assert.Equal(["folder-a", "folder-b"], viewModel.Folders.Select(folder => folder.ItemId));
    }

    [Fact]
    public async Task ShowsProviderSpecificPermissionMessageAndAllowsRetry()
    {
        var browser = new BrowserStub([], new ProviderOperationException(ProviderFailureCategory.PermissionDenied, "technical"));
        using var viewModel = Create(browser);

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Contains("không có quyền đọc", viewModel.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(viewModel.RetryCommand.CanExecute(null));
    }

    [Fact]
    public async Task CancellationDoesNotPublishPartialFolderResults()
    {
        var browser = new BrowserStub([Item("first", "Đầu", StorageItemKind.Folder)], waitAfterFirst: true);
        using var viewModel = Create(browser);
        using var cancellation = new CancellationTokenSource();
        var loading = viewModel.LoadAsync(cancellation.Token);
        await browser.FirstItemYielded.Task.WaitAsync(TimeSpan.FromSeconds(2));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loading);
        Assert.Empty(viewModel.Folders);
        Assert.False(viewModel.IsLoading);
    }

    private static FolderPickerViewModel Create(IStorageBrowserCapability browser) => new(
        new FolderPickerRequest("google-drive", "account", "root", "Chọn nguồn", false), browser, null);

    private static StorageItem Item(string id, string name, StorageItemKind kind) => new()
    {
        ProviderId = "google-drive",
        ProviderAccountId = "account",
        ItemId = id,
        Name = name,
        Kind = kind
    };

    private sealed class BrowserStub(
        IReadOnlyList<StorageItem> items,
        Exception? failure = null,
        bool waitAfterFirst = false) : IStorageBrowserCapability
    {
        public TaskCompletionSource FirstItemYielded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<StorageItem> GetChildrenAsync(
            string providerAccountId,
            string parentItemId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (failure is not null) throw failure;
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
                FirstItemYielded.TrySetResult();
                if (waitAfterFirst) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
        }
    }
}

using CloudKeeperSN.App.Development;
using CloudKeeperSN.App.ViewModels;
using CloudKeeperSN.App.Views.Dialogs;
using CloudKeeperSN.Application.Storage;

namespace CloudKeeperSN.App.Services;

public sealed record FolderSelection(string ProviderId, string AccountId, string FolderId, string DisplayPath);
public sealed record FolderPickerRequest(string ProviderId, string AccountId, string RootFolderId, string Title, bool CanCreateFolder);

public interface IFolderPickerService
{
    Task<FolderSelection?> PickAsync(FolderPickerRequest request, CancellationToken cancellationToken);
}

public sealed class FolderPickerService(IEnumerable<IStorageProvider> providers) : IFolderPickerService
{
    public async Task<FolderSelection?> PickAsync(FolderPickerRequest request, CancellationToken cancellationToken)
    {
        var provider = providers.SingleOrDefault(candidate => candidate.Descriptor.ProviderId == request.ProviderId)
            ?? throw new InvalidOperationException($"Không có trình cung cấp thư mục cho {request.ProviderId}.");
        var browser = provider as IStorageBrowserCapability
            ?? throw new InvalidOperationException($"Trình cung cấp {request.ProviderId} không hỗ trợ duyệt thư mục.");
        var folderWriter = request.CanCreateFolder ? provider as IStorageFolderWriteCapability : null;
        var viewModel = new FolderPickerViewModel(request, browser, folderWriter);
        try
        {
            await viewModel.LoadAsync(cancellationToken);
            var dialog = new FolderPickerDialog(viewModel)
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            return dialog.ShowDialog() == true ? viewModel.SelectedFolder : null;
        }
        finally
        {
            viewModel.Dispose();
        }
    }
}

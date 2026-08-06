using CloudKeeperSN.App.Development;
using CloudKeeperSN.App.ViewModels;
using CloudKeeperSN.App.Views.Dialogs;
using CloudKeeperSN.Application.Storage;
using CloudKeeperSN.Providers.GoogleDrive.Fakes;
using CloudKeeperSN.Providers.OneDrive.Fakes;

namespace CloudKeeperSN.App.Services;

public sealed record FolderSelection(string ProviderId, string AccountId, string FolderId, string DisplayPath);
public sealed record FolderPickerRequest(string ProviderId, string AccountId, string RootFolderId, string Title, bool CanCreateFolder);

public interface IFolderPickerService
{
    Task<FolderSelection?> PickAsync(FolderPickerRequest request, CancellationToken cancellationToken);
}

public sealed class FolderPickerService(
    FakeGoogleDriveProvider googleDrive,
    FakeOneDriveProvider oneDrive) : IFolderPickerService
{
    public async Task<FolderSelection?> PickAsync(FolderPickerRequest request, CancellationToken cancellationToken)
    {
        IStorageBrowserCapability browser = request.ProviderId == "google-drive" ? googleDrive : oneDrive;
        var folderWriter = request.ProviderId == "one-drive" ? oneDrive : null;
        var viewModel = new FolderPickerViewModel(request, browser, folderWriter);
        await viewModel.LoadAsync(cancellationToken);
        var dialog = new FolderPickerDialog(viewModel)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        return dialog.ShowDialog() == true ? viewModel.SelectedFolder : null;
    }
}


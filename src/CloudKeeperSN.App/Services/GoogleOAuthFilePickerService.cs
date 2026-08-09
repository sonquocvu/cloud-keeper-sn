using Microsoft.Win32;

namespace CloudKeeperSN.App.Services;

public interface IGoogleOAuthFilePickerService
{
    Task<string?> PickAsync(CancellationToken cancellationToken);
}

public sealed class GoogleOAuthFilePickerService : IGoogleOAuthFilePickerService
{
    public Task<string?> PickAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dialog = new OpenFileDialog
        {
            Title = "Chọn file OAuth của Google",
            Filter = "File OAuth JSON (*.json)|*.json|Tất cả file (*.*)|*.*",
            DefaultExt = ".json",
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false
        };
        return Task.FromResult(dialog.ShowDialog(System.Windows.Application.Current.MainWindow) == true
            ? dialog.FileName
            : null);
    }
}

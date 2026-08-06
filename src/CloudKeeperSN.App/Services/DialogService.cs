using CloudKeeperSN.App.Views.Dialogs;

namespace CloudKeeperSN.App.Services;

public sealed record ConfirmationRequest(
    string Title,
    string Message,
    string ConfirmText,
    string CancelText = "Quay lại",
    bool IsDangerous = false,
    string? SupportingText = null);

public interface IDialogService
{
    Task<bool> ConfirmAsync(ConfirmationRequest request, CancellationToken cancellationToken);
    Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken);
}

public sealed class DialogService : IDialogService
{
    public Task<bool> ConfirmAsync(ConfirmationRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dialog = new ConfirmationDialog(request)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        return Task.FromResult(dialog.ShowDialog() == true);
    }

    public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var request = new ConfirmationRequest(title, message, "Đã hiểu", string.Empty);
        var dialog = new ConfirmationDialog(request, showCancel: false)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        dialog.ShowDialog();
        return Task.CompletedTask;
    }
}


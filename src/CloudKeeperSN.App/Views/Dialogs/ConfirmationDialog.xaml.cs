using System.Windows;
using CloudKeeperSN.App.Services;

namespace CloudKeeperSN.App.Views.Dialogs;

public partial class ConfirmationDialog : Window
{
    public ConfirmationDialog(ConfirmationRequest request, bool showCancel = true)
    {
        InitializeComponent();
        Title = request.Title;
        TitleText.Text = request.Title;
        MessageText.Text = request.Message;
        SupportingText.Text = request.SupportingText ?? string.Empty;
        SupportingText.Visibility = string.IsNullOrWhiteSpace(request.SupportingText) ? Visibility.Collapsed : Visibility.Visible;
        ConfirmButton.Content = request.ConfirmText;
        ConfirmButton.Style = (Style)FindResource(request.IsDangerous ? "DangerButton" : "PrimaryButton");
        CancelButton.Content = request.CancelText;
        CancelButton.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ConfirmClick(object sender, RoutedEventArgs e) => DialogResult = true;
    private void CancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}

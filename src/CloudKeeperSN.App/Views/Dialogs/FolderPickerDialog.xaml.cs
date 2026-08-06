using System.Windows;
using CloudKeeperSN.App.ViewModels;
namespace CloudKeeperSN.App.Views.Dialogs;

public partial class FolderPickerDialog : Window
{
    private readonly FolderPickerViewModel _viewModel;
    public FolderPickerDialog(FolderPickerViewModel viewModel) { InitializeComponent(); _viewModel = viewModel; DataContext = viewModel; Title = viewModel.Title; Closed += (_, _) => _viewModel.Dispose(); }
    private void SelectClick(object sender, RoutedEventArgs e) => DialogResult = true;
    private void CancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}


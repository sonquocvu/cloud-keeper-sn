using System.ComponentModel;
using System.Windows;
using CloudKeeperSN.App.UI.Windowing;
using CloudKeeperSN.App.ViewModels;

namespace CloudKeeperSN.App;

public partial class MainWindow : Window
{
    private readonly IWindowPlacementService _placementService;
    private bool _closing;

    public MainWindowViewModel ViewModel { get; }

    public MainWindow(MainWindowViewModel viewModel, IWindowPlacementService placementService)
    {
        InitializeComponent();
        ViewModel = viewModel;
        _placementService = placementService;
        DataContext = viewModel;
        Loaded += RestorePlacementAsync;
        Closing += SavePlacementAsync;
    }

    private async void RestorePlacementAsync(object sender, RoutedEventArgs e)
    {
        var saved = await _placementService.LoadAsync(CancellationToken.None);
        if (saved is null)
        {
            var fallback = WindowPlacementValidator.EnsureVisible(
                new WindowPlacement(double.NaN, double.NaN, 0, 0, false),
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);
            Apply(fallback);
            return;
        }

        Apply(WindowPlacementValidator.EnsureVisible(
            saved,
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight));
    }

    private async void SavePlacementAsync(object? sender, CancelEventArgs e)
    {
        if (_closing) return;
        _closing = true;
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        await _placementService.SaveAsync(
            new WindowPlacement(bounds.Left, bounds.Top, bounds.Width, bounds.Height, WindowState == WindowState.Maximized),
            CancellationToken.None);
    }

    private void Apply(WindowPlacement placement)
    {
        Left = placement.Left;
        Top = placement.Top;
        Width = placement.Width;
        Height = placement.Height;
        if (placement.IsMaximized) WindowState = WindowState.Maximized;
    }
}


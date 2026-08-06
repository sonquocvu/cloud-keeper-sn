using System.Windows;
using System.Windows.Controls;
namespace CloudKeeperSN.App.Controls;

public partial class LoadingIndicator : UserControl
{
    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(nameof(Message), typeof(string), typeof(LoadingIndicator), new PropertyMetadata("Đang tải…"));
    public string Message { get => (string)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public LoadingIndicator() => InitializeComponent();
}

using System.Windows;
using System.Windows.Controls;
using CloudKeeperSN.App.Presentation;
namespace CloudKeeperSN.App.Controls;

public partial class InlineNotification : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(InlineNotification), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(nameof(Message), typeof(string), typeof(InlineNotification), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(nameof(Tone), typeof(StatusTone), typeof(InlineNotification), new PropertyMetadata(StatusTone.Information));
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Message { get => (string)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public StatusTone Tone { get => (StatusTone)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
    public InlineNotification() => InitializeComponent();
}


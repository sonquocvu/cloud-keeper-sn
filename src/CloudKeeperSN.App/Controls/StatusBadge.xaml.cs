using System.Windows;
using System.Windows.Controls;
using CloudKeeperSN.App.Presentation;

namespace CloudKeeperSN.App.Controls;

public partial class StatusBadge : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(StatusBadge), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(nameof(Tone), typeof(StatusTone), typeof(StatusBadge), new PropertyMetadata(StatusTone.Neutral));
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public StatusTone Tone { get => (StatusTone)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
    public StatusBadge() => InitializeComponent();
}


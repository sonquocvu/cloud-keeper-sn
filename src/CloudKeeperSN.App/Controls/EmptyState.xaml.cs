using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace CloudKeeperSN.App.Controls;

public partial class EmptyState : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(EmptyState), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(nameof(Message), typeof(string), typeof(EmptyState), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty IconGlyphProperty = DependencyProperty.Register(nameof(IconGlyph), typeof(string), typeof(EmptyState), new PropertyMetadata("\uE946"));
    public static readonly DependencyProperty ActionTextProperty = DependencyProperty.Register(nameof(ActionText), typeof(string), typeof(EmptyState), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty ActionCommandProperty = DependencyProperty.Register(nameof(ActionCommand), typeof(ICommand), typeof(EmptyState));
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Message { get => (string)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public string IconGlyph { get => (string)GetValue(IconGlyphProperty); set => SetValue(IconGlyphProperty, value); }
    public string ActionText { get => (string)GetValue(ActionTextProperty); set => SetValue(ActionTextProperty, value); }
    public ICommand? ActionCommand { get => (ICommand?)GetValue(ActionCommandProperty); set => SetValue(ActionCommandProperty, value); }
    public EmptyState() => InitializeComponent();
}


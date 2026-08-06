namespace CloudKeeperSN.App.ViewModels;

public abstract class PageViewModel(string key, string title, string subtitle) : ObservableObject
{
    public string Key { get; } = key;
    public string Title { get; } = title;
    public string Subtitle { get; } = subtitle;
    public virtual Task LoadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class NavigationItemViewModel(string key, string title, string iconGlyph, string toolTip) : ObservableObject
{
    private bool _isSelected;
    public string Key { get; } = key;
    public string Title { get; } = title;
    public string IconGlyph { get; } = iconGlyph;
    public string ToolTip { get; } = toolTip;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}

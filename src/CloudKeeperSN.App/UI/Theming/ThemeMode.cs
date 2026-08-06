namespace CloudKeeperSN.App.UI.Theming;

public enum ThemeMode
{
    System,
    Light,
    Dark
}

public sealed record ThemeOption(ThemeMode Value, string VietnameseLabel);


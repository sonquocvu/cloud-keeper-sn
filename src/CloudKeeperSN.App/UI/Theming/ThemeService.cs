using System.Windows;
using CloudKeeperSN.Application.Persistence;
using Microsoft.Win32;

namespace CloudKeeperSN.App.UI.Theming;

public interface IThemeService
{
    ThemeMode CurrentMode { get; }
    Task InitializeAsync(CancellationToken cancellationToken);
    Task ApplyAsync(ThemeMode mode, CancellationToken cancellationToken);
}

public sealed class ThemeService(IApplicationSettingRepository settings) : IThemeService
{
    private const string SettingKey = "ui.theme";
    private const string LightThemePath = "/CloudKeeperSN.App;component/UI/Themes/LightTheme.xaml";
    private const string DarkThemePath = "/CloudKeeperSN.App;component/UI/Themes/DarkTheme.xaml";

    public ThemeMode CurrentMode { get; private set; } = ThemeMode.System;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var persisted = await settings.GetAsync(SettingKey, cancellationToken);
        var mode = Enum.TryParse<ThemeMode>(persisted, out var parsed) ? parsed : ThemeMode.System;
        ApplyResources(mode);
        CurrentMode = mode;
    }

    public async Task ApplyAsync(ThemeMode mode, CancellationToken cancellationToken)
    {
        ApplyResources(mode);
        CurrentMode = mode;
        await settings.SetAsync(SettingKey, mode.ToString(), cancellationToken);
    }

    private static void ApplyResources(ThemeMode mode)
    {
        var effectiveMode = mode == ThemeMode.System ? GetWindowsTheme() : mode;
        var source = new Uri(effectiveMode == ThemeMode.Dark ? DarkThemePath : LightThemePath, UriKind.Relative);
        var dictionaries = System.Windows.Application.Current.Resources.MergedDictionaries;
        var current = dictionaries.FirstOrDefault(IsThemeDictionary);
        if (current is not null) dictionaries.Remove(current);
        dictionaries.Insert(0, new ResourceDictionary { Source = source });
    }

    private static bool IsThemeDictionary(ResourceDictionary dictionary)
    {
        var value = dictionary.Source?.OriginalString;
        return value is not null && (value.EndsWith("LightTheme.xaml", StringComparison.OrdinalIgnoreCase) ||
                                     value.EndsWith("DarkTheme.xaml", StringComparison.OrdinalIgnoreCase));
    }

    private static ThemeMode GetWindowsTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0 ? ThemeMode.Dark : ThemeMode.Light;
    }
}


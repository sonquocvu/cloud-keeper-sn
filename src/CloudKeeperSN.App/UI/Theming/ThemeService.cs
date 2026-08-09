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

public sealed class ThemeService : IThemeService, IDisposable
{
    private readonly IApplicationSettingRepository _settings;
    private const string SettingKey = "ui.theme";
    // Relative pack URIs remain valid if the executable assembly name changes.
    private const string LightThemePath = "UI/Themes/LightTheme.xaml";
    private const string DarkThemePath = "UI/Themes/DarkTheme.xaml";
    private const string HighContrastThemePath = "UI/Themes/HighContrastTheme.xaml";

    public ThemeService(IApplicationSettingRepository settings)
    {
        _settings = settings;
        SystemEvents.UserPreferenceChanged += SystemPreferenceChanged;
    }

    public ThemeMode CurrentMode { get; private set; } = ThemeMode.System;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var persisted = await _settings.GetAsync(SettingKey, cancellationToken);
        var mode = Enum.TryParse<ThemeMode>(persisted, out var parsed) ? parsed : ThemeMode.System;
        ApplyResources(mode);
        CurrentMode = mode;
    }

    public async Task ApplyAsync(ThemeMode mode, CancellationToken cancellationToken)
    {
        ApplyResources(mode);
        CurrentMode = mode;
        await _settings.SetAsync(SettingKey, mode.ToString(), cancellationToken);
    }

    private static void ApplyResources(ThemeMode mode)
    {
        var themePath = SystemParameters.HighContrast
            ? HighContrastThemePath
            : (mode == ThemeMode.System ? GetWindowsTheme() : mode) == ThemeMode.Dark ? DarkThemePath : LightThemePath;
        var source = new Uri(themePath, UriKind.Relative);
        var dictionaries = System.Windows.Application.Current.Resources.MergedDictionaries;
        var current = dictionaries.FirstOrDefault(IsThemeDictionary);
        if (current is not null) dictionaries.Remove(current);
        dictionaries.Insert(0, new ResourceDictionary { Source = source });
    }

    private static bool IsThemeDictionary(ResourceDictionary dictionary)
    {
        var value = dictionary.Source?.OriginalString;
        return value is not null && (value.EndsWith("LightTheme.xaml", StringComparison.OrdinalIgnoreCase) ||
                                     value.EndsWith("DarkTheme.xaml", StringComparison.OrdinalIgnoreCase) ||
                                     value.EndsWith("HighContrastTheme.xaml", StringComparison.OrdinalIgnoreCase));
    }

    private static ThemeMode GetWindowsTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0 ? ThemeMode.Dark : ThemeMode.Light;
    }

    private void SystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (CurrentMode != ThemeMode.System || e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)) return;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        _ = dispatcher.BeginInvoke(() => ApplyResources(ThemeMode.System));
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= SystemPreferenceChanged;
}

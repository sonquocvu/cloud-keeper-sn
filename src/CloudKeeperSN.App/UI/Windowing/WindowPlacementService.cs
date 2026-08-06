using System.Text.Json;
using CloudKeeperSN.Application.Persistence;

namespace CloudKeeperSN.App.UI.Windowing;

public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool IsMaximized);

public static class WindowPlacementValidator
{
    public static WindowPlacement EnsureVisible(WindowPlacement placement, double virtualLeft, double virtualTop, double virtualWidth, double virtualHeight)
    {
        const double minimumVisible = 80;
        var validSize = placement.Width >= 1100 && placement.Height >= 700;
        var right = placement.Left + placement.Width;
        var bottom = placement.Top + placement.Height;
        var visible = right >= virtualLeft + minimumVisible &&
                      placement.Left <= virtualLeft + virtualWidth - minimumVisible &&
                      bottom >= virtualTop + minimumVisible &&
                      placement.Top <= virtualTop + virtualHeight - minimumVisible;

        return validSize && visible
            ? placement
            : new WindowPlacement(
                virtualLeft + Math.Max(0, (virtualWidth - 1280) / 2),
                virtualTop + Math.Max(0, (virtualHeight - 800) / 2),
                Math.Min(1280, virtualWidth),
                Math.Min(800, virtualHeight),
                false);
    }
}

public interface IWindowPlacementService
{
    Task<WindowPlacement?> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(WindowPlacement placement, CancellationToken cancellationToken);
}

public sealed class WindowPlacementService(IApplicationSettingRepository settings) : IWindowPlacementService
{
    private const string SettingKey = "ui.window-placement";

    public async Task<WindowPlacement?> LoadAsync(CancellationToken cancellationToken)
    {
        var value = await settings.GetAsync(SettingKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            return JsonSerializer.Deserialize<WindowPlacement>(value);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public Task SaveAsync(WindowPlacement placement, CancellationToken cancellationToken) =>
        settings.SetAsync(SettingKey, JsonSerializer.Serialize(placement), cancellationToken);
}

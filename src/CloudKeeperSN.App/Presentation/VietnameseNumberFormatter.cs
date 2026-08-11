using System.Globalization;

namespace CloudKeeperSN.App.Presentation;

public static class VietnameseNumberFormatter
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("vi-VN");

    public static string FormatInteger(long value) => value.ToString("N0", Culture);

    public static string FormatDecimal(double value) => value.ToString("0.#", Culture);

    public static string FormatPercentage(double value) => $"{FormatDecimal(value)}%";

    public static string FormatBytes(long bytes)
    {
        string[] units = ["byte", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{FormatInteger(bytes)} byte"
            : $"{FormatDecimal(value)} {units[unit]}";
    }
}

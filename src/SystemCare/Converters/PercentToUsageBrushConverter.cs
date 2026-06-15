using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SystemCare.Converters;

/// <summary>
/// Maps a 0-100 "used %" to a neon fill brush: cyan when there's plenty of room, easing to yellow,
/// then magenta as the drive fills up. Used for the dashboard drive-usage bars.
/// </summary>
public class PercentToUsageBrushConverter : IValueConverter
{
    private static readonly Color Cyan = Color.FromRgb(0x00, 0xE5, 0xFF);
    private static readonly Color Yellow = Color.FromRgb(0xFF, 0xD3, 0x00);
    private static readonly Color Magenta = Color.FromRgb(0xFF, 0x2A, 0x6D);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double pct = value switch
        {
            double d => d,
            int i => i,
            _ => 0,
        };
        pct = Math.Clamp(pct, 0, 100);

        // cyan -> yellow over 0-75%, yellow -> magenta over 75-100%.
        Color c = pct <= 75
            ? Lerp(Cyan, Yellow, pct / 75.0)
            : Lerp(Yellow, Magenta, (pct - 75) / 25.0);

        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }
}

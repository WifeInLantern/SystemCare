using System.Globalization;
using System.Windows.Data;

namespace SystemCare.Converters;

/// <summary>
/// Maps a 0-100 percentage to a pixel width for a proportional bar. The track width is passed as the
/// converter parameter (e.g. ConverterParameter=114). Used by the disk-analyzer tree's size bars.
/// </summary>
public class PercentToBarWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double pct = value switch
        {
            double d => d,
            int i => i,
            _ => 0,
        };
        double max = parameter is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var m)
            ? m : 100;
        return Math.Clamp(pct, 0, 100) / 100.0 * max;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

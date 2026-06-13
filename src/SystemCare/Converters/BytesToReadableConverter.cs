using System.Globalization;
using System.Windows.Data;
using SystemCare.Helpers;

namespace SystemCare.Converters;

public class BytesToReadableConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        long l => ByteFormatter.Format(l),
        ulong u => ByteFormatter.Format((long)Math.Min(u, long.MaxValue)),
        int i => ByteFormatter.Format(i),
        double d => ByteFormatter.Format((long)d),
        _ => "0 B",
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

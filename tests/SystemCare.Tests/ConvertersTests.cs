using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SystemCare.Converters;
using Xunit;

namespace SystemCare.Tests;

/// <summary>Unit tests for the WPF value converters (pure Convert logic; one-way converters throw on ConvertBack).</summary>
public class ConvertersTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ---------- BytesToReadableConverter ----------

    public static IEnumerable<object?[]> ByteConvertCases() => new[]
    {
        new object?[] { 1024L, "1 KB" },
        new object?[] { 1024UL, "1 KB" },
        new object?[] { 1048576, "1 MB" },        // int
        new object?[] { 1536.0, "1.5 KB" },       // double -> truncated to long
        new object?[] { "not a number", "0 B" },
        new object?[] { null, "0 B" },
    };

    [Theory]
    [MemberData(nameof(ByteConvertCases))]
    public void BytesToReadable_Convert(object? value, string expected)
    {
        Assert.Equal(expected, new BytesToReadableConverter().Convert(value, typeof(string), null, Inv));
    }

    [Fact]
    public void BytesToReadable_UlongOverLongMax_ClampsWithoutOverflow()
    {
        var result = (string)new BytesToReadableConverter().Convert(ulong.MaxValue, typeof(string), null, Inv);
        Assert.EndsWith(" TB", result);
    }

    [Fact]
    public void BytesToReadable_ConvertBack_Throws()
    {
        Assert.Throws<NotSupportedException>(
            () => new BytesToReadableConverter().ConvertBack("1 KB", typeof(long), null, Inv));
    }

    // ---------- InverseBoolConverter ----------

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void InverseBool_InvertsBothDirections(bool input, bool expected)
    {
        var sut = new InverseBoolConverter();
        Assert.Equal(expected, sut.Convert(input, typeof(bool), null, Inv));
        Assert.Equal(expected, sut.ConvertBack(input, typeof(bool), null, Inv));
    }

    [Fact]
    public void InverseBool_NonBoolean_IsFalse_BothDirections()
    {
        var sut = new InverseBoolConverter();
        Assert.Equal(false, sut.Convert(null, typeof(bool), null, Inv));
        Assert.Equal(false, sut.ConvertBack("not a bool", typeof(bool), null, Inv));
    }

    // ---------- BoolToVisibilityConverter ----------

    [Theory]
    [InlineData(true, Visibility.Visible)]
    [InlineData(false, Visibility.Collapsed)]
    [InlineData(null, Visibility.Collapsed)]   // non-bool defaults to not-visible
    public void BoolToVisibility_Default(object? value, Visibility expected)
    {
        Assert.Equal(expected, new BoolToVisibilityConverter().Convert(value, typeof(Visibility), null, Inv));
    }

    [Theory]
    [InlineData(true, Visibility.Collapsed)]
    [InlineData(false, Visibility.Visible)]
    public void BoolToVisibility_Inverted(object value, Visibility expected)
    {
        var sut = new BoolToVisibilityConverter { Invert = true };
        Assert.Equal(expected, sut.Convert(value, typeof(Visibility), null, Inv));
    }

    [Fact]
    public void BoolToVisibility_ConvertBack_Throws()
    {
        Assert.Throws<NotSupportedException>(
            () => new BoolToVisibilityConverter().ConvertBack(Visibility.Visible, typeof(bool), null, Inv));
    }

    // ---------- CountToVisibilityConverter ----------

    [Theory]
    [InlineData(1, Visibility.Visible)]
    [InlineData(5, Visibility.Visible)]
    [InlineData(0, Visibility.Collapsed)]
    [InlineData(-3, Visibility.Collapsed)]
    [InlineData(null, Visibility.Collapsed)]
    public void CountToVisibility_VisibleOnlyWhenPositive(object? value, Visibility expected)
    {
        Assert.Equal(expected, new CountToVisibilityConverter().Convert(value, typeof(Visibility), null, Inv));
    }

    [Fact]
    public void CountToVisibility_ConvertBack_Throws()
    {
        Assert.Throws<NotSupportedException>(
            () => new CountToVisibilityConverter().ConvertBack(Visibility.Visible, typeof(int), null, Inv));
    }

    // ---------- PercentToBarWidthConverter ----------

    [Theory]
    [InlineData(50, "200", 100.0)]
    [InlineData(50.0, "200", 100.0)]
    [InlineData(150, "200", 200.0)]   // clamped to 100%
    [InlineData(-10, "200", 0.0)]     // clamped to 0%
    [InlineData(50, null, 50.0)]      // missing parameter defaults the track width to 100
    [InlineData(50, "abc", 50.0)]     // unparseable parameter defaults the track width to 100
    [InlineData("x", "200", 0.0)]     // non-numeric value treated as 0%
    public void PercentToBarWidth_MapsToPixels(object value, string? parameter, double expected)
    {
        Assert.Equal(expected, new PercentToBarWidthConverter().Convert(value, typeof(double), parameter, Inv));
    }

    [Fact]
    public void PercentToBarWidth_ConvertBack_Throws()
    {
        Assert.Throws<NotSupportedException>(
            () => new PercentToBarWidthConverter().ConvertBack(100.0, typeof(double), "200", Inv));
    }

    // ---------- PercentToUsageBrushConverter ----------

    [Theory]
    [InlineData(0.0, 0x00, 0xE5, 0xFF)]    // cyan (plenty free)
    [InlineData(-5.0, 0x00, 0xE5, 0xFF)]   // clamped to 0 -> cyan
    [InlineData(75.0, 0xFF, 0xD3, 0x00)]   // yellow (mid-point of the ramp)
    [InlineData(100.0, 0xFF, 0x2A, 0x6D)]  // magenta (full)
    [InlineData(150.0, 0xFF, 0x2A, 0x6D)]  // clamped to 100 -> magenta
    public void PercentToUsageBrush_RampEndpoints(double pct, byte r, byte g, byte b)
    {
        var brush = (SolidColorBrush)new PercentToUsageBrushConverter().Convert(pct, typeof(Brush), null, Inv);

        Assert.True(brush.IsFrozen);
        Assert.Equal(Color.FromRgb(r, g, b), brush.Color);
    }

    [Fact]
    public void PercentToUsageBrush_AcceptsIntegerPercent()
    {
        // Exercises the int switch arm and a non-endpoint colour interpolation.
        var brush = (SolidColorBrush)new PercentToUsageBrushConverter().Convert(50, typeof(Brush), null, Inv);

        Assert.True(brush.IsFrozen);
        Assert.IsType<SolidColorBrush>(brush);
    }

    [Fact]
    public void PercentToUsageBrush_NonNumericValue_DefaultsToCyan()
    {
        // Unrecognized value falls through to 0% -> the cyan (plenty-free) end of the ramp.
        var brush = (SolidColorBrush)new PercentToUsageBrushConverter().Convert("x", typeof(Brush), null, Inv);

        Assert.Equal(Color.FromRgb(0x00, 0xE5, 0xFF), brush.Color);
    }

    [Fact]
    public void PercentToUsageBrush_ConvertBack_Throws()
    {
        Assert.Throws<NotSupportedException>(
            () => new PercentToUsageBrushConverter().ConvertBack(Brushes.Red, typeof(double), null, Inv));
    }
}

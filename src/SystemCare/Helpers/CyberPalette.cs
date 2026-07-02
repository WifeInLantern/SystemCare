using System.Windows;
using System.Windows.Media;

namespace SystemCare.Helpers;

/// <summary>
/// Single bridge between the XAML palette (Styles/Theme.xaml) and C# call sites that
/// need raw <see cref="Color"/> values (custom-drawn controls, animation glows, the
/// accent manager). Reads the live application resources so a token edit in Theme.xaml
/// propagates everywhere; falls back to the shipped palette when resources aren't
/// available (design time, unit tests without a merged dictionary).
/// </summary>
public static class CyberPalette
{
    public static Color Accent => Resolve("AccentColor", 0x00, 0xE5, 0xFF);
    public static Color Secondary => Resolve("SecondaryColor", 0xFF, 0x2A, 0x6D);
    public static Color Violet => Resolve("VioletColor", 0xB1, 0x4C, 0xFF);
    public static Color Success => Resolve("SuccessColor", 0x00, 0xFF, 0xA3);
    public static Color Warning => Resolve("WarningColor", 0xFF, 0xD3, 0x00);
    public static Color Danger => Resolve("DangerColor", 0xFF, 0x2A, 0x6D);
    public static Color PanelSolid => Resolve("CyberPanelSolidColor", 0x12, 0x1A, 0x28);
    public static Color Stroke => Resolve("CyberStrokeColor", 0x2A, 0x3A, 0x55);
    public static Color TextPrimary => Resolve("TextPrimaryColor", 0xE6, 0xF6, 0xFF);
    public static Color TextSecondary => Resolve("TextSecondaryColor", 0x8F, 0xA6, 0xC0);
    public static Color Background => Resolve("CyberBackgroundColor", 0x0A, 0x0E, 0x14);
    public static Color BackgroundDeep => Resolve("CyberBackgroundDeepColor", 0x05, 0x07, 0x0B);

    private static Color Resolve(string key, byte r, byte g, byte b)
    {
        if (Application.Current?.Resources[key] is Color color) return color;
        return Color.FromRgb(r, g, b);
    }
}

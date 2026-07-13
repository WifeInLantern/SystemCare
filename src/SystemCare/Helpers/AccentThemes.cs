using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace SystemCare.Helpers;

/// <summary>
/// Accent theme picker (2.16): maps the persisted accent name to one of the three identity neons
/// and applies it through WPF-UI's accent manager. This recolors accent-driven Fluent controls
/// (primary buttons, toggles, selection, progress) — the neon token brushes (NeonCyanBrush, glows,
/// charts) deliberately keep their design-system colors so the identity stays coherent.
/// </summary>
public static class AccentThemes
{
    public static readonly string[] Options = ["Cyan", "Magenta", "Violet"];

    public static Color Resolve(string? name) => name switch
    {
        "Magenta" => CyberPalette.Secondary,
        "Violet" => CyberPalette.Violet,
        _ => CyberPalette.Accent,
    };

    public static void Apply(string? name) =>
        ApplicationAccentColorManager.Apply(Resolve(name), ApplicationTheme.Dark);
}

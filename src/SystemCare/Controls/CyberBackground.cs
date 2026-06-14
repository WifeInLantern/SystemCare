using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace SystemCare.Controls;

/// <summary>
/// Always-on cyberpunk ambient backdrop: a dark gradient base, a slowly scrolling
/// neon grid, two drifting radial glows (cyan + magenta) and a faint scanline overlay.
/// Cheap to draw — frozen brushes/pens, no blur effects, ~30 fps throttled. Hooks
/// <see cref="CompositionTarget.Rendering"/> only while loaded.
/// </summary>
public class CyberBackground : FrameworkElement
{
    private const double Cell = 46;   // grid spacing (px)
    private const double Speed = 14;  // grid scroll (px/sec)

    private readonly Brush _bgBrush;
    private readonly Pen _gridPen;
    private readonly Pen _gridPenBright;
    private readonly RadialGradientBrush _glowCyan;
    private readonly RadialGradientBrush _glowMagenta;
    private readonly DrawingBrush _scanlines;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastRenderMs;
    private bool _hooked;

    public CyberBackground()
    {
        IsHitTestVisible = false;

        var bg = new LinearGradientBrush(
            Color.FromRgb(0x0A, 0x0E, 0x14), Color.FromRgb(0x05, 0x07, 0x0B), 90);
        bg.Freeze();
        _bgBrush = bg;

        _gridPen = new Pen(new SolidColorBrush(Color.FromArgb(0x16, 0x00, 0xE5, 0xFF)), 0.7);
        _gridPen.Freeze();
        _gridPenBright = new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0x00, 0xE5, 0xFF)), 0.9);
        _gridPenBright.Freeze();

        _glowCyan = MakeGlow(Color.FromRgb(0x00, 0xB8, 0xD4));
        _glowMagenta = MakeGlow(Color.FromRgb(0xFF, 0x2A, 0x6D));
        _scanlines = MakeScanlines();

        Loaded += (_, _) => Hook();
        Unloaded += (_, _) => Unhook();
    }

    private static RadialGradientBrush MakeGlow(Color c)
    {
        var b = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.5),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5,
        };
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x3A, c.R, c.G, c.B), 0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, c.R, c.G, c.B), 1));
        b.Freeze();
        return b;
    }

    private static DrawingBrush MakeScanlines()
    {
        var line = new GeometryDrawing(
            new SolidColorBrush(Color.FromArgb(0x12, 0x00, 0x00, 0x00)), null,
            new RectangleGeometry(new Rect(0, 0, 4, 1)));
        var b = new DrawingBrush(line)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 4, 4),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
        b.Freeze();
        return b;
    }

    private void Hook()
    {
        if (_hooked) return;
        CompositionTarget.Rendering += OnRendering;
        _hooked = true;
    }

    private void Unhook()
    {
        if (!_hooked) return;
        CompositionTarget.Rendering -= OnRendering;
        _hooked = false;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        double now = _clock.Elapsed.TotalMilliseconds;
        if (now - _lastRenderMs < 33) return; // ~30 fps
        _lastRenderMs = now;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 2 || h < 2) return;
        double t = _clock.Elapsed.TotalSeconds;

        dc.DrawRectangle(_bgBrush, null, new Rect(0, 0, w, h));

        // Scrolling neon grid (every 5th line brighter for depth).
        double off = (t * Speed) % Cell;
        int col = 0;
        for (double x = -off; x <= w; x += Cell, col++)
            dc.DrawLine(col % 5 == 0 ? _gridPenBright : _gridPen, new Point(x, 0), new Point(x, h));
        int rowI = 0;
        for (double y = -off; y <= h; y += Cell, rowI++)
            dc.DrawLine(rowI % 5 == 0 ? _gridPenBright : _gridPen, new Point(0, y), new Point(w, y));

        // Drifting radial glows (lissajous paths).
        double gr = Math.Max(w, h) * 0.55;
        DrawGlow(dc, _glowCyan, w * (0.5 + 0.35 * Math.Sin(t * 0.13)), h * (0.40 + 0.30 * Math.Cos(t * 0.11)), gr);
        DrawGlow(dc, _glowMagenta, w * (0.5 + 0.40 * Math.Cos(t * 0.09)), h * (0.60 + 0.32 * Math.Sin(t * 0.10)), gr * 0.8);

        // Faint static scanline overlay.
        dc.DrawRectangle(_scanlines, null, new Rect(0, 0, w, h));
    }

    private static void DrawGlow(DrawingContext dc, Brush b, double cx, double cy, double r)
        => dc.DrawEllipse(b, null, new Point(cx, cy), r, r);
}

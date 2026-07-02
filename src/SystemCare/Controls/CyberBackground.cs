using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using SystemCare.Helpers;

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

        // Palette resolved via CyberPalette so Theme.xaml edits propagate to the backdrop.
        var accent = CyberPalette.Accent;
        var bg = new LinearGradientBrush(CyberPalette.Background, CyberPalette.BackgroundDeep, 90);
        bg.Freeze();
        _bgBrush = bg;

        _gridPen = new Pen(new SolidColorBrush(Color.FromArgb(0x16, accent.R, accent.G, accent.B)), 0.7);
        _gridPen.Freeze();
        _gridPenBright = new Pen(new SolidColorBrush(Color.FromArgb(0x30, accent.R, accent.G, accent.B)), 0.9);
        _gridPenBright.Freeze();

        _glowCyan = MakeGlow(accent);
        _glowMagenta = MakeGlow(CyberPalette.Secondary);
        _scanlines = MakeScanlines();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Hook();
        Animations.ReduceMotionChanged -= OnReduceMotionChanged; // avoid a double subscription on reload
        Animations.ReduceMotionChanged += OnReduceMotionChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unhook();
        Animations.ReduceMotionChanged -= OnReduceMotionChanged;
    }

    // Reduce-motion toggled live: settle to a static frame, or resume the animated backdrop.
    private void OnReduceMotionChanged()
    {
        if (!IsLoaded) return;
        if (Animations.ReduceMotion) { Unhook(); InvalidateVisual(); }
        else Hook();
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
        // Slightly quieter than before (0x12 -> 0x0F) so the v3 glass layers read better over it.
        var line = new GeometryDrawing(
            new SolidColorBrush(Color.FromArgb(0x0F, 0x00, 0x00, 0x00)), null,
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
        if (Animations.ReduceMotion) { InvalidateVisual(); return; } // draw one static frame, don't animate
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
        if (Animations.ReduceMotion) { Unhook(); return; } // settle to a static frame if toggled on live
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

using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SystemCare.Helpers;
using SystemCare.Models;

namespace SystemCare.Controls;

/// <summary>
/// Custom-drawn squarified treemap of a FileSystemNode's children, two levels deep.
/// Click a directory rectangle to raise <see cref="NodeClicked"/> (drill-down);
/// hover raises <see cref="NodeHovered"/> with the deepest node under the cursor.
/// </summary>
public class TreemapControl : FrameworkElement
{
    public static readonly DependencyProperty RootNodeProperty = DependencyProperty.Register(
        nameof(RootNode), typeof(FileSystemNode), typeof(TreemapControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public FileSystemNode? RootNode
    {
        get => (FileSystemNode?)GetValue(RootNodeProperty);
        set => SetValue(RootNodeProperty, value);
    }

    public event Action<FileSystemNode>? NodeClicked;
    public event Action<FileSystemNode?>? NodeHovered;

    private readonly List<(Rect Rect, FileSystemNode Node)> _level1Rects = [];
    private readonly List<(Rect Rect, FileSystemNode Node)> _level2Rects = [];

    private static readonly (string[] Extensions, Color Color)[] ColorClasses =
    [
        (new[] { ".exe", ".dll", ".sys", ".msi" }, Color.FromRgb(0x5C, 0x6B, 0xC0)),          // binaries: indigo
        (new[] { ".mp4", ".mkv", ".avi", ".mov", ".mp3", ".wav", ".flac" }, Color.FromRgb(0xAB, 0x47, 0xBC)), // media: purple
        (new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".raw" }, Color.FromRgb(0x26, 0xA6, 0x9A)), // images: teal
        (new[] { ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".pdf", ".txt" }, Color.FromRgb(0x66, 0xBB, 0x6A)), // docs: green
        (new[] { ".zip", ".rar", ".7z", ".iso", ".cab", ".gz" }, Color.FromRgb(0xFF, 0xA7, 0x26)), // archives: orange
    ];

    private static readonly Color DirectoryColor = Color.FromRgb(0x42, 0x85, 0xF4);
    private static readonly Color DefaultFileColor = Color.FromRgb(0x78, 0x90, 0x9C);

    private static Color ColorFor(FileSystemNode node)
    {
        if (node.IsDirectory) return DirectoryColor;
        string ext = Path.GetExtension(node.Name).ToLowerInvariant();
        foreach (var (extensions, color) in ColorClasses)
            if (extensions.Contains(ext)) return color;
        return DefaultFileColor;
    }

    protected override void OnRender(DrawingContext dc)
    {
        _level1Rects.Clear();
        _level2Rects.Clear();

        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)), null, bounds);

        var root = RootNode;
        if (root is null || ActualWidth < 20 || ActualHeight < 20) return;

        var children = root.Children.Where(c => c.Size > 0).ToList();
        if (children.Count == 0) return;

        Squarify(children, Rect.Inflate(bounds, -1, -1), _level1Rects);

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x12)), 1.5);
        var innerPen = new Pen(new SolidColorBrush(Color.FromArgb(0x50, 0x12, 0x12, 0x12)), 0.8);

        foreach (var (rect, node) in _level1Rects)
        {
            if (rect.Width < 2 || rect.Height < 2) continue;
            var baseColor = ColorFor(node);
            dc.DrawRectangle(new SolidColorBrush(baseColor), borderPen, rect);

            // Second level inside directories, slightly darkened.
            if (node.IsDirectory && rect.Width > 36 && rect.Height > 30)
            {
                var inner = Rect.Inflate(rect, -3, -3);
                if (rect.Width > 70 && rect.Height > 44)
                    inner = new Rect(inner.X, inner.Y + 14, inner.Width, Math.Max(0, inner.Height - 14));

                var grandChildren = node.Children.Where(c => c.Size > 0).ToList();
                if (grandChildren.Count > 0 && inner.Width > 8 && inner.Height > 8)
                {
                    var rects = new List<(Rect, FileSystemNode)>();
                    Squarify(grandChildren, inner, rects);
                    foreach (var (r2, n2) in rects)
                    {
                        if (r2.Width < 3 || r2.Height < 3) continue;
                        var c2 = ColorFor(n2);
                        var darker = Color.FromRgb((byte)(c2.R * 0.72), (byte)(c2.G * 0.72), (byte)(c2.B * 0.72));
                        dc.DrawRectangle(new SolidColorBrush(darker), innerPen, r2);
                        _level2Rects.Add((r2, n2));
                    }
                }
            }

            // Label when there is room.
            if (rect.Width > 56 && rect.Height > 18)
            {
                var text = new FormattedText(node.Name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                    11, Brushes.White, dpi)
                {
                    MaxTextWidth = Math.Max(8, rect.Width - 10),
                    MaxTextHeight = Math.Max(8, rect.Height - 4),
                    Trimming = TextTrimming.CharacterEllipsis,
                    MaxLineCount = 1,
                };
                dc.DrawText(text, new Point(rect.X + 5, rect.Y + 2));
            }
        }
    }

    /// <summary>Squarified treemap layout (Bruls, Huizing, van Wijk). Children must be sorted descending by size.</summary>
    private static void Squarify(List<FileSystemNode> nodes, Rect rect, List<(Rect, FileSystemNode)> output)
    {
        double totalSize = nodes.Sum(n => (double)n.Size);
        if (totalSize <= 0 || rect.Width <= 1 || rect.Height <= 1) return;

        double scale = rect.Width * rect.Height / totalSize;
        var areas = nodes.Select(n => n.Size * scale).ToList();

        int index = 0;
        var remaining = new Rect(rect.X, rect.Y, rect.Width, rect.Height);

        while (index < areas.Count)
        {
            double shortSide = Math.Min(remaining.Width, remaining.Height);
            if (shortSide < 1)
            {
                // Degenerate space — drop the rest (rects would be < 1px anyway).
                break;
            }

            // Grow the row while it improves the worst aspect ratio.
            int rowEnd = index + 1;
            double rowSum = areas[index];
            double worst = Worst(areas, index, rowEnd, rowSum, shortSide);
            while (rowEnd < areas.Count)
            {
                double nextSum = rowSum + areas[rowEnd];
                double nextWorst = Worst(areas, index, rowEnd + 1, nextSum, shortSide);
                if (nextWorst > worst) break;
                rowSum = nextSum;
                worst = nextWorst;
                rowEnd++;
            }

            // Lay the row along the short side.
            double rowThickness = rowSum / shortSide;
            bool horizontal = remaining.Width >= remaining.Height;
            double offset = 0;
            for (int i = index; i < rowEnd; i++)
            {
                double itemLength = areas[i] / rowThickness;
                Rect itemRect = horizontal
                    ? new Rect(remaining.X + offset, remaining.Y, itemLength, rowThickness)
                    : new Rect(remaining.X, remaining.Y + offset, rowThickness, itemLength);
                output.Add((itemRect, nodes[i]));
                offset += itemLength;
            }

            remaining = horizontal
                ? new Rect(remaining.X, remaining.Y + rowThickness, remaining.Width, Math.Max(0, remaining.Height - rowThickness))
                : new Rect(remaining.X + rowThickness, remaining.Y, Math.Max(0, remaining.Width - rowThickness), remaining.Height);

            index = rowEnd;
        }
    }

    private static double Worst(List<double> areas, int start, int end, double rowSum, double shortSide)
    {
        double worst = 1;
        double rowThickness = rowSum / shortSide;
        if (rowThickness <= 0) return double.MaxValue;
        for (int i = start; i < end; i++)
        {
            double length = areas[i] / rowThickness;
            if (length <= 0) return double.MaxValue;
            double ratio = Math.Max(rowThickness / length, length / rowThickness);
            worst = Math.Max(worst, ratio);
        }
        return worst;
    }

    private FileSystemNode? HitTestNode(Point point, bool preferDeepest)
    {
        if (preferDeepest)
        {
            foreach (var (rect, node) in _level2Rects)
                if (rect.Contains(point)) return node;
        }
        foreach (var (rect, node) in _level1Rects)
            if (rect.Contains(point)) return node;
        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var node = HitTestNode(e.GetPosition(this), preferDeepest: true);
        NodeHovered?.Invoke(node);
        Cursor = node is { IsDirectory: true } ? Cursors.Hand : Cursors.Arrow;
        if (node is not null)
            ToolTip = $"{node.FullPath}\n{ByteFormatter.Format(node.Size)}";
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        NodeHovered?.Invoke(null);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var node = HitTestNode(e.GetPosition(this), preferDeepest: false);
        if (node is { IsDirectory: true })
            NodeClicked?.Invoke(node);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateVisual();
    }
}

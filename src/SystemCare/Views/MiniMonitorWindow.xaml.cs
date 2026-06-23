using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using SystemCare.Helpers;
using SystemCare.Models;
using SystemCare.Services;

namespace SystemCare.Views;

/// <summary>
/// The always-on-top mini live-monitor: CPU/RAM/network sparklines + hottest temperature, fed from
/// <see cref="ILiveMetricsService"/>. Borderless, draggable, hidden from the taskbar and Alt-Tab.
/// </summary>
public partial class MiniMonitorWindow : Window
{
    public MiniMonitorWindow()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the user clicks the "✕" to hide the widget.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Repaints every row from the latest metrics. Called on the UI thread.</summary>
    public void Render(ILiveMetricsService metrics)
    {
        var snap = metrics.Current;
        CpuValue.Text = MetricsFormatter.Cpu(snap?.CpuPercent);
        RamValue.Text = snap is null ? "—" : MetricsFormatter.Ram(snap.RamLoadPercent);
        NetValue.Text = snap is null ? "—" : MetricsFormatter.NetRate(snap.NetRecvBytesPerSec + snap.NetSentBytesPerSec);

        CpuSpark.Values = metrics.CpuHistory;
        RamSpark.Values = metrics.RamHistory;
        NetSpark.Values = metrics.NetHistory;

        // Network has no fixed scale — auto-range the sparkline to its own recent peak.
        double netMax = 1;
        foreach (double v in metrics.NetHistory)
            if (v > netMax) netMax = v;
        NetSpark.Max = netMax * 1.2;

        double? hottest = Hottest(metrics.Temperatures);
        TempValue.Text = hottest is double t ? $"{t:0}°C" : "—";
        TempRow.Visibility = hottest is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private static double? Hottest(IReadOnlyList<ComponentTemperature> temps)
    {
        double? hottest = null;
        foreach (var t in temps)
            if (hottest is null || t.Celsius > hottest) hottest = t.Celsius;
        return hottest;
    }

    private void OnWindowMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, CloseGlyph)) return; // let the close handler run instead
        if (e.ChangedButton == MouseButton.Left)
        {
            try { DragMove(); } catch (Exception) { /* DragMove can throw if the button was already released */ }
        }
    }

    private void OnCloseClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    // Add WS_EX_TOOLWINDOW so the widget never shows up in the Alt-Tab switcher.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(handle, GWL_EXSTYLE);
            SetWindowLong(handle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
        }
        catch (Exception)
        {
            // cosmetic only — ignore if the style can't be applied
        }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}

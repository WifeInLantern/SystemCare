using System.Drawing;
using System.Runtime.InteropServices;
using SystemCare.Helpers;
using Forms = System.Windows.Forms;
using WpfApp = System.Windows.Application;

namespace SystemCare.Services;

public interface ITrayIconService
{
    void Initialize();
    void ShowBalloon(string title, string message);
    void ShowMainWindow();
    /// <summary>Show/stop live CPU/RAM in the tray icon + tooltip.</summary>
    void EnableLiveStats(bool enabled);
    void Dispose();
}

/// <summary>
/// System-tray icon backed by the WinForms <see cref="Forms.NotifyIcon"/> — the most reliable
/// tray implementation on Windows 10/11 (single-click, double-click, and balloon tips all work).
/// </summary>
public class TrayIconService(
    IScheduledMaintenanceService maintenance,
    ISettingsService settings,
    ILiveMetricsService metrics,
    IMiniMonitorService miniMonitor) : ITrayIconService
{
    private Forms.NotifyIcon? _icon;
    private Icon? _dynamicIcon;   // the managed CPU-meter icon we currently own (disposed on replace)
    private bool _liveStats;

    public void Initialize()
    {
        if (_icon is not null) return;

        _icon = new Forms.NotifyIcon
        {
            Text = "SystemCare",
            Icon = LoadIcon(),
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        // Single OR double left-click restores — both are wired for maximum reliability.
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) ShowMainWindow(); };
        _icon.DoubleClick += (_, _) => ShowMainWindow();
    }

    private static Icon? LoadIcon()
    {
        try
        {
            // Works under single-file publish: extract the running exe's own icon.
            string? exe = Environment.ProcessPath;
            return exe is not null ? Icon.ExtractAssociatedIcon(exe) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip();

        menu.Items.Add("Open SystemCare", null, (_, _) => ShowMainWindow());

        menu.Items.Add("Run maintenance now", null, async (_, _) =>
        {
            var result = await maintenance.RunMaintenanceNowAsync();
            ShowBalloon("Maintenance complete",
                $"Removed {ByteFormatter.Format(result.BytesRemoved)} of junk and freed {ByteFormatter.Format(result.BytesFreed)} of RAM.");
        });

        var auto = new Forms.ToolStripMenuItem("Automatic maintenance")
        {
            CheckOnClick = true,
            Checked = settings.Current.AutoMaintenanceEnabled,
        };
        auto.Click += (_, _) =>
        {
            settings.Current.AutoMaintenanceEnabled = auto.Checked;
            settings.Save();
            maintenance.Sync();
        };
        menu.Items.Add(auto);

        var widget = new Forms.ToolStripMenuItem("Live monitor widget");
        widget.Click += (_, _) => miniMonitor.Toggle();
        menu.Items.Add(widget);
        menu.Opening += (_, _) => widget.Checked = miniMonitor.IsVisible;

        menu.Items.Add(new Forms.ToolStripSeparator());

        menu.Items.Add("Exit", null, (_, _) =>
        {
            WpfApp.Current?.Dispatcher.Invoke(() =>
            {
                if (WpfApp.Current.MainWindow is MainWindow window) window.ForceExit = true;
                WpfApp.Current.Shutdown();
            });
        });

        return menu;
    }

    public void ShowMainWindow()
    {
        var app = WpfApp.Current;
        if (app is null) return;
        app.Dispatcher.Invoke(() =>
        {
            // Prefer the real MainWindow; fall back to the first live instance in case
            // Application.MainWindow still points at the (closed) splash window.
            var window = app.MainWindow as MainWindow
                ?? app.Windows.OfType<MainWindow>().FirstOrDefault();
            if (window is null) return;

            try
            {
                if (!window.IsVisible) window.Show();
                if (window.WindowState == System.Windows.WindowState.Minimized)
                    window.WindowState = System.Windows.WindowState.Normal;
                window.Activate();
                window.Topmost = true;
                window.Topmost = false;
                window.Focus();
            }
            catch (Exception) { }
        });
    }

    public void ShowBalloon(string title, string message)
    {
        try
        {
            if (_icon is null) return;
            _icon.BalloonTipTitle = title;
            _icon.BalloonTipText = message;
            _icon.ShowBalloonTip(5000);
        }
        catch (Exception) { }
    }

    public void EnableLiveStats(bool enabled)
    {
        if (_liveStats == enabled) return;
        _liveStats = enabled;

        if (enabled)
        {
            metrics.AddConsumer();
            metrics.Updated += OnMetricsUpdated;
        }
        else
        {
            metrics.Updated -= OnMetricsUpdated;
            metrics.RemoveConsumer();
            if (_icon is not null)
            {
                _icon.Text = "SystemCare";
                _icon.Icon = LoadIcon();
            }
            _dynamicIcon?.Dispose();
            _dynamicIcon = null;
        }
    }

    private void OnMetricsUpdated(object? sender, EventArgs e)
    {
        if (_icon is null) return;
        var snap = metrics.Current;
        string text = MetricsFormatter.TrayTooltip(snap);
        _icon.Text = text.Length > 63 ? text[..63] : text; // NotifyIcon tooltip length cap
        UpdateTrayIcon(snap?.CpuPercent);
    }

    /// <summary>Draws a tiny CPU meter as the tray icon, freeing the previous GDI/managed icon each update.</summary>
    private void UpdateTrayIcon(double? cpuPercent)
    {
        if (_icon is null) return;
        try
        {
            using Bitmap bmp = RenderCpuIcon(cpuPercent);
            IntPtr hicon = bmp.GetHicon();
            try
            {
                using var fromHandle = Icon.FromHandle(hicon);
                var owned = (Icon)fromHandle.Clone(); // managed copy independent of the GDI handle
                _icon.Icon = owned;
                _dynamicIcon?.Dispose();
                _dynamicIcon = owned;
            }
            finally
            {
                DestroyIcon(hicon); // GetHicon allocates a handle we must release ourselves
            }
        }
        catch (Exception)
        {
            // a failed icon draw shouldn't break the tooltip updates
        }
    }

    private static Bitmap RenderCpuIcon(double? cpuPercent)
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        double pct = Math.Clamp(cpuPercent ?? 0, 0, 100);
        Color fill = pct >= 80 ? Color.FromArgb(0xFF, 0x2A, 0x6D)   // magenta
                   : pct >= 50 ? Color.FromArgb(0xFF, 0xD3, 0x00)   // yellow
                   : Color.FromArgb(0x00, 0xE5, 0xFF);              // cyan

        using (var track = new SolidBrush(Color.FromArgb(0x99, 0x10, 0x18, 0x24)))
            g.FillRectangle(track, 2, 2, 12, 12);

        int h = (int)Math.Round(pct / 100.0 * 12);
        if (h > 0)
            using (var fb = new SolidBrush(fill))
                g.FillRectangle(fb, 2, 2 + (12 - h), 12, h);

        using (var pen = new Pen(Color.FromArgb(0x80, fill.R, fill.G, fill.B)))
            g.DrawRectangle(pen, 2, 2, 11, 11);

        return bmp;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public void Dispose()
    {
        EnableLiveStats(false);
        if (_icon is null) return;
        _icon.Visible = false;
        _icon.Dispose();
        _icon = null;
    }
}

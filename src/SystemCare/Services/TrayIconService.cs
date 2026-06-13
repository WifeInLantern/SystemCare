using System.Drawing;
using SystemCare.Helpers;
using Forms = System.Windows.Forms;
using WpfApp = System.Windows.Application;

namespace SystemCare.Services;

public interface ITrayIconService
{
    void Initialize();
    void ShowBalloon(string title, string message);
    void ShowMainWindow();
    void Dispose();
}

/// <summary>
/// System-tray icon backed by the WinForms <see cref="Forms.NotifyIcon"/> — the most reliable
/// tray implementation on Windows 10/11 (single-click, double-click, and balloon tips all work).
/// </summary>
public class TrayIconService(
    IScheduledMaintenanceService maintenance,
    ISettingsService settings) : ITrayIconService
{
    private Forms.NotifyIcon? _icon;

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

    public void Dispose()
    {
        if (_icon is null) return;
        _icon.Visible = false;
        _icon.Dispose();
        _icon = null;
    }
}

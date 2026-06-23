using System.Windows;
using SystemCare.Views;

namespace SystemCare.Services;

public interface IMiniMonitorService
{
    bool IsVisible { get; }
    void Show();
    void Hide();
    void Toggle();
    /// <summary>Close the widget for good (app exit).</summary>
    void Shutdown();
}

/// <summary>
/// Owns the single <see cref="MiniMonitorWindow"/> and is the source of truth for the
/// <c>ShowMiniMonitor</c> setting. Registers a live-metrics consumer only while the widget is shown, and
/// persists the widget's on-screen position.
/// </summary>
public sealed class MiniMonitorService(ILiveMetricsService metrics, ISettingsService settings) : IMiniMonitorService
{
    private MiniMonitorWindow? _window;
    private bool _consuming;

    public bool IsVisible => _window is { IsVisible: true };

    public void Toggle()
    {
        if (IsVisible) Hide();
        else Show();
    }

    public void Show()
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (_window is null)
            {
                _window = new MiniMonitorWindow();
                RestorePosition(_window);
                _window.CloseRequested += (_, _) => Hide();
                _window.Closed += (_, _) => _window = null;
            }
            if (!_consuming)
            {
                metrics.AddConsumer();
                metrics.Updated += OnUpdated;
                _consuming = true;
            }
            _window.Render(metrics);
            _window.Show(); // intentionally not Activate() — the widget shouldn't steal focus
        });

        settings.Current.ShowMiniMonitor = true;
        settings.Save();
    }

    public void Hide()
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            StopConsuming();
            if (_window is not null)
            {
                SavePosition(_window);
                _window.Hide();
            }
        });

        settings.Current.ShowMiniMonitor = false;
        settings.Save();
    }

    public void Shutdown()
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            StopConsuming();
            if (_window is not null)
            {
                SavePosition(_window);
                try { _window.Close(); } catch (Exception) { }
                _window = null;
            }
        });
    }

    private void StopConsuming()
    {
        if (!_consuming) return;
        metrics.Updated -= OnUpdated;
        metrics.RemoveConsumer();
        _consuming = false;
    }

    // Updated is raised on the UI thread, so rendering directly is safe.
    private void OnUpdated(object? sender, EventArgs e) => _window?.Render(metrics);

    private void RestorePosition(Window w)
    {
        if (settings.Current.MiniMonitorLeft is double left &&
            settings.Current.MiniMonitorTop is double top &&
            IsOnScreen(left, top))
        {
            w.Left = left;
            w.Top = top;
        }
        else
        {
            var area = SystemParameters.WorkArea; // default to the bottom-right corner
            w.Left = area.Right - 252;
            w.Top = area.Bottom - 176;
        }
    }

    private void SavePosition(Window w)
    {
        settings.Current.MiniMonitorLeft = w.Left;
        settings.Current.MiniMonitorTop = w.Top;
        settings.Save();
    }

    // Guards against a saved position that's now off-screen (e.g. a monitor was disconnected).
    private static bool IsOnScreen(double left, double top)
    {
        double vl = SystemParameters.VirtualScreenLeft;
        double vt = SystemParameters.VirtualScreenTop;
        double vr = vl + SystemParameters.VirtualScreenWidth;
        double vb = vt + SystemParameters.VirtualScreenHeight;
        return left >= vl - 50 && left <= vr - 50 && top >= vt - 50 && top <= vb - 50;
    }
}

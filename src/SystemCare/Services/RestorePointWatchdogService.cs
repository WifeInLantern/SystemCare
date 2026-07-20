namespace SystemCare.Services;

public interface IRestorePointWatchdogService
{
    /// <summary>
    /// Restore-point watchdog (2.19): the whole app leans on System Restore as its safety net,
    /// but nothing monitored it. Warns (tray, at most once per 14 days) when there is no restore
    /// point from the last 30 days — which also covers System Protection being switched off.
    /// Best-effort; never throws. Call once per interactive app start.
    /// </summary>
    Task CheckAsync();
}

public sealed class RestorePointWatchdogService(
    IRestorePointService restore,
    ISettingsService settings,
    ITrayIconService tray,
    IHistoryService history,
    ILogService log) : IRestorePointWatchdogService
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromDays(30);
    private static readonly TimeSpan WarnCooldown = TimeSpan.FromDays(14);

    public async Task CheckAsync()
    {
        try
        {
            if (settings.Current.LastRestorePointWarningUtc is DateTime last &&
                DateTime.UtcNow - last < WarnCooldown)
                return;

            var points = await restore.GetRestorePointsAsync();
            var newest = points.OrderByDescending(p => p.CreationTime).FirstOrDefault();
            bool stale = newest is null || DateTime.Now - newest.CreationTime > StaleAfter;
            if (!stale) return;

            string message = newest is null
                ? "No restore points exist — System Protection may be off. Rescue Center can create one in a click."
                : $"Your newest restore point is from {newest.CreationTime:d} — consider creating a fresh one in Rescue Center.";
            tray.ShowBalloon("Safety net check", message);
            history.Record("Rescue Center", "Restore-point watchdog: " + (newest is null ? "no restore points found" : "newest point is stale"),
                icon: "ArrowUndo24");
            log.Info("RestoreWatchdog", message);

            settings.Current.LastRestorePointWarningUtc = DateTime.UtcNow;
            settings.Save();
        }
        catch (Exception ex)
        {
            log.Error("RestoreWatchdog", "Check failed", ex);
        }
    }
}

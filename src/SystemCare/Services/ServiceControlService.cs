using System.Management;
using System.ServiceProcess;
using SystemCare.Models;

namespace SystemCare.Services;

public interface IServiceControlService
{
    Task<List<ServiceEntry>> GetServicesAsync();
    Task<bool> StartAsync(string serviceName);
    Task<bool> StopAsync(string serviceName);
}

public class ServiceControlService : IServiceControlService
{
    public Task<List<ServiceEntry>> GetServicesAsync() => Task.Run(() =>
    {
        // Start mode + description come from WMI; status/can-stop from ServiceController.
        var meta = new Dictionary<string, (string StartMode, string Description)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, StartMode, Description FROM Win32_Service")
            {
                Options = { Timeout = TimeSpan.FromSeconds(8) },
            };
            foreach (ManagementBaseObject mo in searcher.Get())
            {
                string name = mo["Name"]?.ToString() ?? "";
                if (name.Length > 0)
                    meta[name] = (mo["StartMode"]?.ToString() ?? "", mo["Description"]?.ToString() ?? "");
            }
        }
        catch (Exception)
        {
            // WMI unavailable — fall back to status only
        }

        var entries = new List<ServiceEntry>();
        foreach (var sc in ServiceController.GetServices())
        {
            using (sc)
            {
                try
                {
                    meta.TryGetValue(sc.ServiceName, out var m);
                    entries.Add(new ServiceEntry
                    {
                        Name = sc.ServiceName,
                        DisplayName = sc.DisplayName,
                        State = MapState(sc.Status),
                        StartMode = m.StartMode,
                        Description = m.Description,
                        CanStop = sc.CanStop,
                    });
                }
                catch (Exception)
                {
                    // service became unreadable — skip
                }
            }
        }

        return entries
            .OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    });

    private static ServiceState MapState(ServiceControllerStatus status) => status switch
    {
        ServiceControllerStatus.Running => ServiceState.Running,
        ServiceControllerStatus.Stopped => ServiceState.Stopped,
        ServiceControllerStatus.Paused => ServiceState.Paused,
        ServiceControllerStatus.StartPending or ServiceControllerStatus.StopPending
            or ServiceControllerStatus.ContinuePending or ServiceControllerStatus.PausePending => ServiceState.Pending,
        _ => ServiceState.Unknown,
    };

    public Task<bool> StartAsync(string serviceName) => Task.Run(() =>
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            if (sc.Status == ServiceControllerStatus.Running) return true;
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    });

    public Task<bool> StopAsync(string serviceName) => Task.Run(() =>
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            if (sc.Status == ServiceControllerStatus.Stopped) return true;
            if (!sc.CanStop) return false;
            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    });
}

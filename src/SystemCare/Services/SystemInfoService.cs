using System.Diagnostics;
using System.Net.NetworkInformation;
using SystemCare.Models;
using SystemCare.Native;

namespace SystemCare.Services;

public interface ISystemInfoService
{
    /// <summary>Cheap synchronous snapshot. CPU% is null until two samples ~have been taken.</summary>
    SystemSnapshot GetSnapshot(bool includeDrives = true);
}

public class SystemInfoService : ISystemInfoService
{
    private ulong _lastIdle;
    private ulong _lastKernel;
    private ulong _lastUser;
    private bool _hasSample;
    private IReadOnlyList<DriveStat> _lastDrives = [];

    private long _lastRecvBytes;
    private long _lastSentBytes;
    private long _lastNetTimestamp;

    public SystemSnapshot GetSnapshot(bool includeDrives = true)
    {
        double? cpu = SampleCpu();
        var memory = NativeMethods.GetMemoryStatus();
        var (recvRate, sentRate) = SampleNetwork();

        if (includeDrives || _lastDrives.Count == 0)
            _lastDrives = ReadDrives();

        return new SystemSnapshot
        {
            CpuPercent = cpu,
            RamTotalBytes = memory.ullTotalPhys,
            RamAvailableBytes = memory.ullAvailPhys,
            RamLoadPercent = memory.dwMemoryLoad,
            Drives = _lastDrives,
            NetRecvBytesPerSec = recvRate,
            NetSentBytesPerSec = sentRate,
        };
    }

    private (double Recv, double Sent) SampleNetwork()
    {
        long recv = 0, sent = 0;
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;
                var stats = nic.GetIPv4Statistics();
                recv += stats.BytesReceived;
                sent += stats.BytesSent;
            }
        }
        catch (Exception)
        {
            return (0, 0);
        }

        long now = Stopwatch.GetTimestamp();
        double recvRate = 0, sentRate = 0;
        if (_lastNetTimestamp != 0)
        {
            double seconds = (now - _lastNetTimestamp) / (double)Stopwatch.Frequency;
            if (seconds > 0)
            {
                recvRate = Math.Max(0, (recv - _lastRecvBytes) / seconds);
                sentRate = Math.Max(0, (sent - _lastSentBytes) / seconds);
            }
        }
        _lastRecvBytes = recv;
        _lastSentBytes = sent;
        _lastNetTimestamp = now;
        return (recvRate, sentRate);
    }

    private double? SampleCpu()
    {
        if (!NativeMethods.GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
            return null;

        ulong idle = idleTime.ToUInt64();
        ulong kernel = kernelTime.ToUInt64();
        ulong user = userTime.ToUInt64();

        double? result = null;
        if (_hasSample)
        {
            ulong idleDelta = idle - _lastIdle;
            ulong kernelDelta = kernel - _lastKernel;
            ulong userDelta = user - _lastUser;
            // Kernel time includes idle time, so total busy = kernel + user - idle.
            ulong total = kernelDelta + userDelta;
            if (total > 0)
                result = Math.Clamp((total - idleDelta) * 100.0 / total, 0, 100);
        }

        _lastIdle = idle;
        _lastKernel = kernel;
        _lastUser = user;
        _hasSample = true;
        return result;
    }

    private static List<DriveStat> ReadDrives()
    {
        var drives = new List<DriveStat>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;
                drives.Add(new DriveStat
                {
                    Name = drive.Name.TrimEnd('\\'),
                    Label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Local Disk" : drive.VolumeLabel,
                    TotalBytes = drive.TotalSize,
                    FreeBytes = drive.TotalFreeSpace,
                });
            }
            catch (Exception)
            {
                // drive vanished or not accessible — skip
            }
        }
        return drives;
    }
}

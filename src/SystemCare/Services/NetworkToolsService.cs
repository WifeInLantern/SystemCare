using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using SystemCare.Models;
using SystemCare.Native;

namespace SystemCare.Services;

public interface INetworkToolsService
{
    List<NetConnection> GetConnections();
    /// <summary>TCP sockets in the Listen state plus all UDP sockets (which have no connection state).</summary>
    List<ListeningPort> GetListeningPorts();
    Task PingAsync(string host, Action<string> onLine, CancellationToken ct);
    Task TracerouteAsync(string host, Action<string> onLine, CancellationToken ct);
    string FlushDns();
    Task RenewIpAsync(Action<string> onLine, CancellationToken ct);
}

public class NetworkToolsService(IDiskMaintenanceService runner) : INetworkToolsService
{
    // ---------- active TCP connections with owning PID ----------

    private const int AF_INET = 2;
    private const int TCP_TABLE_OWNER_PID_ALL = 5;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int dwOutBufLen, bool sort,
        int ipVersion, int tableClass, int reserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint state;
        public uint localAddr;
        public uint localPort;
        public uint remoteAddr;
        public uint remotePort;
        public uint owningPid;
    }

    public List<NetConnection> GetConnections()
    {
        var result = new List<NetConnection>();
        int bufferSize = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            if (GetExtendedTcpTable(buffer, ref bufferSize, true, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0) != 0)
                return result;

            int count = Marshal.ReadInt32(buffer);
            IntPtr rowPtr = buffer + 4;
            int rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();
            var names = new Dictionary<int, string>();

            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                rowPtr += rowSize;

                int pid = (int)row.owningPid;
                if (!names.TryGetValue(pid, out var name))
                {
                    name = SafeProcessName(pid);
                    names[pid] = name;
                }

                result.Add(new NetConnection
                {
                    Local = $"{new IPAddress(row.localAddr)}:{Port(row.localPort)}",
                    Remote = $"{new IPAddress(row.remoteAddr)}:{Port(row.remotePort)}",
                    State = StateName(row.state),
                    Pid = pid,
                    ProcessName = name,
                });
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return result.OrderBy(c => c.ProcessName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static int Port(uint raw) => ((int)(raw & 0xFF) << 8) | (int)((raw >> 8) & 0xFF);

    private static string SafeProcessName(int pid)
    {
        if (pid <= 0) return "System";
        try { using var p = Process.GetProcessById(pid); return p.ProcessName; }
        catch (Exception) { return $"PID {pid}"; }
    }

    private static string? SafeProcessPath(int pid)
    {
        if (pid <= 0) return null;
        try { using var p = Process.GetProcessById(pid); return p.MainModule?.FileName; }
        catch (Exception) { return null; }
    }

    private static string StateName(uint state) => state switch
    {
        1 => "Closed", 2 => "Listen", 3 => "SynSent", 4 => "SynReceived", 5 => "Established",
        6 => "FinWait1", 7 => "FinWait2", 8 => "CloseWait", 9 => "Closing", 10 => "LastAck",
        11 => "TimeWait", 12 => "DeleteTcb", _ => "Unknown",
    };

    private const uint TCP_STATE_LISTEN = 2;

    // ---------- listening sockets (TCP "Listen" rows + all UDP rows) ----------

    private const int UDP_TABLE_OWNER_PID = 1;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(IntPtr pUdpTable, ref int dwOutBufLen, bool sort,
        int ipVersion, int tableClass, int reserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_UDPROW_OWNER_PID
    {
        public uint localAddr;
        public uint localPort;
        public uint owningPid;
    }

    public List<ListeningPort> GetListeningPorts()
    {
        var result = new List<ListeningPort>();
        var names = new Dictionary<int, string>();
        var paths = new Dictionary<int, string?>();

        string NameFor(int pid)
        {
            if (!names.TryGetValue(pid, out var name)) { name = SafeProcessName(pid); names[pid] = name; }
            return name;
        }
        string? PathFor(int pid)
        {
            if (!paths.TryGetValue(pid, out var path)) { path = SafeProcessPath(pid); paths[pid] = path; }
            return path;
        }

        int tcpBufferSize = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref tcpBufferSize, true, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
        IntPtr tcpBuffer = Marshal.AllocHGlobal(tcpBufferSize);
        try
        {
            if (GetExtendedTcpTable(tcpBuffer, ref tcpBufferSize, true, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0) == 0)
            {
                int count = Marshal.ReadInt32(tcpBuffer);
                IntPtr rowPtr = tcpBuffer + 4;
                int rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();
                for (int i = 0; i < count; i++)
                {
                    var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                    rowPtr += rowSize;
                    if (row.state != TCP_STATE_LISTEN) continue;

                    int pid = (int)row.owningPid;
                    result.Add(new ListeningPort
                    {
                        Protocol = "TCP",
                        LocalAddress = new IPAddress(row.localAddr).ToString(),
                        Port = Port(row.localPort),
                        Pid = pid,
                        ProcessName = NameFor(pid),
                        ProcessPath = PathFor(pid),
                    });
                }
            }
        }
        finally { Marshal.FreeHGlobal(tcpBuffer); }

        int udpBufferSize = 0;
        GetExtendedUdpTable(IntPtr.Zero, ref udpBufferSize, true, AF_INET, UDP_TABLE_OWNER_PID, 0);
        IntPtr udpBuffer = Marshal.AllocHGlobal(udpBufferSize);
        try
        {
            if (GetExtendedUdpTable(udpBuffer, ref udpBufferSize, true, AF_INET, UDP_TABLE_OWNER_PID, 0) == 0)
            {
                int count = Marshal.ReadInt32(udpBuffer);
                IntPtr rowPtr = udpBuffer + 4;
                int rowSize = Marshal.SizeOf<MIB_UDPROW_OWNER_PID>();
                for (int i = 0; i < count; i++)
                {
                    var row = Marshal.PtrToStructure<MIB_UDPROW_OWNER_PID>(rowPtr);
                    rowPtr += rowSize;

                    int pid = (int)row.owningPid;
                    result.Add(new ListeningPort
                    {
                        Protocol = "UDP",
                        LocalAddress = new IPAddress(row.localAddr).ToString(),
                        Port = Port(row.localPort),
                        Pid = pid,
                        ProcessName = NameFor(pid),
                        ProcessPath = PathFor(pid),
                    });
                }
            }
        }
        finally { Marshal.FreeHGlobal(udpBuffer); }

        return result.OrderBy(p => p.Port).ThenBy(p => p.Protocol, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ---------- ping / traceroute ----------

    public async Task PingAsync(string host, Action<string> onLine, CancellationToken ct)
    {
        onLine($"Pinging {host}…");
        using var ping = new Ping();
        var buffer = new byte[32];
        for (int i = 0; i < 4 && !ct.IsCancellationRequested; i++)
        {
            try
            {
                var reply = await ping.SendPingAsync(host, 4000, buffer);
                onLine(reply.Status == IPStatus.Success
                    ? $"Reply from {reply.Address}: time={reply.RoundtripTime}ms"
                    : $"Request failed: {reply.Status}");
            }
            catch (Exception ex)
            {
                onLine($"Error: {ex.Message}");
                break;
            }
            await Task.Delay(500, ct).ContinueWith(_ => { });
        }
    }

    public async Task TracerouteAsync(string host, Action<string> onLine, CancellationToken ct)
    {
        onLine($"Tracing route to {host} (max 30 hops)…");
        using var ping = new Ping();
        var buffer = new byte[32];
        for (int ttl = 1; ttl <= 30 && !ct.IsCancellationRequested; ttl++)
        {
            try
            {
                var reply = await ping.SendPingAsync(host, 4000, buffer, new PingOptions(ttl, true));
                string addr = reply.Address?.ToString() ?? "*";
                onLine($"{ttl,2}  {(reply.RoundtripTime >= 0 ? reply.RoundtripTime + "ms" : "*"),6}  {addr}");
                if (reply.Status == IPStatus.Success) { onLine("Trace complete."); break; }
            }
            catch (Exception)
            {
                onLine($"{ttl,2}  request timed out");
            }
        }
    }

    public string FlushDns() =>
        NativeMethods.DnsFlushResolverCache() != 0 ? "DNS resolver cache flushed." : "Could not flush the DNS cache.";

    public async Task RenewIpAsync(Action<string> onLine, CancellationToken ct)
    {
        onLine("Releasing IP address…");
        await runner.RunAsync("ipconfig", "/release", onLine, null, ct);
        onLine("Renewing IP address…");
        await runner.RunAsync("ipconfig", "/renew", onLine, null, ct);
        onLine("Done.");
    }
}

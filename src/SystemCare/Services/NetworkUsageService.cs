using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using SystemCare.Models;

namespace SystemCare.Services;

/// <summary>
/// Per-process network byte accounting via an ETW kernel session (the same mechanism Resource Monitor
/// uses). Counts TCP+UDP send/receive bytes per PID with near-zero overhead (the kernel already emits
/// these events). Requires elevation; degrades gracefully to <see cref="IsAvailable"/> = false if the
/// session can't be created.
/// </summary>
public interface INetworkUsageService : IDisposable
{
    bool IsAvailable { get; }
    string StatusMessage { get; }
    /// <summary>Begins the ETW session (idempotent). Resets counters.</summary>
    void Start();
    /// <summary>Stops the session and clears counters (idempotent).</summary>
    void Stop();
    /// <summary>Cumulative per-PID byte totals since <see cref="Start"/>.</summary>
    IReadOnlyList<ProcessNetSample> Snapshot();
}

public sealed class NetworkUsageService : INetworkUsageService
{
    private sealed class Counter { public long Sent; public long Recv; }

    private readonly ConcurrentDictionary<int, Counter> _counters = new();
    private readonly object _gate = new();
    private TraceEventSession? _session;
    private Thread? _thread;
    private bool _running;

    public bool IsAvailable { get; private set; } = true;
    public string StatusMessage { get; private set; } = "";

    public void Start()
    {
        lock (_gate)
        {
            if (_running) return;
            _counters.Clear();
            try
            {
                if (TraceEventSession.IsElevated() != true)
                {
                    IsAvailable = false;
                    StatusMessage = "Per-process network monitoring needs administrator rights.";
                    return;
                }

                // A uniquely named real-time session (Win8+ allows kernel providers on a private session).
                var session = new TraceEventSession("SystemCareNetMonitor") { StopOnDispose = true };
                session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);

                var k = session.Source.Kernel;
                k.TcpIpRecv += d => AddRecv(d.ProcessID, d.size);
                k.TcpIpRecvIPV6 += d => AddRecv(d.ProcessID, d.size);
                k.TcpIpSend += d => AddSend(d.ProcessID, d.size);
                k.TcpIpSendIPV6 += d => AddSend(d.ProcessID, d.size);
                k.UdpIpRecv += d => AddRecv(d.ProcessID, d.size);
                k.UdpIpRecvIPV6 += d => AddRecv(d.ProcessID, d.size);
                k.UdpIpSend += d => AddSend(d.ProcessID, d.size);
                k.UdpIpSendIPV6 += d => AddSend(d.ProcessID, d.size);

                _session = session;
                _thread = new Thread(() => { try { session.Source.Process(); } catch (Exception) { } })
                {
                    IsBackground = true,
                    Name = "SystemCare.NetMonitor",
                };
                _thread.Start();

                _running = true;
                IsAvailable = true;
                StatusMessage = "";
            }
            catch (Exception)
            {
                IsAvailable = false;
                StatusMessage = "Per-process network monitoring is unavailable on this system.";
                try { _session?.Dispose(); } catch (Exception) { }
                _session = null;
            }
        }
    }

    private void AddRecv(int pid, int size)
    {
        if (pid > 0 && size > 0)
            Interlocked.Add(ref _counters.GetOrAdd(pid, static _ => new Counter()).Recv, size);
    }

    private void AddSend(int pid, int size)
    {
        if (pid > 0 && size > 0)
            Interlocked.Add(ref _counters.GetOrAdd(pid, static _ => new Counter()).Sent, size);
    }

    public IReadOnlyList<ProcessNetSample> Snapshot()
    {
        var list = new List<ProcessNetSample>(_counters.Count);
        foreach (var kv in _counters)
            list.Add(new ProcessNetSample(kv.Key,
                Interlocked.Read(ref kv.Value.Sent),
                Interlocked.Read(ref kv.Value.Recv)));
        return list;
    }

    public void Stop()
    {
        lock (_gate)
        {
            _running = false;
            try { _session?.Dispose(); } catch (Exception) { }  // ends Source.Process() on the worker thread
            _session = null;
            _counters.Clear();
        }
    }

    public void Dispose() => Stop();
}

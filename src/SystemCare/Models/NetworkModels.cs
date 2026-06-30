namespace SystemCare.Models;

public class NetConnection
{
    public string Protocol { get; init; } = "TCP";
    public required string Local { get; init; }
    public required string Remote { get; init; }
    public string State { get; init; } = "";
    public int Pid { get; init; }
    public string ProcessName { get; init; } = "";
}

/// <summary>Cumulative bytes a process has sent/received since per-process monitoring started.</summary>
public readonly record struct ProcessNetSample(int Pid, long SentBytes, long RecvBytes);

/// <summary>A TCP (state = Listen) or UDP socket bound to a local port, with its owning process.</summary>
public class ListeningPort
{
    public string Protocol { get; init; } = "TCP";
    public required string LocalAddress { get; init; }
    public int Port { get; init; }
    public int Pid { get; init; }
    public string ProcessName { get; init; } = "";
    public string? ProcessPath { get; init; }
}

/// <summary>An application SystemCare has blocked from the network via a paired Windows Firewall rule.</summary>
public class BlockedApp
{
    public required string RuleName { get; init; }
    public string DisplayName { get; init; } = "";
    public string ApplicationPath { get; init; } = "";
    public bool Enabled { get; init; } = true;
}

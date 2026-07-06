namespace SystemCare.Models;

/// <summary>Result of a network speed test. Speeds are in megabits/sec; latency in milliseconds.</summary>
public record SpeedTestResult(double DownloadMbps, double UploadMbps, long LatencyMs, bool Ok, string Message);

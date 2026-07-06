using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace SystemCare.Services;

/// <summary>Outcome of a Pwned Passwords lookup. Count is how many breaches the password appears in.</summary>
public record BreachResult(bool Ok, bool Found, long Count, string Message);

public interface IBreachCheckService
{
    /// <summary>
    /// Checks a password against Have I Been Pwned's Pwned Passwords API using k-anonymity: only the
    /// first 5 characters of the SHA-1 hash ever leave the machine, never the password itself.
    /// </summary>
    Task<BreachResult> CheckPasswordAsync(string password, CancellationToken ct = default);
}

public class BreachCheckService : IBreachCheckService
{
    private static readonly HttpClient Http = CreateClient();
    private readonly ILogService _log;

    public BreachCheckService(ILogService log) => _log = log;

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        // HIBP requires a descriptive User-Agent.
        c.DefaultRequestHeaders.Add("User-Agent", "SystemCare-PwnedCheck");
        c.DefaultRequestHeaders.Add("Add-Padding", "true");
        return c;
    }

    public async Task<BreachResult> CheckPasswordAsync(string password, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(password))
            return new BreachResult(false, false, 0, "Enter a password to check.");

        try
        {
            byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(password));
            string hex = Convert.ToHexString(hash); // uppercase
            string prefix = hex[..5];
            string suffix = hex[5..];

            using var resp = await Http.GetAsync($"https://api.pwnedpasswords.com/range/{prefix}", ct);
            if (!resp.IsSuccessStatusCode)
                return new BreachResult(false, false, 0, $"Lookup failed (HTTP {(int)resp.StatusCode}). Check your connection.");

            string body = await resp.Content.ReadAsStringAsync(ct);
            foreach (var line in body.Split('\n'))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                if (!line.AsSpan(0, colon).Trim().Equals(suffix, StringComparison.OrdinalIgnoreCase)) continue;

                long count = long.TryParse(line.AsSpan(colon + 1).Trim(), out var n) ? n : 0;
                return new BreachResult(true, true, count,
                    $"This password has appeared in {count:N0} known data breaches. Stop using it.");
            }

            return new BreachResult(true, false, 0, "Good news — this password wasn't found in any known breach.");
        }
        catch (OperationCanceledException)
        {
            return new BreachResult(false, false, 0, "Cancelled.");
        }
        catch (Exception ex)
        {
            _log.Warn("BreachCheck", $"Lookup failed: {ex.Message}");
            return new BreachResult(false, false, 0, "Couldn't reach the breach database — check your connection.");
        }
    }
}

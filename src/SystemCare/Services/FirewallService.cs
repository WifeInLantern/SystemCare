using SystemCare.Models;

namespace SystemCare.Services;

public interface IFirewallService
{
    /// <summary>Lists only the block rules SystemCare itself created (never the system's built-in rules).</summary>
    Task<List<BlockedApp>> GetRulesAsync();
    Task<bool> BlockApplicationAsync(string applicationPath, string displayName);
    Task<bool> UnblockApplicationAsync(string ruleName);
}

/// <summary>
/// Manages a small set of Windows Firewall block rules via the HNetCfg.FwPolicy2 COM API, late-bound
/// through <c>dynamic</c> — the same zero-NuGet interop pattern <see cref="DriverUpdateService"/> uses
/// for the Windows Update Agent. Every dynamic property access is wrapped in its own try/catch so one
/// malformed rule can't blank the whole list. This service only ever touches rules it created itself,
/// identified by the "SystemCare Block - " name prefix; it never reads or modifies the system's other
/// firewall rules.
/// </summary>
public class FirewallService : IFirewallService
{
    private const string RulePrefix = "SystemCare Block - ";
    private const int NET_FW_RULE_DIR_IN = 1;
    private const int NET_FW_RULE_DIR_OUT = 2;
    private const int NET_FW_ACTION_BLOCK = 0;
    private const int NET_FW_ALL_PROFILES = 0x7FFFFFFF;

    public Task<List<BlockedApp>> GetRulesAsync() => Task.Run(() =>
    {
        var byName = new Dictionary<string, BlockedApp>(StringComparer.OrdinalIgnoreCase);
        try
        {
            dynamic? policy = CreatePolicy();
            if (policy is null) return [];

            foreach (dynamic rule in policy.Rules)
            {
                string name = "";
                try { name = (string)(rule.Name ?? ""); } catch (Exception) { }
                if (!name.StartsWith(RulePrefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (byName.ContainsKey(name)) continue;

                string appPath = "";
                bool enabled = false;
                try { appPath = (string)(rule.ApplicationName ?? ""); } catch (Exception) { }
                try { enabled = (bool)rule.Enabled; } catch (Exception) { }

                byName[name] = new BlockedApp
                {
                    RuleName = name,
                    DisplayName = name[RulePrefix.Length..],
                    ApplicationPath = appPath,
                    Enabled = enabled,
                };
            }
        }
        catch (Exception) { }

        return byName.Values.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    });

    public Task<bool> BlockApplicationAsync(string applicationPath, string displayName) => Task.Run(() =>
    {
        try
        {
            dynamic? policy = CreatePolicy();
            if (policy is null) return false;
            Type? ruleType = Type.GetTypeFromProgID("HNetCfg.FWRule");
            if (ruleType is null) return false;

            string ruleName = RulePrefix + displayName;
            AddBlockRule(policy, ruleType, ruleName, applicationPath, NET_FW_RULE_DIR_IN);
            AddBlockRule(policy, ruleType, ruleName, applicationPath, NET_FW_RULE_DIR_OUT);
            return true;
        }
        catch (Exception) { return false; }
    });

    public Task<bool> UnblockApplicationAsync(string ruleName) => Task.Run(() =>
    {
        try
        {
            dynamic? policy = CreatePolicy();
            if (policy is null) return false;
            // Removes every rule with this name — both the inbound and outbound rule created together.
            policy.Rules.Remove(ruleName);
            return true;
        }
        catch (Exception) { return false; }
    });

    private static void AddBlockRule(dynamic policy, Type ruleType, string ruleName, string applicationPath, int direction)
    {
        dynamic rule = Activator.CreateInstance(ruleType)!;
        rule.Name = ruleName;
        rule.Description = "Created by SystemCare to block network access for this application.";
        rule.ApplicationName = applicationPath;
        rule.Direction = direction;
        rule.Action = NET_FW_ACTION_BLOCK;
        rule.Enabled = true;
        rule.Profiles = NET_FW_ALL_PROFILES;
        policy.Rules.Add(rule);
    }

    private static dynamic? CreatePolicy()
    {
        Type? policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
        return policyType is null ? null : Activator.CreateInstance(policyType);
    }
}

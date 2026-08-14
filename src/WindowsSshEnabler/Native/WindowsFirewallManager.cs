using System.Collections;
using System.Runtime.InteropServices;
using WindowsSshEnabler.Core;

namespace WindowsSshEnabler.Native;

public sealed class WindowsFirewallManager : IFirewallManager
{
    public const string RuleName = "WindowsSshEnabler.LanOpenSsh.Tcp22";
    private const int ProfileDomainAndPrivate = 3;
    private const int DirectionInbound = 1;
    private const int ActionBlock = 0;
    private const int ActionAllow = 1;
    private const int ProtocolTcp = 6;

    public FirewallPreflightResult Preflight(string expectedProgramPath)
    {
        var rules = ReadRelevantRules();
        var owned = rules.Where(x => NameEquals(x.Name, RuleName)).ToList();
        if (owned.Count > 1)
            return new(false, $"Multiple application-owned firewall rules named '{RuleName}' exist. Remove the duplicates manually and try again.");
        if (owned.Count == 1 && !IsExact(owned[0], expectedProgramPath))
            return new(false, $"The application-owned firewall rule '{RuleName}' exists but is not safely restricted. Remove it manually and try again; it will not be modified while sshd is stopped.");

        foreach (var rule in rules.Where(x => !NameEquals(x.Name, RuleName)))
        {
            if (rule.Action == ActionBlock)
                return new(false, $"Enabled inbound Block rule '{rule.Name}' applies to TCP port 22. Review that administrator policy before enabling SSH.");
            if (rule.Action == ActionAllow && IsBroaderThanRequired(rule, expectedProgramPath))
                return new(false, $"Enabled inbound Allow rule '{rule.Name}' exposes port 22 more broadly than this application permits. Restrict or remove that rule manually first.");
        }
        return new(true, null);
    }

    public FirewallEnsureResult EnsureExactRule(string expectedProgramPath)
    {
        object? policy = null;
        object? rules = null;
        object? owned = null;
        try
        {
            policy = CreateCom("HNetCfg.FwPolicy2");
            dynamic dynamicPolicy = policy;
            rules = dynamicPolicy.Rules ?? throw new InvalidOperationException("Windows Firewall returned no rule collection.");
            var matching = FindOwnedRules(rules);
            if (matching.Count > 1)
            {
                foreach (var duplicate in matching) ReleaseCom(duplicate);
                throw new InvalidOperationException($"Multiple application-owned firewall rules named '{RuleName}' exist; no rule was changed.");
            }
            if (matching.Count == 1)
            {
                owned = matching[0];
                if (IsExact(ReadRule(owned), expectedProgramPath)) return new(true);
                throw new InvalidOperationException("The application-owned firewall rule changed after preflight. For safety, it was not modified.");
            }

            owned = CreateCom("HNetCfg.FWRule");
            ConfigureRule(owned, expectedProgramPath);
            dynamic dynamicRules = rules;
            dynamicRules.Add(owned);
            // Release the creation RCW before independently reading the rule back;
            // this avoids retaining/releasing the same COM identity across two scans.
            ReleaseCom(owned);
            owned = null;
            if (!VerifyExactRule(expectedProgramPath)) throw new InvalidOperationException("The restricted firewall rule was added but failed read-back verification.");
            return new(false);
        }
        catch (COMException ex)
        {
            if (ex.HResult == unchecked((int)0x80070005)) throw new UnauthorizedAccessException("Windows Firewall rejected the change: access denied.", ex);
            throw new InvalidOperationException($"Windows Firewall API failed (0x{ex.HResult:X8}): {ex.Message}", ex);
        }
        finally
        {
            ReleaseCom(owned);
            ReleaseCom(rules);
            ReleaseCom(policy);
        }
    }

    public bool VerifyExactRule(string expectedProgramPath)
    {
        var owned = ReadRelevantRules().Where(x => NameEquals(x.Name, RuleName)).ToList();
        return owned.Count == 1 && IsExact(owned[0], expectedProgramPath);
    }

    private static List<FirewallRuleSnapshot> ReadRelevantRules()
    {
        object? policy = null;
        object? rules = null;
        try
        {
            policy = CreateCom("HNetCfg.FwPolicy2");
            dynamic dynamicPolicy = policy;
            rules = dynamicPolicy.Rules ?? throw new InvalidOperationException("Windows Firewall returned no rule collection.");
            var result = new List<FirewallRuleSnapshot>();
            foreach (object rule in (IEnumerable)rules)
            {
                try
                {
                    var snapshot = ReadRule(rule);
                    if (NameEquals(snapshot.Name, RuleName) ||
                        (snapshot.Enabled && snapshot.Direction == DirectionInbound && AppliesToPort22(snapshot)))
                        result.Add(snapshot);
                }
                finally { ReleaseCom(rule); }
            }
            return result;
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException($"Windows Firewall rules could not be inspected (0x{ex.HResult:X8}): {ex.Message}", ex);
        }
        finally
        {
            ReleaseCom(rules);
            ReleaseCom(policy);
        }
    }

    private static List<object> FindOwnedRules(object rules)
    {
        var result = new List<object>();
        foreach (object rule in (IEnumerable)rules)
        {
            dynamic item = rule;
            string name = item.Name;
            if (NameEquals(name, RuleName)) result.Add(rule);
            else ReleaseCom(rule);
        }
        return result;
    }

    private static void ConfigureRule(object rule, string expectedProgramPath)
    {
        dynamic item = rule;
        item.Name = RuleName;
        item.Description = "Allows Windows in-box OpenSSH Server from the local subnet on trusted network profiles only.";
        item.Protocol = ProtocolTcp;
        item.LocalPorts = "22";
        item.RemoteAddresses = "LocalSubnet";
        item.ApplicationName = expectedProgramPath;
        item.ServiceName = "sshd";
        item.Direction = DirectionInbound;
        item.Profiles = ProfileDomainAndPrivate;
        item.EdgeTraversal = false;
        item.Action = ActionAllow;
        item.Enabled = true;
    }

    private static FirewallRuleSnapshot ReadRule(object rule)
    {
        dynamic item = rule;
        return new(
            (string)item.Name,
            (bool)item.Enabled,
            (int)item.Direction,
            (int)item.Action,
            (int)item.Protocol,
            TryString(() => (string?)item.LocalPorts),
            (int)item.Profiles,
            TryString(() => (string?)item.RemoteAddresses),
            TryString(() => (string?)item.ApplicationName),
            TryString(() => (string?)item.ServiceName),
            (bool)item.EdgeTraversal);
    }

    private static string? TryString(Func<string?> reader)
    {
        try { return reader(); }
        catch (COMException) { return null; }
    }

    private static bool AppliesToPort22(FirewallRuleSnapshot rule)
    {
        if (rule.Protocol is not (ProtocolTcp or 256)) return false;
        if (string.IsNullOrWhiteSpace(rule.LocalPorts) || rule.LocalPorts == "*") return true;
        foreach (var token in rule.LocalPorts.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (token == "22") return true;
            var range = token.Split('-', 2, StringSplitOptions.TrimEntries);
            if (range.Length == 2 && int.TryParse(range[0], out var start) && int.TryParse(range[1], out var end) && start <= 22 && end >= 22) return true;
        }
        return false;
    }

    private static bool IsBroaderThanRequired(FirewallRuleSnapshot rule, string expectedProgramPath) =>
        !IsExact(rule, expectedProgramPath);

    private static bool IsExact(FirewallRuleSnapshot rule, string expectedProgramPath) =>
        rule.Enabled &&
        rule.Direction == DirectionInbound &&
        rule.Action == ActionAllow &&
        rule.Protocol == ProtocolTcp &&
        string.Equals(rule.LocalPorts?.Trim(), "22", StringComparison.Ordinal) &&
        rule.Profiles == ProfileDomainAndPrivate &&
        string.Equals(rule.RemoteAddresses?.Trim(), "LocalSubnet", StringComparison.OrdinalIgnoreCase) &&
        PathsEqual(rule.ApplicationName, expectedProgramPath) &&
        string.Equals(rule.ServiceName?.Trim(), "sshd", StringComparison.OrdinalIgnoreCase) &&
        !rule.EdgeTraversal;

    private static bool PathsEqual(string? left, string right)
    {
        if (string.IsNullOrWhiteSpace(left)) return false;
        try { return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private static object CreateCom(string progId)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows Firewall COM is unavailable.");
        var type = Type.GetTypeFromProgID(progId, throwOnError: true)
            ?? throw new PlatformNotSupportedException($"Windows Firewall COM class '{progId}' is unavailable.");
        return Activator.CreateInstance(type) ?? throw new PlatformNotSupportedException($"Windows Firewall COM class '{progId}' could not be created.");
    }

    private static bool NameEquals(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static void ReleaseCom(object? instance)
    {
        if (instance is not null && Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance);
    }

    private sealed record FirewallRuleSnapshot(
        string Name, bool Enabled, int Direction, int Action, int Protocol, string? LocalPorts,
        int Profiles, string? RemoteAddresses, string? ApplicationName, string? ServiceName, bool EdgeTraversal);
}

using WindowsSshEnabler.Core;

namespace WindowsSshEnabler.Tests;

internal static class Program
{
    private static readonly List<(string Name, Action Test)> Tests =
    [
        ("success creates exact rule", SuccessCreatesRule),
        ("missing capability is actionable and immutable", MissingCapability),
        ("non-admin is rejected before mutation", NonAdmin),
        ("Public-only network is rejected", PublicOnly),
        ("foreign port owner is rejected", PortConflict),
        ("broad Allow firewall rule is rejected", BroadFirewallRule),
        ("Block firewall rule is rejected", BlockFirewallRule),
        ("service mutation failure is partial", ServiceFailureIsPartial),
        ("listener failure is partial and skips firewall", ListenerFailure),
        ("exact rule is reused without duplication", ExactRuleReuse),
        ("duplicate owned rules are rejected", DuplicateRuleRefusal),
        ("firewall failure after service start is partial", FirewallFailureIsPartial),
        ("late firewall conflict prevents success", LateFirewallConflict)
    ];

    private static int Main()
    {
        var failed = 0;
        foreach (var (name, test) in Tests)
        {
            try { test(); Console.WriteLine($"PASS  {name}"); }
            catch (Exception ex) { failed++; Console.Error.WriteLine($"FAIL  {name}: {ex.Message}"); }
        }
        Console.WriteLine($"{Tests.Count - failed}/{Tests.Count} tests passed.");
        return failed == 0 ? 0 : 1;
    }

    private static void SuccessCreatesRule()
    {
        var f = Fixture.Good();
        var result = f.Run();
        IsTrue(result.Success, result.Message);
        AreEqual(1, f.Firewall.EnsureCalls);
        Contains(result.Message, "Remote connectivity was not tested");
    }

    private static void MissingCapability()
    {
        var f = Fixture.Good(); f.Capability.State = CapabilityState.NotInstalled;
        var result = f.Run();
        IsFalse(result.Success); Contains(result.Message, "Optional Features"); AreEqual(0, f.Service.ConfigureCalls);
    }

    private static void NonAdmin()
    {
        var f = Fixture.Good(); f.Platform.Elevated = false;
        var result = f.Run();
        IsFalse(result.Success); Contains(result.Message, "Administrator"); AreEqual(0, f.Service.ConfigureCalls);
    }

    private static void PublicOnly()
    {
        var f = Fixture.Good(); f.Network.Snapshot = new(true, false, true);
        var result = f.Run();
        IsFalse(result.Success); Contains(result.Message, "Public"); AreEqual(0, f.Service.ConfigureCalls);
    }

    private static void PortConflict()
    {
        var f = Fixture.Good(); f.Ports.Before = [new(999, @"C:\Other\server.exe")];
        var result = f.Run();
        IsFalse(result.Success); Contains(result.Message, "PID 999"); AreEqual(0, f.Service.ConfigureCalls);
    }

    private static void BroadFirewallRule()
    {
        var f = Fixture.Good(); f.Firewall.PreflightResult = new(false, "Allow rule 'Legacy SSH' exposes port 22 more broadly than permitted.");
        var result = f.Run();
        IsFalse(result.Success); Contains(result.Message, "more broadly"); AreEqual(0, f.Service.ConfigureCalls);
    }

    private static void BlockFirewallRule()
    {
        var f = Fixture.Good(); f.Firewall.PreflightResult = new(false, "Block rule 'Policy block' applies to TCP port 22.");
        var result = f.Run();
        IsFalse(result.Success); Contains(result.Message, "Block rule"); AreEqual(0, f.Service.ConfigureCalls);
    }

    private static void ServiceFailureIsPartial()
    {
        var f = Fixture.Good(); f.Service.ConfigureException = new InvalidOperationException("service start failed");
        var result = f.Run();
        IsTrue(result.PartialSuccess); Contains(result.Message, "Partial success"); AreEqual(0, f.Firewall.EnsureCalls);
    }

    private static void ListenerFailure()
    {
        var f = Fixture.Good(); f.Ports.After = [];
        var result = f.Run();
        IsTrue(result.PartialSuccess); Contains(result.Message, "did not become"); AreEqual(0, f.Firewall.EnsureCalls);
    }

    private static void ExactRuleReuse()
    {
        var f = Fixture.Good(); f.Firewall.EnsureResult = new(true);
        var result = f.Run();
        IsTrue(result.Success); Contains(result.Message, "reused"); AreEqual(1, f.Firewall.EnsureCalls);
    }

    private static void DuplicateRuleRefusal()
    {
        var f = Fixture.Good(); f.Firewall.PreflightResult = new(false, "Multiple application-owned firewall rules exist.");
        var result = f.Run();
        IsFalse(result.Success); Contains(result.Message, "Multiple"); AreEqual(0, f.Service.ConfigureCalls);
    }

    private static void FirewallFailureIsPartial()
    {
        var f = Fixture.Good(); f.Firewall.EnsureException = new InvalidOperationException("firewall verification failed");
        var result = f.Run();
        IsTrue(result.PartialSuccess); Contains(result.Message, "firewall verification failed");
    }

    private static void LateFirewallConflict()
    {
        var f = Fixture.Good();
        f.Firewall.FinalPreflightResult = new(false, "A concurrent broad Allow rule appeared.");
        var result = f.Run();
        IsTrue(result.PartialSuccess); Contains(result.Message, "appeared during");
    }

    private static void IsTrue(bool value, string? message = null) { if (!value) throw new Exception(message ?? "Expected true."); }
    private static void IsFalse(bool value) { if (value) throw new Exception("Expected false."); }
    private static void AreEqual<T>(T expected, T actual) where T : notnull { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}."); }
    private static void Contains(string actual, string expected) { if (!actual.Contains(expected, StringComparison.OrdinalIgnoreCase)) throw new Exception($"Expected '{actual}' to contain '{expected}'."); }
}

internal sealed class Fixture
{
    public FakePlatform Platform { get; } = new();
    public FakeCapability Capability { get; } = new();
    public FakeService Service { get; } = new();
    public FakeNetwork Network { get; } = new();
    public FakePorts Ports { get; } = new();
    public FakeFirewall Firewall { get; } = new();
    public FakeClock Clock { get; } = new();

    public static Fixture Good()
    {
        var fixture = new Fixture();
        fixture.Ports.Service = fixture.Service;
        return fixture;
    }

    public OperationResult Run() => new EnablerController(Platform, Capability, Service, Network, Ports, Firewall, Clock).Run(new FakeStatus());
}

internal sealed class FakePlatform : IPlatformProbe
{
    public bool Supported { get; set; } = true;
    public bool Elevated { get; set; } = true;
    public bool IsSupportedWindows => Supported;
    public bool IsElevated => Elevated;
    public string ExpectedSshdPath => @"C:\Windows\System32\OpenSSH\sshd.exe";
}

internal sealed class FakeCapability : ICapabilityProbe
{
    public CapabilityState State { get; set; } = CapabilityState.Installed;
    public CapabilityState GetOpenSshServerState() => State;
}

internal sealed class FakeService : IServiceManager
{
    public bool Running { get; private set; }
    public int ConfigureCalls { get; private set; }
    public Exception? ConfigureException { get; set; }
    public ServiceSnapshot InspectSshd() => new(true, @"C:\Windows\System32\OpenSSH\sshd.exe", Running, Running ? 42u : 0u);
    public void ConfigureAutomaticAndStart(TimeSpan timeout)
    {
        ConfigureCalls++;
        if (ConfigureException is not null) throw ConfigureException;
        Running = true;
    }
}

internal sealed class FakeNetwork : INetworkProbe
{
    public NetworkSnapshot Snapshot { get; set; } = new(true, true, false);
    public NetworkSnapshot InspectActiveNetworks() => Snapshot;
}

internal sealed class FakePorts : IPortInspector
{
    public FakeService? Service { get; set; }
    public IReadOnlyList<TcpListener> Before { get; set; } = [];
    public IReadOnlyList<TcpListener> After { get; set; } = [new(42, @"C:\Windows\System32\OpenSSH\sshd.exe")];
    public IReadOnlyList<TcpListener> GetTcp22Listeners() => Service?.Running == true ? After : Before;
}

internal sealed class FakeFirewall : IFirewallManager
{
    public FirewallPreflightResult PreflightResult { get; set; } = new(true, null);
    public FirewallPreflightResult? FinalPreflightResult { get; set; }
    public FirewallEnsureResult EnsureResult { get; set; } = new(false);
    public Exception? EnsureException { get; set; }
    public int EnsureCalls { get; private set; }
    private int PreflightCalls { get; set; }
    public FirewallPreflightResult Preflight(string expectedProgramPath)
    {
        PreflightCalls++;
        return PreflightCalls > 1 && FinalPreflightResult is not null ? FinalPreflightResult : PreflightResult;
    }
    public FirewallEnsureResult EnsureExactRule(string expectedProgramPath)
    {
        EnsureCalls++;
        if (EnsureException is not null) throw EnsureException;
        return EnsureResult;
    }
    public bool VerifyExactRule(string expectedProgramPath) => true;
}

internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = DateTimeOffset.UnixEpoch;
    public void Delay(TimeSpan duration) => UtcNow += duration;
}

internal sealed class FakeStatus : IStatusSink
{
    public void Report(string message) { }
}

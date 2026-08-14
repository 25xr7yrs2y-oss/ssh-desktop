namespace WindowsSshEnabler.Core;

public interface IPlatformProbe
{
    bool IsSupportedWindows { get; }
    bool IsElevated { get; }
    string ExpectedSshdPath { get; }
}

public interface ICapabilityProbe
{
    CapabilityState GetOpenSshServerState();
}

public enum CapabilityState
{
    Installed,
    NotInstalled,
    Pending,
    Unknown
}

public interface IServiceManager
{
    ServiceSnapshot InspectSshd();
    void ConfigureAutomaticAndStart(TimeSpan timeout);
}

public sealed record ServiceSnapshot(bool Exists, string? ExecutablePath, bool Running, uint ProcessId);

public interface INetworkProbe
{
    NetworkSnapshot InspectActiveNetworks();
}

public sealed record NetworkSnapshot(bool HasConnectedNetwork, bool HasTrustedNetwork, bool HasPublicNetwork);

public interface IPortInspector
{
    IReadOnlyList<TcpListener> GetTcp22Listeners();
}

public sealed record TcpListener(uint ProcessId, string? ExecutablePath);

public interface IFirewallManager
{
    FirewallPreflightResult Preflight(string expectedProgramPath);
    FirewallEnsureResult EnsureExactRule(string expectedProgramPath);
    bool VerifyExactRule(string expectedProgramPath);
}

public sealed record FirewallPreflightResult(bool Safe, string? ConflictMessage);
public sealed record FirewallEnsureResult(bool ReusedExistingRule);

public interface IClock
{
    DateTimeOffset UtcNow { get; }
    void Delay(TimeSpan duration);
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public void Delay(TimeSpan duration) => Thread.Sleep(duration);
}

public sealed record OperationResult(bool Success, bool PartialSuccess, string Message)
{
    public static OperationResult Ok(string message) => new(true, false, message);
    public static OperationResult Fail(string message) => new(false, false, message);
    public static OperationResult Partial(string message) => new(false, true, message);
}

public interface IStatusSink
{
    void Report(string message);
}

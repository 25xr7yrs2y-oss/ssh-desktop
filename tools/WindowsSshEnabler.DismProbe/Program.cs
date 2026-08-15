using System.Text.Json;
using WindowsSshEnabler.Native;

namespace WindowsSshEnabler.DismProbe;

internal static class Program
{
    private const string CapabilityName = "OpenSSH.Server~~~~0.0.1.0";

    private static int Main()
    {
        if (!OperatingSystem.IsWindows())
            return WriteFailure("This read-only integration probe can run only on Windows.");

        try
        {
            using var probe = new DismCapabilityProbe();
            var state = probe.GetOpenSshServerState();
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                capabilityName = CapabilityName,
                productionProbeState = state.ToString(),
                mutatingOperations = false,
                queriedAtUtc = DateTimeOffset.UtcNow
            }));
            return 0;
        }
        catch (Exception ex)
        {
            return WriteFailure($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static int WriteFailure(string message)
    {
        Console.Error.WriteLine(JsonSerializer.Serialize(new
        {
            capabilityName = CapabilityName,
            error = message,
            mutatingOperations = false,
            queriedAtUtc = DateTimeOffset.UtcNow
        }));
        return 1;
    }
}

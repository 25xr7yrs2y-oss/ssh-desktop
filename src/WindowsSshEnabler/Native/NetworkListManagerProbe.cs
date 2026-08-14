using System.Runtime.InteropServices;
using WindowsSshEnabler.Core;

namespace WindowsSshEnabler.Native;

public sealed class NetworkListManagerProbe : INetworkProbe
{
    private static readonly Guid NetworkListManagerClassId = new("DCB00C01-570F-4A9B-8D69-199FDBA5723B");

    public NetworkSnapshot InspectActiveNetworks()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows Network List Manager is unavailable.");
        object? manager = null;
        object? networks = null;
        try
        {
            var type = Type.GetTypeFromCLSID(NetworkListManagerClassId, throwOnError: true)
                ?? throw new PlatformNotSupportedException("Windows Network List Manager is unavailable.");
            manager = Activator.CreateInstance(type) ?? throw new PlatformNotSupportedException("Windows Network List Manager could not be created.");
            dynamic dynamicManager = manager;
            networks = dynamicManager.GetNetworks(1) ?? throw new InvalidOperationException("Windows Network List Manager returned no network collection."); // NLM_ENUM_NETWORK_CONNECTED
            var connected = false;
            var trusted = false;
            var publicNetwork = false;
            foreach (object network in (System.Collections.IEnumerable)networks)
            {
                try
                {
                    connected = true;
                    dynamic dynamicNetwork = network;
                    int category = dynamicNetwork.GetCategory();
                    trusted |= category is 1 or 2; // Private or Domain-authenticated
                    publicNetwork |= category == 0;
                }
                finally { ReleaseCom(network); }
            }
            return new(connected, trusted, publicNetwork);
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException($"Windows Network List Manager failed (0x{ex.HResult:X8}): {ex.Message}", ex);
        }
        finally
        {
            ReleaseCom(networks);
            ReleaseCom(manager);
        }
    }

    private static void ReleaseCom(object? instance)
    {
        if (instance is not null && Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance);
    }
}

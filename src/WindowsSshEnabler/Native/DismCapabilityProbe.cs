using System.Runtime.InteropServices;
using WindowsSshEnabler.Core;

namespace WindowsSshEnabler.Native;

public sealed class DismCapabilityProbe : ICapabilityProbe
{
    private const string OnlineImage = "DISM_{53BFAE52-B167-4E2F-A258-0A37B57FF845}";
    private const string OpenSshServerCapability = "OpenSSH.Server~~~~0.0.1.0";

    public CapabilityState GetOpenSshServerState()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DISM servicing is available only on Windows.");

        IntPtr session = IntPtr.Zero;
        IntPtr info = IntPtr.Zero;
        var initialized = false;
        try
        {
            ThrowIfFailed(DismInitialize(0, null, null));
            initialized = true;
            ThrowIfFailed(DismOpenSession(OnlineImage, null, null, out session));
            ThrowIfFailed(DismGetCapabilityInfo(session, OpenSshServerCapability, null, null, out info));
            var native = Marshal.PtrToStructure<DismCapabilityInfo>(info);
            return native.State switch
            {
                5 => CapabilityState.Installed,
                0 or 2 or 3 or 4 or 7 => CapabilityState.NotInstalled,
                1 or 6 or 8 => CapabilityState.Pending,
                _ => CapabilityState.Unknown
            };
        }
        catch (DllNotFoundException ex)
        {
            throw new PlatformNotSupportedException("The documented Windows DISM API (DismApi.dll) is unavailable.", ex);
        }
        finally
        {
            if (info != IntPtr.Zero) DismDelete(info);
            if (session != IntPtr.Zero) DismCloseSession(session);
            if (initialized) DismShutdown();
        }
    }

    private static void ThrowIfFailed(int result)
    {
        if (result < 0) Marshal.ThrowExceptionForHR(result);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DismCapabilityInfo
    {
        public IntPtr Name;
        public int State;
        public IntPtr DisplayName;
        public IntPtr Description;
        public uint DownloadSize;
        public uint InstallSize;
    }

    [DllImport("DismApi.dll", CharSet = CharSet.Unicode)]
    private static extern int DismInitialize(uint logLevel, string? logFilePath, string? scratchDirectory);

    [DllImport("DismApi.dll", CharSet = CharSet.Unicode)]
    private static extern int DismOpenSession(string imagePath, string? windowsDirectory, string? systemDrive, out IntPtr session);

    [DllImport("DismApi.dll", CharSet = CharSet.Unicode)]
    private static extern int DismGetCapabilityInfo(IntPtr session, string name, string? identifier, string? culture, out IntPtr capabilityInfo);

    [DllImport("DismApi.dll")]
    private static extern int DismCloseSession(IntPtr session);

    [DllImport("DismApi.dll")]
    private static extern int DismShutdown();

    [DllImport("DismApi.dll")]
    private static extern void DismDelete(IntPtr dismStructure);
}

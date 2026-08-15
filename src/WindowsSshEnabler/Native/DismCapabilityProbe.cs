using System.Runtime.InteropServices;
using WindowsSshEnabler.Core;

namespace WindowsSshEnabler.Native;

public sealed class DismCapabilityProbe : ICapabilityProbe, IDisposable
{
    private const string OnlineImage = "DISM_{53BFAE52-B167-4E2F-A258-0A37B57FF845}";
    private const string OpenSshServerCapability = "OpenSSH.Server~~~~0.0.1.0";

    private readonly IDismApi api;
    private readonly object sync = new();
    private bool initialized;
    private bool disposed;

    public DismCapabilityProbe() : this(NativeDismApi.Instance) { }

    internal DismCapabilityProbe(IDismApi api) => this.api = api ?? throw new ArgumentNullException(nameof(api));

    public CapabilityState GetOpenSshServerState()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DISM servicing is available only on Windows.");

        try
        {
            lock (sync)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                return GetOpenSshServerStateCore();
            }
        }
        catch (DllNotFoundException ex)
        {
            throw new PlatformNotSupportedException("The documented Windows DISM API (DismApi.dll) is unavailable.", ex);
        }
        catch (EntryPointNotFoundException ex)
        {
            throw new PlatformNotSupportedException("The installed Windows DISM API does not expose the required capability query.", ex);
        }
    }

    internal CapabilityState GetOpenSshServerStateCore()
    {
        EnsureInitialized();

        uint session = 0;
        IntPtr info = IntPtr.Zero;
        var querySucceeded = false;
        try
        {
            ThrowIfFailed(api.OpenSession(OnlineImage, null, null, out session));
            ThrowIfFailed(api.GetCapabilityInfo(session, OpenSshServerCapability, out info));
            var native = Marshal.PtrToStructure<DismCapabilityInfo>(info);
            querySucceeded = true;
            return MapNativeState(native.State);
        }
        finally
        {
            var cleanupFailure = 0;
            if (info != IntPtr.Zero)
                cleanupFailure = FirstFailure(cleanupFailure, api.Delete(info));
            if (session != 0)
                cleanupFailure = FirstFailure(cleanupFailure, api.CloseSession(session));

            // Preserve an exception from the query itself. If the query succeeded,
            // surface a failed release instead of silently reporting a valid state.
            if (querySucceeded)
                ThrowIfFailed(cleanupFailure);
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            if (initialized)
            {
                // No session remains open here because each query closes its session
                // while holding the same lock. Dispose is best effort by convention.
                api.Shutdown();
                initialized = false;
            }
        }
    }

    private void EnsureInitialized()
    {
        if (initialized) return;
        ThrowIfFailed(api.Initialize(DismLogLevel.Errors, null, null));
        initialized = true;
    }

    internal static CapabilityState MapNativeState(DismPackageFeatureState state) => state switch
    {
        DismPackageFeatureState.Installed => CapabilityState.Installed,
        DismPackageFeatureState.NotPresent or
        DismPackageFeatureState.Staged or
        DismPackageFeatureState.Removed or
        DismPackageFeatureState.Superseded => CapabilityState.NotInstalled,
        DismPackageFeatureState.UninstallPending or
        DismPackageFeatureState.InstallPending or
        DismPackageFeatureState.PartiallyInstalled => CapabilityState.Pending,
        _ => CapabilityState.Unknown
    };

    private static int FirstFailure(int current, int candidate) => current < 0 ? current : candidate;

    private static void ThrowIfFailed(int result)
    {
        if (result < 0) Marshal.ThrowExceptionForHR(result);
    }

    internal enum DismLogLevel
    {
        Errors = 0,
        ErrorsWarnings = 1,
        ErrorsWarningsInfo = 2
    }

    internal enum DismPackageFeatureState
    {
        NotPresent = 0,
        UninstallPending = 1,
        Staged = 2,
        Removed = 3,
        Installed = 4,
        InstallPending = 5,
        Superseded = 6,
        PartiallyInstalled = 7
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DismCapabilityInfo
    {
        public IntPtr Name;
        public DismPackageFeatureState State;
        public IntPtr DisplayName;
        public IntPtr Description;
        public uint DownloadSize;
        public uint InstallSize;
    }

    internal interface IDismApi
    {
        int Initialize(DismLogLevel logLevel, string? logFilePath, string? scratchDirectory);
        int OpenSession(string imagePath, string? windowsDirectory, string? systemDrive, out uint session);
        int GetCapabilityInfo(uint session, string name, out IntPtr capabilityInfo);
        int Delete(IntPtr dismStructure);
        int CloseSession(uint session);
        int Shutdown();
    }

    private sealed class NativeDismApi : IDismApi
    {
        internal static NativeDismApi Instance { get; } = new();

        private NativeDismApi() { }

        public int Initialize(DismLogLevel logLevel, string? logFilePath, string? scratchDirectory) =>
            NativeMethods.DismInitialize(logLevel, logFilePath, scratchDirectory);

        public int OpenSession(string imagePath, string? windowsDirectory, string? systemDrive, out uint session) =>
            NativeMethods.DismOpenSession(imagePath, windowsDirectory, systemDrive, out session);

        public int GetCapabilityInfo(uint session, string name, out IntPtr capabilityInfo) =>
            NativeMethods.DismGetCapabilityInfo(session, name, out capabilityInfo);

        public int Delete(IntPtr dismStructure) => NativeMethods.DismDelete(dismStructure);
        public int CloseSession(uint session) => NativeMethods.DismCloseSession(session);
        public int Shutdown() => NativeMethods.DismShutdown();
    }

    private static class NativeMethods
    {
        [DllImport("DismApi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
        internal static extern int DismInitialize(DismLogLevel logLevel, string? logFilePath, string? scratchDirectory);

        [DllImport("DismApi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
        internal static extern int DismOpenSession(string imagePath, string? windowsDirectory, string? systemDrive, out uint session);

        [DllImport("DismApi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
        internal static extern int DismGetCapabilityInfo(uint session, string name, out IntPtr capabilityInfo);

        [DllImport("DismApi.dll", ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
        internal static extern int DismDelete(IntPtr dismStructure);

        [DllImport("DismApi.dll", ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
        internal static extern int DismCloseSession(uint session);

        [DllImport("DismApi.dll", ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
        internal static extern int DismShutdown();
    }
}

using System.ComponentModel;
using System.Runtime.InteropServices;
using WindowsSshEnabler.Core;

namespace WindowsSshEnabler.Native;

public sealed class NativeServiceManager(IClock clock) : IServiceManager
{
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryConfig = 0x0001;
    private const uint ServiceChangeConfig = 0x0002;
    private const uint ServiceQueryStatus = 0x0004;
    private const uint ServiceStart = 0x0010;
    private const uint ServiceNoChange = 0xffffffff;
    private const uint ServiceAutoStart = 2;
    private const int ScStatusProcessInfo = 0;
    private const uint ServiceRunning = 4;
    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorServiceDoesNotExist = 1060;
    private const int ErrorServiceAlreadyRunning = 1056;

    public ServiceSnapshot InspectSshd()
    {
        using var scm = OpenScm();
        using var service = OpenService(scm, ServiceQueryConfig | ServiceQueryStatus, missingIsNull: true);
        if (service is null) return new(false, null, false, 0);
        return ReadSnapshot(service);
    }

    public void ConfigureAutomaticAndStart(TimeSpan timeout)
    {
        using var scm = OpenScm();
        using var service = OpenService(scm, ServiceQueryConfig | ServiceQueryStatus | ServiceChangeConfig | ServiceStart, missingIsNull: false)
            ?? throw new InvalidOperationException("The sshd service disappeared before it could be started.");

        if (!ChangeServiceConfig(service, ServiceNoChange, ServiceAutoStart, ServiceNoChange, null, null, IntPtr.Zero, null, null, null, null))
            ThrowLastWin32("Windows could not set the sshd service to Automatic startup");

        var snapshot = ReadSnapshot(service);
        if (!snapshot.Running && !StartService(service, 0, IntPtr.Zero))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorServiceAlreadyRunning) ThrowWin32(error, "Windows could not start the sshd service");
        }

        var deadline = clock.UtcNow + timeout;
        do
        {
            snapshot = ReadSnapshot(service);
            if (snapshot.Running) return;
            clock.Delay(TimeSpan.FromMilliseconds(250));
        } while (clock.UtcNow < deadline);

        throw new TimeoutException("The sshd service did not reach the Running state within 30 seconds.");
    }

    private static ServiceSnapshot ReadSnapshot(SafeServiceHandle service)
    {
        var statusSize = Marshal.SizeOf<ServiceStatusProcess>();
        var statusBuffer = Marshal.AllocHGlobal(statusSize);
        try
        {
            if (!QueryServiceStatusEx(service, ScStatusProcessInfo, statusBuffer, (uint)statusSize, out _))
                ThrowLastWin32("Windows could not query the sshd service status");
            var status = Marshal.PtrToStructure<ServiceStatusProcess>(statusBuffer);
            return new(true, QueryExecutablePath(service), status.CurrentState == ServiceRunning, status.ProcessId);
        }
        finally { Marshal.FreeHGlobal(statusBuffer); }
    }

    private static string QueryExecutablePath(SafeServiceHandle service)
    {
        _ = QueryServiceConfig(service, IntPtr.Zero, 0, out var needed);
        var error = Marshal.GetLastWin32Error();
        if (needed == 0 || error != ErrorInsufficientBuffer) ThrowWin32(error, "Windows could not size the sshd service configuration");
        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!QueryServiceConfig(service, buffer, needed, out _)) ThrowLastWin32("Windows could not read the sshd service configuration");
            var config = Marshal.PtrToStructure<QueryServiceConfigData>(buffer);
            var commandLine = Marshal.PtrToStringUni(config.BinaryPathName) ?? string.Empty;
            return ExtractExecutable(commandLine);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    internal static string ExtractExecutable(string commandLine)
    {
        var expanded = Environment.ExpandEnvironmentVariables(commandLine).Trim();
        if (expanded.StartsWith('"'))
        {
            var closing = expanded.IndexOf('"', 1);
            if (closing < 2) throw new InvalidOperationException("The sshd service executable path is malformed.");
            return Path.GetFullPath(expanded[1..closing]);
        }
        var exe = expanded.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe < 0) throw new InvalidOperationException("The sshd service registration does not contain an executable path.");
        return Path.GetFullPath(expanded[..(exe + 4)]);
    }

    private static SafeServiceHandle OpenScm()
    {
        var handle = OpenSCManager(null, null, ScManagerConnect);
        if (handle.IsInvalid) ThrowLastWin32("Windows could not open the Service Control Manager");
        return handle;
    }

    private static SafeServiceHandle? OpenService(SafeServiceHandle scm, uint access, bool missingIsNull)
    {
        var handle = OpenServiceW(scm, "sshd", access);
        if (!handle.IsInvalid) return handle;
        var error = Marshal.GetLastWin32Error();
        handle.Dispose();
        if (missingIsNull && error == ErrorServiceDoesNotExist) return null;
        ThrowWin32(error, "Windows could not open the sshd service");
        return null;
    }

    private static void ThrowLastWin32(string context) => ThrowWin32(Marshal.GetLastWin32Error(), context);

    private static void ThrowWin32(int error, string context)
    {
        if (error == 5) throw new UnauthorizedAccessException($"{context}: access denied.");
        throw new InvalidOperationException($"{context}: {new Win32Exception(error).Message} (error {error}).");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct QueryServiceConfigData
    {
        public uint ServiceType;
        public uint StartType;
        public uint ErrorControl;
        public IntPtr BinaryPathName;
        public IntPtr LoadOrderGroup;
        public uint TagId;
        public IntPtr Dependencies;
        public IntPtr ServiceStartName;
        public IntPtr DisplayName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusProcess
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
        public uint ProcessId;
        public uint ServiceFlags;
    }

    private sealed class SafeServiceHandle : Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafeServiceHandle() : base(true) { }
        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeServiceHandle OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "OpenServiceW")]
    private static extern SafeServiceHandle OpenServiceW(SafeServiceHandle serviceControlManager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr serviceHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool QueryServiceStatusEx(SafeServiceHandle service, int infoLevel, IntPtr buffer, uint bufferSize, out uint bytesNeeded);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryServiceConfig(SafeServiceHandle service, IntPtr serviceConfig, uint bufferSize, out uint bytesNeeded);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ChangeServiceConfig(SafeServiceHandle service, uint serviceType, uint startType, uint errorControl,
        string? binaryPathName, string? loadOrderGroup, IntPtr tagId, string? dependencies, string? serviceStartName,
        string? password, string? displayName);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool StartService(SafeServiceHandle service, uint argumentCount, IntPtr arguments);
}

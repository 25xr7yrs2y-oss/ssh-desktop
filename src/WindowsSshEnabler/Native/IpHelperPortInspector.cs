using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using WindowsSshEnabler.Core;

namespace WindowsSshEnabler.Native;

public sealed class IpHelperPortInspector : IPortInspector
{
    private const int AfInet = 2;
    private const int AfInet6 = 23;
    private const int TcpTableOwnerPidListener = 3;
    private const uint ErrorInsufficientBuffer = 122;

    public IReadOnlyList<TcpListener> GetTcp22Listeners()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The Windows IP Helper API is unavailable.");
        var listeners = new List<TcpListener>();
        ReadTable(AfInet, listeners);
        ReadTable(AfInet6, listeners);
        return listeners
            .GroupBy(x => x.ProcessId)
            .Select(x => x.First())
            .ToList();
    }

    private static void ReadTable(int addressFamily, List<TcpListener> listeners)
    {
        uint size = 0;
        var first = GetExtendedTcpTable(IntPtr.Zero, ref size, true, addressFamily, TcpTableOwnerPidListener, 0);
        if (first != ErrorInsufficientBuffer || size < sizeof(uint))
            throw new InvalidOperationException($"Windows could not size the TCP listener table: {new Win32Exception((int)first).Message} (error {first}).");

        var buffer = Marshal.AllocHGlobal(checked((int)size));
        try
        {
            var result = GetExtendedTcpTable(buffer, ref size, true, addressFamily, TcpTableOwnerPidListener, 0);
            if (result != 0) throw new InvalidOperationException($"Windows could not read the TCP listener table: {new Win32Exception((int)result).Message} (error {result}).");
            var count = Marshal.ReadInt32(buffer);
            if (count < 0 || count > 1_000_000) throw new InvalidOperationException("Windows returned an invalid TCP listener count.");
            var rowSize = addressFamily == AfInet ? Marshal.SizeOf<Tcp4RowOwnerPid>() : Marshal.SizeOf<Tcp6RowOwnerPid>();
            var cursor = IntPtr.Add(buffer, sizeof(uint));
            for (var i = 0; i < count; i++)
            {
                var rowPointer = IntPtr.Add(cursor, checked(i * rowSize));
                uint localPort;
                uint owningPid;
                if (addressFamily == AfInet)
                {
                    var row = Marshal.PtrToStructure<Tcp4RowOwnerPid>(rowPointer);
                    localPort = row.LocalPort;
                    owningPid = row.OwningPid;
                }
                else
                {
                    var row = Marshal.PtrToStructure<Tcp6RowOwnerPid>(rowPointer);
                    localPort = row.LocalPort;
                    owningPid = row.OwningPid;
                }
                var port = unchecked((ushort)IPAddress.NetworkToHostOrder((short)localPort));
                if (port == 22) listeners.Add(new(owningPid, TryGetProcessPath(owningPid)));
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static string? TryGetProcessPath(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            return process.MainModule?.FileName;
        }
        catch { return null; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Tcp4RowOwnerPid
    {
        public uint State;
        public uint LocalAddress;
        public uint LocalPort;
        public uint RemoteAddress;
        public uint RemotePort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Tcp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddress;
        public uint LocalScopeId;
        public uint LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] RemoteAddress;
        public uint RemoteScopeId;
        public uint RemotePort;
        public uint State;
        public uint OwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr tcpTable, ref uint size, bool order, int addressFamily, int tableClass, uint reserved);
}

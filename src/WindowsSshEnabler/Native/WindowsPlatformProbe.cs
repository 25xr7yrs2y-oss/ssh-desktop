using System.Security.Principal;
using WindowsSshEnabler.Core;

namespace WindowsSshEnabler.Native;

public sealed class WindowsPlatformProbe : IPlatformProbe
{
    public bool IsSupportedWindows => OperatingSystem.IsWindowsVersionAtLeast(10) && Environment.Is64BitOperatingSystem && Environment.Is64BitProcess;

    public bool IsElevated
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public string ExpectedSshdPath
    {
        get
        {
            var windows = Environment.GetEnvironmentVariable("WINDIR");
            if (string.IsNullOrWhiteSpace(windows))
                throw new PlatformNotSupportedException("The WINDIR environment variable is unavailable.");
            return Path.GetFullPath(Path.Combine(windows, "System32", "OpenSSH", "sshd.exe"));
        }
    }
}

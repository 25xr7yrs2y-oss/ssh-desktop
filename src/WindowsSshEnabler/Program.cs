using WindowsSshEnabler.Core;
using WindowsSshEnabler.Native;
using WindowsSshEnabler.UI;

namespace WindowsSshEnabler;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, args) => ShowFatal(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => ShowFatal(args.ExceptionObject as Exception);

        var clock = new SystemClock();
        using var capabilityProbe = new DismCapabilityProbe();
        var controller = new EnablerController(
            new WindowsPlatformProbe(),
            capabilityProbe,
            new NativeServiceManager(clock),
            new NetworkListManagerProbe(),
            new IpHelperPortInspector(),
            new WindowsFirewallManager(),
            clock);

        Application.Run(new MainForm(controller));
    }

    private static void ShowFatal(Exception? exception)
    {
        MessageBox.Show(
            $"Windows SSH Enabler encountered an unexpected error and must close.\n\n{exception?.Message ?? "Unknown error"}",
            "Windows SSH Enabler",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}

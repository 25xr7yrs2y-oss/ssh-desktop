namespace WindowsSshEnabler.Core;

public sealed class EnablerController(
    IPlatformProbe platform,
    ICapabilityProbe capability,
    IServiceManager service,
    INetworkProbe network,
    IPortInspector ports,
    IFirewallManager firewall,
    IClock clock)
{
    private const string MissingCapabilityMessage =
        "OpenSSH Server is not installed. Open Windows Settings > Apps > Optional Features, add 'OpenSSH Server', then try again.";

    public OperationResult Run(IStatusSink status)
    {
        var serviceWasMutated = false;
        try
        {
            status.Report("Checking Windows and administrator privileges...");
            if (!platform.IsSupportedWindows)
                return OperationResult.Fail("This application requires a supported 64-bit Windows 10, Windows 11, or Windows Server installation.");
            if (!platform.IsElevated)
                return OperationResult.Fail("Administrator privileges are required. Close the application and reopen it by accepting the Windows UAC prompt.");

            status.Report("Checking the Windows OpenSSH Server capability...");
            var capabilityState = capability.GetOpenSshServerState();
            if (capabilityState == CapabilityState.NotInstalled)
                return OperationResult.Fail(MissingCapabilityMessage);
            if (capabilityState != CapabilityState.Installed)
                return OperationResult.Fail($"OpenSSH Server is not ready (state: {capabilityState}). Finish any pending Windows servicing operation and try again.");

            var expectedPath = platform.ExpectedSshdPath;
            status.Report("Validating the sshd service registration...");
            var beforeService = service.InspectSshd();
            if (!beforeService.Exists)
                return OperationResult.Fail("OpenSSH Server appears installed, but the sshd service is missing. Repair the Windows optional feature before trying again.");
            if (!PathsEqual(beforeService.ExecutablePath, expectedPath))
                return OperationResult.Fail($"Safety check failed: the sshd service points to an unexpected executable ('{beforeService.ExecutablePath ?? "unknown"}'). Expected '{expectedPath}'. No changes were made.");

            status.Report("Checking active network categories...");
            var activeNetworks = network.InspectActiveNetworks();
            if (!activeNetworks.HasConnectedNetwork)
                return OperationResult.Fail("No connected network was detected. Connect to a trusted Private or Domain network and try again.");
            if (!activeNetworks.HasTrustedNetwork)
                return OperationResult.Fail("Only Public networks are active. For safety, this application opens SSH only on Private or Domain networks and will not change the network category.");

            status.Report("Checking TCP port 22 ownership...");
            var beforeListeners = ports.GetTcp22Listeners();
            if (!ListenersBelongToService(beforeListeners, beforeService))
                return OperationResult.Fail(DescribePortConflict(beforeListeners));

            status.Report("Checking Windows Defender Firewall for conflicting rules...");
            var preflight = firewall.Preflight(expectedPath);
            if (!preflight.Safe)
                return OperationResult.Fail(preflight.ConflictMessage ?? "A conflicting firewall rule prevents a safe configuration. No changes were made.");

            status.Report("Configuring and starting the sshd service...");
            // The first native operation can persistently change startup type, so
            // all failures from this point are conservatively reported as partial.
            serviceWasMutated = true;
            service.ConfigureAutomaticAndStart(TimeSpan.FromSeconds(30));

            var runningService = service.InspectSshd();
            if (!runningService.Running || runningService.ProcessId == 0)
                return OperationResult.Partial("The sshd service configuration was changed, but the service is not running. Review Windows Event Viewer and the OpenSSH logs.");

            status.Report("Waiting for sshd to listen on TCP port 22...");
            if (!WaitForExclusiveListener(runningService, TimeSpan.FromSeconds(15)))
                return OperationResult.Partial("The sshd service is running, but it did not become the exclusive TCP port 22 listener. The firewall was not changed. Review the OpenSSH logs.");

            status.Report("Creating or verifying the restricted LAN firewall rule...");
            var ruleResult = firewall.EnsureExactRule(expectedPath);

            status.Report("Performing final read-back verification...");
            var finalService = service.InspectSshd();
            if (!finalService.Running || finalService.ProcessId == 0)
                return OperationResult.Partial("The firewall step completed, but sshd stopped before final verification. Review Windows Event Viewer and the OpenSSH logs.");
            var finalListeners = ports.GetTcp22Listeners();
            if (finalListeners.Count == 0 || !ListenersBelongToService(finalListeners, finalService))
                return OperationResult.Partial("The firewall step completed, but sshd is not the exclusive TCP port 22 listener at final verification.");
            if (!firewall.VerifyExactRule(expectedPath))
                return OperationResult.Partial("The service is running and listening locally, but the exact restricted firewall rule could not be verified. Remote access may not work.");
            var finalFirewallPreflight = firewall.Preflight(expectedPath);
            if (!finalFirewallPreflight.Safe)
                return OperationResult.Partial($"The application-owned rule is exact, but a conflicting firewall rule appeared during the operation. {finalFirewallPreflight.ConflictMessage}");

            var ruleText = ruleResult.ReusedExistingRule ? "The existing restricted firewall rule was reused." : "A restricted firewall rule was created.";
            return OperationResult.Ok($"Success. OpenSSH Server is running and locally listening on TCP port 22. {ruleText} Access is limited to Domain/Private profiles and LocalSubnet. Remote connectivity was not tested.");
        }
        catch (Exception ex)
        {
            var detail = FriendlyException(ex);
            return serviceWasMutated
                ? OperationResult.Partial($"Partial success: sshd may have been configured or started, but a later step failed. {detail}")
                : OperationResult.Fail(detail);
        }
    }

    private bool WaitForExclusiveListener(ServiceSnapshot serviceState, TimeSpan timeout)
    {
        var deadline = clock.UtcNow + timeout;
        do
        {
            var listeners = ports.GetTcp22Listeners();
            if (listeners.Count > 0 && ListenersBelongToService(listeners, serviceState))
                return true;
            clock.Delay(TimeSpan.FromMilliseconds(250));
        } while (clock.UtcNow < deadline);
        return false;
    }

    private static bool ListenersBelongToService(IReadOnlyList<TcpListener> listeners, ServiceSnapshot serviceState) =>
        listeners.All(x => serviceState.Running && serviceState.ProcessId != 0 && x.ProcessId == serviceState.ProcessId);

    private static string DescribePortConflict(IReadOnlyList<TcpListener> listeners)
    {
        var owner = listeners.FirstOrDefault();
        return owner is null
            ? "TCP port 22 could not be inspected safely. No changes were made."
            : $"TCP port 22 is already owned by another process (PID {owner.ProcessId}, path '{owner.ExecutablePath ?? "unavailable"}'). Stop or reconfigure that process before trying again.";
    }

    private static bool PathsEqual(string? left, string right)
    {
        if (string.IsNullOrWhiteSpace(left)) return false;
        try { return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private static string FriendlyException(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "Access was denied by Windows. Confirm that UAC elevation was accepted and that your administrator policy permits service and firewall changes.",
        PlatformNotSupportedException => $"A required Windows API is unavailable: {ex.Message}",
        TimeoutException => $"Windows did not complete the operation before the safety timeout: {ex.Message}",
        InvalidOperationException => ex.Message,
        _ => $"An unexpected error occurred ({ex.GetType().Name}): {ex.Message}. No unrelated settings were changed."
    };
}

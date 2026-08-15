# Native DISM integration probe

This test-only console program calls the production `DismCapabilityProbe` and prints one JSON object. It only reads the state of `OpenSSH.Server~~~~0.0.1.0`; it does not add or remove capabilities and does not touch services, firewall rules, networking, accounts, or SSH configuration.

Run the published executable from an elevated Windows session, record its JSON output and exit code, and compare `productionProbeState` with:

```powershell
Get-WindowsCapability -Online -Name 'OpenSSH.Server~~~~0.0.1.0' |
    Select-Object Name, State
```

An installed capability must produce `productionProbeState` equal to `Installed`, and the PowerShell result must also report `Installed`.

# Windows SSH Enabler

Windows SSH Enabler is a small, one-button Windows desktop application that starts an already-installed Windows OpenSSH Server and creates a narrowly scoped Windows Defender Firewall rule for LAN access.

## Supported scope and prerequisites

- x64 Windows 10 22H2, Windows 11, or a supported Windows Server Desktop Experience edition.
- The Windows optional feature **OpenSSH Server must already be installed**. This application never installs or downloads it.
- An administrator account; the application requests elevation through the standard Windows UAC prompt.
- A connected Domain or Private network. Public-only networking is intentionally refused.

The application is published as a self-contained `win-x64` executable, so users do not need to install .NET separately. Other processor architectures and non-Windows systems are not supported.

## Install and use

1. Download `WindowsSshEnabler-Setup-1.0.1-x64.exe`, `SHA256SUMS.txt`, and optionally `artifact-inventory.json` from the [v1.0.1 release](https://github.com/25xr7yrs2y-oss/ssh-desktop/releases/tag/v1.0.1).
2. Verify the installer SHA-256 as described below.
3. Run the installer and accept the normal Windows UAC prompt.
4. Open **Windows SSH Enabler** from the Desktop or Start Menu.
5. Click **Enable SSH Server** and read the status shown in the application.

> **Unsigned prerelease:** Version 1.0.1 is not Authenticode-signed. Windows Defender SmartScreen may warn or block it, and Windows will not show a trusted publisher. Review the source and checksum before running it. This project does not claim code-signing trust or SmartScreen reputation.

## What the button does

1. Verifies supported Windows/x64, elevation, the OpenSSH capability, and the expected in-box `sshd.exe` service registration.
2. Refuses Public-only networking, foreign TCP 22 listeners, blocking firewall rules, and existing firewall rules that expose SSH more broadly.
3. Sets `sshd` to Automatic startup and starts it with bounded waits.
4. Creates or reuses one rule named `WindowsSshEnabler.LanOpenSsh.Tcp22`.
5. Restricts that rule to inbound TCP 22, Domain and Private profiles, `LocalSubnet`, the in-box `sshd.exe`, the `sshd` service, and disabled edge traversal.
6. Re-reads the service, IPv4/IPv6 listener ownership, exact rule, and conflicts before reporting point-in-time local success.

It does not disable the firewall, open TCP 22 on the Public profile or beyond `LocalSubnet`, edit `sshd_config`, change accounts, passwords, keys, or authentication, configure routers or cloud firewalls, or open another port.

## What v1.0.1 fixes

Version 1.0.1 repairs the native DISM capability query used before any service or firewall change. Version 1.0.0 declared `DismGetCapabilityInfo` with five managed parameters even though the Windows API has exactly three, which caused `E_INVALIDARG` on Windows. The repair uses the exact three-parameter ABI and correct `uint` session type, native structure layout, HRESULT-returning cleanup, package-state values (`Installed = 4`, `InstallPending = 5`), and process-lifetime DISM initialization/shutdown.

Regression coverage now includes 17 tests plus a Windows-only, read-only probe that invokes the production DISM code. On the available Windows Server 2022 test instance, locked restores, the Release build, all 17 tests, application/probe publishing, and two production-probe executions succeeded without `E_INVALIDARG`. That instance had OpenSSH Server `NotPresent` and only a Public network, so it safely did not exercise the installed-capability path, start `sshd`, create the application firewall rule, click the GUI under an interactive desktop, or test LAN connectivity. Those remain acceptance limitations rather than claimed results.

## Verify the installer SHA-256

In PowerShell, run:

```powershell
$installer = '.\WindowsSshEnabler-Setup-1.0.1-x64.exe'
$expected = (Select-String -Path '.\SHA256SUMS.txt' -Pattern 'WindowsSshEnabler-Setup-1\.0\.1-x64\.exe$').Line.Split()[0]
$actual = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $expected.ToLowerInvariant()) { throw 'Installer SHA-256 mismatch.' }
"Verified SHA-256: $actual"
```

The same installer digest and byte length are recorded in `artifact-inventory.json`, whose `authenticode` value is expected to be `NotSigned` for this prerelease.

## Uninstall behavior

The standard uninstaller removes only the application file, installation directory when empty, shortcuts, and uninstall registration. It does not stop `sshd`, change its startup mode, uninstall OpenSSH, remove the firewall rule, or revert network state.

## Build

The application targets .NET 10 WinForms and is published as a self-contained, untrimmed, single-file `win-x64` GUI executable. The installer is produced with Inno Setup 6.

On a Windows build machine with the .NET 10 SDK, Inno Setup 6, and the Windows SDK Manifest Tool:

```powershell
.\packaging\build\Build-Package.ps1 `
  -SourceRoot $PWD `
  -Version 1.0.1 `
  -PublisherName '25xr7yrs2y-oss'
```

The build fails closed on failed locked restores/tests, unexpected payload files, a console-subsystem executable, a missing elevation manifest, broadened installer behavior, or an unexpected Authenticode state. Signing is disabled by default.

See [the DISM implementation report](docs/reports/dism-fix-agent1-report.md), [the independent Windows validation report](docs/reports/dism-fix-agent2-report.md), [the original implementation report](docs/reports/agent1-report.md), and [the packaging/security report](docs/reports/agent2-report.md) for architecture, threat analysis, evidence, and limitations.

# Windows SSH Enabler

Windows SSH Enabler is a one-button Windows desktop application that starts an already-installed Windows OpenSSH Server and creates a narrowly scoped Windows Defender Firewall rule for LAN access.

## Download and install

Download `WindowsSshEnabler-Setup-x64.exe` from the [latest release](https://github.com/25xr7yrs2y-oss/ssh-desktop/releases/latest), run the installer, and use the **Windows SSH Enabler** shortcut created on the Desktop or in the Start Menu.

> **Unsigned release:** Code signing is deferred. Version 1.0.0 is an unsigned prerelease and Windows Defender SmartScreen may warn before installation. Review the source, release notes, and SHA-256 checksum before running it.

The application requests administrator access through the standard Windows UAC prompt. Its window has exactly one **Enable SSH Server** button and a read-only status/error area. End users do not need PowerShell, command-line installation steps, or a separate .NET installation.

## Requirements

- x64 Windows 10 22H2, Windows 11, or a supported Windows Server Desktop Experience edition.
- The Windows optional feature **OpenSSH Server** must already be installed.
- An administrator account.
- A connected Domain or Private network. Public-only networks are intentionally refused.

The application never installs or downloads OpenSSH Server.

## What the button does

1. Verifies Windows, elevation, the OpenSSH capability, and the expected in-box `sshd.exe` service registration.
2. Refuses Public-only networking, foreign TCP 22 listeners, blocking firewall rules, and existing firewall rules that expose SSH more broadly.
3. Sets `sshd` to Automatic startup and starts it with bounded waits.
4. Creates or reuses one rule named `WindowsSshEnabler.LanOpenSsh.Tcp22`.
5. Restricts the rule to inbound TCP 22, Domain and Private profiles, `LocalSubnet`, the in-box `sshd.exe`, the `sshd` service, and disabled edge traversal.
6. Reads the service, IPv4/IPv6 listener ownership, exact rule, and conflicts again before reporting point-in-time local success.

It does not disable the firewall, open Public networks, edit `sshd_config`, change accounts, passwords, keys, or authentication, configure a router, or open another port.

## Build

The application targets .NET 10 WinForms and is published as a self-contained, untrimmed, single-file `win-x64` GUI executable. The installer is produced with Inno Setup 6.

On a Windows build machine with the .NET 10 SDK, Inno Setup 6, and the Windows SDK Manifest Tool:

```powershell
.\packaging\build\Build-Package.ps1 `
  -SourceRoot $PWD `
  -Version 1.0.0 `
  -PublisherName '25xr7yrs2y-oss'
```

The build fails closed on failed restores/tests, unexpected payload files, a console-subsystem executable, a missing elevation manifest, broadened installer behavior, or an unexpected Authenticode state. Signing is disabled by default.

## Uninstall behavior

The standard uninstaller removes only the application file, installation directory, shortcuts, and uninstall registration. It does not stop `sshd`, change its startup mode, uninstall OpenSSH, remove firewall rules, or revert network state.

## Validation status

The release workflow compiles the application and tests on a GitHub-hosted Windows runner, executes the mocked orchestration tests, verifies the PE GUI subsystem and elevation manifest, builds the installer, records Authenticode state, and publishes SHA-256 checksums. These automated checks do not replace the native service/firewall acceptance matrix on disposable Windows test machines.

See [Agent 1's implementation report](docs/reports/agent1-report.md) and [Agent 2's packaging/security report](docs/reports/agent2-report.md) for architecture, threat analysis, limitations, and the complete test matrices.


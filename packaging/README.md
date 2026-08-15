# Windows SSH Enabler — packaging and review workstream

This directory contains the conventional Windows installer workflow and the
independent packaging/security review for **Windows SSH Enabler**. It does not
contain OpenSSH, credentials, certificates, or a prebuilt installer.

## Unsigned development status

Code signing is deliberately deferred. Any setup executable produced with the
default build command is an **unsigned development artifact**. It must not be
represented as trusted, production-signed, or guaranteed to pass Microsoft
Defender SmartScreen. Do not distribute it as a production release until a
real public-trust signing process and Windows acceptance tests are complete.

## Windows prerequisites

Run the packaging pipeline on a supported x64 Windows build machine with:

- .NET 10 SDK with the Windows Desktop workload;
- Inno Setup 6 (`ISCC.exe`);
- Windows SDK Manifest Tool (`mt.exe`);
- a checked-in `packages.lock.json` for every application and test project;
- the Agent 1 application source plus at least one test project.

The published application is self-contained for `win-x64`, so an end user does
not need to install .NET. OpenSSH Server itself is not bundled or installed.

## Build pipeline

From Windows PowerShell 5.1 or newer:

```powershell
Set-Location .\outputs\windows-installer\agent2-packaging-review
.\build\Build-Package.ps1 `
  -SourceRoot ..\agent1-app `
  -Version 0.1.0 `
  -PublisherName 'Replace with the verified publisher name'
```

The script fails closed if a tool or lock file is missing, a restore or test
fails, the publish output is absent, staging differs from the single expected
EXE, the PE subsystem is not Windows GUI, the embedded manifest does not request
`requireAdministrator`, the installer contract is broadened, compilation fails,
or the Authenticode status differs from the requested signing mode.
The publish directory itself must also contain exactly that one EXE; unexpected
sidecars are rejected instead of being silently omitted or packaged.
It runs normal test-host projects with `dotnet test`; dependency-free executable
test runners are built and then actually executed with `dotnet run --no-build`.
An unrecognized test-project shape fails instead of being treated as a pass.

Successful unsigned builds produce:

- `artifacts/WindowsSshEnabler-Setup-<version>-x64.exe`;
- `artifacts/artifact-inventory.json`;
- `artifacts/SHA256SUMS.txt`.

The inventory is path-sorted and records the byte length, SHA-256 digest, and
actual Authenticode state of both the staged app and setup executable. Generated
build products are intentionally not committed in this workstream.

## Installer behavior

The Inno Setup definition installs one exact file to 64-bit Program Files,
creates a Start Menu shortcut and a Desktop shortcut by default, and registers a
standard uninstaller. It neither launches the app silently nor contains custom
actions. Opening the shortcut starts the GUI and Windows should show its normal
UAC consent flow because the embedded application manifest requests elevation.
The stable AppId preserves upgrade identity, and normal Inno file-version rules
avoid overwriting a newer application binary with an older one.

No placeholder bitmap is bundled. For a branded build, provide a reviewed local
multi-resolution `.ico` through `-AppIconPath`; the pipeline rejects missing or
non-ICO paths and never downloads artwork. Agent 1's project must separately set
the same icon as its application icon. The default build safely uses the normal
toolchain icons until a real asset is approved.

The installer never installs OpenSSH, changes the `sshd` service, changes the
firewall, modifies Windows security controls, or runs PowerShell/cmd. Those
system decisions belong solely to the visible application action and must remain
inside the reviewed application security boundary.

## Uninstall behavior

Uninstall removes only the application's own file, Start Menu/Desktop shortcuts,
installation directory when empty, and the standard uninstall registration.
It intentionally does **not** stop `sshd`, change its startup mode, uninstall
OpenSSH, remove firewall rules, or revert networking state. This avoids silently
breaking an SSH configuration that may still be in use.

## Cross-platform static check

The static checker is read-only and uses only Python's standard library:

```bash
python3 build/Test-StaticContract.py
python3 build/Test-StaticContract.py --agent1-root ../agent1-app
```

It checks source/configuration contracts only. Passing it on macOS or Linux is
not proof that the Windows app or installer works.

After manually installing on a disposable Windows snapshot, run the read-only
installed-state check (supply the actual Windows SDK `mt.exe` location):

```powershell
.\build\Test-InstalledState.ps1 -MtPath 'C:\Program Files (x86)\Windows Kits\10\bin\<sdk-version>\x64\mt.exe'
```

This verifies the installed executable, elevation manifest, expected unsigned
status, Desktop/Start Menu shortcut targets, uninstall registration, and app
SHA-256. Add `-RequireValidSignature` only after the real signing workflow is
authorized and configured. It does not launch or mutate the application.

## Future signing integration

Signing is disabled by default. After a real, organization-controlled code-
signing certificate and RFC 3161 timestamp service are available, an authorized
release operator may explicitly supply `-EnableSigning`, a certificate
thumbprint, an HTTPS timestamp URL, and the Windows SDK `signtool.exe` path.
The build then applies Authenticode SHA-256 to the application before packaging
and to the setup afterward, followed by `signtool verify` and PowerShell
signature-state checks. No PFX, private key, PIN, token, or fake certificate is
stored or requested by these files.

Trusted signing still does not guarantee immediate SmartScreen reputation.
Release notes must state the observed result rather than promising acceptance.

## Real Windows validation

Before distribution, run the full matrix in `agent2-report.md` on clean snapshots
of Windows 10 22H2 x64, Windows 11 x64, and supported Windows Server Desktop
Experience editions. At minimum, verify install, upgrade, repair/reinstall,
uninstall, shortcut/UAC behavior, locked-file handling, policy blocking, tamper
handling, and the application's SSH/firewall scenarios. Validate the setup with
Inno Setup on Windows, inspect installed files and shortcuts, verify hashes and
Authenticode state, and record the exact OS builds and results.

No Windows installer build, Windows runtime test, trusted signature, or
SmartScreen acceptance is claimed by the artifacts in this directory.

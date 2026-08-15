# Windows SSH Enabler 1.0.1

Version 1.0.1 fixes the native Windows DISM capability probe that prevented version 1.0.0 from reading the OpenSSH Server capability successfully.

## Important: unsigned prerelease

The application and installer are not Authenticode-signed. Windows Defender SmartScreen may warn or block them, and Windows will not show a trusted publisher. Review the source and verify the published SHA-256 checksum before running the installer. This release does not claim code-signing trust, SmartScreen reputation, or complete end-to-end Windows acceptance.

## DISM repair

- Corrects `DismGetCapabilityInfo` from an invalid five-parameter P/Invoke to the exact three-parameter native ABI.
- Uses the native `uint` DISM session type, correct capability-info structure layout, and HRESULT-returning `DismDelete`.
- Corrects the documented package-state values so `Installed = 4` and `InstallPending = 5`.
- Initializes DISM once for the application probe lifetime, closes each query session, releases native results, and shuts DISM down on disposal.
- Adds ABI, mapping, lifecycle, cleanup, and pre-mutation regression coverage, bringing the executable test suite to 17 tests.
- Adds a Windows-only, non-mutating integration probe that calls the exact production implementation.

Independent validation on Windows Server 2022 completed locked restores, a Release build with zero warnings and errors, all 17 tests, application and probe publishing, and two production-probe executions without `E_INVALIDARG`. OpenSSH Server was not installed on that instance and its network was Public, so no service, listener, application firewall-rule, interactive GUI, SmartScreen, installed-path, or LAN-connectivity acceptance is claimed.

## Installation

1. Download `WindowsSshEnabler-Setup-1.0.1-x64.exe` and `SHA256SUMS.txt`.
2. Verify the installer SHA-256; `artifact-inventory.json` records the same digest, byte length, and expected `NotSigned` Authenticode state.
3. Run the installer and complete the standard UAC prompt.
4. Open **Windows SSH Enabler** from the Desktop or Start Menu.
5. Click **Enable SSH Server**.

OpenSSH Server must already be installed through Windows Optional Features. The application will not install it. It permits only Domain/Private `LocalSubnet` inbound TCP 22 access and refuses Public-only networking.

Uninstall removes the application and its shortcuts/registration but intentionally does not stop `sshd`, change its startup mode, uninstall OpenSSH, remove the application firewall rule, or revert network state.

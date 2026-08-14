# Windows SSH Enabler 1.0.0

This is the first public installer build of the Windows SSH Enabler desktop application.

## Important: unsigned prerelease

Code signing is deferred. Both the application and installer are unsigned, so Windows Defender SmartScreen may display a warning. Review the source and verify the published SHA-256 checksum before running the installer. This release does not claim a trusted publisher signature or complete real-device service/firewall acceptance testing.

## Installation

1. Download `WindowsSshEnabler-Setup-x64.exe` and `SHA256SUMS.txt`.
2. Verify the installer checksum.
3. Run the installer and complete the standard UAC prompt.
4. Open **Windows SSH Enabler** from the Desktop or Start Menu.
5. Click **Enable SSH Server**.

OpenSSH Server must already be installed through Windows Optional Features. The application will not install it.

## Automated release checks

- Locked .NET restore.
- Warnings-as-errors compilation on a GitHub-hosted Windows runner.
- Thirteen fake-based orchestration tests.
- Self-contained, untrimmed, single-file `win-x64` publish.
- Windows GUI subsystem and `requireAdministrator` manifest verification.
- Inno Setup installer contract verification.
- Actual unsigned Authenticode-state verification.
- SHA-256 artifact inventory.


# Agent 1 Report — Windows SSH Enabler Native Application

Date: 2026-08-14  
Owned directory: `outputs/windows-installer/agent1-app`  
Status: source implementation and host-safe static validation complete; Windows build/runtime acceptance pending

## Executive summary

I created a production-oriented C# WinForms application design for **Windows SSH Enabler**. It targets `net10.0-windows`, uses a `WinExe` output, requests normal UAC elevation with `requireAdministrator` and `uiAccess=false`, and has exactly one action button plus one read-only status/error area. A `win-x64` publish profile produces an unsigned, self-contained, untrimmed, single-file build on a suitable Windows build machine.

The application does not execute PowerShell, `cmd.exe`, or any shell command. Native integration is isolated behind interfaces and uses DISM, Service Control Manager, Network List Manager, IP Helper, and Windows Firewall COM. The controller performs all non-mutating safety checks before changing the service. It then starts/configures `sshd`, waits for exclusive IPv4/IPv6 TCP 22 ownership, creates or reuses one exact firewall rule, and performs final read-back checks. Persistent failures after service mutation begins are conservatively described as partial success.

No OpenSSH installation, Windows mutation, signing, packaging, repository operation, credential access, remote access, or GitHub operation was performed. There was no disposable Windows environment. The macOS host has no .NET SDK, so the C# projects and mocked tests could not be compiled or executed here. The included Windows validation script and acceptance matrix must be completed before distribution.

## File inventory

| Path | Purpose |
|---|---|
| `packages.lock.json` (one per project; three total) | Minimal dependency-free lock schemas; .NET 10 SDK confirmation pending |
| `Directory.Build.props` | Nullable, deterministic, warnings-as-errors build defaults |
| `WindowsSshEnabler.slnx` | Solution containing Core, application, and tests |
| `src/WindowsSshEnabler.Core/WindowsSshEnabler.Core.csproj` | Cross-platform orchestration assembly used by tests |
| `src/WindowsSshEnabler/Core/Contracts.cs` | Small interfaces and immutable operation models |
| `src/WindowsSshEnabler/Core/EnablerController.cs` | Ordered preflight, mutation, bounded wait, verification, and error orchestration |
| `src/WindowsSshEnabler/WindowsSshEnabler.csproj` | .NET 10 Windows Desktop / WinForms x64 application project |
| `src/WindowsSshEnabler/app.manifest` | UAC `requireAdministrator`, `uiAccess=false`, Windows compatibility manifest |
| `src/WindowsSshEnabler/Properties/PublishProfiles/win-x64.pubxml` | Self-contained, single-file, untrimmed x64 publish profile |
| `src/WindowsSshEnabler/Program.cs` | Composition root and fatal UI error handling |
| `src/WindowsSshEnabler/UI/MainForm.cs` | One-button WinForms UI and read-only status area |
| `src/WindowsSshEnabler/Native/WindowsPlatformProbe.cs` | OS/x64/elevation checks and canonical in-box sshd path |
| `src/WindowsSshEnabler/Native/DismCapabilityProbe.cs` | Read-only OpenSSH Server capability check |
| `src/WindowsSshEnabler/Native/NativeServiceManager.cs` | Service registration/status/startup/start operations with safe handles and bounded waits |
| `src/WindowsSshEnabler/Native/NetworkListManagerProbe.cs` | Connected Public/Private/Domain network category inspection |
| `src/WindowsSshEnabler/Native/IpHelperPortInspector.cs` | IPv4 + IPv6 TCP listener owner-PID inspection |
| `src/WindowsSshEnabler/Native/WindowsFirewallManager.cs` | Conflict inspection and exact application-owned firewall rule management |
| `tests/WindowsSshEnabler.Tests/WindowsSshEnabler.Tests.csproj` | Dependency-free executable test project |
| `tests/WindowsSshEnabler.Tests/Program.cs` | Thirteen orchestration tests using fakes |
| `scripts/validate-source.sh` | Host-safe static structure and security invariant checks |
| `scripts/windows-build-test.ps1` | Windows-only restore/build/test/publish developer validation wrapper |
| `README.md` | English architecture, build, publish, behavior, safety, rollback, and limitations guide |
| `agent1-report.md` | This report |

## Architecture and execution flow

`MainForm` delegates work to `EnablerController` on a worker task and disables the only action button until completion. Native adapters implement narrow interfaces, so orchestration behavior can be tested without changing Windows.

The controller executes in this order:

1. Require supported x64 Windows and a runtime administrator-token check.
2. Query `OpenSSH.Server~~~~0.0.1.0` through DISM and require exactly Installed.
3. Require the `sshd` service and case-insensitive canonical equality with `%WINDIR%\System32\OpenSSH\sshd.exe`.
4. Require at least one connected Private or Domain-authenticated network; a Public network is never reclassified.
5. Inspect IPv4 and IPv6 TCP port 22 listeners. Empty is safe before startup; otherwise every listener owner PID must be the current `sshd` service PID.
6. Inspect enabled inbound port-22 firewall rules. Reject Block rules, broader Allow rules, duplicate owned rules, and a malformed owned rule.
7. Set `sshd` startup to Automatic and start it. Poll service state for at most 30 seconds.
8. Poll for exclusive TCP 22 ownership for at most 15 seconds.
9. Reuse one already-exact owned firewall rule or add exactly one new rule.
10. Re-read service state, listener ownership, the exact owned rule, and conflicts. Report only point-in-time local success.

The firewall rule internal name is `WindowsSshEnabler.LanOpenSsh.Tcp22`. Exactness requires enabled inbound Allow, protocol TCP, local port 22, profiles bitmask Domain + Private only, remote address `LocalSubnet`, expected in-box program, service `sshd`, and disabled edge traversal. The manager does not alter non-owned rules. A malformed owned rule is refused before service startup rather than repaired while exposure could exist.

## Native APIs chosen

| Area | Mechanism | Important defensive behavior |
|---|---|---|
| Capability | `DismInitialize`, `DismOpenSession`, `DismGetCapabilityInfo`, `DismDelete`, `DismCloseSession`, `DismShutdown` in `DismApi.dll` | Fixed capability identity; no install API; native allocations/sessions always released |
| Service | SCM functions in `advapi32.dll` | Least required access flags; `SafeHandle`; two-call configuration buffer sizing; environment expansion; expected executable validation; Win32 errors retained; bounded polling |
| Network | Network List Manager COM | Connected networks only; numeric native categories avoid localized text; COM objects released |
| Port ownership | `GetExtendedTcpTable` in `iphlpapi.dll` | Two-call bounded allocation; validates count; parses both IPv4 and IPv6 owner-PID rows; checked offsets; deduplicates by PID |
| Firewall | `HNetCfg.FwPolicy2` and `HNetCfg.FWRule` COM | Full rule read-back; stable owned name; exact restrictions; no removal or modification of non-owned rules; duplicate and race refusal; COM cleanup |
| Elevation | `WindowsIdentity` and `WindowsPrincipal` | Manifest requests elevation; runtime check still fails closed |

No native buffer is retained beyond its scope. SCM handles derive from `SafeHandleZeroOrMinusOneIsInvalid`. DISM sessions and structures are released in `finally`. IP Helper buffer sizes and row offsets use checked conversions. COM failures retain HRESULTs in actionable errors. The x64-only publish avoids cross-bitness ambiguity.

## Threat and safety decisions

- **No automatic installation:** an absent capability returns instructions for Windows Optional Features. No download, repair, or capability-enable API is called.
- **Service hijack resistance:** the application refuses an `sshd` registration whose executable is not the expected in-box path.
- **Port ownership:** both IPv4 and IPv6 listener tables are checked by service PID. Process paths are informational; an inaccessible process path does not weaken PID comparison.
- **Network boundary:** no trusted active network means refusal. The rule never includes Public; network categories are never changed.
- **Firewall boundary:** other rules are inspected but never changed. Any broader Allow or relevant Block rule requires explicit administrator resolution. The app creates only its stable owned rule.
- **No transient broad-rule repair:** a malformed owned rule is rejected before service mutation. This avoids starting sshd behind a pre-existing broad owned rule.
- **TOCTOU mitigation:** exact listener and firewall checks are repeated after mutation. A concurrent policy change produces partial-success warning, not an unqualified success. Complete atomicity across independent Windows subsystems is not available.
- **Partial-state honesty:** the mutation flag is set before calling the service manager, because changing startup type can succeed even if startup later fails. All later exceptions are reported as partial.
- **No command injection surface:** the application launches no processes and constructs no commands. There is no user text input.
- **No credential/signing misuse:** no credentials are stored/read; no self-signed or fake production trust is created.
- **Uninstall separation:** application removal must not silently undo an administrator's service choice or remove unrelated rules. Rollback targets only the named rule and `sshd` startup setting through standard Windows management UI.

## Test design

The test project is an executable runner with no third-party test packages. It returns nonzero on any failure. Fakes cover:

1. successful rule creation and explicit non-claim of remote connectivity;
2. missing capability and no mutation;
3. non-admin rejection and no mutation;
4. Public-only rejection and no mutation;
5. foreign PID port conflict and no mutation;
6. broad Allow rule rejection;
7. Block rule rejection;
8. service failure reported as partial;
9. listener failure reported as partial and firewall skipped;
10. exact rule reuse without duplicate creation;
11. duplicate owned-rule refusal;
12. firewall failure after service start reported as partial.
13. a conflicting firewall rule appearing after initial preflight prevents unqualified success.

These are source-complete mocked orchestration tests. They were **not executed** because this host has no .NET runtime/SDK. They do not substitute for native Windows tests.

Each project also has a standard minimal `packages.lock.json` because there are no external PackageReferences, and `RestorePackagesWithLockFile` is enabled. The files were not SDK-generated on this host. The Windows pipeline must run locked-mode restore, including `--runtime win-x64` for the app, and treat any lock mismatch as a blocking defect.

## Validation performed and exact results

All commands below were run from `/Users/xiaogong/Documents/Codex/2026-08-14/5-6sol-windows-ssh-server-22` on macOS.

### Toolchain discovery

```text
dotnet --info
```

Result: failed immediately with `zsh:1: command not found: dotnet`.

```text
command -v csc
command -v mcs
command -v msbuild
```

Result: none found.

```text
find /Applications/Codex.app -type f -name dotnet -print
find /Users/xiaogong/.codex -type f -name dotnet -print
ls /usr/local/share/dotnet/dotnet /opt/homebrew/share/dotnet/dotnet
```

Result: no bundled or conventional .NET executable found. A workspace-dependency lookup was also attempted, produced no output for approximately 90 seconds, and was terminated. No software was installed.

### XML validation

```text
xmllint --noout WindowsSshEnabler.slnx Directory.Build.props \
  src/WindowsSshEnabler.Core/WindowsSshEnabler.Core.csproj \
  src/WindowsSshEnabler/WindowsSshEnabler.csproj \
  src/WindowsSshEnabler/app.manifest \
  src/WindowsSshEnabler/Properties/PublishProfiles/win-x64.pubxml
```

Result: exit code 0, no diagnostics.

### Static invariant suite

```text
chmod +x outputs/windows-installer/agent1-app/scripts/validate-source.sh
outputs/windows-installer/agent1-app/scripts/validate-source.sh
```

Result: exit code 0: `Source structure and safety invariant checks passed.`

This verified the solution/project/manifest/profile files, `WinExe`, `net10.0-windows`, UAC manifest, self-contained single-file settings, trimming disabled, stable rule name, `LocalSubnet`, Domain + Private profile assignment, `sshd` service restriction, DISM capability query, IPv6 listener implementation, exactly one constructed `Button`, absence of application `powershell.exe`, `cmd.exe`, `ProcessStartInfo`, or `Process.Start`, and XML well-formedness.

### Source hygiene searches

The invariant suite also requires exactly three lock files and validates their JSON with `jq` when available.

```text
rg -n "TODO|FIXME|NotImplementedException" outputs/windows-installer/agent1-app/src outputs/windows-installer/agent1-app/tests
rg -n "powershell\.exe|cmd\.exe|ProcessStartInfo|Process\.Start" outputs/windows-installer/agent1-app/src
```

Result: no matches.

### Not performed

- `dotnet restore`, build, mocked test execution, and publish: no .NET SDK.
- C# compiler/analyzer execution: no C# compiler or MSBuild.
- Windows native API tests: host is macOS.
- GUI render/UAC verification: host is macOS.
- Service/firewall/listener mutation: no disposable Windows environment and explicitly unsafe on the host.
- Installer, signing, Defender, SmartScreen, remote SSH, GitHub, release, or deployment operations: out of scope for Agent 1.

## Known limitations and unresolved risks

1. **Uncompiled source:** syntax/API/analyzer issues may remain until the Windows build script passes under the final .NET 10 SDK. This is the highest immediate validation gap.
2. **Native structure verification:** the IPv4/IPv6 IP Helper layouts and DISM structure/signatures must be validated on real x64 Windows. Static review is not ABI proof.
3. **Firewall COM behavior:** Windows editions/policies may reject simultaneous program and service restrictions, canonicalize `LocalSubnet`, or present COM values differently. Exact read-back intentionally fails closed, but needs real testing.
4. **Policy interaction:** default OpenSSH or organization rules may be broader than this tool permits. The application intentionally refuses them; this can require administrator cleanup outside the app.
5. **Race windows:** another privileged process can change service, listener, or firewall state between checks. Final read-back detects many cases but cannot make independent Windows subsystems transactional.
6. **Existing malicious service process:** SCM PID ownership is trusted after executable-registration validation. Code-signature/file-hash verification of the in-box binary is not implemented.
7. **Local success only:** routing, client isolation, endpoint policy, SSH authentication/configuration, and remote connectivity remain outside scope.
8. **No rollback UI:** the one-button requirement leaves rollback to standard administrator tools. Packaging must explain persistent state without deleting unrelated configuration.
9. **Unsigned:** both application and future installer remain untrusted until a legitimate signing process is added.
10. **Lock files unconfirmed:** the minimal dependency-free lock files were authored without an SDK. Their exact .NET 10 Windows TFM/RID interpretation must pass locked-mode restore on Windows before packaging.
11. **x64 only:** ARM64 and x86 are intentionally not built in this iteration.

## Windows acceptance-test matrix

Every row must record Windows edition/build, .NET SDK version, installer/app hash, exact observed UI text, Event Viewer/OpenSSH evidence where applicable, firewall rule export, listener owner PID(s), and pass/fail. Use disposable VMs or authorized test machines.

| ID | Environment/precondition | Action | Expected result |
|---|---|---|---|
| W01 | Windows 10 22H2 x64, OpenSSH installed, Private network, no conflicts | Build, publish, launch, accept UAC, click | GUI has one button/status area; sshd Automatic + Running; exact rule; IPv4/IPv6 ownership verified; local-only success text |
| W02 | Current Windows 11 x64, same safe baseline | Repeat W01 | Same as W01; no console window |
| W03 | Supported Windows Server Desktop Experience x64 | Repeat W01 | Same as W01 |
| W04 | OpenSSH capability absent | Click | Explicit Optional Features error; no service/firewall mutation |
| W05 | Capability servicing pending | Click | Pending/not-ready error; no mutation |
| W06 | Launch without successful elevation | Cancel UAC or simulate non-elevated runtime | Windows cancels launch or app reports administrator requirement; no mutation |
| W07 | Capability installed but `sshd` service missing | Click | Repair/missing-service error; no mutation |
| W08 | `sshd` service executable redirected | Click | Expected/actual path error; service not started; firewall unchanged |
| W09 | Only Public network connected | Click | Public-only refusal; category unchanged; no mutation |
| W10 | No connected network | Click | No-connected-network error; no mutation |
| W11 | Both Private and Public connected | Click | Rule remains Domain/Private only; no Public profile bit; success if all other checks pass |
| W12 | Foreign IPv4 process listening on TCP 22 | Click | PID/path conflict; no mutation |
| W13 | Foreign IPv6-only process listening on TCP 22 | Click | PID/path conflict; no mutation |
| W14 | sshd already Running with IPv4 and/or IPv6 listeners | Click twice | Existing service accepted; exactly one owned rule; both clicks safe |
| W15 | sshd starts but never listens | Click | Bounded failure after about 15 seconds; firewall not changed; partial-state warning |
| W16 | sshd start fails | Click | Actionable service/Win32 error; partial-state warning after startup-type attempt |
| W17 | Enabled inbound port-22 Block rule | Click | Named Block conflict; no service mutation |
| W18 | Enabled Public/Any/remote-any/program-any port-22 Allow rule | Click | Named broader-Allow conflict; no service mutation |
| W19 | One exact owned rule already exists | Click repeatedly | Rule reused; no duplicate; final exact read-back |
| W20 | Duplicate owned rules | Click | Duplicate refusal before service mutation |
| W21 | One malformed/disabled owned rule | Click | Unsafe-owned-rule refusal before service mutation; no silent repair |
| W22 | Firewall service/API unavailable or access denied | Click | Partial-state warning if service already changed; no unqualified success |
| W23 | Concurrent privileged firewall change after preflight | Click while changing policy | Final conflict recheck prevents unqualified success |
| W24 | Service stops after firewall creation | Induce stop during final check | Partial-state warning, not success |
| W25 | Long/unicode Windows path or localized OS | Build/run | No localized command parsing; expected native APIs/errors behave safely |
| W26 | Installer-created desktop shortcut | Install and double-click | UAC then GUI opens; no terminal or end-user command required |
| W27 | Upgrade/reinstall/uninstall | Perform packaging lifecycle | No duplicate shortcuts/rules; uninstall removes app files only and clearly documents persistent service/rule state |
| W28 | Defender/SmartScreen on unsigned build | Download/launch in isolated test | Warnings documented honestly; no claim of signed publisher |
| W29 | Remote LAN client after W01 | Attempt authorized SSH connection | Record separately; local success text must not have pre-claimed this result |
| W30 | Static and mocked suite on Windows build host | Run `scripts/windows-build-test.ps1` | Locked restores (including app `win-x64`), warnings-as-errors build, all 13 mocked tests, and single-file publish succeed |

## Required next steps before packaging or release

1. Run `scripts/windows-build-test.ps1` on an x64 Windows build machine with the final .NET 10 SDK; fix all compiler/analyzer/test failures.
2. Perform W01–W25 and W30 in disposable/authorized Windows environments, including native ABI and Firewall COM read-back inspection.
3. Have the packaging owner consume only a verified publish output, preserve the manifest, create normal shortcuts/uninstall entries, and keep system rollback non-destructive.
4. Repeat installation/upgrade/uninstall, Defender, and SmartScreen tests after packaging.
5. Do not describe the application as signed or production-ready until legitimate signing and the acceptance matrix are complete.

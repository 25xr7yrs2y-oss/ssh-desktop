# Agent 1 report: DISM capability ABI repair

Date: 2026-08-15

Role: implementation, regression coverage, and build handoff
Repository state: local and uncommitted; no GitHub access, commit, push, tag, Action, or release

## Outcome

The OpenSSH capability probe has been repaired locally. The production declaration and call now match the native three-parameter `DismGetCapabilityInfo(DismSession, PCWSTR, DismCapabilityInfo**)` contract. Adjacent ABI and lifecycle defects found during the required review were also corrected: `DismSession` is now a 32-bit unsigned integer, `DismDelete` returns an HRESULT, native package-state values match the documented 0-7 enumeration, and DISM is initialized once and shut down once for the lifetime of the application probe.

This agent did not run the native probe on Windows. The separate runtime agent owns the authorized SSM instance and must record the actual result in its own report.

## Root cause

Version 1.0.0 declared `DismGetCapabilityInfo` with five managed parameters:

```text
(session, name, identifier, culture, out capabilityInfo)
```

The native function has only three parameters:

```text
(session, name, DismCapabilityInfo** info)
```

On Windows x64, the first extra null argument occupied the native third-argument register. DISM therefore received a null output pointer and returned `E_INVALIDARG` (`0x80070057`), which .NET surfaced as `ArgumentException: Value does not fall within the expected range.` The failure occurred during the read-only capability check, before service or firewall mutation.

The audit also found that v1.0.0 incorrectly treated native state value 5 (`InstallPending`) as installed; the documented installed value is 4. It also represented `DismSession` as `IntPtr` even though the native typedef is `UINT`, and represented the HRESULT-returning `DismDelete` as `void`.

## Files changed

- `src/WindowsSshEnabler/Native/DismCapabilityProbe.cs`
  - Uses the exact three-parameter `DismGetCapabilityInfo(uint, string, out IntPtr)` P/Invoke.
  - Uses `uint` for every `DismSession` parameter and output.
  - Uses `int` for each HRESULT, including `DismDelete`.
  - Specifies Unicode, exact entry-point spelling, and the WinAPI calling convention.
  - Defines the documented `DismPackageFeatureState` values 0 through 7 and maps only value 4 to application `Installed`.
  - Preserves the documented sequential `DismCapabilityInfo` layout (40 bytes on x64).
  - Initializes DISM once, opens/closes a session per query, deletes each returned structure, and shuts DISM down when the application-owned probe is disposed.
  - Serializes queries and disposal so repeated button clicks do not call DISM after an early shutdown.
  - Attempts both structure deletion and session closure; a cleanup HRESULT is surfaced after a successful query, while an original query exception is preserved.
- `src/WindowsSshEnabler/Program.cs`
  - Owns and disposes the single production probe for the application lifetime.
- `tests/WindowsSshEnabler.Tests/Program.cs`
  - Adds ABI-reflection, x64/x86 layout, complete state-mapping, repeated-lifecycle, cleanup, HRESULT, and pre-mutation failure tests.
- `tests/WindowsSshEnabler.Tests/WindowsSshEnabler.Tests.csproj`
  - Links the exact production probe source into the deterministic cross-platform regression executable.
- `tools/WindowsSshEnabler.DismProbe/*`
  - Adds a Windows-only, non-mutating console integration entry point that references and invokes the production application assembly.
  - Emits a single JSON result and exit code 0 on success, or JSON error and exit code 1 on failure.
  - Includes a locked dependency graph.
- `WindowsSshEnabler.slnx`, `scripts/windows-build-test.ps1`, and `scripts/validate-source.sh`
  - Include, restore, publish, and statically validate the integration probe without weakening locked restores.

## Authoritative API evidence

The implementation was checked against Microsoft Learn:

- [DismGetCapabilityInfo](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dism/dismgetcapabilityinfo?view=windows-11): exactly three parameters; the third receives `DismCapabilityInfo**`.
- [DismSession](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dism/dismsession?view=windows-11): `typedef UINT DismSession`.
- [DismCapabilityInfo](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dism/dismcapabilityinfo?view=windows-11): pointer, state enum, two pointers, and two DWORD fields.
- [DismPackageFeatureState](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dism/dismpackagefeaturestate-enumeration?view=windows-11): values 0 through 7, with `Installed = 4` and `InstallPending = 5`.
- [DismInitialize](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dism/disminitialize-function?view=windows-11), [DismOpenSession](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dism/dismopensession-function?view=windows-11), [DismCloseSession](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dism/dismclosesession-function?view=windows-11), and [DismShutdown](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dism/dismshutdown-function?view=windows-11): required initialization/session/shutdown ordering.
- [DismDelete](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dism/dismdelete-function?view=windows-11): returns HRESULT and is required for DISM-allocated structures.

## Regression coverage

The test executable now contains 17 tests: the original 13 orchestration/safety tests plus four DISM regressions.

1. ABI contract: finds the native import by reflection, requires an HRESULT return, exactly three parameters, `uint` session, Unicode string name, an `out IntPtr`, exact spelling, and HRESULT-returning `DismDelete`. It also checks the structure size and x64/x86 offsets.
2. State mapping: checks all documented values 0-7 and an unknown value. Only native `Installed = 4` maps to application `Installed`; transitional or partial states cannot pass the controller's readiness gate.
3. Lifecycle: performs two deterministic queries through a fake native boundary and requires one initialize, two open/query/delete/close sequences, and one shutdown on disposal, in exact order.
4. Failure immutability: injects `E_INVALIDARG`, verifies that the opened session is closed and no invalid info pointer is deleted, then verifies the controller performs zero service configuration and zero firewall ensure calls when the capability probe fails.

The Windows integration probe calls the exact production `DismCapabilityProbe`, not a fake. It must be compared with:

```powershell
Get-WindowsCapability -Online -Name 'OpenSSH.Server~~~~0.0.1.0' |
    Select-Object Name, State
```

## Commands and results

```text
dotnet --info
```

Result: unavailable (`dotnet: command not found`) on the local macOS host.

An isolated official Microsoft .NET 10 SDK installation was attempted under a temporary `/tmp/windows-ssh-enabler-dotnet-agent1.*` directory. The official 230 MB SDK download was progressing at approximately 60 KB/s with an estimated duration near one hour, so it was interrupted; nothing was installed system-wide. Therefore this agent does not claim a local compile, unit-test run, Windows publish, or native runtime result.

```text
sh scripts/validate-source.sh
```

Result: passed (`Source structure and safety invariant checks passed.`).

```text
git diff --check
```

Result: passed with no whitespace errors.

The runtime agent was given the full source handoff to perform locked restore, Release build, all 17 tests, self-contained `win-x64` publish, and native SSM validation with an isolated SDK.

### Downstream handoff verification

After the final source archive was issued, Agent 2 independently reported that its SHA-256 matched, all three locked restores passed, the Release build completed with zero warnings and zero errors, all 17 tests passed, and both the application and integration probe published. On the authorized instance, two executions of the exact production probe exited 0 and returned application state `NotInstalled`, semantically matching PowerShell's native `NotPresent` state, with no `E_INVALIDARG` or `ArgumentException`. Agent 2 also reported an unchanged instance baseline and removal of its temporary SDK/test artifacts. Those Windows results are independently evidenced in `docs/reports/dism-fix-agent2-report.md`; they were not executed directly by Agent 1.

## Handoff artifact

Archive:

```text
/Users/xiaogong/Documents/Codex/2026-08-14/5-6sol-windows-ssh-server-22/work/ssh-desktop/handoff/agent1/ssh-desktop-dism-fix-source.tar.gz
```

SHA-256:

```text
193ffbdc2d6b98bd342b12f5868cf1c4d438997596cf8cfae1549d88339cf9f0
```

The archive excludes `.git`, build outputs, packaging work/artifacts, the handoff directory itself, this report (to avoid a circular self-hash), and the other agent's in-progress evidence. It contains no credentials. The integration project's lock file was generated authoritatively with .NET SDK 10.0.400 in the runtime agent's isolated Windows copy, copied exactly into the local source, and will be rechecked there with `--locked-mode`. The final archive was created with macOS copy-file metadata disabled, explicitly excludes `._*` and `__MACOSX`, and its 59-entry listing was checked to contain zero AppleDouble entries.

## Safety analysis

- The production fix adds no process or shell execution.
- The integration probe only calls the read-only capability query. It cannot install/remove capabilities or change services, firewall rules, networking, accounts, authentication, or SSH configuration.
- The controller still requires supported 64-bit Windows and elevation before the capability query.
- Capability failures occur before `ConfigureAutomaticAndStart` and before `EnsureExactRule`.
- No AWS resource or Windows configuration was accessed by this agent.
- No GitHub credential was accessed and no remote repository operation occurred.

## Limitations and runtime instructions

The separate runtime agent must independently verify the archive hash before extraction, use locked restore, run the 17 tests, publish the integration probe, and execute it on the authorized Windows instance. It must compare the JSON mapped state with `Get-WindowsCapability` and record the exit code. If OpenSSH is not installed or the only connected network is Public, it must not install OpenSSH or change the network category merely to force a success path; it should verify the correct non-installed state and safe controller refusal instead. Real interactive WinForms click/visual acceptance cannot be claimed from an SSM Session 0 command alone.

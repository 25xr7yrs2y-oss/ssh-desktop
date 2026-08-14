# Agent 2 report — Windows SSH Enabler packaging and independent review

## Executive conclusion

The proposed packaging design is appropriate for an **unsigned development
build**: a per-machine x64 Inno Setup installer places one self-contained .NET
10 WinForms executable in 64-bit Program Files, creates Desktop and Start Menu
shortcuts, and registers a normal uninstaller. The installer has no custom code
or system-configuration actions. Uninstall deliberately leaves OpenSSH, `sshd`,
firewall rules, and networking state alone.

This workstream is **not ready for public production distribution**. The two
release blockers are expected but material: no public-trust Authenticode signing
has been configured, and neither the application nor installer has been built or
exercised on Windows. Static review on macOS cannot establish Windows API,
installer, UAC, firewall, service, Defender, or SmartScreen behavior.

The packaging source and host-safe static checks are complete. The future
signing hook is disabled by default and contains no certificate, private key,
credential prompt, or claim of trust.

## Scope and reviewed material

Agent 2 owns and modified only:

`outputs/windows-installer/agent2-packaging-review`

Agent 1's directory was inspected read-only. No GitHub repository, release,
remote machine, service, firewall, website, certificate store, or network setting
was modified. No OpenSSH binary or runtime was downloaded or bundled.

The final Agent 1 review snapshot and its static-check result are recorded in
the **Validation performed** section below. If Agent 1 changed files after that
snapshot, this report does not cover those later changes.

## Agent 2 file inventory

| File | Purpose |
|---|---|
| `installer/WindowsSshEnabler.iss` | Stable-AppId, per-machine x64 Inno Setup definition with one explicit payload and default Desktop/Start Menu shortcuts. |
| `build/Build-Package.ps1` | Fail-closed Windows restore, test, self-contained publish, staging, manifest/PE/signature checks, Inno compilation, optional future signing, inventory, and SHA-256 workflow. |
| `build/Test-StaticContract.py` | Cross-platform, standard-library static validation for packaging and optional Agent 1 security-contract markers. |
| `build/Test-InstalledState.ps1` | Non-mutating post-install inspection of the app, manifest, signature state, shortcuts, uninstall registration, and hash on a Windows test VM. |
| `README.md` | English prerequisites, pipeline, installer/uninstall behavior, unsigned warning, icon rules, signing insertion point, and Windows validation instructions. |
| `agent2-report.md` | This independent review, threat model, findings, exact validation record, and acceptance matrix. |

No generated `.exe`, certificate, secret, OpenSSH payload, PowerShell application
launcher, or unrelated runtime is part of this inventory.

## Installer design and safety properties

- Stable AppId: `{B1D84CE8-E5D1-4B27-89E8-A72F1A0A6365}`.
- Application target: `WindowsSshEnabler.exe`, self-contained `win-x64`, single
  file, Windows GUI subsystem.
- Installation location: `{autopf}\Windows SSH Enabler` in 64-bit install mode.
- Elevation: setup requires administrator approval; the app's embedded manifest
  must independently request `requireAdministrator`.
- Payload: exactly one explicitly named application EXE; no recursive file copy.
- User entry points: Start Menu and Desktop shortcuts are both created by
  default. There is no silent launch and no post-install `[Run]` action.
- Upgrades: the AppId and path stay stable; previous install location/group are
  reused, and normal file-version rules avoid replacing a newer app binary with
  an older one. Upgrade/downgrade behavior still requires Windows testing.
- Uninstall: only Inno-managed application files, shortcuts, empty app directory,
  and uninstall metadata are removed. No `[UninstallRun]`, custom code, broad
  delete, service, firewall, OpenSSH, or networking rollback is present.
- Icon: no deceptive placeholder is bundled. A reviewed local `.ico` can be
  supplied explicitly; missing or non-ICO paths fail the build. Agent 1 must set
  the matching app icon separately.
- Signing: default output must be `NotSigned`. Explicit signing requires a real
  certificate already available to the authorized Windows build context plus an
  HTTPS RFC 3161 timestamp URL. Both app and setup are verified afterward.

## Installer threat model

### Protected assets

- Integrity of the installed application and user trust in its publisher.
- Administrator authority granted through setup/application UAC prompts.
- Availability and intended configuration of Windows OpenSSH and `sshd`.
- Windows Defender Firewall policy and LAN exposure on TCP 22.
- Build credentials and signing keys when signing is eventually introduced.

### Trust boundaries and attackers

- The downloaded unsigned setup crosses an untrusted distribution boundary.
- Setup crosses from the invoking user into an administrator context after UAC.
- The installed application crosses into Windows servicing, SCM, IP Helper,
  Network List Manager, and firewall COM APIs.
- A local attacker may pre-create a conflicting service, listener, firewall rule,
  file, shortcut, or install directory; an administrator can defeat app policy
  entirely and is outside this utility's defensive boundary.
- A compromised build host or dependency feed could replace publish inputs.
- A network attacker could tamper with an unsigned download if transport or the
  download origin is compromised; a SHA-256 value is useful only when obtained
  through a separately trusted channel.

### Controls present

- One fixed payload and fixed destination; no wildcard/recursive packaging.
- No installer script code, arbitrary commands, shell, registry payload, service
  action, firewall action, OpenSSH installation, or security-control bypass.
- Tests run before publish; locked restore is mandatory; unexpected staging
  contents and missing/incorrect executable metadata fail closed.
- App and setup Authenticode states must match the explicitly requested mode.
- Sorted inventory contains exact paths, lengths, SHA-256 values, and actual
  signature states.
- Application contract requires exact `sshd` path, TCP-22 owner inspection,
  Domain/Private plus LocalSubnet firewall scope, conflict refusal, and final
  read-back.

### Residual threats

- Unsigned setup has no cryptographic publisher identity.
- Checksums do not authenticate themselves.
- Toolchain/dependency compromise is not eliminated by source-level controls.
- UAC confirms elevation intent, not application safety.
- Firewall changes cannot guarantee router configuration or end-to-end LAN
  reachability.
- Uninstall intentionally leaves prior app-created system state in place.

## Severity-ranked findings

### High — release blockers

**H1: Public-trust signing is absent by explicit decision.** An attacker able to
replace the setup can present another unsigned executable under the same name.
The build correctly labels default output unsigned and rejects an unexpected
signature state, but this is not publisher authentication. Before public release,
sign the app and installer using an authorized public-trust Authenticode process
with SHA-256 and an RFC 3161 timestamp, verify both files, protect the key outside
the repository, and publish checksums over a trusted channel. Do not promise
SmartScreen acceptance; record observed results.

**H2: No real Windows build or runtime/installer validation has occurred.** The
available host is macOS and has neither .NET/Inno Setup/Windows SDK tools nor
Windows service/firewall facilities. The setup EXE was therefore not compiled,
installed, run, upgraded, or uninstalled. Execute the full matrix below on clean
Windows snapshots before release.

### Medium — must be resolved or explicitly accepted before release

**M1: Toolchain identity and generated lock state are not yet confirmed on
Windows.** Locked NuGet restores are required, but the exact .NET SDK, Inno
Setup, and Windows SDK versions are not encoded in this directory. Agent 1 added
three minimal dependency-free `packages.lock.json` files, but they could not be
SDK-generated or validated on this host. Add/review a `global.json`, regenerate
and confirm the locks with the selected .NET 10 SDK, pin the build image and
Inno/Windows SDK versions, record installer hashes/provenance, and protect the
Windows build host. Re-run source and binary inventories whenever a tool changes.

**M2: Compiled installer contents are not independently extracted during the
build.** The pipeline statically proves one `[Files]` entry and two shortcuts,
then requires successful ISCC compilation. The read-only installed-state script
checks the resulting installation. This is adequate for development but not a
substitute for installing in a disposable VM, inspecting files/shortcuts, and
then uninstalling. Consider a trusted installer-inspection tool in the controlled
Windows CI image if independent archive introspection is required.

**M3: System configuration remains after uninstall by design.** This satisfies
the safety requirement not to disrupt a possibly used SSH service or delete
rules blindly, but a user may incorrectly assume uninstall closes LAN access.
The README clearly states the behavior. The application/release documentation
should also show a separate, explicit, administrator-reviewed manual cleanup
procedure that targets only the app-owned rule; cleanup must never be an automatic
uninstaller action.

**M4: Conservative firewall-conflict handling may block normal installations.**
The reviewed application contract refuses enabled inbound TCP-22 rules that are
broader than its exact Domain/Private, LocalSubnet, program-and-service-bound
rule. Some OpenSSH installations may already have a broad Microsoft-created
allow rule. Refusal is safer than silently weakening/overwriting policy, but the
message and documentation must clearly explain how an administrator can inspect
and deliberately resolve the existing rule. Do not auto-delete it.

### Low / release hygiene

**L1: Publisher and branding are placeholders.** Replace the publisher string
with the verified future signer identity and supply a reviewed icon before a
branded release. A placeholder publisher must not be shipped as production.

**L2: x64 is the only target.** x86 and Windows on Arm are intentionally outside
scope. The download name and documentation should continue to say x64.

**L3: Downgrade and same-version reinstall semantics require observation.** The
installer uses stable identity and normal file-version behavior, without custom
downgrade code. Verify older/newer/same-version cases and document recovery from
an interrupted or locked-file upgrade.

## Independent application review

The review checks the following agreed security contract:

1. One primary action button and a read-only status/error area.
2. `WinExe` output, no console, no user-facing command line.
3. Embedded `requireAdministrator` with normal UAC.
4. Native/documented Windows APIs rather than PowerShell/cmd or dynamically
   assembled commands.
5. Read-only OpenSSH capability detection; no install/download behavior.
6. Exact `sshd` service executable comparison to `%WINDIR%\System32\OpenSSH\sshd.exe`.
7. Firewall exposure only on Domain/Private profiles, never Public.
8. Exclusive IPv4 **and IPv6** TCP-22 listener ownership by the running `sshd`
   service process.
9. Refusal on other block/broad-allow conflicts and one exact, idempotent,
   app-owned TCP-22 LocalSubnet rule bound to the program and `sshd` service.
10. Partial-success reporting for every point after a persistent service or
    firewall mutation may have occurred, plus final service/listener/rule read-back.
11. No account, authentication, `sshd_config`, router, Defender, SmartScreen,
    UAC, execution-policy, or unrelated firewall changes.

Three issues were raised during review: the mutation flag was originally set only
after service configuration/start returned; the port inspector originally
enumerated IPv4 only; and final rule verification originally did not re-run
conflict detection. Agent 1 corrected all three: mutation is conservatively
marked before the service call, IPv4 and IPv6 owner-PID tables are both read, and
the controller performs a second firewall Preflight after exact-rule verification.
A thirteenth fake orchestration test covers the final conflict race. The final
static snapshot passes; the code and tests still require actual compilation and
execution on Windows.

## Validation performed

### Safe checks run on the available macOS host

The following commands were actually executed from the shared workspace:

```text
python3 -m py_compile outputs/windows-installer/agent2-packaging-review/build/Test-StaticContract.py
python3 outputs/windows-installer/agent2-packaging-review/build/Test-StaticContract.py
rg -n <forbidden installer/shell patterns> outputs/windows-installer/agent2-packaging-review -g '!*.md'
find outputs/windows-installer/agent1-app -type f -print | sort
sed -n ... <every available Agent 1 project, manifest, core, native, program, and UI source file>
```

Observed outcomes:

- Python compilation: **passed**.
- Agent 2 static packaging contract: **passed**.
- Forbidden-pattern scan: no active Inno `[Run]`, `[Registry]`, `[Code]`,
  `[UninstallRun]`, or `[UninstallDelete]` section and no packaged application
  shell command was found; matches were comments or fail-closed validators.
- Agent 1 interim inspection: identified and reported the three issues above.
- Agent 1 final inspection: all three review issues were resolved in source; both
  Agent 2's integration checker and Agent 1's invariant script passed.
- No generated installer exists, which is correct on this non-Windows host.
- No service, firewall, OpenSSH, network, certificate, GitHub, or website change
  was performed.

### Final Agent 1 snapshot

The last read-only snapshot inspected before Agent 2 completion was:

- Snapshot time: **2026-08-14 22:35:37 +0800**, after Agent 1 reported completion.
- Files/revision: **25 files** under `agent1-app`; no Git revision exists in this
  output directory, so the SHA-256 aggregate of the sorted per-file SHA-256 list
  is `7f73e67be24ca47bf68d08ef35e049b42ef2f100b0e589b0be4ab023035fc99d`.
- `python3 .../Test-StaticContract.py --agent1-root .../agent1-app`:
  **passed**, exit code 0.
- `bash outputs/windows-installer/agent1-app/scripts/validate-source.sh`:
  **passed**, exit code 0, message `Source structure and safety invariant checks passed.`
- Partial-success finding: **resolved in source and represented by a fake test;
  not compiled/executed**.
- IPv6 ownership finding: **resolved in source using AF_INET and AF_INET6 tables;
  native ABI/runtime behavior not tested**.
- Final firewall-conflict read-back finding: **resolved in source with a second
  Preflight and fake test; not compiled/executed**.

### Checks not run

- `dotnet restore`, `dotnet test`, or `dotnet publish` (no .NET SDK present).
- Inno Setup compile (not available and requires Windows).
- PE embedded-manifest/GUI-subsystem inspection of a built app.
- Authenticode verification of a built app/setup.
- Installation, UAC, shortcuts, upgrade, repair/reinstall, locked-file, uninstall,
  policy-block, tamper, Defender, or SmartScreen tests.
- Windows service, firewall, network category, port ownership, or LAN SSH tests.

No unrun check is represented as passing.

## Detailed acceptance-test matrix

All rows marked **Not run** require a clean Windows VM or suitable controlled
Windows test device. Preserve setup logs, app status text, Event Viewer/OpenSSH
evidence where relevant, firewall/service read-backs, exact OS build, hashes, and
signature states.

### Build and artifact tests

| ID | Scenario | Expected result | Current status |
|---|---|---|---|
| B01 | Missing .NET, ISCC, or `mt.exe` | Build fails before output. | Static design only; Not run |
| B02 | Missing/stale NuGet lock file | Locked restore fails; no artifact. | Static design only; Not run |
| B03 | Any unit test fails | Publish/package does not occur. | Static design only; Not run |
| B04 | App publish name differs | Missing expected EXE causes failure. | Static design only; Not run |
| B05 | Extra staging file appears | Build fails closed. | Static design only; Not run |
| B06 | App is console subsystem | PE check fails. | Static design only; Not run |
| B07 | Manifest is absent/`asInvoker` | `mt.exe` extraction/check fails. | Static design only; Not run |
| B08 | Default unsigned build | App/setup are `NotSigned`; warning, inventory, hashes emitted. | Not run |
| B09 | Signing disabled but artifact is signed/invalid | Signature-status mismatch fails build. | Not run |
| B10 | Signing requested without tool/thumbprint/HTTPS timestamp | Build fails before signing. | Not run |
| B11 | Authorized signing mode | SHA-256 + RFC 3161 sign/verify both app and setup; status `Valid`. | Deferred; Not run |
| B12 | Inventory repeat | Sorted paths and exact byte/hash/signature data; compare under pinned toolchain. | Not run |

### Installer lifecycle tests

| ID | Scenario | Expected result | Current status |
|---|---|---|---|
| I01 | Clean admin install | UAC shown; one app EXE installed in 64-bit Program Files. | Not run |
| I02 | Standard user with valid admin credential | Normal credential prompt; install only after consent. | Not run |
| I03 | UAC cancelled/credential unavailable | No install or partial files. | Not run |
| I04 | AppLocker/WDAC/organization policy blocks setup | Clear Windows policy failure; no bypass attempt. | Not run |
| I05 | Default shortcuts | Desktop and Start Menu links exist and target quoted exact app path. | Not run |
| I06 | End of setup | App is not launched silently. | Not run |
| I07 | Shortcut launch | No console; normal app UAC; one-button GUI appears. | Not run |
| I08 | Same-version reinstall/repair | Stable AppId/path; application restored without duplicate shortcuts/entries. | Not run |
| I09 | Upgrade older → newer | One uninstall entry; files upgraded; settings/system state not broadened. | Not run |
| I10 | Attempt newer → older | Newer app is not silently replaced; observed metadata behavior documented. | Not run |
| I11 | App EXE running/locked during upgrade | Restart Manager/installer gives safe prompt; no corruption. | Not run |
| I12 | Spaces/non-ASCII in build/source paths | Compile/install/shortcut target quoting remains correct. | Not run |
| I13 | Tampered setup | Hash/signature mismatch detected by release process; do not run. | Not run |
| I14 | Uninstall | Own files/shortcuts/entry removed; no broad/outside deletion. | Not run |
| I15 | Uninstall after app enabled SSH | `sshd`, startup mode, OpenSSH, firewall, network remain unchanged. | Not run |
| I16 | Interrupted install/uninstall | Rerun recovers safely; no orphan custom action. | Not run |
| I17 | Defender/SmartScreen | Record exact warning/detection; do not disable or bypass controls. | Not run |

### Application security and reliability tests

| ID | Scenario | Expected result | Current status |
|---|---|---|---|
| A01 | UI inspection | Exactly one action button; status area read-only; no console/CLI. | Static contract passed; runtime Not run |
| A02 | UAC accepted / elevation read-back | App reports elevated and continues. | Not run |
| A03 | Unelevated test harness/process | Precise administrator error; no mutation. | Not run |
| A04 | OpenSSH capability absent | Explicit install guidance; no automatic install/download. | Not run |
| A05 | Capability pending/unknown | Clear servicing-state error; no mutation. | Not run |
| A06 | Capability installed, service missing | Repair guidance; no firewall change. | Not run |
| A07 | `sshd` path unexpected/malformed | Refuse before mutation and show exact safety error. | Not run |
| A08 | Public network only | Refuse; do not recategorize or create Public rule. | Not run |
| A09 | No connected network | Refuse safely. | Not run |
| A10 | Private/Domain network | Continue; final rule profiles are exactly Domain+Private. | Not run |
| A11 | Simultaneous trusted and Public adapters | Rule still excludes Public; behavior documented. | Not run |
| A12 | IPv4 TCP 22 owned by another PID | Refuse with owner/PID detail; no mutation. | Not run |
| A13 | IPv6 TCP 22 owned by another PID | Refuse identically; no mutation. | Source corrected/static contract passed; native runtime Not run |
| A14 | TCP 22 uninspectable | Fail closed rather than assume free. | Not run |
| A15 | Existing exact app-owned rule | Reuse without duplicate; report reuse. | Not run |
| A16 | Duplicate app-owned rules | Refuse and require manual review. | Not run |
| A17 | Existing broader enabled allow rule | Refuse; do not delete/relax it. | Not run |
| A18 | Existing applicable block rule | Refuse; do not override policy. | Not run |
| A19 | Disabled/stale app-owned rule | Repair only that exact named rule, then read back. | Not run |
| A20 | Service config changes, start fails/times out | Report partial success because startup mode may persist; firewall unchanged. | Source/fake test added; test not compiled/executed |
| A21 | `sshd` runs but never owns TCP 22 | Partial warning; firewall unchanged. | Not run |
| A22 | Firewall add/update denied | Partial warning after service mutation; no broad fallback. | Not run |
| A23 | Rule add succeeds but verification fails | Partial warning identifying uncertain firewall state. | Not run |
| A24 | Service/listener changes before final read-back | Partial warning; never claim success. | Not run |
| A25 | Repeated clicks | Idempotent service/rule outcome; no duplicate rule. | Not run |
| A26 | Remote LAN SSH from LocalSubnet | Connect only on trusted profile; auth remains native OpenSSH policy. | Not run |
| A27 | Public-profile remote attempt | Firewall rule does not allow it. | Not run |
| A28 | Router/non-local-subnet attempt | Not enabled by this utility; no router change. | Not run |

## Compatibility notes

| Platform | Packaging expectation | Validation status |
|---|---|---|
| Windows 10 22H2 x64 | Intended target; .NET 10 WinForms, DISM/SCM/IP Helper/NLM/firewall APIs require real testing. | Not run |
| Windows 11 x64 | Intended target; test current serviced releases and network/UAC policy variants. | Not run |
| Windows Server 2022 Desktop Experience x64 | Candidate server target; desktop shell, firewall policy, optional feature state, and UAC behavior must be confirmed. | Not run |
| Windows Server 2025 Desktop Experience x64 | Candidate server target; same checks plus current .NET support policy. | Not run |
| Server Core | GUI workflow is not appropriate; outside stated Desktop Experience scope. | Unsupported by design |
| Windows x86 / Arm64 | Installer and publish RID are x64 only. | Unsupported by design |

Exact OS build numbers, patch levels, domain policy, .NET SDK version, Inno
Setup version, Windows SDK version, OpenSSH capability state, and network profile
must be recorded with each run. Compatibility must not be inferred solely from
API presence or compilation.

## Signing gap and future release gate

Signing remains intentionally deferred. A production candidate may proceed only
when all of the following are true:

1. The publisher identity and public-trust signing service/certificate are
   authorized and controlled outside source control.
2. No key/PFX/PIN/token is stored in files, scripts, arguments logged by CI, or
   repository history.
3. App is signed before packaging; setup is signed afterward using SHA-256 and
   an RFC 3161 timestamp; both pass `signtool verify /pa /all /v` and PowerShell
   returns `Valid`.
4. Release hashes and signature metadata are generated from the final immutable
   bytes and published through a trusted channel.
5. The full Windows matrix has passed on clean snapshots with exact evidence.
6. Defender/SmartScreen observations are stated honestly. Signing is not
   described as a guarantee of reputation or warning-free execution.

Relevant primary documentation for the release operator:

- [Microsoft SignTool](https://learn.microsoft.com/windows/win32/seccrypto/signtool)
- [.NET single-file deployment](https://learn.microsoft.com/dotnet/core/deploying/single-file/overview)
- [Inno Setup help](https://jrsoftware.org/ishelp/)

## Concrete recommendations

1. Compile and run Agent 1's thirteen fake tests on the selected Windows/.NET 10
   build host; confirm the minimal lock files through an actual locked restore.
2. Pin and document the Windows build image, .NET SDK, Inno Setup, and Windows
   SDK; retain provenance and tool hashes.
3. Compile only on an isolated Windows build worker, run locked restore/tests,
   and retain the generated artifact inventory and setup log.
4. Execute every High/Medium-relevant test in the matrix on clean Windows 10,
   Windows 11, and Server Desktop Experience snapshots.
5. Keep the installer free of service/firewall/custom actions. All privileged
   functional changes must remain behind the visible one-button app flow.
6. Add a clearly separate manual cleanup guide for the exact app-owned firewall
   rule while preserving the intentionally non-mutating uninstaller.
7. Do not publish the unsigned development EXE as a trusted release. When signing
   becomes available, use the disabled integration deliberately and verify final
   bytes before distribution.

## Final disposition

**Packaging source: ready for Windows development validation.**

**Application source contract: passed final static review; Windows compilation
and runtime validation remain pending.**

**Public release: blocked pending real Windows build/test and trusted signing.**

# Agent 2 report — independent DISM ABI review and Windows runtime validation

## Executive conclusion

The corrected `DismGetCapabilityInfo` interop is ABI-correct for the tested x64
Windows target. The final source handoff restored in locked mode, built with zero
warnings and zero errors, passed all 17 automated tests, published the application
and test-only probe, and invoked the exact production `DismCapabilityProbe` twice
without `E_INVALIDARG`, `ArgumentException`, or any other exception. Both native
probe runs exited 0.

The authorized EC2 instance could not validate the required **Installed** path:
`Get-WindowsCapability` reported `OpenSSH.Server~~~~0.0.1.0` as `NotPresent`, the
`sshd` service did not exist, and the only connected network was Public. The
production probe therefore correctly mapped the native state to application state
`NotInstalled`, which agreed with PowerShell semantically. Per the safety boundary,
OpenSSH was not installed and the network category was not changed. Service start,
TCP 22 listener ownership, application firewall-rule creation, GUI clicking, and
LAN connectivity remain untested on this instance.

The instance's service, listener, network, and firewall baseline was unchanged
after testing. The isolated SDK, NuGet cache, source copies, build output, and
transfer chunks were removed. A test-created ASP.NET development certificate and
three first-use sentinel files were identified by exact value and timestamp and
removed. SSM remained Online after cleanup.

No GitHub credential was accessed. No commit, push, tag, release, Action, or other
GitHub operation was performed.

## Scope and authorization

- Instance ID: `i-04c241384a00f3a10`
- Region: `ap-southeast-1` (Singapore)
- Availability Zone: `ap-southeast-1b`
- AWS account and caller: resolved successfully; redacted from this report because
  they are not required to identify the user-authorized instance
- Access method: AWS CLI and AWS Systems Manager Run Command only
- Platform: Microsoft Windows Server 2022 Datacenter, version `10.0.20348`, x64
- SSM Agent: `3.3.4851.0`
- SSM execution identity/session: `NT AUTHORITY\SYSTEM`, Session 0
- Final SSM status: Online at `2026-08-15T07:36:15Z`

The following were explicitly not changed: EC2 Security Groups, NACLs, routing,
Windows network category, Windows accounts, SSH authentication or configuration,
Windows Update policy, unrelated firewall rules, RDP, and OpenSSH installation.

## Authoritative ABI review

The review used these Microsoft sources:

- `DismGetCapabilityInfo`: <https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dism/dismgetcapabilityinfo?view=windows-11>
- `DismCapabilityInfo`: <https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dism/dismcapabilityinfo?view=windows-11>
- `DismPackageFeatureState`: <https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dism/dismpackagefeaturestate-enumeration?view=windows-11>
- `DismSession`: <https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dism/dismsession?view=windows-11>
- `DismInitialize`: <https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dism/disminitialize-function?view=windows-11>
- `DismOpenSession`: <https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dism/dismopensession-function?view=windows-11>
- `DismCloseSession`: <https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dism/dismclosesession-function?view=windows-11>
- `DismDelete`: <https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dism/dismdelete-function?view=windows-11>
- `DismShutdown`: <https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dism/dismshutdown-function?view=windows-11>

### Findings confirmed in the final source

1. Native syntax is exactly three parameters:

   ```cpp
   HRESULT WINAPI DismGetCapabilityInfo(
       DismSession Session,
       PCWSTR Name,
       DismCapabilityInfo** Info);
   ```

   The final P/Invoke has exactly `uint`, UTF-16 `string`, and `out IntPtr`, returns
   a signed 32-bit HRESULT, uses `CallingConvention.Winapi`, and requests the exact
   undecorated export name.

2. `DismSession` is a native `UINT`, so `uint`/`out uint` is correct. The prior
   `IntPtr` representation was not ABI-exact on x64.

3. `DismCapabilityInfo` has this x64 layout:

   | Field | Native type | Offset |
   |---|---:|---:|
   | Name | pointer | 0 |
   | State | 32-bit enum | 8 |
   | DisplayName | pointer | 16 |
   | Description | pointer | 24 |
   | DownloadSize | DWORD | 32 |
   | InstallSize | DWORD | 36 |

   The managed sequential structure is 40 bytes on x64 and its tested offsets
   match the native layout.

4. The documented state values are `NotPresent=0`, `UninstallPending=1`,
   `Staged=2`, `Removed=3`, `Installed=4`, `InstallPending=5`, `Superseded=6`,
   and `PartiallyInstalled=7`. The final source maps `Installed=4` to Installed,
   transient/partial states to Pending, and non-present/staged/removed/superseded
   states to NotInstalled. The former source incorrectly treated value 5 as
   Installed.

5. `DismDelete` returns HRESULT; the final P/Invoke returns `int`. A successful
   query releases the returned structure with `DismDelete`, closes its session,
   and surfaces a failed cleanup HRESULT. An exception from the primary query is
   preserved while cleanup still runs.

6. The application owns one `DismCapabilityProbe` for its process lifetime.
   Initialization is synchronized and performed once; individual sessions are
   opened/closed per query; `DismShutdown` is invoked from process-lifetime
   disposal. This fixes repeated button-click behavior that would otherwise have
   violated the documented initialize/shutdown lifecycle.

## Final handoff integrity

- Final source archive:
  `handoff/agent1/ssh-desktop-dism-fix-source.tar.gz`
- Final SHA-256:
  `193ffbdc2d6b98bd342b12f5868cf1c4d438997596cf8cfae1549d88339cf9f0`
- Archive entries: 59
- AppleDouble / `__MACOSX` entries: 0
- Remote reconstructed length: 52,154 bytes
- Remote reconstructed SHA-256: exact match
- Extracted source files: 38

The archive was transferred as 12 bounded base64 chunks through SSM. No S3 bucket
or other persistent AWS resource was created.

Two earlier handoffs were rejected before runtime acceptance:

1. SHA-256 `917f70eb55c6abe9d6851de3e7c33a50fc6b1d56c66541d4a8505a76c65fae21`:
   locked restore failed with NU1004 because the probe lock omitted the transitive
   `WindowsSshEnabler.Core` project dependency.
2. SHA-256 `886ac0a880c45ae655991118f242211729e8e7b4e3a9116a9cc67dd7be5918ae`:
   all locked restores passed, but compilation failed with CS2015 because the
   macOS archive included AppleDouble `._*.cs` files.

Agent 1 corrected the lock from an authoritative .NET 10.0.400 `--force-evaluate`
restore and regenerated the final archive with macOS metadata disabled. The final
acceptance run again used `--locked-mode`; it did not bypass lock validation.

## Instance baseline

Initial baseline command ID: `4dfcfc52-c025-4b54-8967-b4ea67aaaefe`

Baseline time: `2026-08-15T07:06:31Z`

| Item | Initial value |
|---|---|
| OpenSSH capability | `NotPresent` |
| `sshd` service | absent |
| TCP 22 listeners | none |
| Active network | `Network 2`, Public, IPv4 Internet |
| Application-owned rule | absent |
| Explicit TCP 22 firewall rules | none |
| Enabled inbound wildcard rules seen by app preflight | 2, both named `Remote Desktop - Shadow (TCP-In)` |
| Firewall profiles | Domain, Private, and Public enabled; default actions reported NotConfigured |

Because OpenSSH was absent, there was no service startup type or running state to
restore. Because the application-owned rule was absent and no application backend
mutation was permitted, no firewall rule needed removal.

## Isolated toolchain

The instance had no system `dotnet` command. An official ZIP SDK was downloaded
only inside the dedicated directory
`C:\ProgramData\WindowsSshEnabler-Agent2-DismFix-20260815T0715Z`.

- Version: .NET SDK `10.0.400`
- URL:
  <https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.400/dotnet-sdk-10.0.400-win-x64.zip>
- Length: 300,546,129 bytes
- SHA-512:
  `9b8b88590e4da131bfd0da7aa089d0fc04d5418d5f8607ec13d55dc5a17b4399afd54d496c12657fa05c6c6546dc5eab930f26ac6c50f2d3a7712c0fb378c366`
- Official release metadata:
  <https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json>

`DOTNET_ROOT`, `DOTNET_CLI_HOME`, and `NUGET_PACKAGES` were set only for the test
processes and pointed under the dedicated test directory. No system-wide SDK was
installed and no persistent PATH change was made.

## Final Windows build and test results

Final command ID: `5b276724-ad5e-4b33-bd68-79111b36217a`

| Step | Result | Exit code |
|---|---|---:|
| Locked restore: tests/Core | passed | 0 |
| Locked restore: application | passed | 0 |
| Locked restore: integration probe | passed | 0 |
| Release solution build | passed; 0 warnings, 0 errors | 0 |
| Automated tests | passed, 17/17 | 0 |
| Self-contained application publish | passed | 0 |
| Self-contained integration-probe publish | passed | 0 |
| Native production probe run 1 | passed | 0 |
| Native production probe run 2 | passed | 0 |

Automated tests included the original orchestration safety tests plus:

- reflection verification of the exact three-parameter ABI and HRESULT return;
- `uint` session type and `out IntPtr` output pointer;
- exact entry-point spelling and `DismDelete` HRESULT return;
- x64 structure size and offsets;
- every documented state mapping;
- one initialize/shutdown pair across repeated fake queries;
- session cleanup and no controller mutation on `E_INVALIDARG`.

Published artifact hashes from the isolated build:

- Test-only probe SHA-256:
  `00481a751c16f8d0fe08d39a39542098dc4a0f8d0c7aa8a7ec694a12ceeda576`
- Application SHA-256:
  `e846a3f4ac5a928ce7b09c7f831097dc36e198d60dc96795f56fcf78347d0f83`

These binaries were test artifacts only and were deleted; they were not copied to
GitHub or released.

## Native integration evidence

PowerShell reported:

```text
PowerShellCapability=NotPresent
```

The exact production `DismCapabilityProbe` produced this result twice in separate
test-probe processes:

```json
{"capabilityName":"OpenSSH.Server~~~~0.0.1.0","productionProbeState":"NotInstalled","mutatingOperations":false}
```

Both runs exited 0. The semantic agreement is correct: DISM native state
`DismStateNotPresent` / PowerShell `NotPresent` maps to application state
`NotInstalled`. No `E_INVALIDARG`, `ArgumentException`, missing entry point, access
error, or cleanup failure occurred.

## Firewall and listener evidence

No application backend mutation was attempted because the capability was absent.
Before testing, after the final runtime probe, and after cleanup:

- `sshd` was absent;
- TCP port 22 had no listener;
- the application-owned firewall rule was absent;
- no explicit TCP 22 firewall rule was present;
- the active network remained Public.

Therefore there was no resulting application rule whose TCP port, LocalSubnet,
profiles, executable, service, action, or edge-traversal values could be validated.
The two wildcard Remote Desktop Shadow rules are unchanged and unrelated to this
test. No remote LAN SSH connection was attempted or claimed.

## Rollback and final read-back

Post-test read-back before cleanup command ID:
`2590868c-f843-4a12-808a-ea94e0bafc4c`.

Final read-back after cleanup command ID:
`d96dc185-a599-4d10-9bbb-ec119e6d6900`.

Final time: `2026-08-15T07:36:12Z`.

The final service/listener/network/firewall read-back exactly matched the initial
baseline values listed above.

Cleanup details:

- Gracefully stopped only the two build-server processes whose executable paths
  were under the exact isolated SDK directory (`dotnet` MSBuild node and
  `VBCSCompiler`); no unrelated process was stopped.
- Removed the exact dedicated test directory, including the 10.0.400 SDK, source
  copies, package cache, build output, logs, archives, and transfer chunks.
- The temporary SDK first use created one ASP.NET Core development certificate
  under the SSM SYSTEM user. It was uniquely matched by thumbprint, localhost
  subject, Microsoft marker OID, and test-time `NotBefore`, then removed.
- Removed exactly three zero-length .NET 10.0.400 first-use sentinels created during
  the test. No other profile file was removed.
- Final cleanup command ID: `4d69d6e0-2bc8-428b-923d-6583b6381a22`.
- Final checks: dedicated test directory absent; attributed certificate absent;
  attributed sentinels absent; SSM Online.

The first two cleanup attempts were incomplete because active .NET build servers
held SDK files and an AppleDouble filename from the rejected first archive was not
handled by PowerShell `Remove-Item`. The final cleanup first shut down only
test-scoped build servers, then used the exact explicit test path with `cmd rd`.
All final absence checks passed.

## Sanitized execution method

The complete sanitized PowerShell scripts are stored under `docs/reports/evidence/`:

- `dism-fix-agent2-baseline.ps1`
- `dism-fix-agent2-toolchain.ps1`
- `dism-fix-agent2-transfer-prep.ps1`
- `dism-fix-agent2-transfer-verify.ps1`
- `dism-fix-agent2-transfer-v2-verify.ps1`
- `dism-fix-agent2-transfer-v3-verify.ps1`
- `dism-fix-agent2-sdk-prepare.ps1`
- `dism-fix-agent2-regenerate-probe-lock.ps1`
- `dism-fix-agent2-build-runtime.ps1`
- `dism-fix-agent2-cleanup.ps1`
- `dism-fix-agent2-cleanup-inspect.ps1`
- `dism-fix-agent2-cleanup-retry.ps1`
- `dism-fix-agent2-cleanup-sideeffect-inspect.ps1`
- `dism-fix-agent2-cleanup-final.ps1`

Each was encoded locally as UTF-16LE base64 and sent with this pattern, with the
instance and region shown here and no credentials embedded:

```text
aws ssm send-command \
  --region ap-southeast-1 \
  --instance-ids i-04c241384a00f3a10 \
  --document-name AWS-RunPowerShellScript \
  --parameters commands=[powershell.exe -NoProfile -NonInteractive -EncodedCommand <redacted-script-payload>]
```

Command status and output were read with `aws ssm get-command-invocation`. Transfer
payloads are omitted from this report because they are the base64 representation of
the independently hashed source archive, not human-reviewable commands.

## Severity-ranked findings and limitations

### High — acceptance coverage blocker

**H1: The designated instance does not have OpenSSH Server installed.** This blocks
the user-requested Installed-state assertion and any safe service/listener/firewall
end-to-end test. Resolution: repeat the same final native and backend acceptance
steps on an authorized disposable Windows instance where the capability was already
installed before testing. Do not install it merely to make this test pass unless the
user separately authorizes that system change.

**H2: The only connected network is Public.** Even with OpenSSH installed, the
application must safely refuse to create its LAN rule. Resolution: use an authorized
test instance with a genuine Private/Domain connection; do not reclassify this
instance solely for testing.

### Medium — operational finding

**M1: Conservative wildcard-rule handling may refuse on this Windows Server.** The
baseline contains two enabled inbound wildcard rules named `Remote Desktop - Shadow
(TCP-In)`. The application treats any enabled inbound Any-protocol/Any-port rule as
applying to port 22, then treats every non-exact Allow rule as broader than its SSH
rule even when a rule may be bound to another program or service. This fails closed
and does not create exposure, but it can be a false-positive usability blocker.
Before changing that behavior, add native firewall tests proving that program/service
bindings cannot authorize `sshd`; never weaken conflict detection based only on a
rule display name.

### Resolved during validation

- The integration-probe lock initially omitted a transitive project dependency;
  final locked restores pass.
- The first two archives contained macOS AppleDouble metadata; the final archive has
  zero such entries and builds successfully on Windows.
- Temporary SDK first-use artifacts were fully and narrowly removed.

### Remaining limitations

- SSM ran in Session 0. No visible WinForms window was inspected or clicked.
- The Installed capability path was not executed on a real device.
- `sshd` start/configuration and exclusive IPv4/IPv6 TCP 22 ownership were not run.
- No application firewall rule was created or verified on Windows.
- No LAN peer performed an SSH connection.
- Installer, upgrade, uninstall, Authenticode, Defender, and SmartScreen behavior
  were outside this fix-specific runtime test.

The corrected DISM ABI is validated for the available NotPresent path, but the
product should not be described as fully end-to-end accepted until the blocked
Installed/Private-or-Domain runtime tests are completed.

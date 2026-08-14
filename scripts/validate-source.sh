#!/bin/sh
set -eu

validation_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
app_root="$validation_root/src/WindowsSshEnabler"

test -f "$validation_root/WindowsSshEnabler.slnx"
test -f "$app_root/app.manifest"
test -f "$app_root/Properties/PublishProfiles/win-x64.pubxml"
test "$(find "$validation_root" -name packages.lock.json -type f | wc -l | tr -d ' ')" = "3"
grep -q 'OutputType>WinExe<' "$app_root/WindowsSshEnabler.csproj"
grep -q 'TargetFramework>net10.0-windows<' "$app_root/WindowsSshEnabler.csproj"
grep -q 'requireAdministrator' "$app_root/app.manifest"
grep -q 'uiAccess="false"' "$app_root/app.manifest"
grep -q 'PublishSingleFile>true<' "$app_root/Properties/PublishProfiles/win-x64.pubxml"
grep -q 'PublishTrimmed>false<' "$app_root/Properties/PublishProfiles/win-x64.pubxml"
grep -q 'WindowsSshEnabler.LanOpenSsh.Tcp22' "$app_root/Native/WindowsFirewallManager.cs"
grep -q 'RemoteAddresses = "LocalSubnet"' "$app_root/Native/WindowsFirewallManager.cs"
grep -q 'Profiles = ProfileDomainAndPrivate' "$app_root/Native/WindowsFirewallManager.cs"
grep -q 'ServiceName = "sshd"' "$app_root/Native/WindowsFirewallManager.cs"
grep -q 'DismGetCapabilityInfo' "$app_root/Native/DismCapabilityProbe.cs"
grep -q 'AfInet6' "$app_root/Native/IpHelperPortInspector.cs"

button_count=$(grep -R 'new Button' "$app_root/UI" | wc -l | tr -d ' ')
test "$button_count" = "1"

if grep -REi 'powershell\.exe|cmd\.exe|ProcessStartInfo|Process\.Start' "$app_root"; then
  echo "ERROR: forbidden shell/process execution was found in application source." >&2
  exit 1
fi

if command -v jq >/dev/null 2>&1; then
  find "$validation_root" -name packages.lock.json -type f -exec jq empty {} \;
fi

if command -v xmllint >/dev/null 2>&1; then
  xmllint --noout \
    "$validation_root/WindowsSshEnabler.slnx" \
    "$validation_root/Directory.Build.props" \
    "$validation_root/src/WindowsSshEnabler.Core/WindowsSshEnabler.Core.csproj" \
    "$app_root/WindowsSshEnabler.csproj" \
    "$app_root/app.manifest" \
    "$app_root/Properties/PublishProfiles/win-x64.pubxml"
fi

echo "Source structure and safety invariant checks passed."

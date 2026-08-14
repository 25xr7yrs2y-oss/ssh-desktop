#requires -Version 5.1
<#
.SYNOPSIS
Performs read-only post-install checks on a Windows test machine.

.DESCRIPTION
This script never starts the app, changes services/firewall/network state, or
installs/uninstalls anything. Run it after a manual installer acceptance test.
#>
[CmdletBinding()]
param(
    [string]$InstallDirectory = (Join-Path $env:ProgramFiles 'Windows SSH Enabler'),
    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$MtPath,
    [switch]$RequireValidSignature
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'Installed-state validation must run on Windows.'
}

function Assert-EqualPath([string]$Actual, [string]$Expected, [string]$Label) {
    $left = [IO.Path]::GetFullPath($Actual).TrimEnd('\')
    $right = [IO.Path]::GetFullPath($Expected).TrimEnd('\')
    if (-not $left.Equals($right, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label targets '$left'; expected '$right'."
    }
}

function Get-ShortcutTarget([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Shortcut is missing: $Path" }
    $shell = New-Object -ComObject WScript.Shell
    try { return $shell.CreateShortcut($Path).TargetPath }
    finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) }
}

$exePath = Join-Path $InstallDirectory 'WindowsSshEnabler.exe'
if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
    throw "Installed application is missing: $exePath"
}

$manifestPath = Join-Path $env:TEMP ('WindowsSshEnabler-manifest-' + [guid]::NewGuid().ToString('N') + '.xml')
try {
    & $MtPath "-inputresource:$exePath;#1" "-out:$manifestPath"
    if ($LASTEXITCODE -ne 0) { throw "mt.exe failed with exit code $LASTEXITCODE." }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw
    if ($manifest -notmatch '<requestedExecutionLevel\s+level=["'']requireAdministrator["'']') {
        throw 'Installed executable does not request requireAdministrator.'
    }
}
finally {
    if (Test-Path -LiteralPath $manifestPath) { Remove-Item -LiteralPath $manifestPath -Force }
}

$signature = Get-AuthenticodeSignature -LiteralPath $exePath
$expectedSignature = if ($RequireValidSignature) { 'Valid' } else { 'NotSigned' }
if ($signature.Status.ToString() -ne $expectedSignature) {
    throw "Installed executable signature is $($signature.Status); expected $expectedSignature."
}

$desktopShortcut = Join-Path $env:PUBLIC 'Desktop\Windows SSH Enabler.lnk'
$menuShortcut = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\Windows SSH Enabler\Windows SSH Enabler.lnk'
Assert-EqualPath (Get-ShortcutTarget $desktopShortcut) $exePath 'Desktop shortcut'
Assert-EqualPath (Get-ShortcutTarget $menuShortcut) $exePath 'Start Menu shortcut'

$uninstallKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{B1D84CE8-E5D1-4B27-89E8-A72F1A0A6365}_is1'
$uninstall = Get-ItemProperty -LiteralPath $uninstallKey
if ($uninstall.DisplayName -ne 'Windows SSH Enabler') {
    throw "Unexpected uninstall DisplayName: $($uninstall.DisplayName)"
}
if (-not $uninstall.UninstallString) { throw 'UninstallString is missing.' }
if ($uninstall.UninstallString -notlike "*$InstallDirectory*") {
    throw 'UninstallString does not point inside the exact application installation directory.'
}

$result = [ordered]@{
    application = $exePath
    sha256 = (Get-FileHash -LiteralPath $exePath -Algorithm SHA256).Hash.ToLowerInvariant()
    authenticode = $signature.Status.ToString()
    manifestElevation = 'requireAdministrator'
    desktopShortcut = $desktopShortcut
    startMenuShortcut = $menuShortcut
    uninstallKey = $uninstallKey
}
$result | ConvertTo-Json -Depth 3

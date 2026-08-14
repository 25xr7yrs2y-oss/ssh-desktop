#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })]
    [string]$SourceRoot,

    [ValidatePattern('^\d+\.\d+\.\d+(?:\.\d+)?$')]
    [string]$Version = '0.1.0',

    [ValidateSet('Release')]
    [string]$Configuration = 'Release',

    [string]$PublisherName = 'Publisher Name (configure before release)',
    [string]$AppIconPath = '',
    [string]$IsccPath = '',
    [string]$MtPath = '',
    [string]$SignToolPath = '',
    [switch]$EnableSigning,
    [string]$SigningCertificateThumbprint = '',
    [string]$TimestampUrl = '',
    [switch]$KeepPublishDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$script:BuildRoot = Split-Path -Parent $PSScriptRoot
$script:InstallerScript = Join-Path $script:BuildRoot 'installer\WindowsSshEnabler.iss'
$script:WorkRoot = Join-Path $script:BuildRoot 'work'
$script:PublishRoot = Join-Path $script:WorkRoot 'publish'
$script:StageRoot = Join-Path $script:WorkRoot 'stage'
$script:ArtifactRoot = Join-Path $script:BuildRoot 'artifacts'
$script:ExpectedExe = 'WindowsSshEnabler.exe'

function Write-Step([string]$Message) {
    Write-Host "[build] $Message"
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    Write-Host ('[exec] {0} {1}' -f $FilePath, ($Arguments -join ' '))
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $($LASTEXITCODE): $FilePath"
    }
}

function Resolve-RequiredCommand {
    param([string]$ExplicitPath, [string]$CommandName, [string[]]$Candidates = @())

    if ($ExplicitPath) {
        if (-not (Test-Path -LiteralPath $ExplicitPath -PathType Leaf)) {
            throw "Required tool does not exist: $ExplicitPath"
        }
        return (Resolve-Path -LiteralPath $ExplicitPath).Path
    }

    $command = Get-Command $CommandName -ErrorAction SilentlyContinue
    if ($command) { return $command.Path }

    foreach ($candidate in $Candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }
    throw "Required tool was not found: $CommandName"
}

function Reset-ExactDirectory([string]$Path) {
    $resolvedBuildRoot = [IO.Path]::GetFullPath($script:BuildRoot).TrimEnd('\')
    $resolvedPath = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    if (-not $resolvedPath.StartsWith($resolvedBuildRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear a directory outside the Agent 2 build root: $resolvedPath"
    }
    if (Test-Path -LiteralPath $resolvedPath) {
        Remove-Item -LiteralPath $resolvedPath -Recurse -Force
    }
    New-Item -ItemType Directory -Path $resolvedPath -Force | Out-Null
}

function Assert-AuthenticodeState {
    param([string]$Path, [bool]$MustBeSigned)

    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($MustBeSigned) {
        if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
            throw "Expected a valid Authenticode signature, but '$Path' is $($signature.Status)."
        }
    }
    elseif ($signature.Status -ne [System.Management.Automation.SignatureStatus]::NotSigned) {
        throw "Signing is disabled, but '$Path' has unexpected signature status $($signature.Status)."
    }
    return $signature.Status.ToString()
}

function Get-PeSubsystem([string]$Path) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        $reader = New-Object IO.BinaryReader($stream)
        if ($reader.ReadUInt16() -ne 0x5A4D) { throw "Not an MZ executable: $Path" }
        $stream.Position = 0x3C
        $peOffset = $reader.ReadUInt32()
        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x00004550) { throw "Missing PE signature: $Path" }
        $optionalHeader = $peOffset + 4 + 20
        $stream.Position = $optionalHeader
        $magic = $reader.ReadUInt16()
        if (($magic -ne 0x10B) -and ($magic -ne 0x20B)) {
            throw "Unsupported PE optional-header magic 0x$('{0:X}' -f $magic)."
        }
        $stream.Position = $optionalHeader + 68
        return $reader.ReadUInt16()
    }
    finally {
        $stream.Dispose()
    }
}

function Assert-ManifestRequiresAdministrator {
    param([string]$ExePath, [string]$ManifestTool)

    $manifestPath = Join-Path $script:WorkRoot 'extracted.application.manifest'
    if (Test-Path -LiteralPath $manifestPath) { Remove-Item -LiteralPath $manifestPath -Force }
    Invoke-Checked $ManifestTool @("-inputresource:$ExePath;#1", "-out:$manifestPath")
    $manifest = Get-Content -LiteralPath $manifestPath -Raw
    if ($manifest -notmatch '<requestedExecutionLevel\s+level=["'']requireAdministrator["'']') {
        throw 'The application manifest does not request requireAdministrator.'
    }
}

function Assert-InnoContract([string]$Path) {
    $text = Get-Content -LiteralPath $Path -Raw
    $required = @(
        'AppId={{B1D84CE8-E5D1-4B27-89E8-A72F1A0A6365}',
        'ArchitecturesAllowed=x64compatible',
        'ArchitecturesInstallIn64BitMode=x64compatible',
        'PrivilegesRequired=admin',
        'DefaultDirName={autopf}\\Windows SSH Enabler',
        'Name: "{group}\\Windows SSH Enabler"',
        'Name: "{autodesktop}\\Windows SSH Enabler"'
    )
    foreach ($item in $required) {
        if (-not $text.Contains($item)) { throw "Installer contract is missing: $item" }
    }
    foreach ($forbidden in @('[Run]', '[Registry]', '[Tasks]', '[Code]', '[UninstallRun]', '[UninstallDelete]')) {
        if ($text -match "(?m)^\s*$([regex]::Escape($forbidden))\s*$") {
            throw "Forbidden active installer section found: $forbidden"
        }
    }
    $activeFileLines = @($text -split "`r?`n" | Where-Object { $_ -match '^\s*Source:' })
    if (($activeFileLines.Count -ne 1) -or ($activeFileLines[0] -notmatch 'WindowsSshEnabler\.exe')) {
        throw 'The installer must contain exactly one explicitly named application payload.'
    }
}

function Invoke-Signing {
    param([string]$Path, [string]$ToolPath)
    if (-not $EnableSigning) { return }
    if (-not $SigningCertificateThumbprint -or -not $TimestampUrl) {
        throw 'Signing requires explicit SigningCertificateThumbprint and TimestampUrl values.'
    }
    if ($SigningCertificateThumbprint -notmatch '^[0-9A-Fa-f]{40,64}$') {
        throw 'The certificate thumbprint format is invalid.'
    }
    if ($TimestampUrl -notmatch '^https://') {
        throw 'The RFC 3161 timestamp URL must use HTTPS.'
    }
    Invoke-Checked $ToolPath @(
        'sign', '/fd', 'SHA256', '/sha1', $SigningCertificateThumbprint,
        '/tr', $TimestampUrl, '/td', 'SHA256', $Path
    )
    Invoke-Checked $ToolPath @('verify', '/pa', '/all', '/v', $Path)
}

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'This packaging script must run on Windows; it will not emulate an Inno Setup build on another OS.'
}
if ([string]::IsNullOrWhiteSpace($PublisherName) -or $PublisherName.Length -gt 128 -or $PublisherName -match '[\x00-\x1F\r\n]') {
    throw 'PublisherName must be 1-128 characters and contain no control characters.'
}
if ($AppIconPath) {
    if (-not (Test-Path -LiteralPath $AppIconPath -PathType Leaf)) { throw "Application icon is missing: $AppIconPath" }
    if ([IO.Path]::GetExtension($AppIconPath) -ine '.ico') { throw 'AppIconPath must be a local .ico file.' }
    $AppIconPath = (Resolve-Path -LiteralPath $AppIconPath).Path
}

Write-Step 'Resolving required toolchain.'
$dotnet = Resolve-RequiredCommand '' 'dotnet'
$isccCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
)
$iscc = Resolve-RequiredCommand $IsccPath 'ISCC.exe' $isccCandidates
$mt = Resolve-RequiredCommand $MtPath 'mt.exe'
$signTool = $null
if ($EnableSigning) { $signTool = Resolve-RequiredCommand $SignToolPath 'signtool.exe' }

$projectFiles = @(Get-ChildItem -LiteralPath $SourceRoot -Filter '*.csproj' -File -Recurse)
$appProjects = @($projectFiles | Where-Object {
    (Get-Content -LiteralPath $_.FullName -Raw) -match '<OutputType>\s*WinExe\s*</OutputType>'
})
$testProjects = @($projectFiles | Where-Object { $_.Name -match '(?i)(test|tests)\.csproj$' })
if ($appProjects.Count -ne 1) { throw "Expected exactly one WinExe application project; found $($appProjects.Count)." }
if ($testProjects.Count -lt 1) { throw 'At least one automated test project is required before packaging.' }
$appProject = $appProjects[0].FullName

Reset-ExactDirectory $script:WorkRoot
Reset-ExactDirectory $script:ArtifactRoot
New-Item -ItemType Directory -Path $script:PublishRoot, $script:StageRoot -Force | Out-Null

Write-Step 'Restoring application and tests.'
foreach ($testProject in $testProjects) {
    Invoke-Checked $dotnet @('restore', $testProject.FullName, '--locked-mode')
}
Invoke-Checked $dotnet @('restore', $appProject, '--locked-mode', '--runtime', 'win-x64')

Write-Step 'Running all test projects.'
foreach ($testProject in $testProjects) {
    $testProjectText = Get-Content -LiteralPath $testProject.FullName -Raw
    $usesTestHost = ($testProjectText -match '<IsTestProject>\s*true\s*</IsTestProject>') -or
        ($testProjectText -match 'Microsoft\.NET\.Test\.Sdk')
    if ($usesTestHost) {
        Invoke-Checked $dotnet @('test', $testProject.FullName, '--configuration', $Configuration, '--no-restore', '--verbosity', 'normal')
    }
    elseif ($testProjectText -match '<OutputType>\s*Exe\s*</OutputType>') {
        Invoke-Checked $dotnet @('build', $testProject.FullName, '--configuration', $Configuration, '--no-restore', '--verbosity', 'normal')
        Invoke-Checked $dotnet @('run', '--project', $testProject.FullName, '--configuration', $Configuration, '--no-build', '--no-restore')
    }
    else {
        throw "Test project is neither a recognized test-host project nor an executable test runner: $($testProject.FullName)"
    }
}

Write-Step 'Publishing a self-contained win-x64 single-file GUI application.'
Invoke-Checked $dotnet @(
    'publish', $appProject,
    '--configuration', $Configuration,
    '--runtime', 'win-x64',
    '--self-contained', 'true',
    '--no-restore',
    '--output', $script:PublishRoot,
    "/p:Version=$Version",
    '/p:PublishSingleFile=true',
    '/p:IncludeNativeLibrariesForSelfExtract=true',
    '/p:PublishTrimmed=false',
    '/p:DebugType=None',
    '/p:DebugSymbols=false'
)

$publishedExe = Join-Path $script:PublishRoot $script:ExpectedExe
if (-not (Test-Path -LiteralPath $publishedExe -PathType Leaf)) {
    throw "Expected publish output is missing: $publishedExe"
}
$publishItems = @(Get-ChildItem -LiteralPath $script:PublishRoot -Force)
if (($publishItems.Count -ne 1) -or ($publishItems[0].Name -cne $script:ExpectedExe)) {
    throw 'Single-file publish contains unexpected sidecar files; packaging stopped.'
}
Copy-Item -LiteralPath $publishedExe -Destination (Join-Path $script:StageRoot $script:ExpectedExe)
$stageItems = @(Get-ChildItem -LiteralPath $script:StageRoot -Force)
if (($stageItems.Count -ne 1) -or ($stageItems[0].Name -cne $script:ExpectedExe)) {
    throw 'Staging contains unexpected files; packaging stopped.'
}

$stagedExe = $stageItems[0].FullName
Write-Step 'Validating application executable metadata.'
if ((Get-PeSubsystem $stagedExe) -ne 2) {
    throw 'The application is not a Windows GUI-subsystem executable.'
}
Assert-ManifestRequiresAdministrator $stagedExe $mt
Assert-InnoContract $script:InstallerScript

if ($EnableSigning) {
    Write-Step 'Signing application through the explicitly enabled integration point.'
    Invoke-Signing $stagedExe $signTool
}
$appSignatureState = Assert-AuthenticodeState $stagedExe ([bool]$EnableSigning)

Write-Step 'Compiling the Inno Setup installer.'
$isccArguments = @(
    "/DAppVersion=$Version",
    "/DAppStageDir=$script:StageRoot",
    "/DInstallerOutputDir=$script:ArtifactRoot",
    "/DPublisherName=$PublisherName"
)
if ($AppIconPath) { $isccArguments += "/DAppIconFile=$AppIconPath" }
$isccArguments += $script:InstallerScript
Invoke-Checked $iscc $isccArguments

$setupPath = Join-Path $script:ArtifactRoot 'WindowsSshEnabler-Setup-x64.exe'
if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf)) {
    throw "Inno Setup returned without the expected installer: $setupPath"
}
if ($EnableSigning) {
    Write-Step 'Signing installer through the explicitly enabled integration point.'
    Invoke-Signing $setupPath $signTool
}
$setupSignatureState = Assert-AuthenticodeState $setupPath ([bool]$EnableSigning)

Write-Step 'Writing deterministic, sorted artifact inventory and checksums.'
$inventory = @(
    [ordered]@{
        path = 'work/stage/WindowsSshEnabler.exe'
        bytes = (Get-Item -LiteralPath $stagedExe).Length
        sha256 = (Get-FileHash -LiteralPath $stagedExe -Algorithm SHA256).Hash.ToLowerInvariant()
        authenticode = $appSignatureState
    },
    [ordered]@{
        path = 'artifacts/WindowsSshEnabler-Setup-x64.exe'
        bytes = (Get-Item -LiteralPath $setupPath).Length
        sha256 = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()
        authenticode = $setupSignatureState
    }
) | Sort-Object { $_.path }

$inventoryPath = Join-Path $script:ArtifactRoot 'artifact-inventory.json'
@($inventory) | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $inventoryPath -Encoding UTF8
$checksumPath = Join-Path $script:ArtifactRoot 'SHA256SUMS.txt'
$checksumLines = @($inventory | ForEach-Object { '{0}  {1}' -f $_.sha256, $_.path })
[IO.File]::WriteAllLines($checksumPath, $checksumLines, (New-Object Text.UTF8Encoding($false)))

if (-not $EnableSigning) {
    Write-Warning 'UNSIGNED DEVELOPMENT ARTIFACTS: do not describe these files as trusted or production-signed.'
}
Write-Step "Build complete: $setupPath"

if (-not $KeepPublishDirectory -and (Test-Path -LiteralPath $script:PublishRoot)) {
    Remove-Item -LiteralPath $script:PublishRoot -Recurse -Force
    Write-Host '[build] Removed intermediate publish output; validated staging was retained for inventory verification.'
}

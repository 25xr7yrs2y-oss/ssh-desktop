$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ProjectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $ProjectRoot
try {
    dotnet --info
    dotnet restore .\src\WindowsSshEnabler.Core\WindowsSshEnabler.Core.csproj --locked-mode
    dotnet restore .\tests\WindowsSshEnabler.Tests\WindowsSshEnabler.Tests.csproj --locked-mode
    dotnet restore .\src\WindowsSshEnabler\WindowsSshEnabler.csproj --runtime win-x64 --locked-mode
    dotnet build .\WindowsSshEnabler.slnx -c Release --no-restore
    dotnet run --project .\tests\WindowsSshEnabler.Tests\WindowsSshEnabler.Tests.csproj -c Release --no-build
    dotnet publish .\src\WindowsSshEnabler\WindowsSshEnabler.csproj -c Release -p:PublishProfile=win-x64 --no-restore
}
finally {
    Pop-Location
}

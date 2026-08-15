$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ProjectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $ProjectRoot
try {
    dotnet --info
    dotnet restore .\tests\WindowsSshEnabler.Tests\WindowsSshEnabler.Tests.csproj --runtime win-x64 --locked-mode
    dotnet restore .\src\WindowsSshEnabler\WindowsSshEnabler.csproj --runtime win-x64 --locked-mode
    dotnet restore .\tools\WindowsSshEnabler.DismProbe\WindowsSshEnabler.DismProbe.csproj --runtime win-x64 --locked-mode
    dotnet build .\WindowsSshEnabler.slnx -c Release --no-restore
    dotnet run --project .\tests\WindowsSshEnabler.Tests\WindowsSshEnabler.Tests.csproj -c Release --no-build
    dotnet publish .\src\WindowsSshEnabler\WindowsSshEnabler.csproj -c Release -p:PublishProfile=win-x64 --no-restore
    dotnet publish .\tools\WindowsSshEnabler.DismProbe\WindowsSshEnabler.DismProbe.csproj -c Release -r win-x64 --self-contained true --no-restore
}
finally {
    Pop-Location
}

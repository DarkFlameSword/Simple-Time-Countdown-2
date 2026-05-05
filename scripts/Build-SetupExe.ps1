[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64'
)

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) {
    $dotnet = 'dotnet'
}

$outputDir = Join-Path $root 'artifacts\packages'
$publishDir = Join-Path $root "artifacts\publish\setup\$RuntimeIdentifier"
$portableZip = Join-Path $outputDir "SimpleTimeCountdown-$Configuration-$RuntimeIdentifier-portable.zip"
$payloadDir = Join-Path $root 'artifacts\installer-payload'
$payloadZip = Join-Path $payloadDir 'TimeCountdown-portable.zip'
$targetExe = Join-Path $outputDir "SimpleTimeCountdown-Setup-$RuntimeIdentifier.exe"
$signTool = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe'
$pfxPath = Join-Path $root 'artifacts\certificates\TimeCountdownDev.pfx'
$certPassword = 'TimeCountdownDev123!'

& (Join-Path $PSScriptRoot 'Publish-Portable.ps1') -Configuration $Configuration -RuntimeIdentifier $RuntimeIdentifier
if ($LASTEXITCODE) { exit $LASTEXITCODE }

New-Item -ItemType Directory -Force -Path $outputDir, $publishDir, $payloadDir | Out-Null
Copy-Item -LiteralPath $portableZip -Destination $payloadZip -Force
Remove-Item -LiteralPath $targetExe -Force -ErrorAction SilentlyContinue

& $dotnet publish (Join-Path $root 'src\SimpleTimeCountdown.Setup\SimpleTimeCountdown.Setup.csproj') `
    -c $Configuration `
    -r $RuntimeIdentifier `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDir
if ($LASTEXITCODE) { exit $LASTEXITCODE }

$builtExe = Join-Path $publishDir 'TimeCountdown.Setup.exe'
if (-not (Test-Path $builtExe)) {
    throw "Setup publish did not produce the expected executable: $builtExe"
}

Copy-Item -LiteralPath $builtExe -Destination $targetExe -Force

if ((Test-Path $signTool) -and (Test-Path $pfxPath)) {
    & $signTool sign /fd SHA256 /f $pfxPath /p $certPassword $targetExe
}

Write-Host "Setup EXE ready: $targetExe"

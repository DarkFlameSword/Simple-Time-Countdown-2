[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64'
)

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) {
    $dotnet = 'dotnet'
}

& (Join-Path $PSScriptRoot 'Generate-AppAssets.ps1') -Root $root
if ($LASTEXITCODE) { exit $LASTEXITCODE }

$publishDir = Join-Path $root "artifacts\publish\portable\$RuntimeIdentifier"
$zipDir = Join-Path $root 'artifacts\packages'
New-Item -ItemType Directory -Force -Path $publishDir, $zipDir | Out-Null

& $dotnet publish (Join-Path $root 'src\SimpleTimeCountdown.App\SimpleTimeCountdown.App.csproj') `
    -c $Configuration `
    -r $RuntimeIdentifier `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o $publishDir
if ($LASTEXITCODE) { exit $LASTEXITCODE }

$zipPath = Join-Path $zipDir "SimpleTimeCountdown-$Configuration-$RuntimeIdentifier-portable.zip"
if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath
Write-Host "Portable package ready: $zipPath"

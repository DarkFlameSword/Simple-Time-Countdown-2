[CmdletBinding()]
param(
    [string]$Root,
    [string]$Version = '',
    [string]$RuntimeIdentifier = 'win-x64'
)

if ([string]::IsNullOrWhiteSpace($Root)) {
    $scriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $PSCommandPath }
    $Root = (Resolve-Path (Join-Path $scriptRoot '..')).Path
}

$certPath = Join-Path $Root 'artifacts\certificates\TimeCountdownDev.cer'
if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$versionProps = Get-Content (Join-Path $Root 'Directory.Build.props')
    $Version = $versionProps.Project.PropertyGroup.FileVersion
}
$msixPath = Join-Path $Root "artifacts\packages\SimpleTimeCountdown_$Version`_$RuntimeIdentifier.msix"

if (-not (Test-Path $certPath)) {
    throw "Certificate not found: $certPath"
}

if (-not (Test-Path $msixPath)) {
    throw "MSIX package not found: $msixPath"
}

$certutil = Join-Path $env:SystemRoot 'System32\certutil.exe'
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).
    IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

# MSIX sideloading of a self-signed package only needs the signing certificate in the
# TrustedPeople store. The certificate is intentionally NOT added to the Trusted Root
# Certification Authorities store, which would grant it far broader, machine-wide code-signing trust.
if ($isAdmin) {
    & $certutil -f -addstore TrustedPeople $certPath | Out-Null
}
else {
    Write-Warning 'Running without elevation. The certificate is being added to the current user''s Trusted People store. If the MSIX install still fails, re-run this script from an elevated PowerShell to add it to the machine-level Trusted People store.'
    & $certutil -user -f -addstore TrustedPeople $certPath | Out-Null
}

Add-AppxPackage -Path $msixPath -ErrorAction Stop
Write-Host "Installed $msixPath"

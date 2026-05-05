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

if ($isAdmin) {
    & $certutil -f -addstore TrustedPeople $certPath | Out-Null
    & $certutil -f -addstore Root $certPath | Out-Null
}
else {
    Write-Warning 'Running without elevation. Current-user certificate trust will be applied, but some Windows setups still require importing the certificate into the LocalMachine Root store from an elevated PowerShell before MSIX install.'
    & $certutil -user -f -addstore TrustedPeople $certPath | Out-Null
    & $certutil -user -f -addstore Root $certPath | Out-Null
}

Add-AppxPackage -Path $msixPath -ErrorAction Stop
Write-Host "Installed $msixPath"

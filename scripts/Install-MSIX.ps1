<#
.SYNOPSIS
    Installs an MSIX built by Publish-MSIX.ps1 on this machine, for testing.

.DESCRIPTION
    A package signed with a certificate Windows already trusts is installed as it is. A package signed
    with this machine's development certificate (Publish-MSIX.ps1 -DevCertificate) needs that
    certificate in the Trusted People store first; the script adds it there, and only after checking
    that it is the very certificate that signed the package. Unsigned packages are refused.

    The certificate is deliberately NOT added to Trusted Root Certification Authorities, which would
    trust it for far more than sideloading this one package.

.EXAMPLE
    .\scripts\Install-MSIX.ps1

.EXAMPLE
    .\scripts\Install-MSIX.ps1 -Path .\artifacts\packages\SimpleTimeCountdown_2.0.0.0_win-arm64.msix
#>
[CmdletBinding()]
param(
    # The package to install. Defaults to the one Publish-MSIX.ps1 builds for -RuntimeIdentifier.
    [string]$Path,

    [ValidateSet('win-x64', 'win-x86', 'win-arm64')]
    [string]$RuntimeIdentifier = 'win-x64'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ReleaseCommon.ps1')

$root = Get-RepositoryRoot
if (-not $Path) {
    $Path = Get-MsixPackagePath -Root $root -RuntimeIdentifier $RuntimeIdentifier
}

if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    throw "MSIX package not found: $Path. Build it with scripts\Publish-MSIX.ps1 first."
}

$Path = (Resolve-Path -LiteralPath $Path).Path
$signature = Get-AuthenticodeSignature -LiteralPath $Path
if ($signature.Status -eq 'NotSigned' -or -not $signature.SignerCertificate) {
    throw "$Path is not signed, so Windows will not install it. Rebuild it with a signing certificate or with -DevCertificate."
}

if ($signature.Status -ne 'Valid') {
    $cerPath = Get-DevCertificatePath -Root $root
    if (-not (Test-Path -LiteralPath $cerPath)) {
        throw "The package signature is not trusted ($($signature.StatusMessage)), and no development certificate was found at $cerPath."
    }

    $devCertificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($cerPath)
    if ($devCertificate.Thumbprint -ne $signature.SignerCertificate.Thumbprint) {
        throw ("The package signature is not trusted ($($signature.StatusMessage)), and the package was not signed with " +
            "this machine's development certificate, so it is not trusted automatically.")
    }

    # Sideloading needs the signer in the machine's Trusted People store. Without elevation only the
    # current user's store is writable, which some Windows versions do not accept for MSIX.
    $isAdministrator = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).
        IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    $store = if ($isAdministrator) { 'Cert:\LocalMachine\TrustedPeople' } else { 'Cert:\CurrentUser\TrustedPeople' }
    if (-not $isAdministrator) {
        Write-Warning ('Not elevated: trusting the development certificate for the current user only. If the install ' +
            'still fails, run this script from an elevated PowerShell to trust it machine-wide.')
    }

    if (-not (Test-Path -LiteralPath (Join-Path $store $devCertificate.Thumbprint))) {
        Import-Certificate -FilePath $cerPath -CertStoreLocation $store | Out-Null
        Write-Host "Trusted the development certificate $($devCertificate.Thumbprint) in $store."
    }
}

Add-AppxPackage -Path $Path
Write-Host "Installed $Path"

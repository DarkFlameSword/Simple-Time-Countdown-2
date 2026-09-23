<#
.SYNOPSIS
    Builds the portable package: artifacts\packages\SimpleTimeCountdown-<configuration>-<rid>-portable.zip.

.DESCRIPTION
    Publishes the self-contained app into a clean folder, adds LICENSE.txt and THIRD-PARTY-NOTICES.txt,
    moves the symbols to artifacts\symbols, signs the app binaries when a certificate is configured (see
    scripts\ReleaseCommon.ps1) and zips the result. The same zip is the payload of Setup.exe.

.EXAMPLE
    .\scripts\Publish-Portable.ps1

.EXAMPLE
    .\scripts\Publish-Portable.ps1 -RuntimeIdentifier win-arm64 -CertificateThumbprint 0123...ABCD
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',

    [ValidateSet('win-x64', 'win-x86', 'win-arm64')]
    [string]$RuntimeIdentifier = 'win-x64',

    [string]$CertificateThumbprint,
    [string]$CertificatePath,
    [SecureString]$CertificatePassword,
    [string]$TimestampUrl,
    [switch]$DevCertificate,
    [switch]$RequireSigning
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ReleaseCommon.ps1')

$root = Get-RepositoryRoot
Push-Location -LiteralPath $root
try {
    $signingParameters = Get-SigningParameters -BoundParameters $PSBoundParameters
    $signing = Resolve-CodeSigning -Root $root @signingParameters
    $dotnet = Resolve-DotNet

    $zipPath = New-PortablePackage -DotNet $dotnet -Root $root -Configuration $Configuration `
        -RuntimeIdentifier $RuntimeIdentifier -Signing $signing

    Write-Host "Portable package ready: $zipPath"
    Write-SigningSummary -Signing $signing
}
finally {
    Pop-Location
}

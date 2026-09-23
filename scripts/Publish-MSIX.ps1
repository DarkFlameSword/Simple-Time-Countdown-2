<#
.SYNOPSIS
    Builds the MSIX package: artifacts\packages\SimpleTimeCountdown_<version>_<rid>.msix.

.DESCRIPTION
    Publishes the app into a clean layout, adds the logos from packaging\msix\Assets, fills in the
    manifest template (version from Directory.Build.props, publisher from the signing certificate,
    architecture from -RuntimeIdentifier), indexes the scaled logos into resources.pri, packs the layout
    and signs the package.

    An MSIX must be signed by a certificate whose subject equals the manifest's Publisher before Windows
    installs it. See scripts\ReleaseCommon.ps1 for the signing options. For local testing, -DevCertificate
    signs with a self-signed certificate that Install-MSIX.ps1 can trust on this machine.

.EXAMPLE
    .\scripts\Publish-MSIX.ps1 -DevCertificate

.EXAMPLE
    .\scripts\Publish-MSIX.ps1 -CertificateThumbprint 0123...ABCD -RequireSigning
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',

    [ValidateSet('win-x64', 'win-x86', 'win-arm64')]
    [string]$RuntimeIdentifier = 'win-x64',

    # Package publisher (an X.500 name such as "CN=Contoso, O=Contoso Ltd, C=US"). Defaults to the signing
    # certificate's subject, which it must match.
    [string]$Publisher,

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

    if ($signing.Subject) {
        if ($Publisher -and $Publisher -ne $signing.Subject) {
            throw "-Publisher '$Publisher' must equal the signing certificate's subject '$($signing.Subject)', or Windows rejects the package."
        }

        $Publisher = $signing.Subject
    }
    elseif (-not $Publisher) {
        # Unsigned packages still need a well-formed publisher; the development one is as good as any.
        $Publisher = $script:DevCertificateSubject
    }

    if ($Publisher -notmatch '(^|,\s*)CN=') {
        throw "-Publisher '$Publisher' is not an X.500 distinguished name containing CN=."
    }

    & (Join-Path $PSScriptRoot 'Generate-AppAssets.ps1') -Verify

    $dotnet = Resolve-DotNet
    $makeAppx = Find-WindowsSdkTool -Name 'makeappx.exe'
    $makePri = Find-WindowsSdkTool -Name 'makepri.exe'
    $packageVersion = "$(Get-ProductVersion -Root $root).0"
    $msixPath = Get-MsixPackagePath -Root $root -RuntimeIdentifier $RuntimeIdentifier
    if (Test-Path -LiteralPath $msixPath) {
        Remove-Item -LiteralPath $msixPath -Force
    }

    $workDirectory = Get-ArtifactsPath -Root $root -ChildPath "publish\msix\$RuntimeIdentifier"
    $layoutDirectory = Join-Path $workDirectory 'layout'
    $priDirectory = Join-Path $workDirectory 'pri'
    Reset-Directory -Root $root -Path $workDirectory

    $symbolsDirectory = Get-SymbolsPath -Root $root -Configuration $Configuration -RuntimeIdentifier $RuntimeIdentifier -Package msix
    Publish-App -DotNet $dotnet -Root $root -Configuration $Configuration -RuntimeIdentifier $RuntimeIdentifier `
        -OutputDirectory $layoutDirectory -SymbolsDirectory $symbolsDirectory -Signing $signing

    # The app already publishes an Assets folder (its window and tray icon); the logos join it.
    $logos = @(Get-ChildItem -LiteralPath (Join-Path $root 'packaging\msix\Assets') -File)
    New-Item -ItemType Directory -Path (Join-Path $layoutDirectory 'Assets') -Force | Out-Null
    $logos | Copy-Item -Destination (Join-Path $layoutDirectory 'Assets')

    # Edit the template as XML rather than by text replacement, so values are escaped correctly.
    $manifest = New-Object System.Xml.XmlDocument
    $manifest.PreserveWhitespace = $true
    $manifest.Load((Join-Path $root 'packaging\msix\AppxManifest.xml'))
    $namespaces = New-Object System.Xml.XmlNamespaceManager($manifest.NameTable)
    $namespaces.AddNamespace('m', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
    $identity = $manifest.SelectSingleNode('/m:Package/m:Identity', $namespaces)
    $identity.SetAttribute('Version', $packageVersion)
    $identity.SetAttribute('Publisher', $Publisher)
    $identity.SetAttribute('ProcessorArchitecture', (Get-MsixArchitecture -RuntimeIdentifier $RuntimeIdentifier))
    if ($manifest.OuterXml -match '__[A-Z]+__') {
        throw "packaging\msix\AppxManifest.xml has a placeholder this script does not fill in: $($Matches[0])"
    }

    $layoutManifest = Join-Path $layoutDirectory 'AppxManifest.xml'
    $manifest.Save($layoutManifest)

    # resources.pri maps each logo's base name to its scale-* and targetsize-* files. Only the logos and
    # the manifest are indexed; indexing the whole layout would list every runtime DLL as a resource.
    New-Item -ItemType Directory -Path (Join-Path $priDirectory 'Assets') -Force | Out-Null
    $logos | Copy-Item -Destination (Join-Path $priDirectory 'Assets')
    Copy-Item -LiteralPath $layoutManifest -Destination $priDirectory
    $priConfig = Join-Path $workDirectory 'priconfig.xml'
    Invoke-NativeCommand -FilePath $makePri -Description 'Creating the resource index configuration' -Quiet -ArgumentList @(
        'createconfig', '/cf', $priConfig, '/dq', 'en-US', '/pv', '10.0.0', '/o')

    # The default configuration splits scale-200 candidates into their own .pri for bundle resource
    # packages. A single package never loads those, so keep every candidate in the one resources.pri.
    $config = New-Object System.Xml.XmlDocument
    $config.Load($priConfig)
    $packaging = $config.SelectSingleNode('/resources/packaging')
    if ($packaging) {
        [void]$packaging.ParentNode.RemoveChild($packaging)
    }

    $config.Save($priConfig)
    Invoke-NativeCommand -FilePath $makePri -Description 'Indexing the package resources' -Quiet -ArgumentList @(
        'new', '/pr', $priDirectory, '/cf', $priConfig, '/mn', (Join-Path $priDirectory 'AppxManifest.xml'),
        '/of', (Join-Path $layoutDirectory 'resources.pri'), '/o')

    # Packed and signed in the work folder; only a finished package is moved to the release path.
    $builtMsix = Join-Path $workDirectory (Split-Path -Leaf $msixPath)
    Invoke-NativeCommand -FilePath $makeAppx -Description 'Packing the MSIX' -Quiet -ArgumentList @(
        'pack', '/d', $layoutDirectory, '/p', $builtMsix, '/o')
    Invoke-CodeSigning -Signing $signing -Path $builtMsix -Description 'the MSIX package'
    Complete-Package -Path $builtMsix -Destination $msixPath

    Write-Host "MSIX package ready: $msixPath"
    if ($signing.Kind -eq 'Development') {
        Write-Host "Install it on this machine with scripts\Install-MSIX.ps1 (trusts $(Get-DevCertificatePath -Root $root))."
    }

    Write-SigningSummary -Signing $signing
}
finally {
    Pop-Location
}

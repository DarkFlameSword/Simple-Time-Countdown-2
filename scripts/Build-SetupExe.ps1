<#
.SYNOPSIS
    Builds the classic installer: artifacts\packages\SimpleTimeCountdown-Setup-<rid>.exe.

.DESCRIPTION
    Builds the portable package (see Publish-Portable.ps1), publishes the single-file installer with that
    zip appended as its payload, checks the payload really is in the executable and signs it (the
    signature then covers the payload too). With a certificate configured (see scripts\ReleaseCommon.ps1)
    both the installed app binaries and the installer are signed and timestamped; without one, the script
    warns that everything is unsigned.

.EXAMPLE
    .\scripts\Build-SetupExe.ps1

.EXAMPLE
    $env:TIMECOUNTDOWN_SIGNING_THUMBPRINT = '0123...ABCD'
    .\scripts\Build-SetupExe.ps1 -RequireSigning
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

function Assert-AppendedPayload {
    <#
    .SYNOPSIS
        Checks that the published installer carries exactly this build's payload, so an installer that can
        only report "Installer payload is missing" (or installs an older build) never reaches users.
    .NOTES
        The layout is written by the AppendInstallerPayload target in SimpleTimeCountdown.Setup.csproj and
        read by EmbeddedPayload.cs: [single-file setup][payload zip][zero padding][trailer], where the
        32-byte trailer is the payload offset (int64), the payload length (int64) and a 16-byte magic.
    #>
    param(
        [Parameter(Mandatory)][string]$Executable,
        [Parameter(Mandatory)][string]$Payload
    )

    $trailerLength = 32
    $magic = [Text.Encoding]::ASCII.GetBytes("STC-PAYLOAD-V1`0`0")
    $problem = "The installer publish did not append the payload $Payload to $Executable"

    $expectedHash = (Get-FileHash -LiteralPath $Payload -Algorithm SHA256).Hash
    $expectedLength = (Get-Item -LiteralPath $Payload).Length
    $stream = [IO.File]::OpenRead($Executable)
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        if ($stream.Length -lt $expectedLength + $trailerLength) {
            throw "$problem (the executable is smaller than the payload)."
        }

        $trailer = New-Object byte[] $trailerLength
        $stream.Position = $stream.Length - $trailerLength
        $read = 0
        while ($read -lt $trailerLength) {
            $count = $stream.Read($trailer, $read, $trailerLength - $read)
            if ($count -le 0) {
                throw "$problem (the trailer could not be read)."
            }

            $read += $count
        }

        $trailerMagic = New-Object byte[] $magic.Length
        [Array]::Copy($trailer, 16, $trailerMagic, 0, $magic.Length)
        if ([Convert]::ToBase64String($trailerMagic) -ne [Convert]::ToBase64String($magic)) {
            throw "$problem (no payload trailer at the end of the file)."
        }

        $payloadOffset = [BitConverter]::ToInt64($trailer, 0)
        $payloadLength = [BitConverter]::ToInt64($trailer, 8)
        if ($payloadLength -ne $expectedLength -or $payloadOffset -le 0 -or
            $payloadOffset + $payloadLength -gt $stream.Length - $trailerLength) {
            throw "$problem (the trailer describes $payloadLength bytes at offset $payloadOffset; the payload has $expectedLength)."
        }

        # Compare the bytes too: a leftover zip of the same size must not pass for this build's payload.
        $stream.Position = $payloadOffset
        $buffer = New-Object byte[] (1MB)
        $remaining = $payloadLength
        while ($remaining -gt 0) {
            $count = $stream.Read($buffer, 0, [int][Math]::Min($buffer.Length, $remaining))
            if ($count -le 0) {
                throw "$problem (the file ends inside the payload)."
            }

            [void]$sha256.TransformBlock($buffer, 0, $count, $null, 0)
            $remaining -= $count
        }

        [void]$sha256.TransformFinalBlock($buffer, 0, 0)
        $appendedHash = [BitConverter]::ToString($sha256.Hash).Replace('-', '')
        if ($appendedHash -ne $expectedHash) {
            throw "$problem (the appended bytes differ from it)."
        }
    }
    finally {
        $sha256.Dispose()
        $stream.Dispose()
    }
}

$root = Get-RepositoryRoot
Push-Location -LiteralPath $root
try {
    $signingParameters = Get-SigningParameters -BoundParameters $PSBoundParameters
    $signing = Resolve-CodeSigning -Root $root @signingParameters
    $dotnet = Resolve-DotNet

    # Remove the previous installer first, so a failed run cannot leave it looking like this run's output.
    $targetExe = Get-SetupExePath -Root $root -RuntimeIdentifier $RuntimeIdentifier
    if (Test-Path -LiteralPath $targetExe) {
        Remove-Item -LiteralPath $targetExe -Force
    }

    $portableZip = New-PortablePackage -DotNet $dotnet -Root $root -Configuration $Configuration `
        -RuntimeIdentifier $RuntimeIdentifier -Signing $signing

    # The Setup project appends its payload to the published executable. The zip goes to the project's
    # default payload path, so that path never holds an older build, and is also passed explicitly, so
    # the installer cannot pick up any other file should that default change.
    $payloadDirectory = Get-ArtifactsPath -Root $root -ChildPath 'installer-payload'
    $payloadZip = Join-Path $payloadDirectory 'TimeCountdown-portable.zip'
    Reset-Directory -Root $root -Path $payloadDirectory
    Copy-Item -LiteralPath $portableZip -Destination $payloadZip

    $setupProject = Join-Path $root 'src\SimpleTimeCountdown.Setup\SimpleTimeCountdown.Setup.csproj'
    $publishDirectory = Get-ArtifactsPath -Root $root -ChildPath "publish\setup\$RuntimeIdentifier"
    Reset-Directory -Root $root -Path $publishDirectory
    # Single-file and self-contained settings come from the project file, the one place they are set.
    Invoke-NativeCommand -FilePath $dotnet -Description 'Publishing the installer' -ArgumentList @(
        'publish', $setupProject, '--configuration', $Configuration, '--runtime', $RuntimeIdentifier,
        '--output', $publishDirectory, "-property:InstallerPayloadZip=$payloadZip", '--nologo')

    $builtExe = Join-Path $publishDirectory 'TimeCountdown.Setup.exe'
    if (-not (Test-Path -LiteralPath $builtExe)) {
        throw "The installer publish did not produce $builtExe."
    }

    Assert-AppendedPayload -Executable $builtExe -Payload $payloadZip
    Move-Symbols -Root $root -PublishDirectory $publishDirectory `
        -SymbolsDirectory (Get-SymbolsPath -Root $root -Configuration $Configuration -RuntimeIdentifier $RuntimeIdentifier -Package setup)

    # Signed where it was built; only a finished installer is moved to the release path.
    Invoke-CodeSigning -Signing $signing -Path $builtExe -Description 'the installer'
    Complete-Package -Path $builtExe -Destination $targetExe

    Write-Host "Portable package ready: $portableZip"
    Write-Host "Setup EXE ready: $targetExe"
    Write-SigningSummary -Signing $signing
}
finally {
    Pop-Location
}

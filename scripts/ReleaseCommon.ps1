<#
.SYNOPSIS
    Shared helpers for the release scripts in this folder.

.DESCRIPTION
    Dot-source this file from a release script; it is not an entry point. It finds the build tools,
    runs native commands with exit-code checking, reads the product version from Directory.Build.props,
    publishes the app payload (with its licence files) and signs files.

    Signing is configured the same way for every script, by parameters or by environment variables.
    Parameters win: when -CertificateThumbprint, -CertificatePath or -DevCertificate is passed, the
    TIMECOUNTDOWN_SIGNING_THUMBPRINT and TIMECOUNTDOWN_SIGNING_CERTIFICATE variables are ignored, so a
    certificate chosen on the command line never clashes with one configured in the environment. The
    password and the timestamp URL each fall back to their variable when their parameter is absent.

      -CertificateThumbprint / TIMECOUNTDOWN_SIGNING_THUMBPRINT
          A code-signing certificate in the CurrentUser or LocalMachine "My" store (this also covers
          hardware tokens). Preferred: no secret ever appears on a command line.
      -CertificatePath / TIMECOUNTDOWN_SIGNING_CERTIFICATE, with
      -CertificatePassword / TIMECOUNTDOWN_SIGNING_PASSWORD
          A .pfx file and its password. signtool receives the password on its command line.
      -TimestampUrl / TIMECOUNTDOWN_TIMESTAMP_URL
          RFC 3161 timestamp server; defaults to DigiCert's. A timestamp keeps signatures valid after
          the certificate expires, so release signatures are always timestamped.
      -DevCertificate
          Sign with a self-signed development certificate (created once per user, key not exportable).
          For local MSIX testing only: nothing signed with it may be distributed. Certificates made by
          earlier versions of these scripts, whose keys can be exported, are never reused.
      -RequireSigning
          Fail instead of producing unsigned output (use it for release builds).

    With none of these, the outputs are left unsigned and every script says so in a warning.
#>

Set-StrictMode -Version Latest

# Files built from this repository. The rest of the app payload is the .NET runtime, which Microsoft
# has already signed; signing it again would claim authorship of their binaries.
$script:FirstPartyAppBinaries = @('TimeCountdown.exe', 'TimeCountdown.dll')
$script:DevCertificateSubject = 'CN=TimeCountdownDev'
$script:DefaultTimestampUrl = 'http://timestamp.digicert.com'
$script:CodeSigningOid = '1.3.6.1.5.5.7.3.3'
$script:SigningParameterNames = @(
    'CertificateThumbprint', 'CertificatePath', 'CertificatePassword', 'TimestampUrl', 'DevCertificate', 'RequireSigning')

function Get-RepositoryRoot {
    return (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
}

function Get-ArtifactsPath {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$ChildPath
    )

    return Join-Path (Join-Path $Root 'artifacts') $ChildPath
}

function Get-PortableZipPath {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Configuration,
        [Parameter(Mandatory)][string]$RuntimeIdentifier
    )

    return Get-ArtifactsPath -Root $Root -ChildPath "packages\SimpleTimeCountdown-$Configuration-$RuntimeIdentifier-portable.zip"
}

function Get-SetupExePath {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$RuntimeIdentifier
    )

    return Get-ArtifactsPath -Root $Root -ChildPath "packages\SimpleTimeCountdown-Setup-$RuntimeIdentifier.exe"
}

function Get-MsixPackagePath {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$RuntimeIdentifier
    )

    $version = Get-ProductVersion -Root $Root
    return Get-ArtifactsPath -Root $Root -ChildPath "packages\SimpleTimeCountdown_$version.0_$RuntimeIdentifier.msix"
}

function Get-DevCertificatePath {
    param([Parameter(Mandatory)][string]$Root)

    return Get-ArtifactsPath -Root $Root -ChildPath 'certificates\TimeCountdownDev.cer'
}

function Get-SymbolsPath {
    <#
    .SYNOPSIS
        Where one package's debug symbols are kept: artifacts\symbols\<configuration>\<rid>\<package>.
    .NOTES
        One folder per build flavour and package, because the portable zip, the MSIX and Setup.exe can
        come from different runs; a crash dump must be matched with the symbols of its own binaries.
    #>
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Configuration,
        [Parameter(Mandatory)][string]$RuntimeIdentifier,
        [Parameter(Mandatory)][ValidateSet('portable', 'msix', 'setup')][string]$Package
    )

    return Get-ArtifactsPath -Root $Root -ChildPath "symbols\$Configuration\$RuntimeIdentifier\$Package"
}

function Get-MsixArchitecture {
    param([Parameter(Mandatory)][string]$RuntimeIdentifier)

    switch ($RuntimeIdentifier) {
        'win-x64' { return 'x64' }
        'win-x86' { return 'x86' }
        'win-arm64' { return 'arm64' }
    }

    throw "Unsupported runtime identifier '$RuntimeIdentifier'."
}

function Get-ProductVersion {
    <#
    .SYNOPSIS
        Reads the product version (major.minor.patch) from Directory.Build.props, its single source.
    #>
    param([Parameter(Mandatory)][string]$Root)

    $props = New-Object System.Xml.XmlDocument
    $props.Load((Join-Path $Root 'Directory.Build.props'))
    $node = $props.SelectSingleNode('/Project/PropertyGroup/VersionPrefix')
    $version = if ($node) { $node.InnerText.Trim() } else { '' }

    # File and MSIX versions are four 16-bit numbers; the fourth is always 0 for this product.
    $isValid = $version -match '^(\d{1,5})\.(\d{1,5})\.(\d{1,5})$' -and
        -not (@($Matches[1], $Matches[2], $Matches[3]) | Where-Object { [int]$_ -gt 65535 })
    if (-not $isValid) {
        throw "Directory.Build.props must set <VersionPrefix> to a literal major.minor.patch version (each part 0-65535); found '$version'."
    }

    return $version
}

function Invoke-NativeCommand {
    <#
    .SYNOPSIS
        Runs a native program and throws when it exits with a non-zero code.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [Parameter(Mandatory)][string]$Description,
        # Return the program's standard output instead of showing it.
        [switch]$PassThru,
        # Show the program's output only if it fails (for tools that list every file they touch).
        [switch]$Quiet
    )

    # Native programs report failure only through their exit code. Their stderr output must not turn
    # into a terminating PowerShell error (Windows PowerShell wraps it in ErrorRecords in some hosts),
    # so the preference is relaxed for this function's scope and the exit code alone decides.
    $ErrorActionPreference = 'Continue'
    $captured = $PassThru -or $Quiet
    if ($captured) {
        $output = @(& $FilePath @ArgumentList)
    }
    else {
        & $FilePath @ArgumentList | Out-Host
    }

    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        if ($captured) {
            # makepri writes UTF-16 to a pipe, which arrives here with a NUL after every character.
            $output | ForEach-Object { "$_" -replace "`0", '' } | Out-Host
        }

        throw "$Description failed with exit code $exitCode."
    }

    if ($PassThru) {
        return $output
    }
}

function Resolve-DotNet {
    <#
    .SYNOPSIS
        Returns a dotnet executable whose SDKs satisfy global.json: the one on PATH first, then the
        per-user install in %USERPROFILE%\.dotnet that dotnet-install.ps1 creates.
    .NOTES
        dotnet picks its SDK from the global.json above the *current directory*, so the release scripts
        run every dotnet command with the repository root as the current location.
    #>
    $candidates = @()
    $onPath = Get-Command 'dotnet' -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($onPath) {
        $candidates += $onPath.Source
    }

    $userInstall = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
    if ((Test-Path -LiteralPath $userInstall) -and $candidates -notcontains $userInstall) {
        $candidates += $userInstall
    }

    $ErrorActionPreference = 'Continue'
    foreach ($candidate in $candidates) {
        # 'dotnet --version' fails when none of the installed SDKs matches global.json.
        $sdkVersion = & $candidate --version 2>$null
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Using .NET SDK $sdkVersion from $candidate"
            return $candidate
        }
    }

    throw 'No .NET SDK matching global.json was found on PATH or in %USERPROFILE%\.dotnet. Install the SDK version that global.json pins.'
}

function Find-WindowsSdkTool {
    <#
    .SYNOPSIS
        Finds makeappx.exe, makepri.exe or signtool.exe: on PATH (for example in a Developer
        PowerShell), otherwise in the newest installed Windows SDK.
    #>
    param([Parameter(Mandatory)][string]$Name)

    $onPath = Get-Command $Name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($onPath) {
        return $onPath.Source
    }

    $programFiles = ${env:ProgramFiles(x86)}
    if (-not $programFiles) {
        $programFiles = $env:ProgramFiles
    }

    $kitsBin = Join-Path $programFiles 'Windows Kits\10\bin'
    if (Test-Path -LiteralPath $kitsBin) {
        $hostArchitecture = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }
        # The packaging and signing tools are backward compatible, so the newest SDK is always fine.
        $sdks = Get-ChildItem -LiteralPath $kitsBin -Directory |
            Where-Object { $_.Name -match '^\d+(\.\d+){3}$' } |
            Sort-Object { [version]$_.Name } -Descending
        foreach ($sdk in $sdks) {
            foreach ($architecture in @($hostArchitecture, 'x86')) {
                $candidate = Join-Path $sdk.FullName "$architecture\$Name"
                if (Test-Path -LiteralPath $candidate) {
                    return $candidate
                }
            }
        }
    }

    throw "$Name was not found on PATH or under $kitsBin. Install the Windows 10/11 SDK."
}

function Reset-Directory {
    <#
    .SYNOPSIS
        Deletes and recreates a folder under artifacts\, so nothing from an earlier run can ship.
    #>
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Path
    )

    # A recursive delete is only ever allowed inside the generated-output folder.
    $artifacts = [IO.Path]::GetFullPath((Join-Path $Root 'artifacts')).TrimEnd('\') + '\'
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($artifacts, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean '$fullPath': the release scripts only clean folders under $artifacts"
    }

    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }

    New-Item -ItemType Directory -Path $fullPath -Force | Out-Null
}

function Complete-Package {
    <#
    .SYNOPSIS
        Moves a finished package (built, checked and signed if a certificate is configured) to its release
        path in artifacts\packages. Scripts call it as their last step, so a run that fails while signing,
        verifying or writing never leaves an unsigned or partial file where a release is picked up.
    .NOTES
        Every package is built somewhere under artifacts\ as well, so this is a rename on one volume: the
        package appears complete or not at all.
    #>
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Destination
    )

    New-Item -ItemType Directory -Path (Split-Path -Parent $Destination) -Force | Out-Null
    Move-Item -LiteralPath $Path -Destination $Destination -Force
}

function Move-Symbols {
    <#
    .SYNOPSIS
        Moves the .pdb files out of a publish folder into an emptied symbols folder (see Get-SymbolsPath).
    .NOTES
        Symbols help with crash dumps but do not belong on users' machines. The folder is emptied first,
        so it never mixes these symbols with those of an earlier build.
    #>
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$PublishDirectory,
        [Parameter(Mandatory)][string]$SymbolsDirectory
    )

    Reset-Directory -Root $Root -Path $SymbolsDirectory
    Get-ChildItem -LiteralPath $PublishDirectory -Filter '*.pdb' -File -Recurse |
        Move-Item -Destination $SymbolsDirectory -Force
}

function Get-SigningParameters {
    <#
    .SYNOPSIS
        Picks the signing parameters out of a script's $PSBoundParameters, for splatting into
        Resolve-CodeSigning.
    #>
    param([Parameter(Mandatory)][System.Collections.IDictionary]$BoundParameters)

    $signingParameters = @{}
    foreach ($name in $script:SigningParameterNames) {
        if ($BoundParameters.ContainsKey($name)) {
            $signingParameters[$name] = $BoundParameters[$name]
        }
    }

    return $signingParameters
}

function Test-CodeSigningCertificate {
    param([Parameter(Mandatory)][System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate)

    foreach ($extension in $Certificate.Extensions) {
        if ($extension -is [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]) {
            foreach ($usage in $extension.EnhancedKeyUsages) {
                if ($usage.Value -eq $script:CodeSigningOid) {
                    return $true
                }
            }
        }
    }

    return $false
}

function Assert-SigningCertificate {
    param([Parameter(Mandatory)][System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate)

    $now = Get-Date
    if ($Certificate.NotBefore -gt $now -or $Certificate.NotAfter -lt $now) {
        throw "The signing certificate '$($Certificate.Subject)' is only valid from $($Certificate.NotBefore) to $($Certificate.NotAfter)."
    }

    if (-not $Certificate.HasPrivateKey) {
        throw "The private key of the signing certificate '$($Certificate.Subject)' is not available to this user."
    }

    if (-not (Test-CodeSigningCertificate -Certificate $Certificate)) {
        throw "The certificate '$($Certificate.Subject)' is not a code-signing certificate."
    }
}

function Test-NonExportablePrivateKey {
    <#
    .SYNOPSIS
        True only when the certificate's private key provably cannot be exported from this machine.
    #>
    param([Parameter(Mandatory)][System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate)

    $key = $null
    try {
        $key = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($Certificate)
        if ($key -is [System.Security.Cryptography.RSACng]) {
            $exportable = [System.Security.Cryptography.CngExportPolicies]::AllowExport -bor
                [System.Security.Cryptography.CngExportPolicies]::AllowPlaintextExport
            return ($key.Key.ExportPolicy -band $exportable) -eq 0
        }

        if ($key -is [System.Security.Cryptography.RSACryptoServiceProvider]) {
            return -not $key.CspKeyContainerInfo.Exportable
        }

        return $false
    }
    catch [System.Security.Cryptography.CryptographicException] {
        # A key this user cannot open is no use for signing either.
        return $false
    }
    finally {
        if ($key) {
            $key.Dispose()
        }
    }
}

function Write-LegacyDevCertificateWarning {
    <#
    .SYNOPSIS
        Tells the developer to delete a development certificate that an earlier version of these scripts
        created with an exportable key (and exported with a password that is public in this repository's
        history), including the trust Install-MSIX.ps1 gave it.
    #>
    param([Parameter(Mandatory)][System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate)

    $thumbprint = $Certificate.Thumbprint
    $removals = @("Remove-Item Cert:\CurrentUser\My\$thumbprint -DeleteKey")
    $what = 'it'
    foreach ($store in @('Cert:\CurrentUser\TrustedPeople', 'Cert:\LocalMachine\TrustedPeople')) {
        if (Test-Path -LiteralPath (Join-Path $store $thumbprint)) {
            $removals += "Remove-Item $store\$thumbprint"
            $what = 'it and the trust it was given'
        }
    }

    if ($removals -like 'Remove-Item Cert:\LocalMachine\*') {
        $what += ' (from an elevated PowerShell)'
    }

    Write-Warning ("Not reusing the development certificate $thumbprint, which an earlier version of these scripts created " +
        "with an exportable private key. Remove $($what): $($removals -join '; ')")
}

function Get-DevelopmentCertificate {
    <#
    .SYNOPSIS
        Returns this user's self-signed development certificate, creating it on first use, and exports
        its public part for Install-MSIX.ps1 to trust.
    #>
    param([Parameter(Mandatory)][string]$Root)

    # Earlier versions of these scripts exported the key next to the .cer, protected by a password that
    # is public in this repository's history: with it, anyone could sign code this machine trusts.
    $legacyPfx = Get-ArtifactsPath -Root $Root -ChildPath 'certificates\TimeCountdownDev.pfx'
    if (Test-Path -LiteralPath $legacyPfx) {
        Remove-Item -LiteralPath $legacyPfx -Force
        Write-Warning "Deleted $legacyPfx, a development signing key that an earlier version of these scripts exported with a publicly known password."
    }

    # Only a certificate whose key cannot leave this user's store is reused; the exportable ones those
    # earlier versions created are reported instead.
    $reusable = @()
    foreach ($candidate in Get-ChildItem -LiteralPath 'Cert:\CurrentUser\My') {
        if ($candidate.Subject -ne $script:DevCertificateSubject -or -not $candidate.HasPrivateKey) {
            continue
        }

        if (Test-NonExportablePrivateKey -Certificate $candidate) {
            $reusable += $candidate
        }
        else {
            Write-LegacyDevCertificateWarning -Certificate $candidate
        }
    }

    # Reuse the newest usable certificate rather than adding another one to the store on every run.
    $certificate = $reusable |
        Where-Object { $_.NotAfter -gt (Get-Date).AddDays(1) -and (Test-CodeSigningCertificate -Certificate $_) } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1

    if (-not $certificate) {
        # Non-exportable: the key never leaves this user's certificate store, so there is no .pfx file
        # or password that could leak. The code-signing EKU and basic constraints are what MSIX needs.
        $certificate = New-SelfSignedCertificate `
            -Type Custom `
            -Subject $script:DevCertificateSubject `
            -KeyUsage DigitalSignature `
            -KeyExportPolicy NonExportable `
            -FriendlyName 'Simple Time Countdown development signing (not for release)' `
            -CertStoreLocation 'Cert:\CurrentUser\My' `
            -NotAfter (Get-Date).AddYears(1) `
            -TextExtension @("2.5.29.37={text}$script:CodeSigningOid", '2.5.29.19={text}')
    }

    $cerPath = Get-DevCertificatePath -Root $Root
    New-Item -ItemType Directory -Path (Split-Path -Parent $cerPath) -Force | Out-Null
    Export-Certificate -Cert $certificate -FilePath $cerPath -Type CERT -Force | Out-Null
    return $certificate
}

function Resolve-CodeSigning {
    <#
    .SYNOPSIS
        Decides how this run signs its outputs (release certificate, development certificate or not at
        all). Scripts call it before building anything, so a misconfigured certificate fails in seconds
        instead of after a full publish.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Root,
        [string]$CertificateThumbprint,
        [string]$CertificatePath,
        [SecureString]$CertificatePassword,
        [string]$TimestampUrl,
        [switch]$DevCertificate,
        [switch]$RequireSigning
    )

    # A certificate chosen by parameter replaces the environment's choice as a whole, so a developer
    # profile or CI runner that sets one never turns an explicit -DevCertificate or thumbprint into a
    # "both configured" conflict.
    $fromParameters = $DevCertificate -or -not [string]::IsNullOrWhiteSpace($CertificateThumbprint) -or
        -not [string]::IsNullOrWhiteSpace($CertificatePath)
    if (-not $fromParameters) {
        $CertificateThumbprint = $env:TIMECOUNTDOWN_SIGNING_THUMBPRINT
        $CertificatePath = $env:TIMECOUNTDOWN_SIGNING_CERTIFICATE
    }

    $hasThumbprint = -not [string]::IsNullOrWhiteSpace($CertificateThumbprint)
    $hasPfx = -not [string]::IsNullOrWhiteSpace($CertificatePath)
    if ($hasThumbprint -and $hasPfx) {
        if ($fromParameters) {
            throw 'Pass either -CertificateThumbprint or -CertificatePath, not both.'
        }

        throw 'Set either TIMECOUNTDOWN_SIGNING_THUMBPRINT or TIMECOUNTDOWN_SIGNING_CERTIFICATE, not both.'
    }

    if ($DevCertificate -and ($hasThumbprint -or $hasPfx)) {
        throw '-DevCertificate cannot be combined with -CertificateThumbprint or -CertificatePath.'
    }

    if (-not $CertificatePassword -and $env:TIMECOUNTDOWN_SIGNING_PASSWORD) {
        # The secret already arrives as plain text (a CI secret); wrapping it keeps one code path.
        $CertificatePassword = ConvertTo-SecureString -String $env:TIMECOUNTDOWN_SIGNING_PASSWORD -AsPlainText -Force
    }

    if (-not $TimestampUrl) {
        $TimestampUrl = $env:TIMECOUNTDOWN_TIMESTAMP_URL
    }

    if (-not $TimestampUrl) {
        $TimestampUrl = $script:DefaultTimestampUrl
    }

    if ($hasThumbprint) {
        # Thumbprints copied from the certificate dialog carry spaces (and sometimes an invisible mark).
        $thumbprint = ($CertificateThumbprint -replace '[^0-9A-Fa-f]', '').ToUpperInvariant()
        if ($thumbprint.Length -ne 40) {
            throw "'$CertificateThumbprint' is not a certificate thumbprint (40 hexadecimal digits)."
        }

        $certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$thumbprint" -ErrorAction SilentlyContinue
        $storeArguments = @()
        if (-not $certificate) {
            $certificate = Get-Item -LiteralPath "Cert:\LocalMachine\My\$thumbprint" -ErrorAction SilentlyContinue
            $storeArguments = @('/sm')
        }

        if (-not $certificate) {
            throw "No certificate with thumbprint $thumbprint is in the CurrentUser or LocalMachine 'My' store."
        }

        Assert-SigningCertificate -Certificate $certificate
        Write-Host "Signing with '$($certificate.Subject)' (thumbprint $thumbprint), timestamped by $TimestampUrl."
        return [pscustomobject]@{
            Kind = 'Release'
            Subject = $certificate.Subject
            CertificateArguments = @('/sha1', $thumbprint) + $storeArguments
            Password = $null
            TimestampUrl = $TimestampUrl
        }
    }

    if ($hasPfx) {
        if (-not (Test-Path -LiteralPath $CertificatePath -PathType Leaf)) {
            throw "Signing certificate not found: $CertificatePath"
        }

        if (-not $CertificatePassword) {
            throw 'The .pfx password is missing: pass -CertificatePassword or set TIMECOUNTDOWN_SIGNING_PASSWORD.'
        }

        $pfxPath = (Resolve-Path -LiteralPath $CertificatePath).Path
        # EphemeralKeySet: inspect the certificate without persisting its private key on this machine.
        # A wrong password fails here, before anything is built.
        try {
            $certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
                $pfxPath, $CertificatePassword, [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
        }
        catch {
            $reason = if ($_.Exception.InnerException) { $_.Exception.InnerException.Message } else { $_.Exception.Message }
            throw "Cannot open the signing certificate $pfxPath ($($reason.Trim())). Check the password."
        }

        try {
            Assert-SigningCertificate -Certificate $certificate
            $subject = $certificate.Subject
        }
        finally {
            $certificate.Dispose()
        }

        Write-Host "Signing with '$subject' from $pfxPath, timestamped by $TimestampUrl."
        return [pscustomobject]@{
            Kind = 'Release'
            Subject = $subject
            CertificateArguments = @('/f', $pfxPath)
            Password = $CertificatePassword
            TimestampUrl = $TimestampUrl
        }
    }

    if ($DevCertificate) {
        if ($RequireSigning) {
            throw '-RequireSigning needs a release certificate; the self-signed development certificate is not trusted by Windows.'
        }

        $certificate = Get-DevelopmentCertificate -Root $Root
        Write-Warning ("DEVELOPMENT SIGNING: outputs are signed with the self-signed '$($certificate.Subject)' certificate " +
            "(thumbprint $($certificate.Thumbprint)). Windows and SmartScreen do not trust it and the signatures are not " +
            'timestamped. Use these builds for local testing only and never distribute them.')
        return [pscustomobject]@{
            Kind = 'Development'
            Subject = $certificate.Subject
            CertificateArguments = @('/sha1', $certificate.Thumbprint)
            Password = $null
            TimestampUrl = $null
        }
    }

    if ($RequireSigning) {
        throw ('Signing is required (-RequireSigning) but no certificate is configured. Pass -CertificateThumbprint or ' +
            '-CertificatePath, or set TIMECOUNTDOWN_SIGNING_THUMBPRINT or TIMECOUNTDOWN_SIGNING_CERTIFICATE.')
    }

    Write-Warning ('UNSIGNED BUILD: no signing certificate is configured, so the outputs of this run will not be signed. ' +
        'See scripts\ReleaseCommon.ps1 for the signing options.')
    return [pscustomobject]@{
        Kind = 'None'
        Subject = $null
        CertificateArguments = @()
        Password = $null
        TimestampUrl = $null
    }
}

function ConvertTo-PlainText {
    param([Parameter(Mandatory)][SecureString]$Value)

    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Value)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

function Invoke-CodeSigning {
    <#
    .SYNOPSIS
        Signs files as decided by Resolve-CodeSigning, or warns that they stay unsigned.
    #>
    param(
        [Parameter(Mandatory)][psobject]$Signing,
        [Parameter(Mandatory)][string[]]$Path,
        [Parameter(Mandatory)][string]$Description
    )

    $names = ($Path | ForEach-Object { Split-Path -Leaf $_ }) -join ', '
    if ($Signing.Kind -eq 'None') {
        Write-Warning "SIGNING SKIPPED: not signing $Description ($names)."
        return
    }

    $signTool = Find-WindowsSdkTool -Name 'signtool.exe'
    $arguments = @('sign', '/fd', 'SHA256') + $Signing.CertificateArguments
    if ($Signing.Password) {
        $arguments += @('/p', (ConvertTo-PlainText -Value $Signing.Password))
    }

    if ($Signing.TimestampUrl) {
        $arguments += @('/tr', $Signing.TimestampUrl, '/td', 'SHA256')
    }

    $arguments += $Path

    # Public timestamp servers fail transiently now and then; retry rather than lose a release build.
    $attempts = if ($Signing.TimestampUrl) { 3 } else { 1 }
    for ($attempt = 1; ; $attempt++) {
        try {
            Invoke-NativeCommand -FilePath $signTool -ArgumentList $arguments -Description "Signing $Description"
            break
        }
        catch {
            if ($attempt -ge $attempts) {
                throw
            }

            Write-Warning "$($_.Exception.Message) Retrying in 10 seconds."
            Start-Sleep -Seconds 10
        }
    }

    if ($Signing.Kind -eq 'Release') {
        # /pa verifies against the default Authenticode policy, so an untrusted chain fails the build here.
        Invoke-NativeCommand -FilePath $signTool -ArgumentList (@('verify', '/pa', '/q') + $Path) `
            -Description "Verifying the signature of $Description"
    }

    Write-Host "Signed $Description ($names)."
}

function Write-SigningSummary {
    param([Parameter(Mandatory)][psobject]$Signing)

    switch ($Signing.Kind) {
        'Release' {
            Write-Host "Signed with '$($Signing.Subject)' and timestamped by $($Signing.TimestampUrl)."
        }
        'Development' {
            Write-Warning "Signed with the DEVELOPMENT certificate '$($Signing.Subject)': for local testing only, do not distribute."
        }
        default {
            Write-Warning 'The outputs are UNSIGNED: do not distribute them. See scripts\ReleaseCommon.ps1 for the signing options.'
        }
    }
}

function Get-NuGetPackagesRoot {
    param([Parameter(Mandatory)][string]$DotNet)

    if ($env:NUGET_PACKAGES) {
        return $env:NUGET_PACKAGES
    }

    $output = Invoke-NativeCommand -FilePath $DotNet -ArgumentList @('nuget', 'locals', 'global-packages', '--list') `
        -Description 'Locating the NuGet global packages folder' -PassThru
    foreach ($line in $output) {
        if ($line -match '^\s*global-packages:\s*(.+?)\s*$') {
            return $Matches[1]
        }
    }

    throw "Could not read the NuGet global packages folder from: $output"
}

function Add-LicenseFiles {
    <#
    .SYNOPSIS
        Adds LICENSE.txt and THIRD-PARTY-NOTICES.txt to a published app folder.
    .DESCRIPTION
        The self-contained app redistributes the .NET runtime, whose MIT licence requires its notices to
        travel with it. THIRD-PARTY-NOTICES.txt is the repository's THIRD-PARTY-NOTICES.md followed,
        verbatim, by the notices file of every runtime pack this build actually bundled (read from the
        app's .deps.json), so the text always matches the shipped runtime version.
    #>
    param(
        [Parameter(Mandatory)][string]$DotNet,
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$PublishDirectory
    )

    Copy-Item -LiteralPath (Join-Path $Root 'LICENSE') -Destination (Join-Path $PublishDirectory 'LICENSE.txt')

    $notices = New-Object System.Text.StringBuilder
    [void]$notices.Append((Get-Content -LiteralPath (Join-Path $Root 'THIRD-PARTY-NOTICES.md') -Raw -Encoding UTF8))

    $depsFile = Get-ChildItem -LiteralPath $PublishDirectory -Filter '*.deps.json' -File | Select-Object -First 1
    if (-not $depsFile) {
        throw "No .deps.json in $PublishDirectory, so the bundled runtime packs cannot be identified."
    }

    $deps = Get-Content -LiteralPath $depsFile.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    $runtimePacks = @($deps.libraries.PSObject.Properties.Name | Where-Object { $_ -like 'runtimepack.*' } | Sort-Object)
    if ($runtimePacks.Count -gt 0) {
        $packagesRoot = Get-NuGetPackagesRoot -DotNet $DotNet
        foreach ($runtimePack in $runtimePacks) {
            # "runtimepack.Microsoft.NETCore.App.Runtime.win-x64/8.0.25" lives in
            # <packages>\microsoft.netcore.app.runtime.win-x64\8.0.25.
            $id, $version = $runtimePack.Substring('runtimepack.'.Length).Split('/')
            $packDirectory = Join-Path (Join-Path $packagesRoot $id.ToLowerInvariant()) $version
            if (-not (Test-Path -LiteralPath $packDirectory)) {
                throw "The runtime pack $id $version is not in $packagesRoot; its notices cannot be bundled."
            }

            $packNotices = Get-ChildItem -LiteralPath $packDirectory -File |
                Where-Object { $_.Name -in @('THIRD-PARTY-NOTICES.TXT', 'ThirdPartyNotices.txt') }
            foreach ($file in $packNotices) {
                $heading = "Notices shipped with $id $version ($($file.Name))"
                [void]$notices.AppendLine().AppendLine().AppendLine(('=' * $heading.Length)).AppendLine($heading)
                [void]$notices.AppendLine(('=' * $heading.Length)).AppendLine()
                [void]$notices.Append((Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8))
            }
        }
    }

    [IO.File]::WriteAllText(
        (Join-Path $PublishDirectory 'THIRD-PARTY-NOTICES.txt'), $notices.ToString(), (New-Object System.Text.UTF8Encoding($false)))
}

function Publish-App {
    <#
    .SYNOPSIS
        Publishes the WPF app into an empty folder and turns it into a shippable payload: symbols moved
        out, licence files added, first-party binaries signed.
    #>
    param(
        [Parameter(Mandatory)][string]$DotNet,
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Configuration,
        [Parameter(Mandatory)][string]$RuntimeIdentifier,
        [Parameter(Mandatory)][string]$OutputDirectory,
        [Parameter(Mandatory)][string]$SymbolsDirectory,
        [Parameter(Mandatory)][psobject]$Signing
    )

    Reset-Directory -Root $Root -Path $OutputDirectory
    $project = Join-Path $Root 'src\SimpleTimeCountdown.App\SimpleTimeCountdown.App.csproj'
    # Self-contained and single-file settings come from the project file, the one place they are set.
    Invoke-NativeCommand -FilePath $DotNet -Description 'Publishing the app' -ArgumentList @(
        'publish', $project, '--configuration', $Configuration, '--runtime', $RuntimeIdentifier,
        '--output', $OutputDirectory, '--nologo')

    $binaries = foreach ($name in $script:FirstPartyAppBinaries) {
        $binary = Join-Path $OutputDirectory $name
        if (-not (Test-Path -LiteralPath $binary)) {
            throw "The app publish did not produce $name."
        }

        $binary
    }

    Move-Symbols -Root $Root -PublishDirectory $OutputDirectory -SymbolsDirectory $SymbolsDirectory
    Add-LicenseFiles -DotNet $DotNet -Root $Root -PublishDirectory $OutputDirectory
    Invoke-CodeSigning -Signing $Signing -Path $binaries -Description 'the app binaries'
}

function New-ZipFromDirectory {
    <#
    .SYNOPSIS
        Zips a folder's contents with '/'-separated entry names (Compress-Archive in Windows PowerShell
        writes '\') and checks that the required entries are in it.
    .NOTES
        The zip is written under a temporary name and renamed only once it is complete and checked, so a
        failed or interrupted write never leaves a truncated zip at the destination.
    #>
    param(
        [Parameter(Mandatory)][string]$SourceDirectory,
        [Parameter(Mandatory)][string]$DestinationPath,
        [string[]]$RequiredEntries = @()
    )

    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

    $partialPath = "$DestinationPath.partial"
    New-Item -ItemType Directory -Path (Split-Path -Parent $DestinationPath) -Force | Out-Null
    if (Test-Path -LiteralPath $partialPath) {
        Remove-Item -LiteralPath $partialPath -Force
    }

    try {
        $source = [IO.Path]::GetFullPath($SourceDirectory).TrimEnd('\')
        $archive = [IO.Compression.ZipFile]::Open($partialPath, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($file in Get-ChildItem -LiteralPath $source -File -Recurse) {
                $entryName = $file.FullName.Substring($source.Length + 1).Replace('\', '/')
                [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                    $archive, $file.FullName, $entryName, [IO.Compression.CompressionLevel]::Optimal)
            }
        }
        finally {
            $archive.Dispose()
        }

        $archive = [IO.Compression.ZipFile]::OpenRead($partialPath)
        try {
            $entries = @($archive.Entries | ForEach-Object { $_.FullName })
        }
        finally {
            $archive.Dispose()
        }

        $missing = @($RequiredEntries | Where-Object { $entries -notcontains $_ })
        if ($missing.Count -gt 0) {
            throw "The zip for $DestinationPath is missing: $($missing -join ', ')"
        }

        Complete-Package -Path $partialPath -Destination $DestinationPath
    }
    finally {
        if (Test-Path -LiteralPath $partialPath) {
            Remove-Item -LiteralPath $partialPath -Force
        }
    }
}

function New-PortablePackage {
    <#
    .SYNOPSIS
        Publishes the app and zips it as the portable package (also the Setup.exe payload). Returns the
        zip's path.
    #>
    param(
        [Parameter(Mandatory)][string]$DotNet,
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Configuration,
        [Parameter(Mandatory)][string]$RuntimeIdentifier,
        [Parameter(Mandatory)][psobject]$Signing
    )

    # Remove the previous zip first, so a failed run cannot leave it looking like this run's output.
    $zipPath = Get-PortableZipPath -Root $Root -Configuration $Configuration -RuntimeIdentifier $RuntimeIdentifier
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    $publishDirectory = Get-ArtifactsPath -Root $Root -ChildPath "publish\portable\$RuntimeIdentifier"
    $symbolsDirectory = Get-SymbolsPath -Root $Root -Configuration $Configuration -RuntimeIdentifier $RuntimeIdentifier -Package portable
    Publish-App -DotNet $DotNet -Root $Root -Configuration $Configuration -RuntimeIdentifier $RuntimeIdentifier `
        -OutputDirectory $publishDirectory -SymbolsDirectory $symbolsDirectory -Signing $Signing

    New-ZipFromDirectory -SourceDirectory $publishDirectory -DestinationPath $zipPath `
        -RequiredEntries @('TimeCountdown.exe', 'LICENSE.txt', 'THIRD-PARTY-NOTICES.txt')
    return $zipPath
}

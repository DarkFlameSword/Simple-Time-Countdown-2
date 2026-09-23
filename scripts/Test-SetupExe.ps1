<#
.SYNOPSIS
    Smoke-tests the Setup.exe that Build-SetupExe.ps1 built: a silent install, a silent reinstall over it
    and a silent uninstall through the command Settings > Apps runs, checking the files, shortcuts and
    registry after each step.

.DESCRIPTION
    This really installs Simple Time Countdown for the current user and removes it again, so run it on a
    CI runner or a throwaway virtual machine. It refuses to start when the product is already installed
    for this user: the test install would take over that copy's shortcuts and Settings > Apps entry, and
    the uninstall would then remove them.

    After the install and the reinstall it checks that every file of the portable zip (the installer's
    payload) was installed intact, together with the uninstaller, the Start menu and desktop shortcuts and
    the Settings > Apps entry, and that no command is scheduled to delete the installation folder at the
    next sign-in. After the uninstall it waits for everything to be gone, including the uninstaller's
    temporary copy of itself, which is only removed after the uninstaller has exited.

    Every Setup run writes its log to artifacts\test-results\setup. If a step fails, the test uninstalls
    whatever it installed before it stops.

.EXAMPLE
    .\scripts\Build-SetupExe.ps1
    .\scripts\Test-SetupExe.ps1
#>
[CmdletBinding()]
param(
    # Must match the Build-SetupExe.ps1 run being tested: they locate its portable zip and Setup.exe.
    [string]$Configuration = 'Release',

    [ValidateSet('win-x64', 'win-x86', 'win-arm64')]
    [string]$RuntimeIdentifier = 'win-x64'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ReleaseCommon.ps1')

# These names mirror src\Shared\ProductConstants.cs and the installer's InstallerContext.
$productName = 'Simple Time Countdown'
$uninstallKeyPath = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\TimeCountdown'
$runOnceKeyPath = 'Software\Microsoft\Windows\CurrentVersion\RunOnce'
$uninstallerRelativePath = "Installer\$productName Setup.exe"
$installManifestRelativePath = 'Installer\install-manifest.json'

$setupTimeout = [TimeSpan]::FromMinutes(5)
# The uninstaller removes its own copy only after it has exited, retrying for up to a minute.
$removalTimeout = [TimeSpan]::FromSeconds(90)

$localAppData = [Environment]::GetFolderPath('LocalApplicationData')
$expectedInstallRoot = Join-Path $localAppData "Programs\$productName"
$legacyInstallRoot = Join-Path $localAppData 'Programs\Time Countdown'
$startMenuDirectory = Join-Path ([Environment]::GetFolderPath('Programs')) $productName
$startMenuShortcut = Join-Path $startMenuDirectory "$productName.lnk"
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) "$productName.lnk"

function Get-RegistryValue {
    param(
        [Parameter(Mandatory)][string]$KeyPath,
        [Parameter(Mandatory)][string]$Name
    )

    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($KeyPath)
    if (-not $key) {
        return $null
    }

    try {
        return $key.GetValue($Name)
    }
    finally {
        $key.Dispose()
    }
}

function Test-Registered {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($uninstallKeyPath)
    if (-not $key) {
        return $false
    }

    $key.Dispose()
    return $true
}

function Get-RunOnceCommands {
    <#
    .SYNOPSIS
        The current user's RunOnce commands, as name/command pairs.
    #>
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runOnceKeyPath)
    if (-not $key) {
        return
    }

    try {
        foreach ($name in $key.GetValueNames()) {
            [pscustomobject]@{ Name = $name; Command = [string]$key.GetValue($name) }
        }
    }
    finally {
        $key.Dispose()
    }
}

function Assert-NotInstalled {
    $existing = @()
    if (Test-Registered) {
        $existing += "the Settings > Apps entry HKCU\$uninstallKeyPath"
    }

    foreach ($path in @($expectedInstallRoot, $legacyInstallRoot, $startMenuDirectory, $desktopShortcut)) {
        if (Test-Path -LiteralPath $path) {
            $existing += $path
        }
    }

    if ($existing.Count -gt 0) {
        throw ("$productName is already installed for this user ($($existing -join '; ')). This test installs and " +
            'uninstalls the product for the current user, which would take over and then remove that copy. Run it ' +
            'on a CI runner or a throwaway virtual machine.')
    }
}

function Invoke-Setup {
    <#
    .SYNOPSIS
        Runs Setup (or the installed uninstaller) with a log in the test's log folder and throws, showing
        the log, unless it exits with 0 within the time limit.
    #>
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string]$Arguments,
        [Parameter(Mandatory)][string]$Step
    )

    $logPath = Join-Path $logDirectory "$Step.log"
    $commandLine = "$Arguments --log=`"$logPath`""
    Write-Host "${Step}: `"$FilePath`" $commandLine"

    # Process.Start rather than Start-Process: Windows PowerShell's Start-Process -PassThru can lose the
    # exit code of a process that ends before its handle is first read.
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo($FilePath, $commandLine)
    $startInfo.UseShellExecute = $false
    $process = [System.Diagnostics.Process]::Start($startInfo)
    try {
        if (-not $process.WaitForExit([int]$setupTimeout.TotalMilliseconds)) {
            $process.Kill()
            throw "The $Step did not finish within $($setupTimeout.TotalMinutes) minutes."
        }

        $exitCode = $process.ExitCode
    }
    finally {
        $process.Dispose()
    }

    if ($exitCode -ne 0) {
        if (Test-Path -LiteralPath $logPath) {
            Get-Content -LiteralPath $logPath -Encoding UTF8 | Out-Host
        }

        throw "The $Step failed with exit code $exitCode (log: $logPath)."
    }
}

function Get-QuietUninstallCommand {
    <#
    .SYNOPSIS
        Splits the QuietUninstallString that Settings > Apps and deployment tools run into the program
        and its arguments.
    #>
    $command = Get-RegistryValue -KeyPath $uninstallKeyPath -Name 'QuietUninstallString'
    if ($command -notmatch '^\s*"(?<file>[^"]+)"\s*(?<arguments>.*)$') {
        throw "The Settings > Apps entry has no usable QuietUninstallString: '$command'."
    }

    return [pscustomobject]@{ FilePath = $Matches['file']; Arguments = $Matches['arguments'].Trim() }
}

function Test-SamePath {
    param([string]$Left, [string]$Right)

    return [string]::Equals($Left.TrimEnd('\'), $Right.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)
}

function Get-ShortcutTarget {
    param([Parameter(Mandatory)][string]$Path)

    $shell = New-Object -ComObject WScript.Shell
    try {
        return $shell.CreateShortcut($Path).TargetPath
    }
    finally {
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
    }
}

function Assert-Installed {
    <#
    .SYNOPSIS
        Checks a complete installation in the default folder, as a silent install with default options
        leaves it.
    #>
    param(
        [Parameter(Mandatory)][string]$Step,
        [Parameter(Mandatory)][string]$PayloadZip,
        [Parameter(Mandatory)][string]$ProductVersion
    )

    if (-not (Test-Registered)) {
        throw "After the $Step there is no Settings > Apps entry (HKCU\$uninstallKeyPath)."
    }

    # Everything below is checked in the default folder, so a different one fails right here.
    $installRoot = [string](Get-RegistryValue -KeyPath $uninstallKeyPath -Name 'InstallLocation')
    if (-not (Test-SamePath $installRoot $expectedInstallRoot)) {
        throw "After the $Step, Settings > Apps names the install folder '$installRoot' instead of '$expectedInstallRoot'."
    }

    $problems = New-Object System.Collections.Generic.List[string]
    $displayVersion = [string](Get-RegistryValue -KeyPath $uninstallKeyPath -Name 'DisplayVersion')
    if ($displayVersion -notlike "$ProductVersion*") {
        $problems.Add("Settings > Apps shows version '$displayVersion'; Directory.Build.props says $ProductVersion.")
    }

    # Every payload file must be installed, and intact as far as its size tells.
    $archive = [IO.Compression.ZipFile]::OpenRead($PayloadZip)
    try {
        foreach ($entry in $archive.Entries) {
            if (-not $entry.Name) {
                continue
            }

            $installed = Get-Item -LiteralPath (Join-Path $expectedInstallRoot $entry.FullName.Replace('/', '\')) -ErrorAction SilentlyContinue
            if (-not $installed) {
                $problems.Add("$($entry.FullName) was not installed.")
            }
            elseif ($installed.Length -ne $entry.Length) {
                $problems.Add("$($entry.FullName) was installed with $($installed.Length) bytes instead of $($entry.Length).")
            }
        }
    }
    finally {
        $archive.Dispose()
    }

    foreach ($relativePath in @($uninstallerRelativePath, $installManifestRelativePath)) {
        if (-not (Test-Path -LiteralPath (Join-Path $expectedInstallRoot $relativePath) -PathType Leaf)) {
            $problems.Add("$relativePath is missing from the installation.")
        }
    }

    $appPath = Join-Path $expectedInstallRoot 'TimeCountdown.exe'
    foreach ($shortcut in @($startMenuShortcut, $desktopShortcut)) {
        if (-not (Test-Path -LiteralPath $shortcut -PathType Leaf)) {
            $problems.Add("The shortcut $shortcut is missing.")
        }
        elseif (-not (Test-SamePath (Get-ShortcutTarget -Path $shortcut) $appPath)) {
            $problems.Add("The shortcut $shortcut does not start $appPath.")
        }
    }

    # Setup 2.0 left a sign-in command that deleted a reinstalled copy; a scheduled cleanup may only ever
    # remove one of Setup's own uniquely named work folders, never the installation itself.
    $rootPattern = '(?i)\b(rd|rmdir)\s+/s\s+/q\s+"' + [regex]::Escape($expectedInstallRoot.TrimEnd('\')) + '\\?"'
    foreach ($runOnce in Get-RunOnceCommands) {
        if ($runOnce.Command -match $rootPattern) {
            $problems.Add("The RunOnce command '$($runOnce.Name)' would delete the installation at the next sign-in: $($runOnce.Command)")
        }
    }

    if ($problems.Count -gt 0) {
        throw "After the ${Step}:`n  $($problems -join "`n  ")"
    }

    Write-Host "The $Step left a complete installation in $expectedInstallRoot."
}

function Get-UninstallLeftovers {
    param([Parameter(Mandatory)][datetime]$StartedAtUtc)

    if (Test-Registered) {
        "the Settings > Apps entry HKCU\$uninstallKeyPath"
    }

    foreach ($path in @($expectedInstallRoot, $startMenuDirectory, $desktopShortcut)) {
        if (Test-Path -LiteralPath $path) {
            $path
        }
    }

    foreach ($runOnce in Get-RunOnceCommands) {
        if ($runOnce.Command.IndexOf($expectedInstallRoot, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            "the RunOnce command '$($runOnce.Name)'"
        }
    }

    # The installed uninstaller runs the uninstall from a copy in %TEMP%\stc-uninstall-<id>; that copy
    # moves the installed uninstaller into a second stc-uninstall-<id> folder. Both are removed once
    # their processes have exited.
    Get-ChildItem -LiteralPath ([IO.Path]::GetTempPath()) -Directory -Filter 'stc-uninstall-*' -ErrorAction SilentlyContinue |
        Where-Object { $_.CreationTimeUtc -ge $StartedAtUtc } |
        ForEach-Object { $_.FullName }
}

function Assert-Uninstalled {
    param([Parameter(Mandatory)][datetime]$StartedAtUtc)

    $deadline = [DateTime]::UtcNow + $removalTimeout
    do {
        $leftovers = @(Get-UninstallLeftovers -StartedAtUtc $StartedAtUtc)
        if ($leftovers.Count -eq 0) {
            Write-Host 'The uninstall removed the installation, its shortcuts, its Settings > Apps entry and its temporary files.'
            return
        }

        Start-Sleep -Seconds 2
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "$($removalTimeout.TotalSeconds) seconds after the uninstall, these are still present:`n  $($leftovers -join "`n  ")"
}

$root = Get-RepositoryRoot
$setupExe = Get-SetupExePath -Root $root -RuntimeIdentifier $RuntimeIdentifier
$payloadZip = Get-PortableZipPath -Root $root -Configuration $Configuration -RuntimeIdentifier $RuntimeIdentifier
foreach ($path in @($setupExe, $payloadZip)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "$path was not found. Build it with scripts\Build-SetupExe.ps1 -Configuration $Configuration -RuntimeIdentifier $RuntimeIdentifier first."
    }
}

Assert-NotInstalled
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$productVersion = Get-ProductVersion -Root $root
$logDirectory = Get-ArtifactsPath -Root $root -ChildPath 'test-results\setup'
Reset-Directory -Root $root -Path $logDirectory
$startedAtUtc = [DateTime]::UtcNow

$passed = $false
try {
    Invoke-Setup -FilePath $setupExe -Arguments '--silent' -Step 'install'
    Assert-Installed -Step 'install' -PayloadZip $payloadZip -ProductVersion $productVersion

    # Installing again over the same version is how users repair an installation; it must leave a
    # complete one behind.
    Invoke-Setup -FilePath $setupExe -Arguments '--silent' -Step 'reinstall'
    Assert-Installed -Step 'reinstall' -PayloadZip $payloadZip -ProductVersion $productVersion

    $uninstall = Get-QuietUninstallCommand
    if (-not (Test-SamePath $uninstall.FilePath (Join-Path $expectedInstallRoot $uninstallerRelativePath))) {
        throw "The QuietUninstallString runs '$($uninstall.FilePath)', not the installed uninstaller."
    }

    Invoke-Setup -FilePath $uninstall.FilePath -Arguments $uninstall.Arguments -Step 'uninstall'
    Assert-Uninstalled -StartedAtUtc $startedAtUtc
    $passed = $true
    Write-Host "Setup smoke test passed. Logs: $logDirectory"
}
finally {
    # Leave the machine as it was found, as far as a failed test allows.
    if (-not $passed -and (Test-Registered)) {
        Write-Warning 'The smoke test failed; uninstalling what it installed.'
        try {
            $cleanup = Get-QuietUninstallCommand
            Invoke-Setup -FilePath $cleanup.FilePath -Arguments $cleanup.Arguments -Step 'cleanup-uninstall'
        }
        catch {
            Write-Warning "Could not uninstall the test installation: $($_.Exception.Message)"
        }
    }
}

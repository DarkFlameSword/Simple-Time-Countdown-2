<#
.SYNOPSIS
    Builds the app icon and the MSIX logos from the pocket-watch brand masters.

.DESCRIPTION
    The design source is src\SimpleTimeCountdown.App\Assets\AppIcon.svg. Its raster exports in the same
    folder, AppIcon-16/32/48/64/128/256/512/1024.png, are the masters this script works from. When the
    artwork changes, re-export those PNGs from the SVG and run this script with -Force.

      src\SimpleTimeCountdown.App\Assets\AppIcon.ico
          The 16-256 px exports combined into one multi-size icon (exe, shortcuts, tray, installer).
      packaging\msix\Assets\*.png
          The MSIX logos at scale-100 and scale-200, plus the Square44x44Logo target sizes the taskbar
          and Start menu use. A size with an exact export uses that file unchanged; every other size is
          resampled from AppIcon-1024.png. The wide tile and the splash screen set the icon on Paper
          (#F0E6D2), the background colour AppxManifest.xml declares for them.

    Existing files are kept unless -Force is given, so hand-tuned assets are never overwritten by
    accident. -Verify changes nothing: it checks that every output exists at the right pixel size and
    that packaging\msix\Assets holds nothing else (an unqualified logo such as StoreLogo.png would
    clash with its scale-* variants in resources.pri).

.EXAMPLE
    .\scripts\Generate-AppAssets.ps1 -Verify
#>
[CmdletBinding(DefaultParameterSetName = 'Generate')]
param(
    # Repository root to read the masters from and write the outputs to; defaults to this repository.
    [string]$Root,

    [Parameter(ParameterSetName = 'Generate')]
    [switch]$Force,

    [Parameter(ParameterSetName = 'Verify')]
    [switch]$Verify
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = Join-Path $PSScriptRoot '..'
}

$Root = (Resolve-Path -LiteralPath $Root).Path
$brandDirectory = Join-Path $Root 'src\SimpleTimeCountdown.App\Assets'
$msixDirectory = Join-Path $Root 'packaging\msix\Assets'
$iconPath = Join-Path $brandDirectory 'AppIcon.ico'

$masterSizes = @(16, 32, 48, 64, 128, 256, 512, 1024)
$iconFrameSizes = @(16, 32, 48, 64, 128, 256)
# The Casebook "Paper" token; AppxManifest.xml uses the same colour behind the tiles and splash screen.
$paperColor = [System.Drawing.ColorTranslator]::FromHtml('#F0E6D2')
# Share of the canvas height the icon takes on the wide tile and splash screen.
$bannerIconScale = 0.8

function New-AssetSpec {
    param(
        [string]$Name,
        [int]$Width,
        [int]$Height = $Width,
        [switch]$Banner
    )

    [pscustomobject]@{ Name = $Name; Width = $Width; Height = $Height; Banner = [bool]$Banner }
}

$msixAssets = @(
    New-AssetSpec 'Square44x44Logo.scale-100.png' 44
    New-AssetSpec 'Square44x44Logo.scale-200.png' 88
    New-AssetSpec 'Square150x150Logo.scale-100.png' 150
    New-AssetSpec 'Square150x150Logo.scale-200.png' 300
    New-AssetSpec 'StoreLogo.scale-100.png' 50
    New-AssetSpec 'StoreLogo.scale-200.png' 100
    New-AssetSpec 'Wide310x150Logo.scale-100.png' 310 150 -Banner
    New-AssetSpec 'Wide310x150Logo.scale-200.png' 620 300 -Banner
    New-AssetSpec 'SplashScreen.scale-100.png' 620 300 -Banner
    New-AssetSpec 'SplashScreen.scale-200.png' 1240 600 -Banner

    # The taskbar, Start and Alt+Tab pick these by pixel size. The icon carries its own paper plate, so
    # the "unplated" variants (drawn without the accent-colour plate behind them) use the same art.
    foreach ($size in 16, 24, 32, 48, 256) {
        New-AssetSpec "Square44x44Logo.targetsize-$size.png" $size
        New-AssetSpec "Square44x44Logo.targetsize-${size}_altform-unplated.png" $size
    }
)

function Get-MasterPath {
    param([int]$Size)

    return Join-Path $brandDirectory "AppIcon-$Size.png"
}

function Get-ImageSize {
    param([string]$Path)

    $image = [System.Drawing.Image]::FromFile($Path)
    try {
        return "$($image.Width)x$($image.Height)"
    }
    finally {
        $image.Dispose()
    }
}

function Get-IconFrameSizes {
    param([string]$Path)

    # ICONDIR: reserved, type, image count; then one 16-byte ICONDIRENTRY per image, whose first byte is
    # the width (0 meaning 256).
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 6 -or [BitConverter]::ToUInt16($bytes, 2) -ne 1) {
        return @()
    }

    $count = [BitConverter]::ToUInt16($bytes, 4)
    return @(for ($i = 0; $i -lt $count; $i++) {
        $width = $bytes[6 + 16 * $i]
        if ($width -eq 0) { 256 } else { [int]$width }
    })
}

function Write-IconFile {
    <#
    .SYNOPSIS
        Writes a multi-size .ico whose images are the given PNG files, stored as PNG (Windows Vista+).
    #>
    param(
        [string]$Path,
        [int[]]$Sizes
    )

    $frames = New-Object 'System.Collections.Generic.List[byte[]]'
    foreach ($size in $Sizes) {
        $frames.Add([IO.File]::ReadAllBytes((Get-MasterPath $size)))
    }

    $stream = [IO.File]::Create($Path)
    try {
        $writer = New-Object System.IO.BinaryWriter($stream)
        # ICONDIR: reserved, type 1 = icon, image count.
        $writer.Write([UInt16]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]$frames.Count)

        $offset = 6 + 16 * $frames.Count
        for ($i = 0; $i -lt $frames.Count; $i++) {
            # ICONDIRENTRY: width, height (0 means 256), palette size, reserved, colour planes, bits per
            # pixel, data size, data offset.
            $dimension = if ($Sizes[$i] -ge 256) { 0 } else { $Sizes[$i] }
            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([UInt16]1)
            $writer.Write([UInt16]32)
            $writer.Write([UInt32]$frames[$i].Length)
            $writer.Write([UInt32]$offset)
            $offset += $frames[$i].Length
        }

        foreach ($frame in $frames) {
            $writer.Write($frame)
        }

        $writer.Flush()
    }
    finally {
        $stream.Dispose()
    }
}

function New-PaperBackedMaster {
    <#
    .SYNOPSIS
        Returns the master composited onto Paper. The icon's plate is Paper too, so the result is an
        opaque image with no transparent edge for the resampling filter to ring on.
    #>
    param([System.Drawing.Image]$Master)

    $flattened = New-Object System.Drawing.Bitmap($Master.Width, $Master.Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($flattened)
    try {
        $graphics.Clear($paperColor)
        $graphics.DrawImage($Master, 0, 0, $Master.Width, $Master.Height)
    }
    finally {
        $graphics.Dispose()
    }

    return $flattened
}

function New-ResampledBitmap {
    <#
    .SYNOPSIS
        Draws the source, resized to IconSize and centred, on a Width x Height canvas.
    #>
    param(
        [System.Drawing.Image]$Source,
        [int]$Width,
        [int]$Height,
        [int]$IconSize,
        [System.Drawing.Color]$Background,
        [System.Drawing.Drawing2D.InterpolationMode]$Interpolation
    )

    $bitmap = New-Object System.Drawing.Bitmap($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $attributes = New-Object System.Drawing.Imaging.ImageAttributes
    try {
        # The high-quality modes prefilter when shrinking, so a 1024 px master stays clean at 24 px.
        $graphics.InterpolationMode = $Interpolation
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        # Mirror the source at its border instead of sampling transparent black beyond it.
        $attributes.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)
        $graphics.Clear($Background)
        $destination = New-Object System.Drawing.Rectangle(
            [int][Math]::Floor(($Width - $IconSize) / 2), [int][Math]::Floor(($Height - $IconSize) / 2), $IconSize, $IconSize)
        $graphics.DrawImage($Source, $destination, 0, 0, $Source.Width, $Source.Height, [System.Drawing.GraphicsUnit]::Pixel, $attributes)
    }
    finally {
        $attributes.Dispose()
        $graphics.Dispose()
    }

    return $bitmap
}

function Copy-AlphaChannel {
    param(
        [System.Drawing.Bitmap]$From,
        [System.Drawing.Bitmap]$To
    )

    $bounds = New-Object System.Drawing.Rectangle(0, 0, $To.Width, $To.Height)
    $format = [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
    $fromData = $From.LockBits($bounds, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, $format)
    $toData = $To.LockBits($bounds, [System.Drawing.Imaging.ImageLockMode]::ReadWrite, $format)
    try {
        $length = $toData.Stride * $To.Height
        $fromBytes = New-Object byte[] $length
        $toBytes = New-Object byte[] $length
        [Runtime.InteropServices.Marshal]::Copy($fromData.Scan0, $fromBytes, 0, $length)
        [Runtime.InteropServices.Marshal]::Copy($toData.Scan0, $toBytes, 0, $length)
        # Pixels are stored B, G, R, A: every fourth byte is alpha.
        for ($i = 3; $i -lt $length; $i += 4) {
            $toBytes[$i] = $fromBytes[$i]
        }

        [Runtime.InteropServices.Marshal]::Copy($toBytes, 0, $toData.Scan0, $length)
    }
    finally {
        $From.UnlockBits($fromData)
        $To.UnlockBits($toData)
    }
}

function Save-ResampledAsset {
    param(
        [System.Drawing.Image]$Master,
        [System.Drawing.Image]$PaperMaster,
        [psobject]$Spec,
        [string]$Path
    )

    $bicubic = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    if ($Spec.Banner) {
        $bitmap = New-ResampledBitmap -Source $PaperMaster -Width $Spec.Width -Height $Spec.Height `
            -IconSize ([int][Math]::Round($Spec.Height * $bannerIconScale)) -Background $paperColor -Interpolation $bicubic
    }
    else {
        # Bicubic keeps the dial crisp but overshoots along a transparent edge, leaving a light rim around
        # the plate that shows on a dark taskbar. So the colour comes from the opaque paper-backed master
        # (bicubic) and the transparency from the original master (bilinear, which cannot overshoot).
        $bitmap = New-ResampledBitmap -Source $PaperMaster -Width $Spec.Width -Height $Spec.Height `
            -IconSize $Spec.Width -Background ([System.Drawing.Color]::Transparent) -Interpolation $bicubic
        $alpha = New-ResampledBitmap -Source $Master -Width $Spec.Width -Height $Spec.Height `
            -IconSize $Spec.Width -Background ([System.Drawing.Color]::Transparent) `
            -Interpolation ([System.Drawing.Drawing2D.InterpolationMode]::HighQualityBilinear)
        try {
            Copy-AlphaChannel -From $alpha -To $bitmap
        }
        finally {
            $alpha.Dispose()
        }
    }

    try {
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bitmap.Dispose()
    }
}

function Get-UnexpectedMsixFiles {
    if (-not (Test-Path -LiteralPath $msixDirectory)) {
        return @()
    }

    $expectedNames = @($msixAssets | ForEach-Object { $_.Name })
    return @(Get-ChildItem -LiteralPath $msixDirectory -File | Where-Object { $expectedNames -notcontains $_.Name })
}

if ($Verify) {
    $problems = New-Object System.Collections.Generic.List[string]

    if (-not (Test-Path -LiteralPath $iconPath)) {
        $problems.Add("missing $iconPath")
    }
    elseif (((Get-IconFrameSizes $iconPath) -join ',') -ne ($iconFrameSizes -join ',')) {
        $problems.Add("$iconPath must hold the $($iconFrameSizes -join '/') px images")
    }

    foreach ($spec in $msixAssets) {
        $path = Join-Path $msixDirectory $spec.Name
        if (-not (Test-Path -LiteralPath $path)) {
            $problems.Add("missing $path")
        }
        elseif ((Get-ImageSize $path) -ne "$($spec.Width)x$($spec.Height)") {
            $problems.Add("$path must be $($spec.Width)x$($spec.Height) px")
        }
    }

    foreach ($file in Get-UnexpectedMsixFiles) {
        $problems.Add("unexpected $($file.FullName) (not produced by this script; remove it)")
    }

    if ($problems.Count -gt 0) {
        throw ("Brand assets are incomplete. Run scripts\Generate-AppAssets.ps1 to create missing files.`n  " + ($problems -join "`n  "))
    }

    Write-Host "All $($msixAssets.Count + 1) brand assets are present."
    return
}

foreach ($size in $masterSizes) {
    $masterPath = Get-MasterPath $size
    if (-not (Test-Path -LiteralPath $masterPath)) {
        throw "Brand master missing: $masterPath. Export it from AppIcon.svg."
    }

    if ((Get-ImageSize $masterPath) -ne "${size}x${size}") {
        throw "$masterPath must be exactly $size x $size px."
    }
}

New-Item -ItemType Directory -Path $msixDirectory -Force | Out-Null
$written = 0
$kept = 0

if ($Force -or -not (Test-Path -LiteralPath $iconPath)) {
    Write-IconFile -Path $iconPath -Sizes $iconFrameSizes
    $written++
}
else {
    $kept++
}

$master = [System.Drawing.Image]::FromFile((Get-MasterPath 1024))
$paperMaster = New-PaperBackedMaster -Master $master
try {
    foreach ($spec in $msixAssets) {
        $path = Join-Path $msixDirectory $spec.Name
        if (-not $Force -and (Test-Path -LiteralPath $path)) {
            $kept++
            continue
        }

        if (-not $spec.Banner -and $masterSizes -contains $spec.Width) {
            Copy-Item -LiteralPath (Get-MasterPath $spec.Width) -Destination $path -Force
        }
        else {
            Save-ResampledAsset -Master $master -PaperMaster $paperMaster -Spec $spec -Path $path
        }

        $written++
    }
}
finally {
    $paperMaster.Dispose()
    $master.Dispose()
}

foreach ($file in Get-UnexpectedMsixFiles) {
    Write-Warning "$($file.FullName) is not produced by this script and would clash with the generated logos in resources.pri; remove it."
}

Write-Host "Brand assets: $written written, $kept kept (pass -Force to rebuild existing files)."

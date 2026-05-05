[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# Idempotent by default: if every output asset already exists, do nothing. Pass -Force
# to regenerate. This protects user-supplied icons from being overwritten on every publish.

if ([string]::IsNullOrWhiteSpace($Root)) {
    $scriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $PSCommandPath }
    $Root = (Resolve-Path (Join-Path $scriptRoot '..')).Path
}

Add-Type -AssemblyName System.Drawing

$assetDir = Join-Path $Root 'src\SimpleTimeCountdown.App\Assets'
$msixAssetDir = Join-Path $Root 'packaging\msix\Assets'

New-Item -ItemType Directory -Force -Path $assetDir | Out-Null
New-Item -ItemType Directory -Force -Path $msixAssetDir | Out-Null

function New-RoundedPath {
    param(
        [System.Drawing.RectangleF]$Bounds,
        [float]$Radius
    )

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $maxRadius = [Math]::Max(0.5, [Math]::Min($Bounds.Width, $Bounds.Height) / 2)
    $Radius = [Math]::Min($Radius, $maxRadius)
    $diameter = $Radius * 2
    $path.AddArc($Bounds.X, $Bounds.Y, $diameter, $diameter, 180, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Y, $diameter, $diameter, 270, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($Bounds.X, $Bounds.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function Draw-AppMark {
    param(
        [System.Drawing.Graphics]$Graphics,
        [float]$X,
        [float]$Y,
        [float]$Size
    )

    $cardRect = [System.Drawing.RectangleF]::new($X, $Y, $Size, $Size)
    $cardRadius = [Math]::Round($Size * 0.22)
    $shadowRect = [System.Drawing.RectangleF]::new($X + ($Size * 0.03), $Y + ($Size * 0.05), $Size, $Size)

    $shadowPath = New-RoundedPath -Bounds $shadowRect -Radius $cardRadius
    $cardPath = New-RoundedPath -Bounds $cardRect -Radius $cardRadius

    $shadowBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(48, 47, 105, 176))
    $cardBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(232, 252, 254, 255))
    $cardStroke = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(245, 255, 255, 255), [Math]::Max(2, $Size * 0.018))

    $Graphics.FillPath($shadowBrush, $shadowPath)
    $Graphics.FillPath($cardBrush, $cardPath)
    $Graphics.DrawPath($cardStroke, $cardPath)

    $ringRect = [System.Drawing.RectangleF]::new(
        $X + ($Size * 0.19),
        $Y + ($Size * 0.18),
        $Size * 0.62,
        $Size * 0.62)

    $ringPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(38, 91, 156), [Math]::Max(4, $Size * 0.075))
    $ringPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $ringPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $Graphics.DrawArc($ringPen, $ringRect, 135, 270)

    $accentPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 163, 42), [Math]::Max(4, $Size * 0.085))
    $accentPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $accentPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $Graphics.DrawArc($accentPen, $ringRect, -20, 108)

    $centerX = $ringRect.X + ($ringRect.Width / 2)
    $centerY = $ringRect.Y + ($ringRect.Height / 2)
    $hourPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(27, 66, 112), [Math]::Max(4, $Size * 0.05))
    $hourPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $hourPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $Graphics.DrawLine($hourPen, $centerX, $centerY, $centerX, $centerY - ($ringRect.Height * 0.20))
    $Graphics.DrawLine($hourPen, $centerX, $centerY, $centerX + ($ringRect.Width * 0.18), $centerY + ($ringRect.Height * 0.07))

    $centerBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(27, 66, 112))
    $Graphics.FillEllipse($centerBrush, $centerX - ($Size * 0.03), $centerY - ($Size * 0.03), $Size * 0.06, $Size * 0.06)

    $barRect = [System.Drawing.RectangleF]::new(
        $X + ($Size * 0.17),
        $Y + ($Size * 0.79),
        $Size * 0.66,
        $Size * 0.08)
    $barPath = New-RoundedPath -Bounds $barRect -Radius ([Math]::Round($barRect.Height / 2))
    $barBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 163, 42))
    $Graphics.FillPath($barBrush, $barPath)

    $ringPen.Dispose()
    $accentPen.Dispose()
    $hourPen.Dispose()
    $centerBrush.Dispose()
    $barBrush.Dispose()
    $cardStroke.Dispose()
    $cardBrush.Dispose()
    $shadowBrush.Dispose()
    $barPath.Dispose()
    $cardPath.Dispose()
    $shadowPath.Dispose()
}

function New-AppBitmap {
    param(
        [int]$Size,
        [string]$OutputPath,
        [switch]$Wide
    )

    if ($Wide) {
        $width = 310
        $height = 150
    }
    else {
        $width = $Size
        $height = $Size
    }

    $bitmap = New-Object System.Drawing.Bitmap($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

    if ($Wide) {
        $graphics.Clear([System.Drawing.Color]::FromArgb(234, 244, 255))
        $backgroundRect = [System.Drawing.RectangleF]::new(0, 0, $width, $height)
        $gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            $backgroundRect,
            [System.Drawing.Color]::FromArgb(246, 250, 255),
            [System.Drawing.Color]::FromArgb(185, 220, 255),
            45.0)
        $graphics.FillRectangle($gradient, $backgroundRect)

        $glowBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(62, 255, 255, 255))
        $graphics.FillEllipse($glowBrush, -40, -18, $width * 0.55, $height * 0.65)
        $graphics.FillEllipse($glowBrush, $width * 0.42, $height * 0.48, $width * 0.42, $height * 0.42)

        Draw-AppMark -Graphics $graphics -X 24 -Y 22 -Size 104

        $titleFont = New-Object System.Drawing.Font('Segoe UI Semibold', 22, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
        $subtitleFont = New-Object System.Drawing.Font('Segoe UI', 10.5, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
        $titleBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(25, 73, 122))
        $subtitleBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(77, 113, 149))

        $graphics.DrawString('Simple Time', $titleFont, $titleBrush, [System.Drawing.PointF]::new(148, 40))
        $graphics.DrawString('Countdown', $titleFont, $titleBrush, [System.Drawing.PointF]::new(148, 70))
        $graphics.DrawString('Lightweight floating countdown panel', $subtitleFont, $subtitleBrush, [System.Drawing.PointF]::new(150, 110))

        $titleFont.Dispose()
        $subtitleFont.Dispose()
        $titleBrush.Dispose()
        $subtitleBrush.Dispose()
        $glowBrush.Dispose()
        $gradient.Dispose()
    }
    else {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $markSize = [Math]::Round([Math]::Min($width, $height) * 0.76)
        $markX = ($width - $markSize) / 2
        $markY = ($height - $markSize) / 2
        Draw-AppMark -Graphics $graphics -X $markX -Y $markY -Size $markSize
    }

    $bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)

    $graphics.Dispose()
    $bitmap.Dispose()
}

function Convert-PngToIcon {
    param(
        [string]$PngPath,
        [string]$IcoPath
    )

    $pngBytes = [System.IO.File]::ReadAllBytes($PngPath)
    $stream = [System.IO.File]::Create($IcoPath)
    $writer = New-Object System.IO.BinaryWriter($stream)
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]1)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$pngBytes.Length)
    $writer.Write([UInt32]22)
    $writer.Write($pngBytes)
    $writer.Dispose()
}

$expectedAssets = @(
    Join-Path $assetDir 'AppIcon-256.png'
    Join-Path $assetDir 'AppIcon.ico'
    Join-Path $msixAssetDir 'Square44x44Logo.png'
    Join-Path $msixAssetDir 'Square150x150Logo.png'
    Join-Path $msixAssetDir 'StoreLogo.png'
    Join-Path $msixAssetDir 'Wide310x150Logo.png'
    Join-Path $msixAssetDir 'SplashScreen.png'
)

if (-not $Force -and ($expectedAssets | ForEach-Object { Test-Path $_ }) -notcontains $false) {
    Write-Host "Assets already present, skipping generation. Pass -Force to overwrite."
    return
}

New-AppBitmap -Size 256 -OutputPath (Join-Path $assetDir 'AppIcon-256.png')
Convert-PngToIcon -PngPath (Join-Path $assetDir 'AppIcon-256.png') -IcoPath (Join-Path $assetDir 'AppIcon.ico')

New-AppBitmap -Size 44 -OutputPath (Join-Path $msixAssetDir 'Square44x44Logo.png')
New-AppBitmap -Size 150 -OutputPath (Join-Path $msixAssetDir 'Square150x150Logo.png')
New-AppBitmap -Size 50 -OutputPath (Join-Path $msixAssetDir 'StoreLogo.png')
New-AppBitmap -Size 620 -OutputPath (Join-Path $msixAssetDir 'Wide310x150Logo.png') -Wide
New-AppBitmap -Size 620 -OutputPath (Join-Path $msixAssetDir 'SplashScreen.png')

Write-Host "Assets generated under $assetDir and $msixAssetDir"

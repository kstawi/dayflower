# Dayflower - https://github.com/kstawi/dayflower
# Copyright (C) 2026 kstawi
# Licensed under the GNU General Public License v3.0; see the LICENSE file.
#
# Development aid: draws the placeholder logo (a dayflower: two blue petals, a small pale petal and a yellow
# centre) at the sizes the MSIX package and Windows use, and writes them to src\Dayflower.App\Assets.
# Run it with Windows PowerShell, which includes System.Drawing:
#   powershell -ExecutionPolicy Bypass -File tools\MakeAssets.ps1
# The results are committed, so a normal build doesn't need this.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$assets = Join-Path $PSScriptRoot '..\src\Dayflower.App\Assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null
$assets = (Resolve-Path $assets).Path

# Draws the flower in a size x size square whose top-left corner is at (x, y). Designed on a 256-unit square.
function Draw-Flower([System.Drawing.Graphics] $g, [single] $x, [single] $y, [single] $size) {
    $state = $g.Save()
    $g.TranslateTransform($x, $y)
    $g.ScaleTransform($size / 256, $size / 256)

    # Small pale lower petal, behind the others.
    $pale = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 222, 233, 255))
    $g.FillEllipse($pale, [single]104, [single]150, [single]48, [single]68)
    $pale.Dispose()

    # Two large blue petals, lighter towards the tips.
    $tip = [System.Drawing.Color]::FromArgb(255, 96, 146, 255)
    $base = [System.Drawing.Color]::FromArgb(255, 31, 84, 214)
    foreach ($angle in @(-30, 30)) {
        $petal = $g.Save()
        $g.TranslateTransform([single]128, [single]152)
        $g.RotateTransform([single]$angle)
        $rect = New-Object System.Drawing.RectangleF ([single]-46, [single]-118, [single]92, [single]120)
        $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush ($rect, $tip, $base, [single]90)
        $g.FillEllipse($brush, $rect)
        $brush.Dispose()
        $g.Restore($petal)
    }

    # Yellow centre.
    $yellow = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 200, 20))
    $g.FillEllipse($yellow, [single]111, [single]135, [single]34, [single]34)
    $yellow.Dispose()

    $g.Restore($state)
}

# A transparent width x height image with the flower centred, taking up $fill of the shorter side.
function New-Image([int] $width, [int] $height, [double] $fill) {
    $bmp = New-Object System.Drawing.Bitmap ($width, $height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $size = [Math]::Min($width, $height) * $fill
    Draw-Flower $g (($width - $size) / 2) (($height - $size) / 2) $size
    $g.Dispose()
    return $bmp
}

function Save-Png([string] $name, [int] $width, [int] $height, [double] $fill) {
    $bmp = New-Image $width $height $fill
    $bmp.Save((Join-Path $assets $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

foreach ($scale in @(100, 200)) {
    $f = $scale / 100
    Save-Png "Square44x44Logo.scale-$scale.png" (44 * $f) (44 * $f) 1.0
    Save-Png "Square150x150Logo.scale-$scale.png" (150 * $f) (150 * $f) 0.66
    Save-Png "Wide310x150Logo.scale-$scale.png" (310 * $f) (150 * $f) 0.66
    Save-Png "SplashScreen.scale-$scale.png" (620 * $f) (300 * $f) 0.6
    Save-Png "StoreLogo.scale-$scale.png" (50 * $f) (50 * $f) 1.0
}

# Taskbar, Start and title bar icons at exact pixel sizes.
foreach ($size in @(16, 24, 32, 48, 256)) {
    Save-Png "Square44x44Logo.targetsize-$size.png" $size $size 1.0
    Save-Png "Square44x44Logo.targetsize-${size}_altform-unplated.png" $size $size 1.0
}

# Title bar icon: one plain file, loaded by path, so no resource qualifiers are involved.
Save-Png "TitleBarIcon.png" 64 64 1.0

# app.ico for the .exe and the window: one PNG-compressed image per size.
$icoSizes = @(16, 20, 24, 32, 40, 48, 64, 256)
$images = foreach ($size in $icoSizes) {
    $bmp = New-Image $size $size 1.0
    $stream = New-Object System.IO.MemoryStream
    $bmp.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $stream.ToArray()
}
$writer = New-Object System.IO.BinaryWriter ([System.IO.File]::Create((Join-Path $assets 'app.ico')))
try {
    $writer.Write([uint16]0)                  # reserved
    $writer.Write([uint16]1)                  # type: icon
    $writer.Write([uint16]$icoSizes.Count)
    $offset = 6 + 16 * $icoSizes.Count
    for ($i = 0; $i -lt $icoSizes.Count; $i++) {
        $dimension = if ($icoSizes[$i] -ge 256) { 0 } else { $icoSizes[$i] }   # 0 means 256
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)                # palette size
        $writer.Write([byte]0)                # reserved
        $writer.Write([uint16]1)              # colour planes
        $writer.Write([uint16]32)             # bits per pixel
        $writer.Write([uint32]$images[$i].Length)
        $writer.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($image in $images) { $writer.Write([byte[]]$image) }
}
finally {
    $writer.Dispose()
}

Write-Host "Wrote assets to $assets"

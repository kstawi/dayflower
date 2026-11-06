# Dayflower - https://github.com/kstawi/dayflower
# Copyright (C) 2026 kstawi
# Licensed under the GNU General Public License v3.0; see the LICENSE file.
#
# Development aid: draws Dayflower's icons and writes them to src\Dayflower.App\Assets:
#  - the package icon (taskbar, Start, Store, notifications): a tear-off calendar pad with a blue header, binder rings
#    and a curling corner, showing a grid of days with today highlighted. It can't change from day to day, so it shows
#    no date;
#  - a window icon for each day of the month, the same pad with that date in large type, as the Calendar icon on a
#    Mac. The running app shows today's in Alt+Tab and Task View.
#
# The style follows Microsoft's current app icons (such as Outlook's): layers that cast soft shadows on each other,
# gradients on a navy-to-sky ramp lit from the top left, and tinted rather than plain whites, all drawn straight on.
# Small sizes drop detail and land on whole pixels so they stay sharp.
#
# Run it with Windows PowerShell, which includes System.Drawing:
#   powershell -ExecutionPolicy Bypass -File tools\MakeAssets.ps1
#   powershell -ExecutionPolicy Bypass -File tools\MakeAssets.ps1 -Preview preview.png                 (all sizes)
#   powershell -ExecutionPolicy Bypass -File tools\MakeAssets.ps1 -Preview preview.png -PreviewDay 26  (a dated one)
# The results are committed, so a normal build doesn't need this.

param([string] $Preview, [int] $PreviewDay = 0)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$assets = Join-Path $PSScriptRoot '..\src\Dayflower.App\Assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null
$assets = (Resolve-Path $assets).Path

function Color([int] $r, [int] $g, [int] $b, [int] $a = 255) { [System.Drawing.Color]::FromArgb($a, $r, $g, $b) }

function Rounded([double] $x, [double] $y, [double] $w, [double] $h, [double] $radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [single][Math]::Min($radius * 2, [Math]::Min($w, $h))
    $path.AddArc([single]$x, [single]$y, $d, $d, 180, 90)
    $path.AddArc([single]($x + $w - $d), [single]$y, $d, $d, 270, 90)
    $path.AddArc([single]($x + $w - $d), [single]($y + $h - $d), $d, $d, 0, 90)
    $path.AddArc([single]$x, [single]($y + $h - $d), $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

# A gradient across a box; 60 degrees runs from the top left (lit) to the bottom right.
function Gradient([double] $x, [double] $y, [double] $w, [double] $h, $from, $to, [double] $angle = 60) {
    $rect = New-Object System.Drawing.RectangleF ([single]$x, [single]$y, [single][Math]::Max(1, $w), [single][Math]::Max(1, $h))
    return New-Object System.Drawing.Drawing2D.LinearGradientBrush ($rect, $from, $to, ([single]$angle))
}

function Fill($g, $path, $brush) {
    $g.FillPath($brush, $path)
    $brush.Dispose()
}

# Three colours on a ramp from navy to sky blue (with cyan-tinted whites), as Microsoft's own app icons use them.
$navy = Color 9 46 128
$royal = Color 18 92 208
$sky = Color 52 152 244
$paper = Color 252 254 255
$ice = Color 214 238 255
$dayGrey = Color 176 204 236
$ringLight = Color 245 250 255
$ringShade = Color 168 200 240

# A three-step gradient: light, middle and dark along one direction.
function Gradient3([double] $x, [double] $y, [double] $w, [double] $h, $c1, $c2, $c3, [double] $angle = 60) {
    $brush = Gradient $x $y $w $h $c1 $c3 $angle
    $blend = New-Object System.Drawing.Drawing2D.ColorBlend 3
    $blend.Colors = [System.Drawing.Color[]]@($c1, $c2, $c3)
    $blend.Positions = [single[]]@(0, 0.5, 1)
    $brush.InterpolationColors = $blend
    return $brush
}

# A soft shadow a shape casts on what's beneath it, down and a little to the right. Too fine to see below 32 px.
function Shadow($g, $shape, [int] $pixels, [double] $strength = 1, [double] $distance = 1) {
    if ($pixels -lt 32) {
        return
    }

    foreach ($i in 1..6) {
        $state = $g.Save()
        $g.TranslateTransform([single]($i * 0.5 * $distance), [single]((2 + $i * 0.9) * $distance))
        $pen = New-Object System.Drawing.Pen ((Color 0 20 60 ([int](5 * $strength))), [single]($i * 1.8 * $distance))
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $g.DrawPath($pen, $shape)
        $g.FillPath((New-Object System.Drawing.SolidBrush (Color 0 20 60 ([int](4 * $strength)))), $shape)
        $pen.Dispose()
        $g.Restore($state)
    }
}

# Draws the icon in a size x size square at (x, y): a tear-off calendar pad a few sheets thick, with a blue header, two
# binder rings and its bottom right corner curling up. With a $day (1-31) the front sheet shows that date; without, a
# grid of days with today highlighted. Designed on a 256-unit square; $pixels is the size it ends up on screen, which
# decides how much detail fits.
function Draw-Icon([System.Drawing.Graphics] $g, [single] $x, [single] $y, [single] $size, [int] $pixels, [int] $day = 0) {
    $state = $g.Save()
    $g.TranslateTransform($x, $y)
    $g.ScaleTransform($size / 256, $size / 256)

    # Edges land on whole pixels at small sizes, so the icon stays sharp instead of blurring.
    $unit = 256 / $pixels
    function Snap([double] $v) { [Math]::Round($v / $unit) * $unit }
    $small = $pixels -le 20

    # The front sheet and, below it, the edges of the sheets underneath.
    $l = Snap 30; $t = Snap 30; $r = Snap 226; $b = Snap 222; $radius = 24
    $thickness = @(8, 16)
    if ($small) {
        $l = Snap 18; $t = Snap 24; $r = Snap 238; $b = Snap 216; $radius = [Math]::Max(2 * $unit, 24)
        $thickness = @($unit, (2 * $unit))
    }

    $header = (Snap ($t + 58)) - $t
    if ($small) { $header = [Math]::Max(3 * $unit, (Snap ($t + 52)) - $t) }
    $curl = [Math]::Max(3 * $unit, (Snap 54))

    $under = @((Color 120 168 228), (Color 170 204 242))
    for ($i = $thickness.Count - 1; $i -ge 0; $i--) {
        $sheet = Rounded $l ($t + $thickness[$i]) ($r - $l) ($b - $t) $radius
        if ($i -eq $thickness.Count - 1) { Shadow $g $sheet $pixels 1 }
        Fill $g $sheet (New-Object System.Drawing.SolidBrush ($under[$i]))
        $sheet.Dispose()
    }

    # The front sheet, its bottom right corner cut where it curls up.
    $front = New-Object System.Drawing.Drawing2D.GraphicsPath
    $front.AddArc([single]$l, [single]$t, [single](2 * $radius), [single](2 * $radius), 180, 90)
    $front.AddArc([single]($r - 2 * $radius), [single]$t, [single](2 * $radius), [single](2 * $radius), 270, 90)
    $front.AddLine([single]$r, [single]($t + $radius), [single]$r, [single]($b - $curl))
    $front.AddLine([single]$r, [single]($b - $curl), [single]($r - $curl), [single]$b)
    $front.AddArc([single]$l, [single]($b - 2 * $radius), [single](2 * $radius), [single](2 * $radius), 90, 90)
    $front.CloseFigure()
    Shadow $g $front $pixels 1.2 0.6
    Fill $g $front (Gradient $l $t ($r - $l) ($b - $t) $paper $ice)

    $clip = $g.Save()
    $g.SetClip($front)
    $g.FillRectangle((Gradient3 $l $t ($r - $l) $header $sky $royal $navy), [single]$l, [single]$t, [single]($r - $l), [single]$header)
    if ($pixels -ge 32) {
        # The header's shadow on the sheet, and a faint highlight along its top edge.
        $g.FillRectangle((Gradient $l ($t + $header) ($r - $l) 10 (Color 0 30 90 46) (Color 0 30 90 0) 90), [single]$l, [single]($t + $header), [single]($r - $l), [single]9)
        $shine = New-Object System.Drawing.Pen ((Color 255 255 255 80), ([single]2.5))
        $g.DrawLine($shine, [single]($l + $radius), [single]($t + 1.5), [single]($r - $radius), [single]($t + 1.5))
        $shine.Dispose()
    }

    $g.Restore($clip)
    $front.Dispose()

    # Two binder rings across the top edge.
    if ($pixels -ge 20) {
        $ringWidth = [Math]::Max(2 * $unit, 20); $ringTop = Snap 16; $ringHeight = (Snap ($t + 30)) - $ringTop
        foreach ($ringX in (Snap 74), (Snap 162)) {
            $ring = Rounded $ringX $ringTop $ringWidth $ringHeight ($ringWidth / 2)
            Shadow $g $ring $pixels 1.4 0.4
            Fill $g $ring (Gradient $ringX $ringTop $ringWidth $ringHeight $ringLight $ringShade 90)
            $ring.Dispose()
        }
    }

    $bodyTop = $t + $header
    if ($day -gt 0) {
        # The date, as large as the sheet allows, every date at the height of a two-digit one, and clear of the curl. Digits
        # are round at their corners, so the box may reach a little past the fold line before the curl would cut a stroke.
        $boxLeft = $l + 20; $boxRight = $r - 20; $boxTop = $bodyTop + 14; $boxBottom = $b - 18
        if ($small) { $boxLeft = $l + $unit; $boxRight = $r - 2 * $unit; $boxTop = $bodyTop + $unit; $boxBottom = $b - $unit }
        $over = ($boxRight - ($r - $curl)) + ($boxBottom - ($b - $curl)) - $curl * 0.75
        if ($over -gt 0) { $boxRight -= $over / 2; $boxBottom -= $over / 2 }
        $family = New-Object System.Drawing.FontFamily ('Segoe UI Semibold')
        $digits = New-Object System.Drawing.Drawing2D.GraphicsPath
        $digits.AddString([string]$day, $family, [int][System.Drawing.FontStyle]::Regular, [single]100, (New-Object System.Drawing.PointF (0, 0)), [System.Drawing.StringFormat]::GenericTypographic)
        $reference = New-Object System.Drawing.Drawing2D.GraphicsPath
        $reference.AddString('28', $family, [int][System.Drawing.FontStyle]::Regular, [single]100, (New-Object System.Drawing.PointF (0, 0)), [System.Drawing.StringFormat]::GenericTypographic)
        $bounds = $digits.GetBounds(); $refBounds = $reference.GetBounds()
        $reference.Dispose()
        $scale = [Math]::Min(($boxRight - $boxLeft) / $refBounds.Width, ($boxBottom - $boxTop) / $refBounds.Height)
        $fit = New-Object System.Drawing.Drawing2D.Matrix
        $fit.Translate([single]($boxLeft + (($boxRight - $boxLeft) - $bounds.Width * $scale) / 2), [single]($boxTop + (($boxBottom - $boxTop) - $bounds.Height * $scale) / 2))
        $fit.Scale([single]$scale, [single]$scale)
        $fit.Translate([single](-$bounds.X), [single](-$bounds.Y))
        $digits.Transform($fit)
        $g.FillPath((Gradient $boxLeft $boxTop ($boxRight - $boxLeft) ($boxBottom - $boxTop) $royal $navy 90), $digits)
        $digits.Dispose(); $fit.Dispose(); $family.Dispose()
    }
    else {
        # A grid of days, today highlighted: four by three, three by two at medium sizes, two by two when small.
        $columns = 4; $rows = 3; $today = 5; $ratio = 0.5
        $areaLeft = $l + 26; $areaRight = $r - 30; $areaTop = $bodyTop + 18; $areaBottom = $b - 24
        if ($pixels -le 48) { $columns = 3; $rows = 2; $today = 0; $ratio = 0.55 }
        $cell = [Math]::Min(($areaRight - $areaLeft) / ($columns + ($columns - 1) * $ratio), ($areaBottom - $areaTop) / ($rows + ($rows - 1) * $ratio))
        $gap = $cell * $ratio
        $startX = $areaLeft + (($areaRight - $areaLeft) - ($cell * $columns + $gap * ($columns - 1))) / 2
        $startY = $areaTop + (($areaBottom - $areaTop) - ($cell * $rows + $gap * ($rows - 1))) / 2
        $grey = $dayGrey
        if ($small) {
            # Each day a whole block of pixels with a clear gap, in a stronger blue-grey.
            $columns = 2; $rows = 2; $today = 0; $grey = Color 140 172 216
            $cell = [Math]::Max(2 * $unit, (Snap 40))
            $gap = [Math]::Max($unit, (Snap 18))
            $startX = Snap ($l + (($r - $l) - (2 * $cell + $gap)) / 2 - $unit / 2)
            $startY = Snap ($bodyTop + (($b - $bodyTop) - (2 * $cell + $gap)) / 2)
        }

        for ($row = 0; $row -lt $rows; $row++) {
            for ($col = 0; $col -lt $columns; $col++) {
                $cx = $startX + $col * ($cell + $gap); $cy = $startY + $row * ($cell + $gap)
                $cw = $cell; $ch = $cell
                if ($pixels -le 48) {
                    $x2 = Snap ($cx + $cw); $y2 = Snap ($cy + $ch); $cx = Snap $cx; $cy = Snap $cy; $cw = $x2 - $cx; $ch = $y2 - $cy
                }

                # Square corners at the smallest sizes, where a rounded block of a few pixels would only blur.
                $corner = $cell * 0.3
                if ($small) { $corner = [Math]::Min($corner, $unit * 0.35) }
                $square = Rounded $cx $cy $cw $ch $corner
                if ($row * $columns + $col -eq $today) { Fill $g $square (Gradient $cx $cy $cw $ch $sky $royal) }
                else { Fill $g $square (New-Object System.Drawing.SolidBrush $grey) }
                $square.Dispose()
            }
        }
    }

    # The curl, last, since it's the sheet folding over whatever is on its corner: lit on its underside, casting a
    # little shadow on the sheet.
    $flap = New-Object System.Drawing.Drawing2D.GraphicsPath
    $flap.AddPolygon([System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF ([single]$r, [single]($b - $curl))),
        (New-Object System.Drawing.PointF ([single]($r - $curl), [single]$b)),
        (New-Object System.Drawing.PointF ([single]($r - $curl * 0.92), [single]($b - $curl * 0.92)))))
    Shadow $g $flap $pixels 2.2 0.5
    Fill $g $flap (Gradient ($r - $curl) ($b - $curl) $curl $curl (Color 150 194 242) (Color 236 246 255) 225)
    $flap.Dispose()

    $g.Restore($state)
}

# A transparent width x height image with the icon centred, taking up $fill of the shorter side.
function New-Image([int] $width, [int] $height, [double] $fill, [int] $day = 0) {
    $bmp = New-Object System.Drawing.Bitmap ($width, $height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $size = [Math]::Min($width, $height) * $fill
    Draw-Icon $g (($width - $size) / 2) (($height - $size) / 2) $size ([int]$size) $day
    $g.Dispose()
    return $bmp
}

function Save-Png([string] $name, [int] $width, [int] $height, [double] $fill) {
    $bmp = New-Image $width $height $fill
    $bmp.Save((Join-Path $assets $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

# An .ico file: one PNG-compressed image per size.
# An icon image in the classic format: a 32-bit bitmap, bottom row first, then a 1-bit mask (set where transparent).
# Windows' guidance is PNG only for 256 px; the taskbar's loader draws a generic icon for a program whose smaller
# sizes are PNG (seen with an installed copy's Start menu shortcut).
function Get-IconBitmap([System.Drawing.Bitmap] $bmp) {
    $size = $bmp.Width
    $rect = New-Object System.Drawing.Rectangle (0, 0, $size, $size)
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $pixels = New-Object byte[] ($size * $size * 4)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $bmp.UnlockBits($data)

    $maskStride = [int]([Math]::Ceiling($size / 32.0) * 4)
    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter $stream
    $writer.Write([uint32]40)                       # BITMAPINFOHEADER
    $writer.Write([int32]$size)
    $writer.Write([int32]($size * 2))               # colour and mask together
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]0)                        # uncompressed
    $writer.Write([uint32]($size * $size * 4 + $maskStride * $size))
    $writer.Write([int32]0); $writer.Write([int32]0); $writer.Write([uint32]0); $writer.Write([uint32]0)
    for ($row = $size - 1; $row -ge 0; $row--) {
        $writer.Write($pixels, $row * $size * 4, $size * 4)
    }
    for ($row = $size - 1; $row -ge 0; $row--) {
        $mask = New-Object byte[] $maskStride
        for ($col = 0; $col -lt $size; $col++) {
            if ($pixels[($row * $size + $col) * 4 + 3] -eq 0) { $mask[[Math]::Floor($col / 8)] = $mask[[Math]::Floor($col / 8)] -bor (0x80 -shr ($col % 8)) }
        }
        $writer.Write($mask)
    }
    $writer.Flush()
    return , $stream.ToArray()
}

function Save-Ico([string] $path, [int[]] $icoSizes, [int] $day = 0) {
    $images = foreach ($size in $icoSizes) {
        $bmp = New-Image $size $size 1.0 $day
        if ($size -ge 256) {
            $stream = New-Object System.IO.MemoryStream
            $bmp.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $bytes = $stream.ToArray()
        }
        else {
            $bytes = Get-IconBitmap $bmp
        }
        $bmp.Dispose()
        , $bytes
    }
    $writer = New-Object System.IO.BinaryWriter ([System.IO.File]::Create($path))
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
}

if ($Preview) {
    # Every icon size at actual size (top row) and enlarged 4x up to 64 px, on a light and a dark background.
    $sizes = @(16, 20, 24, 32, 48, 64, 256)
    $sheet = New-Object System.Drawing.Bitmap (1500, 760)
    $g = [System.Drawing.Graphics]::FromImage($sheet)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $g.FillRectangle((New-Object System.Drawing.SolidBrush (Color 243 243 243)), 0, 0, 1500, 380)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush (Color 32 32 32)), 0, 380, 1500, 380)
    foreach ($band in @(0, 380)) {
        $x = 20
        foreach ($s in $sizes) {
            $icon = New-Image $s $s 1.0 $PreviewDay
            $g.DrawImage($icon, $x, $band + 20, $s, $s)
            if ($s -le 64) { $g.DrawImage($icon, $x, $band + 100, 4 * $s, 4 * $s) }
            $x += [Math]::Max($s, 4 * [Math]::Min($s, 64)) + 24
            $icon.Dispose()
        }
    }

    $g.Dispose()
    $sheet.Save($Preview, [System.Drawing.Imaging.ImageFormat]::Png)
    $sheet.Dispose()
    Write-Host "Wrote $Preview"
    return
}

foreach ($scale in @(100, 125, 150, 200, 400)) {
    $f = $scale / 100
    Save-Png "Square44x44Logo.scale-$scale.png" (44 * $f) (44 * $f) 1.0
    Save-Png "Square150x150Logo.scale-$scale.png" (150 * $f) (150 * $f) 0.66
    Save-Png "Wide310x150Logo.scale-$scale.png" (310 * $f) (150 * $f) 0.66
    Save-Png "SplashScreen.scale-$scale.png" (620 * $f) (300 * $f) 0.6
    Save-Png "StoreLogo.scale-$scale.png" (50 * $f) (50 * $f) 1.0
}

# Taskbar, Start and Alt+Tab icons at every size Windows asks for at any display scale (24 px on the taskbar is 30 at
# 125 %, 36 at 150 %), so none is scaled up or down. Unplated: drawn without a coloured tile behind them; the light
# variant is what a light taskbar uses.
foreach ($size in @(16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256)) {
    Save-Png "Square44x44Logo.targetsize-$size.png" $size $size 1.0
    Save-Png "Square44x44Logo.targetsize-${size}_altform-unplated.png" $size $size 1.0
    Save-Png "Square44x44Logo.targetsize-${size}_altform-lightunplated.png" $size $size 1.0
}

# app.ico, for the .exe and for the window until it shows today's date.
Save-Ico (Join-Path $assets 'app.ico') @(16, 20, 24, 32, 40, 48, 64, 256)

# The window's icon for each day of the month, at the sizes the taskbar and Alt+Tab use.
$days = Join-Path $assets 'Days'
New-Item -ItemType Directory -Force -Path $days | Out-Null
foreach ($day in 1..31) {
    Save-Ico (Join-Path $days "day-$day.ico") @(16, 20, 24, 32, 40, 48, 64) $day
}

Write-Host "Wrote assets to $assets"

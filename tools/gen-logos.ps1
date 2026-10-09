# Renders winPaint's own logo (vector art, no third-party assets) into every MSIX/Store asset size and the .exe icon.
# Design: an indigo-to-teal rounded tile, a white canvas sheet, a coral brush stroke and a text I-beam
# (the app's re-editable text feature).
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$root = Split-Path $PSScriptRoot -Parent
$images = Join-Path $root "packaging\Images"
New-Item -ItemType Directory -Force $images | Out-Null

function New-Logo([int]$w, [int]$h, [bool]$plated = $true) {
    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    $s = [Math]::Min($w, $h)
    $ox = ($w - $s) / 2.0
    $oy = ($h - $s) / 2.0
    $u = $s / 100.0
    function Get-LogoRect($x, $y, $rw, $rh) { New-Object System.Windows.Rect ($ox + $x * $u), ($oy + $y * $u), ($rw * $u), ($rh * $u) }
    function Get-LogoPoint($x, $y) { New-Object System.Windows.Point ($ox + $x * $u), ($oy + $y * $u) }
    if ($plated) {
        $grad = New-Object System.Windows.Media.LinearGradientBrush ([System.Windows.Media.Color]::FromRgb(0x4B, 0x3F, 0xD8)), ([System.Windows.Media.Color]::FromRgb(0x14, 0xB8, 0xA6)), 45
        $dc.DrawRoundedRectangle($grad, $null, (Get-LogoRect 4 4 92 92), (20 * $u), (20 * $u))
    }
    # Canvas sheet.
    $sheet = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(0xFF, 0xFF, 0xFF))
    $shadow = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromArgb(0x40, 0, 0, 0))
    $dc.DrawRoundedRectangle($shadow, $null, (Get-LogoRect 21 23 60 58), (7 * $u), (7 * $u))
    $dc.DrawRoundedRectangle($sheet, $null, (Get-LogoRect 19 20 60 58), (7 * $u), (7 * $u))
    # Coral brush stroke.
    $stroke = New-Object System.Windows.Media.StreamGeometry
    $ctx = $stroke.Open()
    $ctx.BeginFigure((Get-LogoPoint 27 64), $false, $false)
    $ctx.BezierTo((Get-LogoPoint 36 44), (Get-LogoPoint 48 74), (Get-LogoPoint 58 52), $true, $true)
    $ctx.BezierTo((Get-LogoPoint 63 42), (Get-LogoPoint 68 46), (Get-LogoPoint 71 40), $true, $true)
    $ctx.Close()
    $pen = New-Object System.Windows.Media.Pen ((New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(0xFF, 0x6B, 0x5B)))), (7 * $u)
    $pen.StartLineCap = "Round"; $pen.EndLineCap = "Round"; $pen.LineJoin = "Round"
    $dc.DrawGeometry($null, $pen, $stroke)
    # Text I-beam (live text).
    $ink = New-Object System.Windows.Media.Pen ((New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(0x1E, 0x1B, 0x4B)))), (4.2 * $u)
    $ink.StartLineCap = "Round"; $ink.EndLineCap = "Round"
    $dc.DrawLine($ink, (Get-LogoPoint 44 27), (Get-LogoPoint 44 45))
    $dc.DrawLine($ink, (Get-LogoPoint 39 27), (Get-LogoPoint 49 27))
    $dc.DrawLine($ink, (Get-LogoPoint 39 45), (Get-LogoPoint 49 45))
    $dc.Close()
    $bmp = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $w, $h, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($dv)
    return $bmp
}

function Save-Png($bmp, [string]$path) {
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $fs = [System.IO.File]::Create($path)
    $enc.Save($fs)
    $fs.Close()
}

$scales = @{ 100 = 1.0; 125 = 1.25; 150 = 1.5; 200 = 2.0; 400 = 4.0 }
$assets = @(
    @{ Name = "Square44x44Logo"; W = 44; H = 44 },
    @{ Name = "Square150x150Logo"; W = 150; H = 150 },
    @{ Name = "Wide310x150Logo"; W = 310; H = 150 },
    @{ Name = "SmallTile"; W = 71; H = 71 },
    @{ Name = "LargeTile"; W = 310; H = 310 },
    @{ Name = "StoreLogo"; W = 50; H = 50 }
)
foreach ($a in $assets) {
    foreach ($k in $scales.Keys) {
        $w = [int][Math]::Round($a.W * $scales[$k]); $h = [int][Math]::Round($a.H * $scales[$k])
        Save-Png (New-Logo $w $h) (Join-Path $images "$($a.Name).scale-$k.png")
    }
}

# Taskbar/Start icons at exact target sizes (plated and unplated variants).
foreach ($t in 16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256) {
    Save-Png (New-Logo $t $t) (Join-Path $images "Square44x44Logo.targetsize-$t.png")
    Save-Png (New-Logo $t $t) (Join-Path $images "Square44x44Logo.targetsize-${t}_altform-unplated.png")
    Save-Png (New-Logo $t $t) (Join-Path $images "Square44x44Logo.targetsize-${t}_altform-lightunplated.png")
}

# Application .ico (PNG-compressed entries) for the exe and window.
$ico = Join-Path $root "src\WinPaint.App\Resources\winPaint.ico"
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($sz in $sizes) {
    $ms = New-Object System.IO.MemoryStream
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create((New-Logo $sz $sz)))
    $enc.Save($ms)
    , $ms.ToArray()
}
$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $out
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $d = if ($sz -ge 256) { 0 } else { $sz }
    $bw.Write([byte]$d); $bw.Write([byte]$d); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([int]$pngs[$i].Length); $bw.Write([int]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($ico, $out.ToArray())
Write-Host "Logos written to $images; icon written to $ico"

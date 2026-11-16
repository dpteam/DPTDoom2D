param(
    [string]$Source,
    [string]$Out = "app.ico",
    [int]$X = 52, [int]$Y = 0, [int]$W = 220, [int]$H = 132,
    [string]$Preview
)

<#
    Builds app.ico from the game's own title artwork.

    The title screen is 320x200 and the icon has to be square, so the wordmark is
    cut out and centred on a background sampled from the artwork itself (the sky
    colour) rather than on black - a black border reads as a mistake at 16 px.

    Frames are written by hand: System.Drawing's Icon.Save only ever emits one
    size, and Windows picks 16/32/48 for the taskbar and explorer.
#>

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot

# Without an explicit source, render the title screen with the game itself, so
# the icon always matches what the port actually draws.
if (-not $Source)
{
    $exe = Join-Path $root 'bin\Release\net40\dptdoom2d.exe'
    if (-not (Test-Path $exe)) { throw 'build the game first (tools\build.ps1), or pass -Source' }
    $Source = Join-Path ([System.IO.Path]::GetTempPath()) 'doom2d-icon-title.bmp'
    & $exe --shell 1 $Source | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'could not render the title screen' }
}

$src = [System.Drawing.Bitmap]::FromFile($Source)

# Crop the wordmark.
$crop = New-Object System.Drawing.Bitmap $W, $H
$g = [System.Drawing.Graphics]::FromImage($crop)
$g.DrawImage($src, (New-Object System.Drawing.Rectangle 0, 0, $W, $H),
    (New-Object System.Drawing.Rectangle $X, $Y, $W, $H), [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose()
$src.Dispose()

# Background: the sky colour just above the wordmark, so the padding blends in.
$side = [Math]::Max($W, $H)
$pad = $crop.GetPixel($W - 1, 1)

$square = New-Object System.Drawing.Bitmap $side, $side
$g = [System.Drawing.Graphics]::FromImage($square)
$g.Clear($pad)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$g.DrawImage($crop, (New-Object System.Drawing.Rectangle ([int](($side - $W) / 2)), ([int](($side - $H) / 2)), $W, $H))
$g.Dispose()
$crop.Dispose()

if ($Preview)
{
    $square.Save($Preview, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Output "preview: $Preview"
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = @()
foreach ($s in $sizes)
{
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($square, 0, 0, $s, $s)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $frames += , $ms.ToArray()
    $ms.Dispose()
}
$square.Dispose()

$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; ++$i)
{
    $s = $sizes[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim)
    $bw.Write([byte]$dim)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$frames[$i].Length)
    $bw.Write([UInt32]$offset)
    $offset += $frames[$i].Length
}
foreach ($f in $frames) { $bw.Write($f) }
$bw.Flush(); $bw.Dispose(); $fs.Dispose()

Write-Output "$Out ($($sizes -join ', '))"

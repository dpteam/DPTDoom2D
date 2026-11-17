param(
    [string]$Source,
    [string]$Out = "app.ico",
    [int]$Side = 200,
    [int]$X = 60,
    [int]$Y = 0,
    [string]$Preview
)

<#
    Builds app.ico from the game's own title artwork.

    The crop is **square and 1:1** - no padding, no letterboxing. The first
    version of this script cut the wordmark out and centred it on a square,
    which added empty sky bands above and below and made the logo look squeezed
    the wrong way round. The icon is now simply a square window onto the title
    screen: the whole wordmark plus the marine and the player, which is what the
    artwork is.

    Frames are written as 32-bit BMP frames with an AND mask. PNG-compressed ICO
    frames are a Vista+ feature; a file that mixes the two is read as raw pixel
    noise by everything older (and by System.Drawing.Icon), so the whole icon is
    written in the one format every Windows understands.
#>

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot

if (-not $Source)
{
    $exe = Join-Path $root 'bin\Release\net40\dptdoom2d.exe'
    if (-not (Test-Path $exe)) { throw 'build the game first (tools\build.ps1), or pass -Source' }
    $Source = Join-Path ([System.IO.Path]::GetTempPath()) 'dptdoom2d-icon-title.bmp'
    & $exe --shell 1 $Source | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'could not render the title screen' }
}

$src = [System.Drawing.Bitmap]::FromFile($Source)

if ($Side -le 0) { $Side = 200 }
if ($X -lt 0) { $X = ($src.Width - $Side) / 2 }
if ($Y -lt 0) { $Y = 0 }

$X = [int]$X
$Y = [int]$Y

if ($X -lt 0) { $X = 0 }
if ($Y -lt 0) { $Y = 0 }
if ($X + $Side -gt $src.Width) { $X = $src.Width - $Side }
if ($Y + $Side -gt $src.Height) { $Y = $src.Height - $Side }

$crop = New-Object System.Drawing.Bitmap $Side, $Side
$g = [System.Drawing.Graphics]::FromImage($crop)
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$g.DrawImage($src, (New-Object System.Drawing.Rectangle 0, 0, $Side, $Side),
    (New-Object System.Drawing.Rectangle $X, $Y, $Side, $Side), [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose()
$src.Dispose()

if ($Preview)
{
    $big = New-Object System.Drawing.Bitmap 384, 384
    $pg = [System.Drawing.Graphics]::FromImage($big)
    $pg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $pg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $pg.DrawImage($crop, 0, 0, 384, 384)
    $pg.Dispose()
    $big.Save($Preview, [System.Drawing.Imaging.ImageFormat]::Png)
    $big.Dispose()
    Write-Output "preview: $Preview"
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)

# ---- one 32-bit DIB frame ---------------------------------------------------

function New-Frame([System.Drawing.Bitmap]$image, [int]$size)
{
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($image, 0, 0, $size, $size)
    $g.Dispose()

    $maskRow = [int]([Math]::Floor(($size + 31) / 32) * 4)
    $pixelBytes = $size * $size * 4
    $maskBytes = $maskRow * $size

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)

    # BITMAPINFOHEADER: height is doubled, because it covers the XOR image and
    # the AND mask that follows it.
    $bw.Write([UInt32]40)
    $bw.Write([Int32]$size)
    $bw.Write([Int32]($size * 2))
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]0)                       # BI_RGB
    $bw.Write([UInt32]($pixelBytes + $maskBytes))
    $bw.Write([Int32]0); $bw.Write([Int32]0)
    $bw.Write([UInt32]0); $bw.Write([UInt32]0)

    # XOR image: BGRA, bottom-up.
    for ($y = $size - 1; $y -ge 0; --$y)
    {
        for ($x = 0; $x -lt $size; ++$x)
        {
            $c = $bmp.GetPixel($x, $y)
            $bw.Write([byte]$c.B); $bw.Write([byte]$c.G)
            $bw.Write([byte]$c.R); $bw.Write([byte]255)
        }
    }

    # AND mask: the artwork is fully opaque, so every bit stays 0.
    $bw.Write((New-Object 'byte[]' $maskBytes), 0, $maskBytes)

    $bw.Flush()
    $data = $ms.ToArray()
    $bw.Dispose(); $ms.Dispose(); $bmp.Dispose()

    # The leading comma keeps PowerShell from unrolling the array: without it the
    # function returns one byte per output item and every frame lands as 1 byte.
    return , $data
}

$frames = New-Object System.Collections.ArrayList
foreach ($s in $sizes) { [void]$frames.Add((New-Frame $crop $s)) }
$crop.Dispose()

# ---- ICO container ----------------------------------------------------------

$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter($fs)

$bw.Write([UInt16]0)          # reserved
$bw.Write([UInt16]1)          # type: icon
$bw.Write([UInt16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; ++$i)
{
    $s = $sizes[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }

    $bw.Write([byte]$dim)     # width (0 means 256)
    $bw.Write([byte]$dim)     # height
    $bw.Write([byte]0)        # palette colours
    $bw.Write([byte]0)        # reserved
    $bw.Write([UInt16]1)      # planes
    $bw.Write([UInt16]32)     # bits per pixel
    $bw.Write([UInt32]$frames[$i].Length)
    $bw.Write([UInt32]$offset)

    $offset += $frames[$i].Length
}

foreach ($f in $frames) { $bw.Write($f) }

$bw.Flush(); $bw.Dispose(); $fs.Dispose()

Write-Output "$Out ($($sizes -join ', ')) from a ${Side}x${Side} crop at $X,$Y"

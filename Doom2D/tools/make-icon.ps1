param(
    [string]$Source = "$env:TEMP\title.png",
    [string]$Out = "app.ico",
    [int]$X = 78, [int]$Y = 0, [int]$W = 164, [int]$H = 104,
    [string]$Preview = "$env:TEMP\icon-preview.png"
)

Add-Type -AssemblyName System.Drawing

$src = [System.Drawing.Bitmap]::FromFile($Source)
$crop = New-Object System.Drawing.Bitmap $W, $H
$g = [System.Drawing.Graphics]::FromImage($crop)
$g.DrawImage($src, (New-Object System.Drawing.Rectangle 0, 0, $W, $H),
    (New-Object System.Drawing.Rectangle $X, $Y, $W, $H), [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose()
$src.Dispose()

# Square canvas, logo centred, black background: an icon must be square.
$side = [Math]::Max($W, $H)
$square = New-Object System.Drawing.Bitmap $side, $side
$g = [System.Drawing.Graphics]::FromImage($square)
$g.Clear([System.Drawing.Color]::Black)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$g.DrawImage($crop, (New-Object System.Drawing.Rectangle ([int](($side - $W) / 2)), ([int](($side - $H) / 2)), $W, $H))
$g.Dispose()
$crop.Dispose()

$square.Save($Preview, [System.Drawing.Imaging.ImageFormat]::Png)

# Multi-size ICO: PNG-compressed frames, 16..256, written by hand because
# System.Drawing's Icon.Save only ever writes a single frame.
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
    $frames += ,$ms.ToArray()
    $ms.Dispose()
}

$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0)              # reserved
$bw.Write([UInt16]1)              # type: icon
$bw.Write([UInt16]$sizes.Count)   # image count

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; ++$i)
{
    $s = $sizes[$i]
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]0)            # palette colours
    $bw.Write([byte]0)            # reserved
    $bw.Write([UInt16]1)          # colour planes
    $bw.Write([UInt16]32)         # bits per pixel
    $bw.Write([UInt32]$frames[$i].Length)
    $bw.Write([UInt32]$offset)
    $offset += $frames[$i].Length
}
foreach ($f in $frames) { $bw.Write($f) }
$bw.Flush(); $bw.Dispose(); $fs.Dispose()
$square.Dispose()

Write-Output "$Out ($($sizes -join ', '))"

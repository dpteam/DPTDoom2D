<#
.SYNOPSIS
    Build the documentation images from the game's own framebuffer.

.DESCRIPTION
    Every image in docs/images comes from the real renderer, not from a
    screenshot tool: the game is run headless, the framebuffer is dumped to BMP
    and converted here. That keeps the docs honest - if the port draws something
    wrong, the docs show it.

    The overlay and the launcher are the exceptions: they are GDI, not the
    framebuffer, so they are captured from the running window instead.

    Requires a built game (tools\build.ps1).

.PARAMETER OutDir
    Where the images go. Default: ..\docs\images
#>
[CmdletBinding()]
param(
	[string]$OutDir
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $root '..\docs\images' }

$exe = Join-Path $root 'bin\Release\net40\dptdoom2d.exe'
$launcher = Join-Path $root '..\DPTDOOM2D.Launcher\bin\Release\net40\dptdoom2d-launcher.exe'

if (-not (Test-Path $exe)) { throw "build the game first (tools\build.ps1)" }

$tmp = Join-Path ([System.IO.Path]::GetTempPath()) 'dptdoom2d-docs'
New-Item -ItemType Directory -Path $tmp -Force | Out-Null
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

function Run([string[]]$arguments)
{
	& $exe @arguments | Out-Null
	if ($LASTEXITCODE -ne 0) { throw "dptdoom2d.exe $($arguments -join ' ') failed" }
}

function Convert-Bmp([string]$bmp, [string]$png, [int]$scale = 1)
{
	$src = [System.Drawing.Bitmap]::FromFile($bmp)
	$w = $src.Width * $scale
	$h = $src.Height * $scale

	$dst = New-Object System.Drawing.Bitmap $w, $h
	$g = [System.Drawing.Graphics]::FromImage($dst)
	$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
	$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
	$g.DrawImage($src, 0, 0, $w, $h)
	$g.Dispose()

	$dst.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
	$dst.Dispose()
	$src.Dispose()
	Write-Host "  $([System.IO.Path]::GetFileName($png)) ($w x $h)"
}

Write-Host '== rendering frames' -ForegroundColor Cyan

# Title screen: the game boots to it, so a zero-tick shell run is enough.
Run @('--shell', '1', (Join-Path $tmp 'title.bmp'))

# Menu over the title.
Run @('--shell', '40', (Join-Path $tmp 'menu.bmp'), '5:space,8:-space,20:down,23:-down')

# Gameplay on the first level: walk, shoot a little, look up.
Run @('--simulate', '300', (Join-Path $tmp 'gameplay.bmp'), '1',
	'5:right,40:fire,44:-fire,90:jump,120:up,150:down,200:right,240:fire,244:-fire')

# A later, darker level for variety.
Run @('--simulate', '420', (Join-Path $tmp 'level14.bmp'), '14',
	'5:right,60:fire,64:-fire,150:next,200:fire,204:-fire,280:fire,284:-fire')

# Split screen, cooperative.
Run @('--simulate', '300', (Join-Path $tmp 'coop.bmp'), '1', '5:right,80:fire,84:-fire', '2p')

# Deathmatch: both players fight on the same map.
Run @('--simulate', '400', (Join-Path $tmp 'deathmatch.bmp'), '1', '5:right,120:fire,124:-fire', 'dm')

# Intermission screen, produced by actually leaving the level.
Push-Location (Join-Path $root 'bin\Release\net40')
try { Run @('--levelend', '1') } finally { Pop-Location }
Copy-Item (Join-Path $root 'bin\Release\net40\inter.bmp') (Join-Path $tmp 'intermission.bmp')

Write-Host '== writing docs images' -ForegroundColor Cyan
Convert-Bmp (Join-Path $tmp 'title.bmp') (Join-Path $OutDir 'title.png') 2
Convert-Bmp (Join-Path $tmp 'menu.bmp') (Join-Path $OutDir 'menu.png') 2
Convert-Bmp (Join-Path $tmp 'gameplay.bmp') (Join-Path $OutDir 'gameplay.png') 2
Convert-Bmp (Join-Path $tmp 'level14.bmp') (Join-Path $OutDir 'level14.png') 2
Convert-Bmp (Join-Path $tmp 'coop.bmp') (Join-Path $OutDir 'coop.png') 2
Convert-Bmp (Join-Path $tmp 'deathmatch.bmp') (Join-Path $OutDir 'deathmatch.png') 2
Convert-Bmp (Join-Path $tmp 'intermission.bmp') (Join-Path $OutDir 'intermission.png') 2

# The logo is the game's own wordmark, cropped out of the title screen and
# scaled up 4x with nearest-neighbour so it stays pixel art.
Write-Host '== logo' -ForegroundColor Cyan
$title = [System.Drawing.Bitmap]::FromFile((Join-Path $tmp 'title.bmp'))
$cropX = 52; $cropY = 0; $cropW = 220; $cropH = 132
$crop = New-Object System.Drawing.Bitmap $cropW, $cropH
$g = [System.Drawing.Graphics]::FromImage($crop)
$g.DrawImage($title, (New-Object System.Drawing.Rectangle 0, 0, $cropW, $cropH),
	(New-Object System.Drawing.Rectangle $cropX, $cropY, $cropW, $cropH), [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose()

$logoScale = 4
$logo = New-Object System.Drawing.Bitmap ($cropW * $logoScale), ($cropH * $logoScale)
$g = [System.Drawing.Graphics]::FromImage($logo)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$g.DrawImage($crop, 0, 0, $cropW * $logoScale, $cropH * $logoScale)
$g.Dispose()
$logo.Save((Join-Path $OutDir 'logo.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$logo.Dispose(); $crop.Dispose(); $title.Dispose()
Write-Host "  logo.png ($($cropW * $logoScale) x $($cropH * $logoScale))"

# ---- live windows: the overlay and the launcher -----------------------------
# These are drawn by GDI on top of the frame, so they are captured from the
# running window rather than from the framebuffer.

Write-Host '== overlay and launcher' -ForegroundColor Cyan
$shot = Join-Path $PSScriptRoot 'screenshot.ps1'

$game = Start-Process -FilePath $exe -PassThru
try
{
	Start-Sleep -Seconds 3
	& powershell -ExecutionPolicy Bypass -File $shot -ProcessId $game.Id `
		-Out (Join-Path $OutDir 'overlay.png') -Keys 'F2' | Out-Null
	Write-Host '  overlay.png'
}
finally
{
	if (-not $game.HasExited) { $game.Kill() }
}

if (Test-Path $launcher)
{
	$form = Start-Process -FilePath $launcher -PassThru
	try
	{
		Start-Sleep -Seconds 3
		& powershell -ExecutionPolicy Bypass -File $shot -ProcessId $form.Id `
			-Out (Join-Path $OutDir 'launcher.png') -WindowTitle 'DPT.DOOM 2D Launcher' | Out-Null
		Write-Host '  launcher.png'
	}
	finally
	{
		if (-not $form.HasExited) { $form.Kill() }
	}
}

Write-Host '== done' -ForegroundColor Green

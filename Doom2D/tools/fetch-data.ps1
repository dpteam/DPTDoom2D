<#
.SYNOPSIS
    Fetch the Doom2D data (WAD, music, gamma table) into refs/doom2d-rembo.

.DESCRIPTION
    The game data is not committed: doom2d.wad is 4.4 MB of sprites, sounds and
    levels, and the music is another 1.7 MB. Both ship in the upstream sources,
    so a fresh clone fetches them once:

        tools\fetch-data.ps1

    Re-running is safe: an existing checkout is updated, not replaced.

.PARAMETER Force
    Delete refs/doom2d-rembo and clone it again.
#>
[CmdletBinding()]
param(
	[switch]$Force
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$refs = Join-Path $root '..\refs'
$target = Join-Path $refs 'doom2d-rembo'
$url = 'https://github.com/Doom2D/doom2d-rembo.git'

function Fail([string]$message)
{
	throw "$message"
}

New-Item -ItemType Directory -Path $refs -Force | Out-Null

if ($Force -and (Test-Path $target))
{
	Write-Host "== removing $target" -ForegroundColor Cyan
	Remove-Item $target -Recurse -Force
}

if (Test-Path (Join-Path $target '.git'))
{
	Write-Host '== update refs/doom2d-rembo' -ForegroundColor Cyan
	& git -C $target fetch --depth 1 origin
	if ($LASTEXITCODE -ne 0) { Fail 'git fetch failed' }
	& git -C $target reset --hard FETCH_HEAD
	if ($LASTEXITCODE -ne 0) { Fail 'git reset failed' }
}
else
{
	Write-Host '== clone refs/doom2d-rembo' -ForegroundColor Cyan
	& git clone --depth 1 $url $target
	if ($LASTEXITCODE -ne 0) { Fail 'git clone failed' }
}

$wad = Join-Path $target 'src\doom2d.wad'
if (-not (Test-Path $wad)) { Fail "doom2d.wad is missing from $target" }

$size = '{0:N1} MB' -f ((Get-Item $wad).Length / 1MB)
Write-Host "== data ready: $wad ($size)" -ForegroundColor Green

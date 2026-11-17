<#
.SYNOPSIS
    Fetch the FreeDoom data into refs/freedoom.

.DESCRIPTION
    FreeDoom is the free replacement for Doom's data (modified BSD, redistribution
    allowed). The converter needs one of its WADs to build freedoom2d.wad, and the
    release ships that set, so a fresh clone fetches it once:

        tools\fetch-freedoom.ps1

    The Phase 1 WAD is used rather than Phase 2 because it is the smaller of the
    two and the converter takes its music, sounds and sprites either way; Phase 2
    only adds levels, and Doom2D cannot use a Doom level.

    Re-running is safe: an existing checkout is updated, not replaced.

.PARAMETER Force
    Delete refs/freedoom and clone it again.
#>
[CmdletBinding()]
param(
	[switch]$Force
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$refs = Join-Path $root '..\refs'
$target = Join-Path $refs 'freedoom'
$url = 'https://github.com/freedoom/freedoom.git'

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
	Write-Host '== update refs/freedoom' -ForegroundColor Cyan
	& git -C $target fetch --depth 1 origin
	if ($LASTEXITCODE -ne 0) { Fail 'git fetch failed' }
	& git -C $target reset --hard FETCH_HEAD
	if ($LASTEXITCODE -ne 0) { Fail 'git reset failed' }
}
else
{
	Write-Host '== clone refs/freedoom' -ForegroundColor Cyan
	& git clone --depth 1 $url $target
	if ($LASTEXITCODE -ne 0) { Fail 'git clone failed' }
}

# The built WADs live in a release, not in the repository: the tree holds the
# sources for them. Accept whichever of the two names is present, because the
# project has renamed them between releases.
$candidates = @(
	(Join-Path $target 'wads\freedoom1.wad'),
	(Join-Path $target 'freedoom1.wad'),
	(Join-Path $target 'wads\freedm.wad')
)

$wad = $null
foreach ($candidate in $candidates)
{
	if (Test-Path $candidate) { $wad = $candidate; break }
}

if (-not $wad)
{
	Fail @"
freedoom1.wad is missing from $target.

The FreeDoom repository carries the sources, not the built WAD. Download a
release and drop freedoom1.wad into $target:

    https://github.com/freedoom/freedoom/releases

This is the same file the build server fetches; nothing here is Doom2D's.
"@
}

$size = '{0:N1} MB' -f ((Get-Item $wad).Length / 1MB)
Write-Host "== freedoom ready: $wad ($size)" -ForegroundColor Green

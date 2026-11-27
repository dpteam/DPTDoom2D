<#
.SYNOPSIS
    Fetch the FreeDoom data into refs/freedoom.

.DESCRIPTION
    FreeDoom is the free replacement for Doom's data (modified BSD, redistribution
    allowed). The converter needs one of its WADs to build freedoom2d.wad, and the
    release ships that set, so a fresh clone fetches it once:

        tools\fetch-freedoom.ps1

    The WAD is downloaded from a release, not cloned: the FreeDoom repository holds
    the sources that build the WADs, and a checkout of it has no freedoom1.wad in it.
    That mistake is why the first version of this script failed on CI with "no such
    file" instead of fetching anything.

    Phase 1 is used rather than Phase 2 because it is the smaller of the two and the
    converter takes its music, sounds and sprites either way; Phase 2 only adds
    levels, and Doom2D cannot use a Doom level.

    The download is checked against the SHA256 published in the release's CHECKSUM
    file. Not because the transport is suspect, but because a truncated 23 MB zip
    would otherwise surface much later as a broken data set.

    Re-running is safe: an existing, verified file is left alone.

.PARAMETER Version
    Release tag to fetch. Defaults to the one this project was tested against.

.PARAMETER Force
    Download again even when the file is already there.
#>
[CmdletBinding()]
param(
	[string]$Version = 'v0.13.0',
	[switch]$Force
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$refs = Join-Path $root '..\refs'
$target = Join-Path $refs 'freedoom'
$wad = Join-Path $target 'freedoom1.wad'

function Fail([string]$message)
{
	throw $message
}

New-Item -ItemType Directory -Path $target -Force | Out-Null

if ((Test-Path $wad) -and -not $Force)
{
	$size = '{0:N1} MB' -f ((Get-Item $wad).Length / 1MB)
	Write-Host "== freedoom ready: $wad ($size)" -ForegroundColor Green
	return
}

$base = "https://github.com/freedoom/freedoom/releases/download/$Version"
$zipName = "freedoom-$($Version.TrimStart('v')).zip"
$zipUrl = "$base/$zipName"
$sumUrl = "$base/freedoom-$($Version.TrimStart('v'))-CHECKSUM"

$zip = Join-Path $env:TEMP $zipName

Write-Host "== downloading $zipName" -ForegroundColor Cyan

# Invoke-WebRequest with -UseBasicParsing: the progress bar makes a 23 MB download
# take minutes on a build agent, and parsing the DOM is pointless here.
$ProgressPreference = 'SilentlyContinue'

try
{
	Invoke-WebRequest -Uri $zipUrl -OutFile $zip -UseBasicParsing
}
catch
{
	Fail "cannot download ${zipUrl}: $($_.Exception.Message)"
}

# $LASTEXITCODE is for native commands only; after Invoke-WebRequest it still holds
# whatever the last git call left there, so checking it here made a successful
# download look like a failure. The file itself is the evidence.
if (-not (Test-Path $zip)) { Fail "cannot download $zipUrl" }
if ((Get-Item $zip).Length -lt 1MB) { Fail "$zipName is too small to be the release" }

# Verify against the published checksum. The file lists every artifact of the
# release, so the right line is picked by name rather than assumed to be first.
Write-Host '== verifying checksum' -ForegroundColor Cyan

try
{
	$response = Invoke-WebRequest -Uri $sumUrl -UseBasicParsing

	# .Content is a byte[] when the server does not label the response as text —
	# PowerShell 5 returns the raw bytes and indexing it gives numbers, which no
	# regex would ever match. Decode it explicitly.
	$sums = if ($response.Content -is [byte[]])
		{ [System.Text.Encoding]::UTF8.GetString($response.Content) }
		else { [string]$response.Content }
}
catch
{
	Fail "cannot download the checksum file from $sumUrl"
}

$expected = $null
foreach ($line in ($sums -split "`n"))
{
	# "SHA256 (freedoom-0.13.0.zip) = 3f9b..."
	if ($line -match 'SHA256\s+\((.+?)\)\s*=\s*([0-9a-fA-F]+)')
	{
		if ($matches[1].Trim() -eq $zipName) { $expected = $matches[2].ToLower(); break }
	}
}

if (-not $expected) { Fail "the checksum file has no entry for $zipName" }

$actual = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()

if ($actual -ne $expected)
{
	Remove-Item $zip -Force -ErrorAction SilentlyContinue
	Fail "checksum mismatch for $zipName`: expected $expected, got $actual"
}

Write-Host '== extracting freedoom1.wad' -ForegroundColor Cyan

# ZipArchive from the BCL: Expand-Archive is a PowerShell 5 cmdlet and this script
# also has to run on the 4.0 runtime the rest of the project targets.
Add-Type -AssemblyName System.IO.Compression.FileSystem

$archive = [System.IO.Compression.ZipFile]::OpenRead($zip)

try
{
	$entry = $null
	foreach ($e in $archive.Entries)
	{
		if ($e.Name -eq 'freedoom1.wad') { $entry = $e; break }
	}

	if (-not $entry) { Fail "$zipName does not contain freedoom1.wad" }

	[System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $wad, $true)
}
finally
{
	$archive.Dispose()
	Remove-Item $zip -Force -ErrorAction SilentlyContinue
}

$size = '{0:N1} MB' -f ((Get-Item $wad).Length / 1MB)
Write-Host "== freedoom ready: $wad ($size)" -ForegroundColor Green

<#
.SYNOPSIS
    Build DPT.DOOM 2D, run the checks and assemble the portable distribution.

.DESCRIPTION
    One command for the whole pipeline, so a release never skips a step:

        tools\build.ps1              # build + headless checks
        tools\build.ps1 -SkipTests   # build only
        tools\build.ps1 -Run         # ... and start the game
        tools\build.ps1 -Pack        # ... + dist/DPTDOOM2D/ ready to copy anywhere

    The checks need no GPU and no sound card, so they run on a build server. A
    graphical smoke test still has to be done by hand.

.PARAMETER Configuration
    Debug or Release. Default: Release.

.PARAMETER SkipTests
    Build only. Use for a quick syntax check; never for a release.

.PARAMETER Net
    Also run the two-machine network checks (host + guest over loopback, and a
    relayed session through a local master). Slower, and needs two free ports.

.PARAMETER Pack
    Assemble dist/DPTDOOM2D: game, launcher, master, data and docs.

.PARAMETER Zip
    Also write dist/DPTDOOM2D-<date>.zip. Implies -Pack.
#>
[CmdletBinding()]
param(
	[ValidateSet('Debug', 'Release')]
	[string]$Configuration = 'Release',
	[switch]$SkipTests,
	[switch]$Net,
	[switch]$Pack,
	[switch]$Zip
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root '..\DPTDOOM2D.slnx'
$output = Join-Path $root "bin\$Configuration\net40"
$exe = Join-Path $output 'dptdoom2d.exe'
$launcher = Join-Path $root "..\DPTDOOM2D.Launcher\bin\$Configuration\net40\dptdoom2d-launcher.exe"
$master = Join-Path $root "..\DPTDOOM2D.Master\bin\$Configuration\net40\dptdoom2d-master.exe"

function Fail([string]$message)
{
	throw "$message (see the output above)"
}

# The game data lives in the upstream sources; a fresh clone has to fetch them.
function Assert-Data
{
	$wad = Join-Path $root '..\refs\doom2d-rembo\src\doom2d.wad'
	if (-not (Test-Path $wad))
	{
		Fail 'doom2d.wad is missing - run tools\fetch-data.ps1 first'
	}
}

Push-Location $root
try
{
	Assert-Data

	Write-Host "== build ($Configuration)" -ForegroundColor Cyan
	& dotnet build $solution -c $Configuration -v q --nologo
	if ($LASTEXITCODE -ne 0) { Fail 'build failed' }

	if (-not $SkipTests)
	{
		Write-Host '== headless checks' -ForegroundColor Cyan
		$tmp = Join-Path ([System.IO.Path]::GetTempPath()) 'dptdoom2d-checks'
		New-Item -ItemType Directory -Path $tmp -Force | Out-Null

		$failed = 0

		# Every level has to load and simulate without throwing.
		for ($map = 1; $map -le 19; ++$map)
		{
			& $exe --simulate 200 (Join-Path $tmp "map$map.bmp") $map '5:right,60:fire,120:jump' 2>&1 | Out-Null
			if ($LASTEXITCODE -ne 0) { Write-Host "  map $map FAILED" -ForegroundColor Red; $failed++ }
			else { Write-Host "  map $map ok" -ForegroundColor DarkGray }
		}

		# Save/load has to round-trip the whole world.
		& $exe --saveload 150 | Out-Null
		if ($LASTEXITCODE -ne 0) { Write-Host '  save/load FAILED' -ForegroundColor Red; $failed++ }

		# The front end has to reach the game from the title screen.
		& $exe --shell 140 (Join-Path $tmp 'menu.bmp') '5:space,8:-space,20:enter,23:-enter,40:space,43:-space' | Out-Null
		if ($LASTEXITCODE -ne 0) { Write-Host '  front end FAILED' -ForegroundColor Red; $failed++ }

		# Every sound lump has to parse into playable data. This is the check that
		# catches a wrong DMI header - the bug that made the game silent.
		& $exe --sounds 2>&1 | Out-Null
		if ($LASTEXITCODE -ne 0) { Write-Host '  sounds FAILED' -ForegroundColor Red; $failed++ }
		else { Write-Host '  sounds ok' -ForegroundColor DarkGray }

		# Every music track has to parse and produce sound.
		$music = Get-ChildItem (Join-Path $root '..\refs\doom2d-rembo\music') -File -ErrorAction SilentlyContinue |
			Where-Object { $_.Name -ne 'README_INSTALL' }
		foreach ($track in $music)
		{
			$line = & $exe --music $track.Name 2>&1 | Select-Object -Last 1
			if ($LASTEXITCODE -ne 0) { Write-Host "  music $($track.Name) FAILED" -ForegroundColor Red; $failed++ }
		}
		if ($music) { Write-Host "  music: $($music.Count) tracks ok" -ForegroundColor DarkGray }

		# The same tracks have to play from the DMM songs inside the WAD, which is
		# the fallback when Data/Music is missing.
		$musicDir = Join-Path (Split-Path -Parent $exe) 'Data\Music'
		$hidden = $false
		if (Test-Path $musicDir)
		{
			Rename-Item $musicDir "$musicDir.hidden" -Force
			$hidden = $true
		}
		try
		{
			foreach ($track in $music)
			{
				& $exe --music $track.Name 2>&1 | Out-Null
				if ($LASTEXITCODE -ne 0) { Write-Host "  wad music $($track.Name) FAILED" -ForegroundColor Red; $failed++ }
			}
			if ($music) { Write-Host "  music from WAD: $($music.Count) tracks ok" -ForegroundColor DarkGray }
		}
		finally
		{
			if ($hidden) { Rename-Item "$musicDir.hidden" $musicDir -Force }
		}

		# Custom map packs: each one has to load and simulate a tick. This is what
		# keeps the Deluxe map library in the distribution honest.
		$mapPacks = Get-ChildItem (Join-Path $root 'Maps') -Filter *.wad -ErrorAction SilentlyContinue
		$packFailures = 0
		foreach ($wadFile in $mapPacks)
		{
			& $exe -map-pack $wadFile.Name --simulate 40 (Join-Path $tmp 'pack.bmp') 1 2>&1 | Out-Null
			if ($LASTEXITCODE -ne 0) { Write-Host "  map pack $($wadFile.Name) FAILED" -ForegroundColor Red; $packFailures++ }
		}
		if ($mapPacks) { Write-Host "  map packs: $($mapPacks.Count - $packFailures)/$($mapPacks.Count) ok" -ForegroundColor DarkGray }
		$failed += $packFailures

		# Cheats: every original code has to fire. The Cyrillic ones are the same
		# key presses, so this covers both layouts.
		& $exe --cheats 2>&1 | Out-Null
		if ($LASTEXITCODE -ne 0) { Write-Host '  cheats FAILED' -ForegroundColor Red; $failed++ }
		else { Write-Host '  cheats ok' -ForegroundColor DarkGray }

		# Mod kit: build a PK3 out of a map pack, list it, and load it back. This is
		# the whole mod workflow in one round trip.
		$modSrc = Join-Path $tmp 'modsrc'
		$modPk3 = Join-Path $tmp 'mod.pk3'
		Remove-Item $modSrc -Recurse -Force -ErrorAction SilentlyContinue
		New-Item -ItemType Directory -Path (Join-Path $modSrc 'maps') -Force | Out-Null
		$samplePack = Get-ChildItem (Join-Path $root 'Maps') -Filter *.wad -ErrorAction SilentlyContinue | Select-Object -First 1
		if ($samplePack)
		{
			Copy-Item $samplePack.FullName (Join-Path $modSrc 'maps\sample.wad')
			Remove-Item $modPk3 -Force -ErrorAction SilentlyContinue

			& $exe --modbuild $modPk3 $modSrc 2>&1 | Out-Null
			if ($LASTEXITCODE -ne 0) { Write-Host '  modbuild FAILED' -ForegroundColor Red; $failed++ }

			& $exe --modlist $modPk3 2>&1 | Out-Null
			if ($LASTEXITCODE -ne 0) { Write-Host '  modlist FAILED' -ForegroundColor Red; $failed++ }

			& $exe -file $modPk3 --simulate 40 (Join-Path $tmp 'mod.bmp') 1 2>&1 | Out-Null
			if ($LASTEXITCODE -ne 0) { Write-Host '  pk3 load FAILED' -ForegroundColor Red; $failed++ }
			else { Write-Host '  mod kit ok' -ForegroundColor DarkGray }
		}

		# Prototypes: the catalogue has to be intact, and the WAD builder has to
		# round-trip a real WAD. The visual demos need a window, so they are not
		# run here; building the WAD exercises the same code paths they do.
		& $exe --proto-list 2>&1 | Out-Null
		if ($LASTEXITCODE -ne 0) { Write-Host '  proto list FAILED' -ForegroundColor Red; $failed++ }

		$wadSrc = Join-Path $tmp 'wadsrc'
		$wadOut = Join-Path $tmp 'built.wad'
		Remove-Item $wadSrc -Recurse -Force -ErrorAction SilentlyContinue
		Remove-Item $wadOut -Force -ErrorAction SilentlyContinue
		New-Item -ItemType Directory -Path $wadSrc -Force | Out-Null

		if ($samplePack)
		{
			Copy-Item $samplePack.FullName (Join-Path $wadSrc 'MAP01')

			& $exe --wadbuild $wadOut (Join-Path $wadSrc 'MAP01') 2>&1 | Out-Null
			if ($LASTEXITCODE -ne 0) { Write-Host '  wadbuild FAILED' -ForegroundColor Red; $failed++ }

			& $exe --wadlist $wadOut 2>&1 | Out-Null
			if ($LASTEXITCODE -ne 0) { Write-Host '  wadlist FAILED' -ForegroundColor Red; $failed++ }
			else { Write-Host '  prototypes ok' -ForegroundColor DarkGray }
		}

		if ($failed -gt 0) { Fail "$failed check(s) failed" }
		Write-Host '  all checks passed' -ForegroundColor Green

		if ($Net)
		{
			Write-Host '== network checks' -ForegroundColor Cyan
			& powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'net-test.ps1')
			if ($LASTEXITCODE -ne 0) { Fail 'direct network check failed' }

			& powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'relay-test.ps1')
			if ($LASTEXITCODE -ne 0) { Fail 'relay network check failed' }

			Write-Host '  network ok' -ForegroundColor Green
		}
	}

	if ($Pack -or $Zip)
	{
		Write-Host '== pack' -ForegroundColor Cyan
		$dist = Join-Path $root 'dist\DPTDOOM2D'
		if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
		New-Item -ItemType Directory -Path $dist -Force | Out-Null

		Copy-Item $exe $dist
		Copy-Item (Join-Path $output 'dptdoom2d.exe.config') $dist
		Copy-Item $launcher $dist
		Copy-Item $master $dist
		Copy-Item (Join-Path $output 'DPTDOOM2D.Shared.dll') $dist -ErrorAction SilentlyContinue
		Copy-Item (Join-Path $output 'Data') (Join-Path $dist 'Data') -Recurse

		# GPLv2 requires the license to travel with the binaries.
		Copy-Item (Join-Path $root '..\LICENSE') $dist
		Copy-Item (Join-Path $root '..\README.md') $dist
		Copy-Item (Join-Path $root '..\CHANGELOG.md') $dist

		Write-Host "   $dist" -ForegroundColor Green

		if ($Zip)
		{
			$archive = Join-Path $root ('dist\DPTDOOM2D-' + (Get-Date -Format 'yyyyMMdd') + '.zip')
			if (Test-Path $archive) { Remove-Item $archive -Force }
			Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $archive
			Write-Host "   $archive" -ForegroundColor Green
		}
	}

	Write-Host '== ok' -ForegroundColor Green
}
finally
{
	Pop-Location
}

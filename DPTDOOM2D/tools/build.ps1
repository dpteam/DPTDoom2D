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

		# Every music track has to parse and produce sound.
		$music = Get-ChildItem (Join-Path $root '..\refs\doom2d-rembo\music') -File -ErrorAction SilentlyContinue |
			Where-Object { $_.Name -ne 'README_INSTALL' }
		foreach ($track in $music)
		{
			$line = & $exe --music $track.Name 2>&1 | Select-Object -Last 1
			if ($LASTEXITCODE -ne 0) { Write-Host "  music $($track.Name) FAILED" -ForegroundColor Red; $failed++ }
		}
		if ($music) { Write-Host "  music: $($music.Count) tracks ok" -ForegroundColor DarkGray }

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

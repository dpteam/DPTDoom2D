<#
.SYNOPSIS
    Build Doom2D and stage the FNA native libraries next to the executable.

.DESCRIPTION
    The FNA native DLLs (SDL2, FNA3D, FAudio, libtheorafile) are binaries, so
    they are not committed. They come from one of:

        $env:TXDK_NATIVE_DIR   explicit folder
        ..\refs\TerrariaXDK   the engine donor tree (default)

    Without SDL2.dll the FNA platform static constructor throws before the game
    starts, and the error says only "DllNotFoundException".

        tools\build.ps1              # build + stage natives
        tools\build.ps1 -Test        # ... + the headless checks
        tools\build.ps1 -Run         # ... + start the game
        tools\build.ps1 -Pack        # ... + dist/Doom2D/ ready to copy anywhere

.PARAMETER Configuration
    Debug or Release. Default: Release.
#>
[CmdletBinding()]
param(
	[ValidateSet('Debug', 'Release')]
	[string]$Configuration = 'Release',
	[switch]$Test,
	[switch]$Run,
	[switch]$Pack
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'Doom2D.csproj'
$output = Join-Path $root "bin\$Configuration\net40"
$exe = Join-Path $output 'Doom2D.exe'
$nativeNames = @('FAudio.dll', 'FNA3D.dll', 'libtheorafile.dll', 'SDL2.dll')

function Fail([string]$message)
{
	throw "$message (see the output above)"
}

function Get-NativeDirectory
{
	$candidates = @(
		$env:TXDK_NATIVE_DIR,
		(Join-Path $root '..\external\fna\native\win64'),
		(Join-Path $root '..\refs\TerrariaXDK'),
		(Join-Path $root '..\refs\TerrariaXDK\Source\bin\Release\net40')
	)
	foreach ($candidate in $candidates)
	{
		if ($candidate -and (Test-Path (Join-Path $candidate 'SDL2.dll')))
		{
			return $candidate
		}
	}
	return $null
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
	& dotnet build $project -c $Configuration -v q --nologo
	if ($LASTEXITCODE -ne 0)
	{
		Fail 'build failed'
	}

	Write-Host '== stage natives' -ForegroundColor Cyan
	$native = Get-NativeDirectory
	if (-not $native)
	{
		Write-Warning 'FNA native libraries not found - set TXDK_NATIVE_DIR to the folder with SDL2.dll'
	}
	else
	{
		foreach ($name in $nativeNames)
		{
			$source = Join-Path $native $name
			if (Test-Path $source) { Copy-Item $source $output -Force }
			else { Write-Warning "$name is missing from $native - the game will not start without it" }
		}
	}

	if ($Test)
	{
		Write-Host '== headless checks' -ForegroundColor Cyan
		$tmp = Join-Path ([System.IO.Path]::GetTempPath()) 'doom2d-checks'
		New-Item -ItemType Directory -Path $tmp -Force | Out-Null

		$failed = 0

		# Every level has to load and simulate without throwing.
		for ($map = 1; $map -le 19; ++$map)
		{
			$out = & $exe --simulate 200 (Join-Path $tmp "map$map.bmp") $map '5:right,60:fire,120:jump' 2>&1
			if ($LASTEXITCODE -ne 0) { Write-Host "  map $map FAILED" -ForegroundColor Red; $failed++ }
			else { Write-Host "  map $map ok" -ForegroundColor DarkGray }
		}

		# Save/load has to round-trip the whole world.
		& $exe --saveload 150
		if ($LASTEXITCODE -ne 0) { Write-Host '  save/load FAILED' -ForegroundColor Red; $failed++ }

		# The front end has to reach the game from the title screen.
		& $exe --shell 140 (Join-Path $tmp 'menu.bmp') '5:space,8:-space,20:enter,23:-enter,40:space,43:-space'
		if ($LASTEXITCODE -ne 0) { Write-Host '  front end FAILED' -ForegroundColor Red; $failed++ }

		if ($failed -gt 0) { Fail "$failed check(s) failed" }
		Write-Host '  all checks passed' -ForegroundColor Green
	}

	if ($Pack)
	{
		Write-Host '== pack' -ForegroundColor Cyan
		$dist = Join-Path $root 'dist\Doom2D'
		if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
		New-Item -ItemType Directory -Path $dist -Force | Out-Null

		foreach ($name in (@('Doom2D.exe', 'Doom2D.exe.config') + $nativeNames))
		{
			$source = Join-Path $output $name
			if (Test-Path $source) { Copy-Item $source $dist }
		}
		Copy-Item (Join-Path $output 'Data') (Join-Path $dist 'Data') -Recurse
		Copy-Item (Join-Path $root 'README.md') $dist
		Write-Host "   $dist" -ForegroundColor Green
	}

	if ($Run)
	{
		Write-Host '== run' -ForegroundColor Cyan
		& $exe
	}

	Write-Host '== ok' -ForegroundColor Green
}
finally
{
	Pop-Location
}

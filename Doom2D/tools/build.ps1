<#
.SYNOPSIS
    Build Doom2D and stage the FNA native libraries next to the executable.

.DESCRIPTION
    The FNA native DLLs (SDL2, FNA3D, FAudio, libtheorafile) are not produced by
    the build. They come from the TerrariaXDK donor tree, which keeps them at its
    root; without SDL2.dll the FNA platform static constructor throws before the
    game ever starts, and the error says only "DllNotFoundException".

        tools\build.ps1                 # build + stage natives
        tools\build.ps1 -Run            # ... and start the game

.PARAMETER Configuration
    Debug or Release. Default: Release.
#>
[CmdletBinding()]
param(
	[ValidateSet('Debug', 'Release')]
	[string]$Configuration = 'Release',
	[switch]$Run
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'Doom2D.csproj'
$output = Join-Path $root "bin\$Configuration\net40"
$nativeNames = @('FAudio.dll', 'FNA3D.dll', 'libtheorafile.dll', 'SDL2.dll')

function Fail([string]$message)
{
	throw "$message (see the output above)"
}

function Get-NativeDirectory
{
	$candidates = @(
		$env:TXDK_NATIVE_DIR,
		(Join-Path $root '..\TerrariaXDK'),
		(Join-Path $root '..\TerrariaXDK\Source\bin\Release\net40')
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

Push-Location $root
try
{
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
		Fail 'FNA native libraries not found - set TXDK_NATIVE_DIR to the folder with SDL2.dll'
	}
	foreach ($name in $nativeNames)
	{
		$source = Join-Path $native $name
		if (Test-Path $source)
		{
			Copy-Item $source $output -Force
		}
		else
		{
			Write-Warning "$name is missing from $native - the game will not start without it"
		}
	}

	if ($Run)
	{
		Write-Host '== run' -ForegroundColor Cyan
		& (Join-Path $output 'Doom2D.exe')
	}

	Write-Host '== ok' -ForegroundColor Green
}
finally
{
	Pop-Location
}

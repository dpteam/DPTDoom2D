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
$editor = Join-Path $root "..\DPTDOOM2D.Editor\bin\$Configuration\net40\dptdoom2d-editor.exe"

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

		# Layout presets: the keys, and the KeyName -> KeyByName round trip the
		# overlay relies on when it saves a rebind.
		& $exe --controls 2>&1 | Out-Null
		if ($LASTEXITCODE -ne 0) { Write-Host '  controls FAILED' -ForegroundColor Red; $failed++ }
		else { Write-Host '  controls ok' -ForegroundColor DarkGray }

		# Actor language: the built-in data has to equal monster.c, and the parser
		# has to reject broken files with the documented codes.
		& $exe --actorcheck 2>&1 | Out-Null
		if ($LASTEXITCODE -ne 0) { Write-Host '  actors FAILED' -ForegroundColor Red; $failed++ }
		else { Write-Host '  actors ok' -ForegroundColor DarkGray }

		# The manifest that decides whether two machines may play together: wire
		# format, comparison, and the digest mixed into the tick checksum.
		& $exe --datacheck 2>&1 | Out-Null
		if ($LASTEXITCODE -ne 0) { Write-Host '  manifest FAILED' -ForegroundColor Red; $failed++ }
		else { Write-Host '  manifest ok' -ForegroundColor DarkGray }

		# Resource audit: run the game and record everything it asks the WAD for.
		# A missing sprite set is a Fatal the first time a monster spawns, so the
		# audit exits non-zero for that; missing glyphs are only reported.
		& $exe --wadreq 2>&1 | Out-Null
		if ($LASTEXITCODE -ne 0) { Write-Host '  wadreq FAILED' -ForegroundColor Red; $failed++ }
		else { Write-Host '  wadreq ok' -ForegroundColor DarkGray }

		# Music converter: a MIDI becomes a DMM, and the real loader reads it back.
		# This is what FreeDooM2D needs — FreeDoom's music is MIDI, Doom2D's is DMM.
		& $exe --muscheck 2>&1 | Out-Null
		if ($LASTEXITCODE -ne 0) { Write-Host '  music converter FAILED' -ForegroundColor Red; $failed++ }
		else { Write-Host '  music converter ok' -ForegroundColor DarkGray }

		# Sound converter: a WAV becomes a DMI and the game's own parser reads it.
		& $exe --sndcheck 2>&1 | Out-Null
		if ($LASTEXITCODE -ne 0) { Write-Host '  sound converter FAILED' -ForegroundColor Red; $failed++ }
		else { Write-Host '  sound converter ok' -ForegroundColor DarkGray }

		# FreeDooM2D: a Doom-format WAD becomes a data set this game can run on.
		# The check builds it and then boots the game with it as the only WAD, which
		# is the only way to know that a missing sprite set is not waiting at spawn.
		$doom = Join-Path (Split-Path -Parent $root) 'refs\D2D_Also\DOOM\DOOM2.WAD'
		if (Test-Path $doom) {
			& $exe --fdcheck $doom 2>&1 | Out-Null
			if ($LASTEXITCODE -ne 0) { Write-Host '  freedoom converter FAILED' -ForegroundColor Red; $failed++ }
			else { Write-Host '  freedoom converter ok' -ForegroundColor DarkGray }
		}
		else {
			Write-Host '  freedoom converter skipped (no refs/D2D_Also/DOOM/DOOM2.WAD)' -ForegroundColor DarkGray
		}

		# And again on the real FreeDoom, because it is not the same input: Doom ships
		# MUS music, FreeDoom ships MIDI. A converter that knows only one of them
		# passes on DOOM2.WAD and produces a set with somebody else's soundtrack on
		# FreeDoom — which is precisely what happened once.
		$free = Join-Path (Split-Path -Parent $root) 'refs\freedoom\freedoom1.wad'
		if (Test-Path $free) {
			& $exe --fdcheck $free 2>&1 | Out-Null
			if ($LASTEXITCODE -ne 0) { Write-Host '  freedoom on MIDI music FAILED' -ForegroundColor Red; $failed++ }
			else { Write-Host '  freedoom on MIDI music ok' -ForegroundColor DarkGray }
		}
		else {
			Write-Host '  freedoom on MIDI music skipped (run tools/fetch-freedoom.ps1)' -ForegroundColor DarkGray
		}

		# AIDooM2D: a data set drawn entirely by code. The check builds it and boots
		# the game on it, which is the only way to know the generator still covers
		# every resource the engine asks for.
		& $exe --aicheck 1 2>&1 | Out-Null
		if ($LASTEXITCODE -ne 0) { Write-Host '  aidoom generator FAILED' -ForegroundColor Red; $failed++ }
		else { Write-Host '  aidoom generator ok' -ForegroundColor DarkGray }

		# Determinism: the same seed must produce the same bytes, or two players with
		# "the same" generated set fail the data check instead of playing.
		$a = Join-Path $env:TEMP 'ai-seed-a.wad'
		$b = Join-Path $env:TEMP 'ai-seed-b.wad'
		& $exe --genwad 7 $a 2>&1 | Out-Null
		& $exe --genwad 7 $b 2>&1 | Out-Null
		if ((Test-Path $a) -and (Test-Path $b) -and
		    ((Get-FileHash $a).Hash -eq (Get-FileHash $b).Hash)) {
			Write-Host '  aidoom determinism ok' -ForegroundColor DarkGray
		}
		else {
			Write-Host '  aidoom determinism FAILED' -ForegroundColor Red; $failed++
		}
		Remove-Item $a, $b -ErrorAction SilentlyContinue

		# The F2 Debug tab's actions. A debug tool that silently does nothing is worse
		# than none, so each action is run and its effect measured.
		& $exe --debugcheck 2>&1 | Out-Null
		if ($LASTEXITCODE -ne 0) { Write-Host '  debug actions FAILED' -ForegroundColor Red; $failed++ }
		else { Write-Host '  debug actions ok' -ForegroundColor DarkGray }

		# The BFG, through the player's own firing code. The first version of this
		# check called Weapons.BfgShot directly and passed while the gun in the game
		# produced nothing: the projectile code was fine and simply never called,
		# because the firing path returned one line early. Checking the layer below
		# the bug proves only that the layer below works.
		& $exe --bfgcheck 2>&1 | Out-Null
		if ($LASTEXITCODE -ne 0) { Write-Host '  bfg fires FAILED' -ForegroundColor Red; $failed++ }
		else { Write-Host '  bfg fires ok' -ForegroundColor DarkGray }

		# The data set the launcher chose has to be the one the game boots on. The old
		# check ran "--simulate -data doom2d", which is the default: it would have
		# passed no matter what the loader did. --setcheck asks the same method the
		# window calls, for every installed set and every spelling the config can
		# contain.
		& $exe --setcheck 2>&1 | Out-Null
		if ($LASTEXITCODE -ne 0) { Write-Host '  data set selection FAILED' -ForegroundColor Red; $failed++ }
		else { Write-Host '  data set selection ok' -ForegroundColor DarkGray }

		# And boot each set for real, not just resolve its name: a set that resolves
		# but cannot load is a window that closes on the player.
		foreach ($set in @('doom2d', 'freedoom2d', 'aidoom2d')) {
			$wad = Join-Path (Join-Path (Split-Path -Parent $exe) 'Data') ($set + '.wad')
			if (-not (Test-Path $wad)) {
				Write-Host "  data set $set skipped (not built)" -ForegroundColor DarkGray
				continue
			}

			& $exe -data $set --simulate 10 $env:TEMP\setcheck.bmp 1 2>&1 | Out-Null
			if ($LASTEXITCODE -ne 0) { Write-Host "  data set $set FAILED to boot" -ForegroundColor Red; $failed++ }
			else { Write-Host "  data set $set boots ok" -ForegroundColor DarkGray }
		}
		Remove-Item $env:TEMP\setcheck.bmp -ErrorAction SilentlyContinue

		# The DMM sequence is a program: 255 jumps, and the byte after it is the
		# offset. Reading it as "the end" replayed a block and was audible as doubled
		# music. This walks the sequence independently and compares.
		& $exe --dmmcheck 2>&1 | Out-Null
		if ($LASTEXITCODE -ne 0) { Write-Host '  dmm sequence FAILED' -ForegroundColor Red; $failed++ }
		else { Write-Host '  dmm sequence ok' -ForegroundColor DarkGray }

		# Map writer: every map in the game and in the 166 custom packs has to survive
		# a read-write-read round trip byte for byte. The editor cannot save a map
		# until this passes, because a writer that is "almost right" corrupts levels.
		& $exe --mapcheck 2>&1 | Out-Null
		if ($LASTEXITCODE -ne 0) { Write-Host '  map writer FAILED' -ForegroundColor Red; $failed++ }
		else { Write-Host '  map writer ok' -ForegroundColor DarkGray }

		# ...and a map written out has to load and play identically, not just parse.
		$savedMap = Join-Path $tmp 'saved-map.wad'
		Remove-Item $savedMap -Force -ErrorAction SilentlyContinue
		& $exe --mapsave MAP01 $savedMap 2>&1 | Out-Null
		if ($LASTEXITCODE -ne 0) { Write-Host '  mapsave FAILED' -ForegroundColor Red; $failed++ }
		else
		{
			& $exe -file $savedMap --simulate 120 (Join-Path $tmp 'saved-map.bmp') 1 '5:right' 2>&1 | Out-Null
			if ($LASTEXITCODE -ne 0) { Write-Host '  saved map does not load' -ForegroundColor Red; $failed++ }
			else { Write-Host '  map writer ok' -ForegroundColor DarkGray }
		}

		# The editor writes maps through its own save path; a map saved there has to
		# load in the game too, or the editor is a very convincing way to lose work.
		$editorExe = Join-Path $root "..\DPTDOOM2D.Editor\bin\$Configuration\net40\dptdoom2d-editor.exe"
		if (Test-Path $editorExe)
		{
			$editorMap = Join-Path $tmp 'editor-map.wad'
			Remove-Item $editorMap -Force -ErrorAction SilentlyContinue

			& $editorExe --save MAP02 $editorMap 2>&1 | Out-Null
			if ($LASTEXITCODE -ne 0 -or -not (Test-Path $editorMap)) { Write-Host '  editor save FAILED' -ForegroundColor Red; $failed++ }
			else
			{
				& $exe -file $editorMap --simulate 60 (Join-Path $tmp 'editor-map.bmp') 2 2>&1 | Out-Null
				if ($LASTEXITCODE -ne 0) { Write-Host '  editor map does not load' -ForegroundColor Red; $failed++ }
				else { Write-Host '  editor save ok' -ForegroundColor DarkGray }
			}

			# Undo has to restore the map byte for byte, and a brand new map has to be
			# playable — those are the two things a mapper does most.
			& $editorExe --undocheck 2>&1 | Out-Null
			if ($LASTEXITCODE -ne 0) { Write-Host '  editor undo FAILED' -ForegroundColor Red; $failed++ }

			$newMap = Join-Path $tmp 'new-map.wad'
			Remove-Item $newMap -Force -ErrorAction SilentlyContinue
			& $editorExe --newmap $newMap 2>&1 | Out-Null
			if ($LASTEXITCODE -ne 0) { Write-Host '  editor new map FAILED' -ForegroundColor Red; $failed++ }
			else
			{
				& $exe -file $newMap --simulate 60 (Join-Path $tmp 'new-map.bmp') 1 2>&1 | Out-Null
				if ($LASTEXITCODE -ne 0) { Write-Host '  a new map does not play' -ForegroundColor Red; $failed++ }
				else { Write-Host '  editor undo and new map ok' -ForegroundColor DarkGray }
			}
		}
		else
		{
			Write-Host '  editor not built, skipping its save check' -ForegroundColor DarkGray
		}

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

			# A mod that ships a track has to be heard. The bundled XM files in
			# Data/Music are the default soundtrack and they used to win
			# unconditionally, so a mod could ship music and the player would hear
			# the default with nothing in the log to explain it.
			$dmmSrc = Join-Path (Split-Path -Parent $root) 'refs\D2D_Also\oldprog\TC\DIGMUS'
			if (Test-Path (Join-Path $dmmSrc 'M2.DMM')) {
				Copy-Item (Join-Path $dmmSrc 'M2.DMM') (Join-Path $modSrc 'INTERMUS.dmm')
				Copy-Item (Join-Path $dmmSrc 'COOLFLUT.DMI') (Join-Path $modSrc 'COOLFLUT.dmi')

				Remove-Item $modPk3 -Force -ErrorAction SilentlyContinue
				& $exe --modbuild $modPk3 $modSrc 2>&1 | Out-Null

				$musicLog = & $exe -file $modPk3 --music INTERMUS 2>&1 | Out-String
				if ($LASTEXITCODE -ne 0 -or $musicLog -notmatch 'from mod') {
					Write-Host '  mod music does not override the bundled track FAILED' -ForegroundColor Red
					$failed++
				}
				else { Write-Host '  mod music overrides the bundled track ok' -ForegroundColor DarkGray }

				Remove-Item (Join-Path $modSrc 'INTERMUS.dmm') -Force -ErrorAction SilentlyContinue
				Remove-Item (Join-Path $modSrc 'COOLFLUT.dmi') -Force -ErrorAction SilentlyContinue
			}
			else { Write-Host '  mod music skipped (no refs/D2D_Also DIGMUS)' -ForegroundColor DarkGray }

			# A mod that ships actors.txt has to reach the actor registry through
			# the same PK3 path, or the language only works for loose files.
			Set-Content (Join-Path $modSrc 'actors.txt') 'actor Zombie { health 500 }' -Encoding ASCII
			Remove-Item $modPk3 -Force -ErrorAction SilentlyContinue
			& $exe --modbuild $modPk3 $modSrc 2>&1 | Out-Null
			$actorLog = & $exe -file $modPk3 --simulate 10 (Join-Path $tmp 'actmod.bmp') 1 2>&1 | Out-String
			if ($LASTEXITCODE -ne 0 -or $actorLog -notmatch 'wad:ACTORDEF') {
				Write-Host '  actors in pk3 FAILED' -ForegroundColor Red; $failed++
			}
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
		Copy-Item $editor $dist
		Copy-Item (Join-Path $output 'Data') (Join-Path $dist 'Data') -Recurse

		# The generated data sets travel with the release: they are build outputs,
		# not source, so they are not in the repository, and without them a player
		# has only the original artwork to choose from.
		foreach ($set in @('freedoom2d.wad', 'aidoom2d.wad'))
		{
			$generated = Join-Path $output "Data\$set"
			if (Test-Path $generated) { Copy-Item $generated (Join-Path $dist 'Data') -Force }
		}

		# GPLv2 requires the license to travel with the binaries.
		Copy-Item (Join-Path $root '..\LICENSE') $dist
		Copy-Item (Join-Path $root '..\README.md') $dist
		Copy-Item (Join-Path $root '..\CHANGELOG.md') $dist

		# Double-clickable entry points. The exes work on their own, but a game
		# folder is used by people who do not read docs: a master that needs
		# "--bind 0.0.0.0 --port 25145" might as well not have a button at all.
		Copy-Item (Join-Path $root 'tools\bat\*') $dist -ErrorAction SilentlyContinue

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

@echo off
rem DPT.DOOM 2D - open the launcher (modes, map packs, mods, servers).
rem ASCII-only, see Play.bat for why.

cd /d "%~dp0"
if not exist dptdoom2d-launcher.exe goto missing

start "" dptdoom2d-launcher.exe
exit /b 0

:missing
echo dptdoom2d-launcher.exe not found next to this file.
pause
exit /b 1

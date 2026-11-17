@echo off
rem DPT.DOOM 2D - the map editor.
rem ASCII-only, see Play.bat for why.

cd /d "%~dp0"
if not exist dptdoom2d-editor.exe goto missing

start "" dptdoom2d-editor.exe
exit /b 0

:missing
echo dptdoom2d-editor.exe not found next to this file.
pause
exit /b 1

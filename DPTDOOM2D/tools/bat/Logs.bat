@echo off
rem DPT.DOOM 2D - open the folder with the game's logs.
rem ASCII-only, see Play.bat for why.

cd /d "%~dp0"

if not exist Storage\Logs (
    echo No logs yet: Storage\Logs does not exist.
    echo Start the game once and it will be created.
    pause
    exit /b 1
)

start "" explorer.exe "%~dp0Storage\Logs"
exit /b 0

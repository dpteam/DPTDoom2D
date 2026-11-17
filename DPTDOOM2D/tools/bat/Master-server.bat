@echo off
rem DPT.DOOM 2D - master server + relay, reachable from the internet.
rem
rem The exe defaults to 127.0.0.1, which is right when only this machine talks to
rem it and wrong when players connect from outside. Here it binds 0.0.0.0.
rem
rem Ports (different from DPT.DOOM's 25135/25136 on purpose):
rem   25145/tcp  session catalogue
rem   25146/tcp  relay for players behind NAT
rem
rem Keep this window open while playing online: closing it ends every session.
rem ASCII-only, see Play.bat for why.

setlocal
cd /d "%~dp0"

set MASTER_PORT=25145
set RELAY_PORT=25146

if not exist dptdoom2d-master.exe goto missing

echo.
echo   DPT.DOOM 2D - master server
echo   ===========================
echo   catalogue  port %MASTER_PORT%
echo   relay      port %RELAY_PORT%
echo.
echo   Players outside your network need:
echo     1. router forward %MASTER_PORT%/tcp and %RELAY_PORT%/tcp to this PC
echo     2. dptdoom2d-launcher.exe - master play.dartpower.xyz:%MASTER_PORT%
echo.
echo   Leave this window open. Ctrl+C or closing it stops the server.
echo.

dptdoom2d-master.exe --bind 0.0.0.0 --port %MASTER_PORT% --relay-port %RELAY_PORT%
set RESULT=%ERRORLEVEL%

echo.
if not "%RESULT%"=="0" (
    echo The master exited with code %RESULT%.
    echo A busy port is the usual reason: another master is already running.
    echo Change MASTER_PORT / RELAY_PORT at the top of this file if you need to.
) else (
    echo The master stopped.
)
pause
exit /b %RESULT%

:missing
echo dptdoom2d-master.exe not found next to this file.
pause
exit /b 1

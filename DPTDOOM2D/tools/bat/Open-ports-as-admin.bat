@echo off
rem DPT.DOOM 2D - allow incoming connections through the Windows Firewall.
rem
rem Run this once, as administrator. Without it the master listens but packets
rem from outside are dropped, and "the server is unreachable" is the only
rem symptom you get.
rem
rem This does not forward anything on the router; that is a separate step in the
rem router's own interface.
rem
rem ASCII-only, see Play.bat for why.

cd /d "%~dp0"

net session >nul 2>&1
if not "%ERRORLEVEL%"=="0" (
    echo Requesting administrator rights...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b 0
)

echo.
echo   DPT.DOOM 2D - firewall rules
echo   ============================
netsh advfirewall firewall delete rule name="DPT.DOOM 2D - master" >nul 2>&1
netsh advfirewall firewall delete rule name="DPT.DOOM 2D - relay" >nul 2>&1
netsh advfirewall firewall delete rule name="DPT.DOOM 2D - game" >nul 2>&1

netsh advfirewall firewall add rule name="DPT.DOOM 2D - master" dir=in action=allow protocol=TCP localport=25145
netsh advfirewall firewall add rule name="DPT.DOOM 2D - relay" dir=in action=allow protocol=TCP localport=25146
netsh advfirewall firewall add rule name="DPT.DOOM 2D - game" dir=in action=allow protocol=TCP localport=5041

echo.
echo   Done. Three inbound rules are in place.
echo   Next: forward 25145/tcp and 25146/tcp on the router.
echo.
pause
exit /b 0

@echo off
rem DPT.DOOM 2D - start the game.
rem Kept ASCII-only on purpose: a .bat is read by the console's OEM code page, and
rem Cyrillic text in one turns into garbage on Windows XP and on a Russian
rem Windows where the page is 866. The filenames are ASCII for the same reason.

cd /d "%~dp0"
if not exist dptdoom2d.exe goto missing

start "" dptdoom2d.exe %*
exit /b 0

:missing
echo dptdoom2d.exe not found next to this file.
echo Run this .bat from the folder you unpacked the game into.
pause
exit /b 1

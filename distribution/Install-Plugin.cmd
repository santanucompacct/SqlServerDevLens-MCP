@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup-Plugin.ps1"
if errorlevel 1 echo Setup did not finish. Read the message above and README-FIRST.txt.
pause

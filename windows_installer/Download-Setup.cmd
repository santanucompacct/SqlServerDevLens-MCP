@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Download-Setup.ps1"
if errorlevel 1 echo Download did not finish. Read the message above and README.md.
pause

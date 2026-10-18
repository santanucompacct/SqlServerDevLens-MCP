@echo off
setlocal
set "SCRIPT_DIR=%~dp0"

if not exist "%SCRIPT_DIR%Start-DevLens-Tunnel.ps1" (
    echo ERROR: Start-DevLens-Tunnel.ps1 was not found next to this batch file.
    pause
    exit /b 1
)

rem Runs the local script only; does not change the machine-wide execution policy.
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%Start-DevLens-Tunnel.ps1"
set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" (
    echo.
    echo Tunnel launcher stopped with exit code %EXIT_CODE%.
    pause
)
endlocal & exit /b %EXIT_CODE%

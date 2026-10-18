SQL SERVER DEVLENS - AUTO-OPEN DASHBOARD UPDATE
=============================================

1. Stop the currently running tunnel by pressing Ctrl+C.
2. Extract these two .ps1 files into D:\sql_mcp\Tunnel\.
3. Allow replacement of Start-DevLens-Tunnel.ps1.
4. KEEP your current devlens-tunnel.ini (it contains your configured API key).
   Do not overwrite or share the INI. Do not modify the BAT file.
5. Double-click Start-DevLens-Tunnel.bat as usual.

The launcher now starts a short-lived hidden watcher. Once the tunnel's local
http://127.0.0.1:8080/readyz reports HTTP 200, it automatically opens the
system's default browser at http://127.0.0.1:8080/ui.
If tunnel startup fails or readiness does not occur within about 90 seconds,
no browser opens. The console remains the place to diagnose errors.

SECURITY: The watcher does not read the INI or require an API key, and is started
before the tunnel API key enters the launcher process environment.

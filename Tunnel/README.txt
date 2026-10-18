SQL SERVER DEVLENS - ONE-CLICK TUNNEL LAUNCHER
=============================================

FILES
  Start-DevLens-Tunnel.bat  - double-click this to launch
  Start-DevLens-Tunnel.ps1  - reads INI and starts tunnel-client.exe
  devlens-tunnel.ini       - local settings; ADD YOUR OWN REPLACEMENT API KEY

SETUP (ONE TIME)
  1. STOP the currently running tunnel with Ctrl+C before starting another.
  2. Copy the three files above into D:\sql_mcp\Tunnel\, next to
     tunnel-client.exe. Do NOT replace tunnel-client.exe.
  3. Open devlens-tunnel.ini with Notepad.
  4. Replace PASTE_YOUR_NEW_REPLACEMENT_API_KEY_HERE with your NEW
     restricted OpenAI runtime key (Tunnels Read + Use). Do not add quotes.
  5. Verify tunnel_id, profile, desktop_exe, and mcp_host_exe.
  6. Save the file; double-click Start-DevLens-Tunnel.bat.
  7. Keep the command window open. The Desktop app may require you to
     select/connect to the database before ChatGPT can use the tools.
  8. Check readiness at http://127.0.0.1:8080/ui.

ON EACH FUTURE USE
  Double-click Start-DevLens-Tunnel.bat. You do not need to rerun init
  or doctor unless troubleshooting. Ctrl+C stops the tunnel.

SECURITY
  - NEVER paste the API key into ChatGPT or share the completed INI.
  - Use the REPLACEMENT key; revoke any previously exposed key.
  - The completed INI is PLAINTEXT. Limit Windows file access to your
    own account, keep it out of Git/backups/shared folders, and delete
    D:\sql_mcp\apikey.txt if it is no longer needed.
  - The API key is only read into a process environment variable; it is
    not printed or passed as a literal CLI argument.
  - The launcher relies on the existing devlens.yaml profile and MCP
    executable. It does not open SQL Server ports or disable approvals.
  - Windows PowerShell is started with a PROCESS-ONLY execution-policy
    override; it does not alter system-wide execution policy.
  - If you change a tunnel or rotate a key, edit only the INI file.

The default tunnel_id and paths reflect the earlier setup; the API key
is deliberately NOT included.

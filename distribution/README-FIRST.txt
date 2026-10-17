SQL Server DevLens - Windows x64 portable package

No Visual Studio, source checkout or separately installed .NET required.
For Intel/AMD 64-bit Windows; ARM64/macOS/Linux binaries are not included.
ChatGPT Desktop with local Work/Codex/plugin access and authorized SQL/Postman access are still required. Plus or higher is the supported DevLens setup, subject to account/workspace availability.

1. Download the Windows x64 ZIP and SHA256SUMS.txt from the GitHub release.
2. Extract the ENTIRE ZIP to a permanent writable folder, such as C:\Tools\SqlServerDevLens. Do not run inside the ZIP or move the registered folder later.
3. Double-click Install-Plugin.cmd. Setup needs no administrator privileges. It uses the Codex CLI supplied with a compatible desktop installation; ChatGPT/Codex itself is not bundled.
4. Fully quit/reopen ChatGPT Desktop once. Enable SQL Server DevLens in a local chat. The DevLens SQL and Postman skill is included.
5. Double-click Start-DevLens.cmd, choose your mode and enter credentials inside DevLens. Leave the server Running.
6. Ask: Use the DevLens workflow skill to call get_server_status and check my connection.

The adapter launches its bundled EXE, not dotnet. Keep desktop/, plugins/, .agents/ and the setup/launch files together. Refresh the plugin after upgrades; ordinary Stop/Start reconnects without restarting ChatGPT.

If Codex CLI is missing, run PowerShell:
  .\Setup-Plugin.ps1 -CodexPath "C:\full\path\to\codex.exe"
Alternatively open this extracted folder as a local project in ChatGPT Desktop, choose DevLens Local in Plugins and install/enable SQL Server DevLens. If Add requests an MCP URL, cancel: this is local stdio. A prompt cannot grant missing account features.

If DevLens Local already points to another folder, setup stops and preserves it. Update/use that installation or ask a local setup assistant to review migration. No other marketplaces or personal catalogs are replaced.

The app is unsigned; Windows may show a publisher/SmartScreen warning. Verify the release source and checksum before choosing to run it. Corporate restrictions may prevent execution.

The plugin includes INSTALL.md; HELP.md contains the connection guide. Never paste credentials in chat. Profiles/optional Credential Manager entries belong to the Windows user and are not shipped. Switching ChatGPT accounts does not clear the active SQL/Postman connection.

SQL/Postman changes, procedure execution and HTTP runs still need DevLens approval. Unknown outcomes must not be retried automatically. Completed activity means the tool returned, not proof of commit.

Downloads: https://github.com/santanucompacct/SqlServerDevLens-MCP/releases
Source: https://github.com/santanucompacct/SqlServerDevLens-MCP
See LICENSE.txt and licenses/ for dependency license metadata and available notices.
No SQL data, Postman collection, saved profiles, keys or backups are shipped.

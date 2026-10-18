SQL Server DevLens 0.11.0 - Windows x64 portable package

No Visual Studio, source checkout or separately installed .NET required.
For Intel/AMD 64-bit Windows; ARM64/macOS/Linux binaries are not included.
ChatGPT Desktop with local Work/Codex/plugin access and authorized SQL/Postman access are still required. Plus or higher is the supported DevLens setup, subject to account/workspace availability.

1. Download the Windows x64 ZIP and SHA256SUMS.txt from the GitHub release.
2. Extract the ENTIRE ZIP to a permanent writable folder, such as C:\Tools\SqlServerDevLens. Do not run inside the ZIP or move the registered folder later.
3. Double-click Install-Plugin.cmd. Setup needs no administrator privileges. It uses the Codex CLI supplied with a compatible desktop installation; ChatGPT/Codex itself is not bundled.
4. Fully quit/reopen ChatGPT Desktop once. Enable SQL Server DevLens in a local chat. The DevLens SQL and Postman skill is included.
5. Double-click Start-DevLens.cmd, choose your mode and enter credentials inside DevLens. Leave the server Running.
6. Ask: Use the DevLens workflow skill to call get_server_status and check my connection. Verify get_table_schema and read_table_page are callable with read-only checks; the adapter advertises 25 tools. For SQL + Postman also check get_postman_selection.
7. If an existing chat retains old tools, start a NEW local chat and enable DevLens. The skill being available is not proof that MCP tools loaded.

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

UPGRADE: 0.11.0 supersedes 0.9.0. Stop DevLens before replacing program files. Use the COMPLETE new package; do not copy just the desktop EXE. Rerun Install-Plugin.cmd from the registered permanent folder. If using a new folder, setup stops when the existing DevLens marketplace points elsewhere; ask the local setup assistant to migrate only devlens-local, preserving other marketplaces. Source users must publish/refresh their source adapter instead. Your running desktop and registered adapter may use different folders: check the registered source before upgrading. Settings and Credential Manager secrets belong to your Windows account; never put them in the package or chat.

The new tools support large procedure/schema pages and ordered table-row pages. Row changes still require individual approvals and at most 500 direct rows per action. Individual long row values remain bounded; row paging is not a consistent export. Install validation must not execute procedures, modify data or run application HTTP requests.

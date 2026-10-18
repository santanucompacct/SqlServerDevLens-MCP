# Install or upgrade SQL Server DevLens 0.11.0

Use **one registered DevLens Local source per computer**: either a source checkout or the complete Windows portable package. Both SQL-only and SQL + Postman use `sql-server-devlens@devlens-local` and the bundled `devlens-workflow` skill. The desktop application and adapter must both be updated for new tools. Building the desktop alone does not update the installed adapter.

## New Windows installation without Visual Studio

1. Download [SqlServerDevLens-0.11.0-win-x64.zip](https://github.com/santanucompacct/SqlServerDevLens-MCP/releases/download/v0.11.0/SqlServerDevLens-0.11.0-win-x64.zip) and [SHA256SUMS.txt](https://github.com/santanucompacct/SqlServerDevLens-MCP/releases/download/v0.11.0/SHA256SUMS.txt). Verify the ZIP with `Get-FileHash -Algorithm SHA256`. The [windows_installer downloader](../windows_installer/README.md) automates download/checksum/extraction.
2. Extract the entire ZIP to a permanent writable folder. Keep `desktop`, `plugins`, hidden `.agents`, setup files and help together. This is a portable ZIP, not an MSI. Windows x64 is supported; .NET and Visual Studio are not required for this package.
3. Run **Install-Plugin.cmd**. It installs the existing plugin and its skill through the compatible desktop Codex CLI. Keep other marketplaces intact. If it reports a different existing DevLens source, follow the upgrade guidance below.
4. Fully quit/reopen the chat app and enable DevLens in a local chat. Run **Start-DevLens.cmd**; enter SQL and optional Postman details only in DevLens and leave it Running.
5. Use the read-only validation prompt below. If this chat still lacks the new tools, start a new local chat and enable the plugin there.

ChatGPT Desktop with local Work/Codex plugin access is a separate prerequisite. Plus or higher is the supported DevLens setup, subject to account and workspace availability; this is not a guarantee of local-plugin access or a universal OpenAI plan requirement. Credentials, data and private backups are not shipped.

## Existing portable installation

Stop DevLens before replacing program files. Preserve your installation choice: check which folder `devlens-local` currently registers, rather than assuming it matches the desktop EXE you launched.

- **Same permanent folder:** replace the old package program/plugin files with the complete extracted 0.11.0 package, keeping its directory structure. Rerun Install-Plugin.cmd from that folder and restart the chat app. Do not mix an old adapter with a new desktop.
- **New permanent folder:** leave the old folder available until the new package is verified. Setup deliberately stops if `devlens-local` points elsewhere. Ask a local setup assistant to inspect the existing source and migrate **only devlens-local** to the new folder, then install/enable the same plugin identity. Other marketplaces must remain intact. Do not create a duplicate plugin or hosted connector.
- **Marketplace points to a source checkout:** either update/publish that source installation below or explicitly migrate the source to the portable folder. Running the downloaded desktop does not automatically switch plugin registration.

Windows Credential Manager entries and normal per-user settings are outside the shipped package. Do not move credentials into JSON, the release ZIP or chat. Changing ChatGPT accounts does not reset the active database/collection in the running DevLens window.

## Source checkout / Visual Studio 2026

Use the repository's default branch **master**. Keep unrelated local changes; do not reset or clean the checkout to upgrade. For an existing checkout, run from the solution folder:

```powershell
git fetch origin
git switch master
git pull --ff-only origin master
dotnet restore SqlServerDevLens.slnx
dotnet build SqlServerDevLens.slnx -c Release
dotnet publish SqlServerDevLens.McpHost -c Release --no-self-contained -o plugins/sql-server-devlens/server
codex plugin add sql-server-devlens@devlens-local
```

The final command assumes DevLens Local already points to this checkout. For first installation, register this folder with `codex plugin marketplace add .` before installing the plugin. A same-named marketplace registered elsewhere requires an explicit migration of that source only. See the [plugin installation guide](../plugins/sql-server-devlens/INSTALL.md).

Open `SqlServerDevLens.slnx` in Visual Studio 2026. Select `SqlServerDevLens.Desktop` as startup project, rebuild it and use **Ctrl+F5**. Stop the old desktop instance before starting the new one. After publishing and reinstalling the adapter, fully quit/reopen the chat app and use a new local chat if its tool list remains stale.

After later source updates, repeat **publish + plugin add** whenever the adapter/tools/skill changed. The installed cache is managed by the host; never edit it directly. Ordinary DevLens Stop/Start reconnects on the next tool call and does not require reinstalling.

## Read-only validation prompt

```text
Use the installed SQL Server DevLens plugin and its devlens-workflow skill.
Call get_server_status and report the actual selected database and last SQL test status.
For SQL + Postman, also call get_postman_selection and report the workspace, collection and execution-enabled state.
Confirm get_table_schema and read_table_page are callable. Inspect a visible table's schema, identify its primary key, and read two small pages using explicit columns and the returned nextOffset. Follow schema/definition nextOffset with expectedRevision until complete; restart on revision mismatch. Respect all truncation flags. Live row pages are not a snapshot.
Do not execute procedures, change SQL/Postman resources or run HTTP requests during setup. Prepare future approval-required actions separately and wait for explicit approval in DevLens. Do not request credentials in chat. If tools are unavailable, state the limitation and do not claim success.
```

Version 0.11.0 advertises **25 tools**, including `get_table_schema`, `read_table_page` and procedure-definition paging options. A loaded skill or successful status call alone does not establish that the new tools reached this chat. Postman being configured/enabled does not establish that HTTP execution works. Never use installation testing as permission for live writes.

The former 128,000-character full-procedure/schema limit is removed; individual pages remain bounded. Row changes remain limited to 500 direct rows per separate approval. Long row values remain shortened or omitted with truncation indicators. Private schema backups are not row backups or full restore scripts.

# SQL Server DevLens MCP

**Your SQL Server, understood by AI.** An MIT-licensed .NET 10 / Avalonia desktop application with owl branding.

## Current milestone

The two-mode startup screen now opens a SQL Server login form: TCP host, port (1433), one required database, SQL authentication or Windows integrated authentication, SQL username/password, encryption (on), and trust-server-certificate (off). Test Connection uses Microsoft.Data.SqlClient and opens/disposes a connection per attempt. It does not maintain a session or execute inspection queries. Provider errors are reduced to safe messages/error numbers, without displaying passwords or connection strings.

Remember profile saves only connection metadata after a successful test. Profiles live in `Environment.SpecialFolder.LocalApplicationData/SqlServerDevLens/connections.json`, outside the repository. **Host, database and username are not encrypted**; use only an appropriate OS account/location. Forget saved profile removes it immediately. Leaving Remember profile unchecked removes a previous profile after a successful test. Password is cleared after every test and when returning to mode selection.

On Windows, check **Remember profile** and **Remember password securely**, then run a successful Test Connection. The password is saved in Windows Credential Manager for the current Windows user on this computer, and reloaded when you reopen connection setup. The JSON profile never contains it. Changing connection identity or TLS settings clears the filled password. Unchecking password remembering removes the previously saved secret after the next successful test; **Forget saved profile** removes both immediately. A failed SQL test preserves the previously saved profile/credential. If secure storage fails, the UI reports that persistence was incomplete and never falls back to JSON. Windows applications running as the same user may access generic credentials; this is not protection from a compromised Windows account. macOS Keychain and Linux Secret Service adapters remain planned, with password remembering disabled on those platforms. Passwords are briefly held as managed strings in memory and cannot be guaranteed zeroed by .NET.

After a successful SQL test, the app automatically shows the **MCP server** screen and starts an official-SDK MCP server. It displays Running or a startup error, the tested server/database, and any profile-saving warning. **Stop MCP server** asks for confirmation before closing the local listener and every desktop session. The updated plugin stays connected to ChatGPT and reports desktop_unavailable while the desktop server is stopped. Cancel, Escape or closing the confirmation dialog leaves the server running. After stopping, **Back to connection setup** lets you test again and restart. Closing the desktop window asks for a separate confirmation; confirming closes the app and stops the server without a second prompt. Cancelling leaves the app and server running. A failed SQL test stays on the login screen; a profile-saving failure does not undo a successful SQL test and its warning remains visible on the server screen.

The server currently exposes **only `get_server_status`**. It reports `status: running`, `transport: desktop-pipe`, `sqlConfigured: false`, `sqlConnected: false`, `lastSqlTestSucceeded: true`, the last SQL test timestamp, and `postmanImplemented: false`. SQL was tested and disposed, not kept connected. This milestone does not retain SQL passwords in the running server or query the database through MCP.

The plugin launches `SqlServerDevLens.McpHost.dll --desktop` as a persistent official-SDK stdio server. It exposes the status tool immediately, even if DevLens has not started. Each status call opens a fresh current-user-only named-pipe MCP session to DevLens; no TCP port, cloud endpoint, SQL credentials or cached successful status are involved. Requests have a three-second connection/handshake/call timeout.

If DevLens is stopped, restarting, busy or unreachable, the tool returns `status: desktop_unavailable`, `adapterStatus: ready`, and null last-SQL-test fields. The ChatGPT/plugin session stays alive. Start DevLens and test SQL until Running; **the next status call reconnects automatically**. You may open ChatGPT before DevLens, or stop/restart DevLens without restarting ChatGPT. Reconnection is on demand, not a background retry loop. A failed in-flight call is not automatically replayed; ask again after DevLens is ready. Caller cancellation is propagated.

The desktop accepts up to 16 independent sessions, allowing concurrent discovery/chat status calls. An idle client does not block others; at capacity extra requests may time out and report unavailable. Stop closes all desktop sessions; only one DevLens server instance may own the endpoint. Standalone host invocation without `--desktop` remains an isolated status smoke test. Logs/errors use stderr; stdout is reserved for MCP JSON messages. Installing/upgrading the plugin itself still requires a one-time ChatGPT desktop reload.
**Postman integration remains planned**, including when SQL Server + Postman is selected. SQL metadata/data tools, macOS/Linux secure password persistence and distributable cross-platform installers are not implemented.

## First-time help and credits

The **Help: connect to ChatGPT Desktop** button is available on every screen. It opens a compact guide covering SQL connection testing, local plugin preparation/installation, the status-call prompt and troubleshooting, with links to this README and official plan details.

The guide uses **ChatGPT Plus or higher as the supported DevLens setup**. This is project guidance, not a universal OpenAI subscription minimum: current OpenAI documentation includes local Work/Codex on other plans, and actual local-plugin availability depends on account, app version and workspace policy. Check [OpenAI plan details](https://learn.chatgpt.com/docs/pricing) before subscribing.

The startup page thanks ChatGPT and OpenAI for development assistance.

## Pull and run in Visual Studio 2026

Install Visual Studio 2026 with .NET desktop development and a .NET 10 SDK. Avalonia packages restore from NuGet; a paid extension is not required. From your existing clone:

```powershell
git fetch origin
git switch feature/sql-login-mcp-status
git pull --ff-only origin feature/sql-login-mcp-status
dotnet restore SqlServerDevLens.slnx
dotnet build SqlServerDevLens.slnx -c Release
dotnet test SqlServerDevLens.slnx -c Release --no-build
```

For a new clone, first run `git clone https://github.com/santanucompacct/SqlServerDevLens-MCP.git` and enter the cloned directory. After the PR is merged, use `master` instead of the feature branch.

Open `SqlServerDevLens.slnx` in Visual Studio 2026. Set `SqlServerDevLens.Desktop` as the startup project, then press F5. Choose either mode and Continue. Enter a TCP host/IP, port and a single database. Named instances must be resolved to their TCP port; do not enter a connection string or `server\instance` in Host. SQL authentication is the default; check Windows authentication to use the current process identity. On macOS/Linux integrated authentication requires correctly configured Kerberos and is not equivalent to entering Windows credentials.

Use Test Connection with an account authorized for the chosen database. Keep encryption enabled and certificate trust disabled with a valid server certificate. For an intentionally self-signed test server, choosing Trust server certificate bypasses certificate validation. A success means that SqlClient opened the selected database. It then closes that SQL connection and opens the desktop MCP control screen. The MCP server starts automatically; client attachment is separate, and there are no SQL inspection tools yet. Test wrong credentials, invalid database, unreachable port, and certificate failure on your own test server. Do not paste credentials into chats, JSON, command arguments or source files.

## Test the local MCP plugin

From the repository root in PowerShell 7:

```powershell
dotnet build SqlServerDevLens.slnx -c Release
./scripts/Test-Mcp.ps1
dotnet publish SqlServerDevLens.McpHost -c Release --no-self-contained -o plugins/sql-server-devlens/server
./scripts/Test-Mcp.ps1 -HostDll ./plugins/sql-server-devlens/server/SqlServerDevLens.McpHost.dll
# With DevLens open and the MCP server showing Running, test its desktop adapter:
./scripts/Test-Mcp.ps1 -Desktop -HostDll ./plugins/sql-server-devlens/server/SqlServerDevLens.McpHost.dll
# With the desktop server stopped, verify the plugin still responds:
./scripts/Test-Mcp.ps1 -ExpectUnavailable -HostDll ./plugins/sql-server-devlens/server/SqlServerDevLens.McpHost.dll
```

The smoke script launches the compiled host directly, sends initialize, tools/list and tools/call messages, checks the status response, and shuts it down. It fails on non-JSON stdout and bounds response waits. The published server directory is ignored by Git. This is a framework-dependent local package requiring .NET 10 on the target machine; do not launch through `dotnet run`, which can print build messages onto the protocol stream.

The repository includes portable `plugins/sql-server-devlens/plugin.json` and `mcp.json`, a `.codex-plugin/plugin.json` compatibility manifest explicitly referencing `./mcp.json`, and a repo-scoped `.agents/plugins/marketplace.json`. The compatibility manifest is required by the tested Codex 0.153.0 loader: with only the portable manifest it reported the plugin installed/enabled but discovered zero MCP servers. Keep both manifest versions synchronized. Both portable JSON files must retain their Agent Plugins `$schema` declarations: the tested loader needs these to normalize `${PLUGIN_ROOT}` in launch arguments and working directory. Missing declarations caused Windows startup error 267 (invalid directory) even after the server was discovered. Version 0.3.2 fixes that; `codex mcp get sql-server-devlens --json` should show fully resolved existing paths. The MCP configuration uses `dotnet` with `${PLUGIN_ROOT}/server/SqlServerDevLens.McpHost.dll --desktop`, so the published server and dependencies must be present **before installation**.

1. Open this repository as a local project in the ChatGPT desktop app's Work/Codex local execution surface.
2. Restart the desktop app to discover the repo marketplace. In Plugins Directory select **DevLens Local** and install **sql-server-devlens**. Availability depends on the account/client; absence of this source is not proof the MCP host failed.
3. Run the DevLens desktop app, successfully test your SQL connection, and leave the MCP server screen showing **Running**. In a locally executed chat enable/mention the plugin and ask: “Call get_server_status from SQL Server DevLens and show the result.”
4. Expect `status: running`, `transport: desktop-pipe`, `sqlConfigured: false`, `sqlConnected: false`, `lastSqlTestSucceeded: true`, the last SQL test timestamp, and `postmanImplemented: false`. Click **Stop MCP server** and confirm. Ask for status in the same chat: expect `desktop_unavailable`. Return to setup and test SQL again, then ask for status in that same chat: expect `running` with the new test timestamp. No ChatGPT restart is needed between these steps. An old cached plugin without `--desktop` only returns `ready`; an old byte-forwarding adapter may disconnect on Stop. Upgrade the installed package to 0.3.4.
5. After republishing/changing plugin files, refresh the installed copy with `codex plugin add sql-server-devlens@devlens-local` and fully restart the app; installed plugins are cached copies. `codex plugin list --marketplace devlens-local --json` should show the installed plugin enabled. This alone does not prove MCP discovery; the plugin loader must also discover the `sql-server-devlens` server. Version 0.3.1 adds the compatibility manifest to fix zero-server discovery; version 0.3.2 adds explicit portable schemas to fix launch-path normalization; version 0.3.3 supports concurrent discovery/chat sessions. After updating to 0.3.3, rebuild and restart the **DevLens desktop application**, then test SQL again to start the updated server. Updating the plugin adapter alone does not replace code inside an already-running desktop server. Version 0.3.4 adds the persistent adapter and reconnection on each status call; publish/reinstall it and restart ChatGPT Desktop once to replace the old adapter. Subsequent DevLens starts/stops do not require a ChatGPT restart.

These steps follow the official [local plugin packaging/marketplace guide](https://developers.openai.com/plugins/build/plugins) and [stdio configuration guide](https://developers.openai.com/plugins/deploy/desktop-only). Public-directory submission is a separate approval process; this repository does not claim account eligibility or a verified ChatGPT installation. No Azure endpoint or OpenAI API key is required by this host.

## Validation and boundaries

Verified locally on Windows with .NET SDK 10.0.402: restore; Release solution build with zero warnings/errors; core safety tests plus password opt-in/reload/forget, opt-out, failed SQL and credential-store failure tests; a Windows-only native credential create/read/update/delete test uses a unique fake secret and cleans it up; real stdio initialize/list/call smoke test. Lifecycle tests cover successful/failed SQL navigation with a fake SQL tester, server startup failure, real named-pipe MCP sessions, reconnect/restart, duplicate listener rejection, concurrent Stop calls, idle-client isolation, concurrent discovery/tool calls, full session-capacity reuse, multiple adapter processes, a persistent stdio adapter discovered before desktop startup, three Stop/start cycles within the same client session, unavailable status without stale data, caller cancellation, and stalled-handshake timeout/recovery. The user reported a successful SQL connection. A native ChatGPT desktop `get_server_status` call through the earlier installed DevLens plugin has succeeded. Automatic reconnection in the current version is verified with real MCP client sessions and adapter processes; the user-facing ChatGPT stop/start cycle should be checked after reloading the updated plugin. The agent has not independently queried the SQL Server. Headless UI tests verify Stop/Close confirm and cancel paths, repeated-action guards, dialog buttons/defaults, and help-window loading; manual visual QA remains pending.

GitHub Actions runs restore/build/tests and the stdio smoke script on Windows, Ubuntu and macOS. Results must be checked on the PR before claiming those platforms pass. CI does not contact a SQL Server.

## Structure and next milestones

- `SqlServerDevLens.Desktop`: Avalonia MVVM startup and login UI.
- `SqlServerDevLens.Core`: connection/profile DTOs and interfaces, SqlClient tester, credential-free JSON storage.
- `SqlServerDevLens.Runtime`: desktop-owned official-SDK server, current-user named pipe and per-call desktop status client.
- `SqlServerDevLens.McpHost`: plugin stdio adapter with `--desktop`, or isolated standalone status smoke host.
- `SqlServerDevLens.Tests`: core safety tests.
- `plugins/`, `.agents/plugins/`: local plugin package and marketplace.
- `scripts/Test-Mcp.ps1`: protocol smoke test.

Next: verified SQL integration tests and UI QA; macOS/Linux credential adapters; SQL session configuration for future inspection tools; controlled read-only metadata tools; optional Postman work later. Use least-privileged SQL accounts and enforce database access in SQL Server. Never commit credentials, customer data, local profiles or tokens. There is no unrestricted SQL execution tool.

Licensed under [MIT](LICENSE.txt). Third-party tools retain their own licenses.

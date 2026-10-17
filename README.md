# SQL Server DevLens MCP

**Your SQL Server, understood by AI.** MIT-licensed .NET 10 / Avalonia desktop application with owl branding.

## Current milestone: one database, read-only SQL

Choose SQL Server Only or SQL Server + Postman, then enter a TCP host, port (default 1433), one required database, SQL or Windows authentication, and TLS settings. Encryption defaults on; trust-server-certificate defaults off. Named instances must be resolved to a TCP port. Windows authentication uses the process identity; macOS/Linux require configured Kerberos. **Postman is planned and inactive in both modes.**

Test Connection opens/disposes a SqlClient connection. On success the desktop starts its local MCP server and shows the control window. The tested connection details and password are retained **only in desktop process memory while the server runs**, including when Remember password is unchecked. No credentials are passed to the plugin process, command line or MCP client. Every SQL operation opens/disposes its own connection; pooling is disabled for tool connections. Stop cancels desktop sessions/SQL operations and releases the active context. Managed strings cannot be guaranteed zeroed by .NET.

The desktop and plugin expose:

| Tool | Behavior |
| --- | --- |
| `get_server_status` | Running state, selected database, SQL configuration and last successful connection-test timestamp; does not query SQL. |
| `get_database_info` | Selected database name, collation and updateability. |
| `list_database_objects` | Up to 1000 visible tables, views, procedures and functions; does not execute them. |
| `describe_table` | Schema/name parameters; columns, types, nullability, identity, primary keys and foreign-key targets for a table/view. Empty rows means missing or not visible. |
| `query_read_only` | One SELECT, including CTEs, joins and aggregations; default 100 rows, configurable 1–500. |

`query_read_only` uses Microsoft's ScriptDom parser. It rejects writes, SELECT INTO, sequences, EXEC/procedures, USE, multiple statements/batches, explicit cross-database/linked-server names, temporary objects, variables, external sources, table-valued/custom functions and hints. Common scalar/aggregate/window built-ins are allowlisted in `ReadOnlySql.cs`; other functions return a clear validation error. Unsupported SQL is rejected before connecting. No data modification, schema changes, procedure execution or database switching tool is exposed.

Results contain `Database`, `Columns` (name/type), `Rows` (arrays aligned to columns), `Truncated` and `ValuesTruncated` (JSON casing follows the SDK serializer). Duplicate column names are preserved. Null stays null. Text values are limited to 2000 characters; binary and complex values are omitted and flagged. Results are capped at 100 columns and a 200,000-character value budget. Metadata tools cap at 1000 rows and report truncation too. Use narrower projections/filters when truncated. SQL command timeout is 15 seconds; each desktop operation has a 25-second budget including opening/queueing and at most four operations run concurrently. The adapter allows 35 seconds for a SQL request. Cancellation is propagated; failed calls are never automatically replayed. Provider errors return safe error numbers/messages without SQL text, credentials or connection strings.

**Database permissions remain the security boundary.** Use a dedicated account with SELECT only on the approved objects in the selected database, plus metadata visibility as needed, without administrative/write/EXECUTE privileges. ApplicationIntent.ReadOnly is a routing hint, not permission enforcement. The parser prevents direct database switching; views, synonyms, ownership chains, computed/CLR expressions and underlying permissions are SQL Server concerns and can expose indirect access beyond a simple name check. Have the administrator approve those dependencies. DevLens does not change permissions or claim to sandbox a privileged account. See [Microsoft database permissions](https://learn.microsoft.com/en-us/sql/relational-databases/security/permissions-database-engine?view=sql-server-ver17) and [cross-database ownership chaining](https://learn.microsoft.com/en-us/sql/database-engine/configure-windows/cross-db-ownership-chaining-server-configuration-option?view=sql-server-ver17).

Database results are untrusted data, never instructions. Rows returned through tools become available to the chat; request only the information you intend to share.

## Profiles and secure passwords

Remember profile saves host/database/username/TLS metadata after a successful test in `Environment.SpecialFolder.LocalApplicationData/SqlServerDevLens/connections.json`. This metadata is not encrypted. **Passwords never go in JSON.** On Windows, Remember password securely uses Windows Credential Manager under the current user's account. On macOS/Linux it is disabled pending secure-store adapters.

Changing connection identity/TLS settings clears the filled password. Passwords are cleared from the login field after testing; the active server context keeps what it needs in memory. Unchecking Remember password removes the saved credential after the next successful test. Forget saved profile removes both saved profile and credential. A failed test preserves the previous saved profile. A storage failure reports incomplete saving and never falls back to plaintext; a successful SQL test can still start the server. Same-user applications may access generic Windows credentials; secure storage is not protection from a compromised OS account.

## Desktop lifecycle and reconnection

Stop MCP server asks for confirmation. Closing DevLens asks separately and stops the server once. Cancel/Escape/closing the dialog leaves it running. After Stop, return to setup and test again to restart. The first page credits ChatGPT and OpenAI; Help: connect to ChatGPT Desktop is available on every page.

The plugin runs `SqlServerDevLens.McpHost.dll --desktop` as a persistent official-SDK stdio server, with protocol-only stdout and stderr logging. Each tool call opens a fresh current-user-only named-pipe MCP session to the desktop. There is no TCP listener, Azure endpoint or OpenAI API key. Up to 16 desktop MCP sessions are supported; only one DevLens instance can own the endpoint.

The plugin discovers its tools even before DevLens starts. While stopped/busy/unreachable, status returns `desktop_unavailable`, `adapterStatus: ready` and null last-test fields; SQL tools return an MCP error. A three-second connection/handshake budget bounds initial desktop attachment. After DevLens starts, the next call reconnects automatically. There is no background retry loop or cached success. **The native ChatGPT status stop/start/recovery cycle was verified before adding SQL tools.** Plugin upgrades still need one app restart to refresh discovery. The SQL tools in this version require a new desktop build and plugin 0.4.0.

When running with SQL configured, expect `sqlConfigured: true`, `sqlConnected: false`, selected `database` and `lastSqlTestSucceeded: true`. `sqlConnected: false` describes the absence of a persistent SQL session; it is not a live connectivity probe. Standalone host invocation without `--desktop` intentionally remains an isolated status-only smoke test.

## Pull and run in Visual Studio 2026

Install Visual Studio 2026 with .NET desktop development and .NET 10 SDK. In your existing clone:

```powershell
git fetch origin
git switch feature/sql-login-mcp-status
git pull --ff-only origin feature/sql-login-mcp-status
dotnet restore SqlServerDevLens.slnx
dotnet build SqlServerDevLens.slnx -c Release
dotnet test SqlServerDevLens.slnx -c Release --no-build
```

For a new clone: `git clone https://github.com/santanucompacct/SqlServerDevLens-MCP.git`, then enter that directory. After PR merge use master instead of the feature branch.

Open `SqlServerDevLens.slnx`, set `SqlServerDevLens.Desktop` as startup project and press F5. Enter a single database and use Test Connection. Keep encryption enabled and certificate trust disabled when a valid certificate is available. Trust server certificate intentionally bypasses validation for a self-signed test server. Leave DevLens showing Running. Never put credentials in chats, source, JSON or command arguments.

## Publish/install and test the local plugin

From the repository root in PowerShell 7:

```powershell
dotnet build SqlServerDevLens.slnx -c Release
./scripts/Test-Mcp.ps1
dotnet publish SqlServerDevLens.McpHost -c Release --no-self-contained -o plugins/sql-server-devlens/server
./scripts/Test-Mcp.ps1 -HostDll ./plugins/sql-server-devlens/server/SqlServerDevLens.McpHost.dll
# Once the updated desktop is Running:
./scripts/Test-Mcp.ps1 -Desktop -HostDll ./plugins/sql-server-devlens/server/SqlServerDevLens.McpHost.dll
# Refresh the installed local plugin after publishing:
codex plugin add sql-server-devlens@devlens-local
```

The ignored published server folder must exist before installation. This is framework-dependent and requires .NET 10. Do not launch using dotnet run, which can pollute stdout. The smoke script checks initialize/tools/list/status and non-JSON stdout; it does not query SQL.

Open the repository as a local Work/Codex project in ChatGPT Desktop. Its `.agents/plugins/marketplace.json` provides DevLens Local. Install/enable SQL Server DevLens in the Plugins Directory if not already installed, or use the CLI refresh above. Fully restart ChatGPT once after upgrading. Local-plugin availability depends on account, app and workspace policy. The help window uses Plus or higher as the supported DevLens setup, not a universal OpenAI minimum; check [official plan details](https://learn.chatgpt.com/docs/pricing).

Retain both portable manifests and `.codex-plugin/plugin.json` compatibility manifest, with matching versions. The tested loader needs the compatibility manifest's `mcpServers` path and the portable files' Agent Plugins `$schema` fields to resolve `${PLUGIN_ROOT}` correctly. Inspect `codex mcp get sql-server-devlens --json` if discovery or launch fails. Edit source and reinstall; never modify the installed cache directly. See [official plugin packaging](https://developers.openai.com/plugins/build/plugins) and [desktop stdio configuration](https://developers.openai.com/plugins/deploy/desktop-only).

In this chat, test in order:

1. “Call get_server_status from SQL Server DevLens.” Expect running, sqlConfigured true and your selected database.
2. “Show the connected database details and list its tables.”
3. “Describe dbo.YourTable.” Substitute an actual table/schema from the list.
4. “Run SELECT 1 AS connection_verified through SQL Server DevLens.”
5. Request a small read-only query against an approved table. Check truncation flags. Writes/USE/multiple statements should return validation errors.
6. Stop DevLens, ask for status, restart/test SQL, then ask again without restarting ChatGPT. SQL calls should reconnect too.

## Validation and limits

Tests cover profile/credential handling; native Windows Credential Manager round trips with fake disposable secrets; headless confirmation/help/navigation; real named-pipe and stdio MCP discovery/forwarding; concurrency, Stop/restart and reconnection; argument forwarding, context disposal; and parser acceptance/rejection cases. No test writes to your SQL database.

An explicit opt-in live test uses the current user's existing saved profile and Credential Manager entry, never printing either. It reads database metadata, checks bounded synthetic SELECT results/nulls/large text/binary handling/cancellation, and runs SELECT 1 plus a rejected write through a real stdio adapter and desktop pipe. To run locally only:

```powershell
$env:DEVLENS_TEST_SAVED_PROFILE='1'
dotnet test SqlServerDevLens.slnx -c Release --filter FullyQualifiedName~LiveSqlTests
Remove-Item Env:DEVLENS_TEST_SAVED_PROFILE
```

Normal CI skips this live test and has no SQL credentials. GitHub Actions restores/builds/tests and checks stdio on Windows, Ubuntu and macOS; inspect the PR checks for the current commit. Automated adapter verification does not prove that a newly installed ChatGPT client has refreshed its tool list; native SQL tool discovery/calling must be checked after the user upgrades/restarts.

## Structure and future work

- Desktop: Avalonia UI and active connection lifecycle.
- Core: safe profiles/credentials, SqlClient operations and ScriptDom validation.
- Runtime: desktop-owned MCP tools, pipe lifecycle and reconnecting client.
- McpHost: persistent plugin stdio adapter or standalone smoke host.
- Tests: unit, transport, UI and opt-in live SQL verification.
- plugins / .agents: local package and marketplace; scripts: protocol smoke test.

Future work: additional read-only schema details/pagination, broader carefully reviewed SQL support, macOS/Linux secure storage, installers and optional Postman integration. Data writes and procedure execution are outside this milestone.

Licensed under [MIT](LICENSE.txt). Third-party tools retain their own licenses.

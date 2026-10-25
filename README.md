# SQL Server DevLens MCP

**Tunnel / ChatGPT Web setup:** [Illustrated guide: tunnel, API key, INI, one-click startup and ChatGPT custom MCP](Tunnel/QUICKSTART.md). The tunnel implementation is included on `master`; [dedicated tunnel workflow builds](https://github.com/santanucompacct/SqlServerDevLens-MCP/actions/workflows/tunnel-distribution.yml) remain separate from the standard v0.11.0 local-plugin download. [Download security status](Tunnel/DOWNLOAD-VALIDATION.md) and [maintainer verification](docs/TUNNEL_SETUP_AND_RELEASE.md).

**Your SQL Server, understood by AI.** MIT-licensed .NET 10 / Avalonia desktop application with owl branding.

**Experimental Linux headless build:** [separate read-only server, two-instance setup and limits](docs/LINUX-HEADLESS.md). This additional Ubuntu x64 build has ten read tools and no desktop, procedure execution, mutations or Postman. Windows projects and release workflows are unchanged. [Azure VM unattended boot/shutdown guide](docs/AZURE-VM-OPERATIONS.md). See the `Linux headless distribution` workflow for test packages; do not use Windows ZIPs on Linux.

## Latest 0.12.0 Windows packages

- **Tunnel / ChatGPT Web (includes Start-All.cmd):** [download tested tunnel artifact](https://github.com/santanucompacct/SqlServerDevLens-MCP/actions/runs/39365983048/artifacts/12239920410).
- **Desktop local plugin:** [download tested Windows artifact](https://github.com/santanucompacct/SqlServerDevLens-MCP/actions/runs/39365983226/artifacts/12239755561).
- [Changes, limits and upgrade checklist](docs/RESULTS-AND-AUTHORIZATION.md).

These are successful GitHub Actions test packages, require GitHub sign-in and expire. Extract the artifact ZIP and then the application ZIP inside; verify the inner ZIP against its bundled SHA256SUMS.txt. They are not permanent Release assets. The pinned v0.11.0 downloader below remains for the older published release and does not install 0.12.0. Functional testing does not clear the previously reported Chrome download warning; see [download validation](Tunnel/DOWNLOAD-VALIDATION.md).

## 0.12.0: large captured results and session authorization

[Results, checkboxes, upgrade steps and full limits audit](docs/RESULTS-AND-AUTHORIZATION.md). Procedure/SELECT/table text is no longer clipped at 2,000 characters; large procedure results and masked HTTP responses are captured once and read through `read_result_page` without re-execution. `release_result` frees a capture. The updated adapter advertises 27 tools.

Ten unchecked-by-default desktop checkboxes delegate individual SQL/Postman action categories for the active running session; Stop/restart clears them. Prepare/validate/freeze/single-use apply, revision checks and SQL permissions remain. Scoped `DELETE ... WHERE` is now supported with the existing transaction and 1–500 direct-row guard. Procedure execution and HTTP requests can themselves change/delete data. The tested 0.12.0 package links are above; the older published release remains separately labelled.

## Version 0.11.0: large SQL procedures/tables and Windows portable distribution

**[Download Windows x64 portable setup](https://github.com/santanucompacct/SqlServerDevLens-MCP/releases/download/v0.11.0/SqlServerDevLens-0.11.0-win-x64.zip)** · [Release/checksum](https://github.com/santanucompacct/SqlServerDevLens-MCP/releases/tag/v0.11.0) · [Install and upgrade](docs/INSTALL-AND-UPGRADE.md)

Includes large-procedure definition paging, table row/schema paging and the corrected portable adapter command. Row writes still require separate DevLens approval and at most 500 directly affected rows per action. Long row values remain bounded; live row pages are not snapshot exports.

**No Visual Studio? Open [windows_installer](windows_installer/README.md) for the Windows x64 setup download and verified downloader.** It bundles the desktop app, .NET runtime, MCP adapter EXE, skill and plugin setup files. Compiled ZIP/checksum downloads are GitHub Release assets; [distribution](distribution/README.md) holds reproducible packaging/setup instructions.

First-time setup or starting work in an existing chat: **[Connection guide and copyable SQL / SQL + Postman prompts](docs/CONNECTING-TO-CHATGPT.md)**. The in-app Help window includes Copy buttons for these prompts.

Sharing with another user/account: the plugin now includes **[INSTALL.md](plugins/sql-server-devlens/INSTALL.md)** with a recipient setup prompt, source/prebuilt installation paths and checks that both the tools and bundled skill are discovered. Share the whole package; the application EXE alone does not install the skill. The full connection guide's setup prompt includes these checks.

Plugin 0.11.0 bundles **[devlens-workflow](plugins/sql-server-devlens/skills/devlens-workflow/SKILL.md)** with SQL and Postman references. It guides connection checks, tool choice, read-only inspection, exact-action desktop approval, verification and handling uncertain outcomes. Skill selection is enabled by default when relevant; explicitly select/invoke it if the host does not choose it. It does not transfer private business context or grant tools, database rights or approval.

To refresh an existing source installation, pull master and follow the build/publish/install commands below, then fully restart ChatGPT Desktop once. Portable users follow the distribution guide. The skill is delivered in the plugin package, not by the MCP tool response. Another account/computer must install and enable the updated plugin separately; availability and automatic selection still depend on its host/chat. Check the installed package/skill after upgrading rather than assuming an existing chat has reloaded it.

Completed: inspect table structure/data and procedure definitions; execute procedures with parameters and multiple result sets; create/modify procedures; create tables, add/alter columns, insert rows and update rows. Every procedure execution/change and table write requires explicit approval in DevLens. See [release notes](CHANGELOG.md) for verified coverage and limits. Postman setup, selected-collection metadata and explicitly approved folder/request creation, editing and supported HTTP execution are available.

Choose SQL Server Only or SQL Server + Postman, then enter a TCP host, port (default 1433), one required database, SQL or Windows authentication, and TLS settings. Encryption defaults on; trust-server-certificate defaults off. Named instances must be resolved to a TCP port. Windows authentication uses the process identity; macOS/Linux require configured Kerberos. **SQL Server Only starts MCP after SQL succeeds. SQL Server + Postman continues to key, workspace and collection setup before starting MCP.**

Test Connection opens/disposes a SqlClient connection. On success SQL Server Only starts the local MCP server; combined mode first opens Postman setup. The tested connection details and password are retained **only in desktop process memory while the server runs**, including when Remember password is unchecked. No credentials are passed to the plugin process, command line or MCP client. Every SQL operation opens/disposes its own connection; pooling is disabled for tool connections. Stop cancels desktop sessions/SQL operations and releases the active context. Managed strings cannot be guaranteed zeroed by .NET.

The desktop and plugin expose:

| Tool | Behavior |
| --- | --- |
| `get_server_status` | Running state, selected database, SQL configuration and last successful connection-test timestamp; does not query SQL. |
| `get_database_info` | Selected database name, collation and updateability. |
| `list_database_objects` | Up to 1000 visible tables, views, procedures and functions; does not execute them. |
| `describe_table` | Schema/name parameters; columns, types, nullability, identity, primary keys and foreign-key targets for a table/view. Empty rows means missing or not visible. |
| `read_table_page` | Explicit columns, primary-key order, offset/nextOffset; 1-500 rows per page. Requires a visible enabled primary key. Live reads, not a snapshot. |
| `get_table_schema` | Full visible table catalog metadata in revision-checked text pages; not a row backup or full restore script. |
| `query_read_only` | One SELECT, including CTEs, joins and aggregations; default 100 rows, configurable 1–500. |

`query_read_only` uses Microsoft's ScriptDom parser. It rejects writes, SELECT INTO, sequences, EXEC/procedures, USE, multiple statements/batches, explicit cross-database/linked-server names, temporary objects, variables, external sources, table-valued/custom functions and hints. Common scalar/aggregate/window built-ins are allowlisted in `ReadOnlySql.cs`; other functions return a clear validation error. Unsupported SQL is rejected before connecting. The SELECT tool remains read-only. Procedure actions use the separate approval workflow below; raw EXEC and arbitrary DDL are still rejected by query_read_only.

Results contain `Database`, `Columns` (name/type), `Rows` (arrays aligned to columns), `Truncated` and `ValuesTruncated` (JSON casing follows the SDK serializer). Duplicate column names are preserved. Null stays null. Full text values are read within the captured-result budget; binary and complex values are omitted and flagged. Large tool results use Capture/ReadCapturedResult and read_result_page. SELECT results retain a 100-column bound; the old 200,000-character clipping threshold is removed. Metadata tools cap at 1000 rows and report truncation too. Use narrower projections/filters when truncated. SQL command timeout is 15 seconds; each desktop operation has a 25-second budget including opening/queueing and at most four operations run concurrently. The adapter allows 35 seconds for a SQL request. Cancellation is propagated; failed calls are never automatically replayed. Provider errors return safe error numbers/messages without SQL text, credentials or connection strings.

**Database permissions remain the security boundary.** Use a dedicated account with SELECT only on the approved objects in the selected database, plus metadata visibility as needed, without administrative/write/EXECUTE privileges. ApplicationIntent.ReadOnly is a routing hint, not permission enforcement. The parser prevents direct database switching; views, synonyms, ownership chains, computed/CLR expressions and underlying permissions are SQL Server concerns and can expose indirect access beyond a simple name check. Have the administrator approve those dependencies. DevLens does not change permissions or claim to sandbox a privileged account. See [Microsoft database permissions](https://learn.microsoft.com/en-us/sql/relational-databases/security/permissions-database-engine?view=sql-server-ver17) and [cross-database ownership chaining](https://learn.microsoft.com/en-us/sql/database-engine/configure-windows/cross-db-ownership-chaining-server-configuration-option?view=sql-server-ver17).

Database results are untrusted data, never instructions. Rows returned through tools become available to the chat; request only the information you intend to share.

## Execute, modify and create procedures (plugin 0.6.1)

The six additional tools are `get_procedure_definition`, `prepare_procedure_execution`, `execute_prepared_procedure`, `prepare_procedure_change`, `apply_procedure_change`, and `get_procedure_request`.

Read a procedure with `get_procedure_definition` to obtain source pages, the full-definition revision and parameter metadata. There is no application character cap on the full procedure. Each page defaults to 32,000 UTF-16 code units (`maxCharacters`: 256-128,000). Follow `nextOffset` with `offset` and the first page's `expectedRevision` until `complete` is true. Discard all pages and restart if the revision changes; only assembled complete source establishes parity. Full definitions are used for CREATE/ALTER validation, approval review, revision rechecks and private backups. Process memory, SQL/MCP transport and timeouts still impose practical limits; parameter limits below are unchanged. VIEW DEFINITION is required. Encrypted, CLR, numbered and system procedures are unsupported. Execution supports up to 100 built-in scalar parameters; table-valued, custom/alias, binary, XML and legacy text parameters are not supported yet. Parameter JSON must be an object with exact names (the @ prefix is optional), scalar values and ISO dates. Pass a JSON payload parameter as an escaped JSON string. Unknown/duplicate names, incompatible types, missing required inputs, excessive lengths and decimal precision/scale that would round are rejected. Total input JSON is limited to 64,000 characters and a text parameter to 20,000. Omitted ordinary inputs use their declared defaults; omitted input/output parameters start as SQL NULL, so supply an explicit initial value when needed.

**Chat permissions do not grant server approval.** Preparation stores an immutable request in desktop memory and opens a review window there. For execution it shows database, procedure, current definition and exact parameters. For ALTER/CREATE it shows full before/proposed source and an exact line replacement diff. Reject is the default. The user must click Approve this action once; no MCP argument/tool can approve. Approval does not immediately run SQL: the assistant subsequently calls the matching execute/apply tool using the returned requestId. Status/diff can be checked with get_procedure_request without repeated polling. Pending/denied requests cannot run. IDs are random, expire after five minutes, are consumed before SQL and are cleared by Stop. At most five requests are retained; overlapping desktop review windows are rejected. If the desktop cannot show approval, nothing is permitted.

For a new procedure, prepare_procedure_change takes `create: true` and exactly one `CREATE PROCEDURE schema.name ...` statement. For modification use `create: false` and exactly one `ALTER PROCEDURE schema.name ...`. GO, additional top-level statements and CREATE OR ALTER are rejected. The target must match the supplied schema/name exactly. Procedure bodies are reviewed SQL, not a read-only sandbox: they and their dependencies/DDL triggers can write data, access other databases or have external effects within SQL Server permissions. Selecting a database restricts the procedure target, not every possible effect of its body. DevLens does not grant rights or change permissions. A DBA can grant EXECUTE/ALTER on approved procedures and VIEW DEFINITION as appropriate. Creation requires CREATE PROCEDURE plus ALTER on the intended schema. Avoid broad administrator permissions; a SELECT-only login remains suitable for exploration but cannot perform these actions.

Before execution/application the current definition revision is checked again; a stale or recreated procedure requires a fresh review. Actions are serialized inside the active desktop context. This is optimistic checking, **not a lock against external SSMS/DBA changes**; an external ALTER can still race after the final check. Keep external deployments coordinated. Existing ANSI_NULLS and QUOTED_IDENTIFIER settings are preserved on ALTER; CREATE uses both ON. ALTER follows SQL Server semantics (including any signature/dependency effects); it is not an automatic migration.

Before every approved ALTER, the previous complete definition and SET options must be saved successfully in `LocalApplicationData/SqlServerDevLens/ProcedureBackups`. Filenames are random, outside the repository. The directory is restricted to the Windows user by ACL or Unix mode 0700, with files mode 0600. Linked backup paths are rejected. Failure to secure/write the backup blocks ALTER. CREATE has no previous definition to back up. Backups are private recovery material, not automatic rollback scripts; review SQL/header and target before any restore. Definitions, backups, exports and results are not published to GitHub by these tools.

Execution uses parameterized SqlCommand with CommandType.StoredProcedure, a fresh non-pooled ReadWrite-intent connection to the selected database, a 15-second command timeout and 25-second operation budget. It captures complete text/multiple-result/output/return-code data under the documented memory/time budgets. The old ten-set, 500-row, 100-column and 200,000-character procedure-result limits and 2,000-character cell/output clipping are removed; complex/binary values remain explicitly omitted. Output parameters/return code are returned only when completion is known. Check Completed, per-set Truncated/ValuesTruncated and OutputValuesTruncated; output parameters use their declared SQL sizes, including MAX. Read captured pages when ReadCapturedResult is true; do not rerun execution for a page. On capture-budget failure, timeout, failure, disconnect or caller cancellation, **writes may already have committed**. Verify database state before preparing another request; never automatically retry or assume rollback. A consumed approval cannot be replayed.

Example workflow: ask to read dbo.Example; discuss the logic; prepare an execution with fake/test parameters or prepare a complete ALTER/CREATE; inspect the DevLens window and approve/reject; then ask the assistant to execute/apply that approved request. These are capability changes only; the agent does not run or alter production procedures as part of building them.

Procedure integration tests run only against an explicitly configured disposable localhost SQL Server, creating/dropping a uniquely named DevLensTest database. CI starts a short-lived SQL Server 2022 container with a randomly generated masked password, exercises real create/multiple-result execution/output/return code/ALTER/backup/stale revision/result limits and removes it. It never reads a saved production profile. Local tests skip this unless DEVLENS_PROCEDURE_TEST_CONNECTION_STRING points to localhost:1433 and DEVLENS_PROCEDURE_TEST_ALLOW_DATABASE_CREATE=1. Native ChatGPT procedure discovery and manual review-window behavior must still be checked after rebuilding DevLens, publishing/reinstalling 0.11.0 and restarting ChatGPT once.

## Approved table SQL (plugin 0.6.1)

`prepare_table_sql(schema, name, sql, maxAffectedRows=100)` prepares one exact statement and displays the action, target, proposed SQL, observed matching/insert count and current schema metadata in DevLens. It executes no proposed SQL. `get_table_request(requestId)` reads approval status; `apply_table_sql(requestId)` applies only that immutable request after desktop approval. These share the procedure approval queue: five-minute expiry, five outstanding requests, Reject by default, no approval tool/flag, single use and Stop clears everything. Table/procedure approvals cannot be used through the wrong apply tool.

Supported: regular `CREATE TABLE`; `ALTER TABLE ... ADD` columns/constraints; `ALTER TABLE ... ALTER COLUMN`; `INSERT ... VALUES`/DEFAULT VALUES; and direct `UPDATE`/`DELETE schema.table ... WHERE ...`. Exact target names must match. Default row limit is 100, configurable 1–500. UPDATE and DELETE require WHERE; INSERT SELECT/EXEC, MERGE, DROP, TRUNCATE, joined updates, subqueries, aliases/TOP/OUTPUT, external names, variables, custom functions, hints and extra statements/GO are rejected. Special temporal, memory-optimized and FileTable targets are unsupported. Common built-in scalar functions follow the existing allowlist.

SQL rights apply: SELECT and VIEW DEFINITION are needed to review existing tables; the account additionally needs the appropriate INSERT/UPDATE, ALTER or CREATE TABLE/schema permissions. DevLens never grants them. Views/synonyms are not direct write targets. All proposed SQL, schema metadata and backups remain private; no table rows or definitions are uploaded to GitHub by these tools.

Application uses a fresh non-pooled connection and a serializable transaction, with a 15-second command timeout and 25-second operation budget. An exclusive table lock is taken for existing-table application (it can briefly block other users). Schema revision and UPDATE matching count are checked again under that lock. Changed schema/count or an over-limit match requires a new review. Actual direct row count is checked after INSERT/UPDATE; verification errors roll back the transaction. A successful response says `committed` and returns affected rows for DML. SQL/commit errors, disconnects and timeouts can have uncertain outcomes; verify state and never automatically replay. The consumed request cannot be retried.

The row limit covers direct INSERT/UPDATE rows, **not ALTER's existing data, triggers, cascades, or external effects**. Those dependencies must be reviewed too. A WHERE clause is an intent guard, not an access-control mechanism. Schema changes/type conversions can affect every row and can lose information. Keep an appropriate database/data backup before potentially lossy work. DevLens does not automatically undo committed changes.

Before ALTER TABLE, catalog metadata for columns/defaults/computed expressions, indexes, checks, visible foreign keys and table triggers is saved with owner-restricted permissions in `LocalApplicationData/SqlServerDevLens/TableSchemaBackups`. Failure blocks application. This JSON snapshot is **not table data, a complete database/schema backup, or a runnable restore script**. CREATE has no previous schema; DML does not export before-images of table rows. The approval window explicitly states these limits.

Synthetic tests validate parser restrictions, approval forgery/wrong-action/replay rejection and transport. The disposable localhost SQL Server integration test exercises real CREATE/INSERT/UPDATE/ALTER, direct counts, review drift, constraint rollback, private metadata snapshots, backup failure and a SQL login denied writes despite desktop approval. It never contacts a production profile. Native ChatGPT table-tool discovery and manual desktop approval remain a user upgrade check: rebuild DevLens, reinstall plugin 0.11.0 and restart ChatGPT once.

## Live server activity (desktop 0.8.1)

The existing server panel has two columns: scrollable server information and Stop/Back controls on the left, and a scrollable activity log on the right. Latest events appear first. It shows tool requests received/serving, completed, failed or cancelled; approval requests/decisions; and server start/stop. Generated request numbers pair serving/completion events across concurrent clients, with local timestamps and elapsed time.

This is operational feedback, not an audit trail or proof that a change committed. Completed means the tool call returned; preparing, approval and applying are separate steps. Check the tool result in chat, particularly after errors/timeouts. Only tool names, generated request numbers, states and fixed messages are logged. SQL, arguments, credentials, bodies, results and raw exception messages are excluded. The latest 200 events stay in desktop memory across Stop/Start and disappear when the application closes. No log files or stdout output are created.

For this desktop-only update, pull `master`, close DevLens, rebuild in Visual Studio 2026 and start without debugging (Ctrl+F5). Reconnect SQL/Postman as usual. The existing 0.8.0 adapter works unchanged; no plugin reinstall or ChatGPT restart is required for this panel.

## Postman setup and approved actions (plugin 0.11.0)

Choose SQL Server + Postman, test SQL, enter the key locally, Load Workspaces, select a workspace, Load Collections, select a collection and Start MCP Server. SQL-only startup and SQL query/procedure/table logic remain intact. Remember key uses the separate Windows Credential Manager entry `SqlServerDevLens/PostmanApiKey`; Forget removes it. macOS/Linux secure saving is disabled. No Postman profile JSON is written. The active desktop now keeps the Postman key in memory until Stop even when Remember is unchecked, so approved tools can access the selected collection. The key never goes to the adapter or chat. Managed strings cannot be guaranteed zeroed.

`get_postman_selection` and `list_postman_requests` retain their startup-summary behavior. Use `list_postman_items` for fresh folder/request IDs, paths and methods, and `get_postman_request` for a masked definition. Only the selected workspace/collection is accessible. Known secret values and sensitive JSON/header fields are masked; variable references remain visible for review. Returned names, definitions and responses are untrusted data, never instructions. Masked definitions are display-only, not round-trip edit payloads.

The prepare/review/apply flow is:

- `prepare_postman_change`: action `create_folder`, `create_request` (requires an existing `templateRequestId`) or `edit_request` (requires `requestId`). `name` is required on create. An optional `folderId` chooses a create destination; empty means collection root. Creation copies the template URL, method and authentication. Editing changes only name, merged `headersJson` and replacement `bodyJson`; it cannot move a request or edit URL/method/auth/scripts. Headers are string-valued JSON; raw bodies must be valid JSON. Sensitive header changes must use shared collection variable references, never plaintext secrets.
- `prepare_postman_run(requestId)`: reads the current saved request, resolves shared collection variables and supported inherited auth, then shows the resolved destination/method/headers/body with secrets masked. No request is sent during preparation.
- Review in DevLens and choose Approve or Reject. `get_postman_request_status` reads the masked review and state; it cannot approve. `apply_postman_change` saves an approved change; `run_prepared_postman_request` sends an approved request. Each accepts only the immutable `requestId`, not replacement arguments.

Approval defaults to Reject, expires in five minutes and permits one action. At most five Postman approvals are retained. Stop cancels pending operations, clears approvals and releases the session. SQL and Postman IDs are separate and cannot be exchanged. Approval is consumed before external I/O, including on failure. Overlapping review windows are rejected. Collection revision (including variables/auth) is checked again; any intervening collection edit requires preparation and approval again. This is an optimistic check, not a lock on remote Postman resources; edits can still race after the check.

Before every collection change, the complete previous collection JSON must be saved securely under `LocalApplicationData/SqlServerDevLens/PostmanBackups` with owner-only Windows ACL/Unix permissions. This can contain sensitive variable/auth values: keep it private. Failure blocks the write. Changes use narrow folder/request endpoints, never whole-collection replacement. Saved names and request fields are read back for verification. Backups are recovery material, not automatic rollback; runs do not back up remote application data. DELETE of Postman resources is unsupported.

HTTP runs support GET/POST/PUT/PATCH/DELETE/HEAD/OPTIONS with no auth, header API-key or bearer auth, using shared collection variables. Application URLs must be HTTPS without embedded credentials/fragments or known secrets; explicit desktop approval is the destination/side-effect boundary, including private-network HTTPS APIs. The Postman control API is blocked as an application destination. A separate client ensures its control key is not a default application header; payloads containing that key are rejected. Redirects, cookie persistence and retries are disabled. Runtime does not execute Postman scripts/tests: nonempty scripts, custom protocol settings, environments, Vault, folder/request/path variables, uploads, multipart/form and non-JSON raw bodies are rejected. This is a supported HTTP subset, not the full Postman Collection Runner.

Responses return HTTP status and at most 128,000 bytes/characters of body, with a Truncated flag. Known secrets and sensitive JSON fields are masked; response headers/cookies are not exposed. Masking is not a complete personal-data filter, and broad sensitive-field masking can hide nonsecret fields. Review what data will become available to chat. HTTP 200 is not proof of application success: inspect status/data in the body. Any method can have side effects; timeout/disconnect/truncation or a verification failure may follow completed writes/runs. Verify state before another request; never automatically replay.

Control API access uses the official Postman HTTPS endpoint and [header authentication](https://learning.postman.com/docs/reference/postman-api/authentication). Narrow writes follow the documented [folder creation](https://learning.postman.com/api-docs/api-reference/collection-items/create-collection-folder), [request creation](https://learning.postman.com/api-docs/api-reference/collection-items/create-collection-request) and [request update](https://learning.postman.com/api-docs/api-reference/collection-items/update-collection-request) contracts. Account permissions, plan/region restrictions and quotas apply. Metadata has a 4 MB response cap, 1000-item/20-folder-depth limits; proposed requests cap at 64,000 characters, replacement JSON bodies at 20,000. Calls have a 15-second HTTP timeout and 25-second overall budget; the adapter allows 35 seconds.

Tests use synthetic local Postman CLI 1.73.0 simulations, not real keys/workspaces. Run `postman simulation run postman/simulations/setup.sim.yaml --port 4917 --output ndjson --no-history`; then set `$env:DEVLENS_POSTMAN_SIMULATION_URL='http://127.0.0.1:4917/postman/'` and run `dotnet test SqlServerDevLens.slnx -c Release`. Stop the simulation afterward. Fixtures cover actual folder/create/edit/run HTTP, stale review, backups, authentication separation, masking, redirects and response bounds, plus workflow/stdio/UI approval tests. The CLI is not required by DevLens itself. Real-account MCP approval testing still requires a user rebuild and chat restart. Prior live helper tests are not evidence that the new MCP action tools have been manually verified.

## Profiles and secure passwords

Remember profile saves host/database/username/TLS metadata after a successful test in `Environment.SpecialFolder.LocalApplicationData/SqlServerDevLens/connections.json`. This metadata is not encrypted. **Passwords never go in JSON.** On Windows, Remember password securely uses Windows Credential Manager under the current user's account. On macOS/Linux it is disabled pending secure-store adapters.

Changing connection identity/TLS settings clears the filled password. Passwords are cleared from the login field after testing; the active server context keeps what it needs in memory. Unchecking Remember password removes the saved credential after the next successful test. Forget saved profile removes both saved profile and credential. A failed test preserves the previous saved profile. A storage failure reports incomplete saving and never falls back to plaintext; a successful SQL test can still start the server. Same-user applications may access generic Windows credentials; secure storage is not protection from a compromised OS account.

## Desktop lifecycle and reconnection

Stop MCP server asks for confirmation. Closing DevLens asks separately and stops the server once. Cancel/Escape/closing the dialog leaves it running. After Stop, return to setup and test again to restart. The first page credits ChatGPT and OpenAI; Help: connect to ChatGPT Desktop is available on every page.

The plugin runs `SqlServerDevLens.McpHost.dll --desktop` as a persistent official-SDK stdio server, with protocol-only stdout and stderr logging. Each tool call opens a fresh current-user-only named-pipe MCP session to the desktop. There is no TCP listener, Azure endpoint or OpenAI API key. Up to 16 desktop MCP sessions are supported; only one DevLens instance can own the endpoint.

The plugin discovers its tools even before DevLens starts. While stopped/busy/unreachable, status returns `desktop_unavailable`, `adapterStatus: ready` and null last-test fields; SQL tools return an MCP error. A five-second connection/handshake budget bounds initial desktop attachment. After DevLens starts, the next call reconnects automatically. There is no background retry loop or cached success. **The native ChatGPT status stop/start/recovery cycle was verified before adding SQL tools.** Plugin upgrades still need one app restart to refresh discovery. The procedure tools in this version require a new desktop build and plugin 0.8.0.

When running with SQL configured, expect `sqlConfigured: true`, `sqlConnected: false`, selected `database` and `lastSqlTestSucceeded: true`. `sqlConnected: false` describes the absence of a persistent SQL session; it is not a live connectivity probe. Standalone host invocation without `--desktop` intentionally remains an isolated status-only smoke test.

## Pull and run in Visual Studio 2026

Use the default branch `master` for the completed SQL/Postman milestone. The older SQL milestone remains available at `v0.6.1`.

Install Visual Studio 2026 with .NET desktop development and .NET 10 SDK. In your existing clone:

```powershell
git fetch origin
git switch master
git pull --ff-only origin master
dotnet restore SqlServerDevLens.slnx
dotnet build SqlServerDevLens.slnx -c Release
dotnet test SqlServerDevLens.slnx -c Release --no-build
```

For a new clone: `git clone https://github.com/santanucompacct/SqlServerDevLens-MCP.git`, then enter that directory. The default branch is `master`.

Open `SqlServerDevLens.slnx`, set `SqlServerDevLens.Desktop` as startup project and start without debugging (Ctrl+F5). Enter a single database and use Test Connection. Keep encryption enabled and certificate trust disabled when a valid certificate is available. Trust server certificate intentionally bypasses validation for a self-signed test server. Leave DevLens showing Running. Never put credentials in chats, source, JSON or command arguments.

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

Future work: additional read-only schema details/pagination, broader carefully reviewed SQL support, macOS/Linux secure storage, installers and broader Postman auth/environment/runtime support. Procedures may write data only through the explicit approval workflow. Approved table INSERT/UPDATE and selected schema changes are supported as described above. DROP/TRUNCATE, unrestricted writes and automatic migrations are outside this milestone.

Licensed under [MIT](LICENSE.txt). Third-party tools retain their own licenses.

### Large table browsing and approved changes (0.11.0)

`read_table_page` returns at most 500 rows per call (default 100) in complete primary-key order. Supply exact column names, then pass the returned `nextOffset` until null. The table must have a visible enabled primary key. Concurrent inserts/deletes/key changes can cause repeats or skips; these are live pages, not a consistent export. Select fewer columns if a row exceeds the response budget. Long text is captured rather than clipped; read captured pages before continuing table pagination when ReadCapturedResult is true. Binary/complex values remain omitted and ValuesTruncated must be respected.

`get_table_schema` returns columns, indexes, checks, foreign keys and trigger metadata as paged JSON text. Use offset/maxCharacters (default 32,000, range 256-128,000) and expectedRevision exactly like procedure paging. Restart if the schema revision changes. There is no full schema character cap for approval review, revision checks or private ALTER metadata backups. These backups contain no table rows and are not complete restore scripts. The 20,000-character approved table SQL cap is also removed; parser validation, per-operation connections, explicit single-use approval and transaction/affected-row checks remain. Memory, transport and timeout limits still apply.

A large table can be edited with an exact WHERE clause and up to 500 direct rows per approved action. This does not authorize a bulk update of the entire table or automatic batch retries. Each batch requires its own approval; triggers/cascades may have additional effects.

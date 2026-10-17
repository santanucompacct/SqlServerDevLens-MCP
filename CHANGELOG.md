# Release notes

## Unreleased — Windows portable plugin discovery

- Fix the portable MCP command to `./server/SqlServerDevLens.McpHost.exe`. The local Agent Plugins loader rejects `${PLUGIN_ROOT}` in `command`, silently leaving the skill available without MCP tools. Arguments and working directory retain their supported configuration.
- Check the package command and its resolved executable during extracted-distribution testing. Raw EXE protocol tests alone do not verify plugin discovery.
- Existing 0.9.0 portable installations can correct this one field in `plugins/sql-server-devlens/mcp.json`, rerun `Install-Plugin.cmd`, then fully restart the chat app. The desktop application needs no rebuild. Native tool availability must still be checked in the restarted chat.

## 0.9.0 — SQL + Postman milestone and Windows distribution (2026-10-17)

- Consolidates the approved SQL/Postman workflows, connection help, bundled skill and activity panel on the default master branch.
- Adds a Windows x64 self-contained desktop/adapter ZIP for users without Visual Studio or separately installed .NET. The release copy launches its adapter EXE; development MCP wiring stays framework-dependent.
- Includes plugin/skill/marketplace, setup and launch files, connection help, project license, dependency license metadata/notices and SHA256 checksum. No private profiles, keys, data or backups are copied.
- Setup uses the existing desktop Codex CLI, preserves unrelated marketplace sources and stops on a conflicting DevLens source. It does not bundle ChatGPT/Codex or bypass account/workspace requirements. Existing-source migration remains explicit.
- Reproducible packaging checks protocol-only EXE output, native desktop startup, archive/checksum and Windows PowerShell 5.1 setup using simulated fresh/repeat/conflict cases and paths with spaces. CI builds release assets; a different recipient account/machine must verify its own local plugin access and connection.

## 0.8.3 — Recipient installation instructions (2026-10-17)

- Included INSTALL.md inside the plugin, with another-user/account setup prompt, source-build/local-marketplace commands, prebuilt-package contents and tools/skill verification.
- Updated the connection guide's creation prompt to preserve and verify the bundled workflow skill. Installing the complete plugin supplies the skill; recipients do not recreate it or share credentials.
- Documentation/package update only; desktop and SQL/Postman implementations are unchanged.

## 0.8.2 — Bundled DevLens workflow skill (2026-10-17)

- Packaged `devlens-workflow` with focused SQL/Postman references and skill-picker metadata. Relevant tasks may select it automatically; explicit selection is also supported by the host.
- Guides active-context checks, bounded inspection, procedure/table/Postman tool selection, exact-action approval/status/apply, read-back verification and uncertain-outcome handling. It includes no private business schema, data or account credentials and cannot grant approval or database rights.
- Bumped both plugin manifests while preserving plugin identity and MCP wiring. Refresh the plugin and restart the chat app to load the skill; this change does not require a desktop application rebuild.

## 0.8.1 — Server activity panel (2026-10-17)

- Added a connection help file, first-time local-plugin setup prompt and separate existing-chat SQL-only/SQL + Postman prompts. In-app Help includes Copy buttons and a full-guide link, with local execution/discovery limitations explained.

- Kept the existing server panel, with scrollable information and Stop/Back controls on the left and a latest-first activity log on the right.
- Added request numbers, serving/completed/failed/cancelled states, elapsed time, approval decisions and server lifecycle events. Completion records tool-call completion, not proof of an applied change.
- Kept only 200 events in desktop memory, with no SQL/arguments/credentials/bodies/results/raw errors in the log and no disk/stdout logging. UI notification failures do not change tool outcomes.
- Existing SQL/Postman operations and approval requirements are preserved. Rebuild the desktop; the existing 0.8.0 adapter remains compatible.

## 0.8.0 — Desktop-approved Postman actions (2026-10-17)

- Added live item IDs/masked request inspection plus separate prepare/status/apply/run MCP tools for the selected collection.
- Added folder creation, request creation from an existing template and request name/header/raw-JSON edits. Existing URL/method/authentication are preserved; resource deletion and whole-collection replacement are unsupported.
- Added one-request HTTP execution with shared collection variables and no-auth/header API-key/bearer authentication. Nonempty scripts/tests and unsupported environments/Vault/uploads are rejected explicitly.
- DevLens shows masked before/proposed or resolved outgoing content for human approval. Approvals expire, are single-use and are cleared by Stop. Collection/variable/auth changes require a fresh approval. No automatic retry after an uncertain outcome.
- Postman changes require a private owner-restricted collection backup and are read back. Runs use a separate HTTP client, block redirects and return bounded, known-secret-masked HTTP status/body. HTTP 200 does not imply business-level success.
- SQL connection/query/procedure/table implementations are unchanged. Verification uses synthetic APIs plus SQL regressions; new live-account MCP approval remains a user rebuild/restart check. No real account writes or application requests are performed during development.

## 0.7.0 — Postman setup milestone (2026-10-17)

- SQL Server + Postman now follows SQL test → masked API-key entry → Load Workspaces → workspace selection → Load Collections → collection selection → Start MCP Server.
- Optional API-key saving uses Windows Credential Manager, separately from SQL passwords; Forget removes it. No key or collection variable values are written to JSON.
- Two MCP tools expose selected workspace/collection and bounded request/variable-name summaries. These are startup snapshots; request execution, scripts, tests and resource editing are not enabled.
- SQL Server Only retains direct startup. SQL query, stored-procedure and table-action implementations are unchanged.
- Postman HTTP integration is checked with synthetic local CLI simulations; real account access still requires the user to enter their key and validate in the rebuilt app. Existing SQL regression and isolated SQL Server tests remain part of CI.
- Updating requires rebuilding DevLens, publishing/reinstalling plugin 0.7.0 and restarting the chat app once for tool discovery. This milestone is developed on `feature/postman-setup`; use that branch until its PR is merged.

## 0.6.1 — 2026-10-17

Completed SQL milestone for one selected database. The local plugin package version is 0.6.1.

- SQL/Windows login, connection testing, TLS settings, safe profiles and optional Windows Credential Manager password storage.
- Desktop-owned MCP server, automatic per-call adapter reconnection, Stop/Close confirmation, first-time help and credits.
- Read table/view structure and bounded data; read full supported procedure definitions and parameters.
- Execute procedures with validated scalar parameters, multiple result sets, output parameters and return code.
- Create and modify procedures after exact SQL review; save a private previous-definition backup before ALTER.
- Create regular tables; add columns/constraints and alter columns; insert values; update rows using WHERE and a direct affected-row limit.
- Mandatory desktop approval for procedure execution/changes and table writes. Requests expire, are single-use and cannot be approved by a chat tool.
- Table writes use transactions, schema/count rechecks and direct affected-row verification. ALTER TABLE requires a private schema metadata snapshot; this is not a table-data backup or complete restore script.

### Verification

118 local regression tests passed; three environment-gated SQL tests were skipped locally. Windows, macOS and Linux CI passed. Separate disposable SQL Server tests verified procedure execution/CREATE/ALTER and table CREATE/INSERT/UPDATE/ALTER, rollback, stale-review rejection, backups and SQL permission enforcement.

After rebuilding and restarting, native chat tools were verified against the user's connected database with explicit DevLens approvals: test-table creation, sample inserts, adding a BIGINT column and updating one row. Procedure CREATE/ALTER/execution were also verified earlier through the installed adapter. Private SQL definitions, result rows and backup contents are not included in these release notes or the repository.

### Scope and next milestone

SQL permissions still apply. Table row limits do not bound DDL conversions, triggers, cascades or external effects; timeout/disconnect can leave an uncertain commit outcome and must not cause automatic replay. DELETE, DROP, TRUNCATE, MERGE and arbitrary SQL batches are unsupported. Secure password persistence on macOS/Linux is pending. Postman integration remains planned and inactive.

### Upgrade

Pull `master`, rebuild `SqlServerDevLens.slnx` in Visual Studio 2026/.NET 10, and run the desktop application. Publish/reinstall the local plugin as described in README, then restart ChatGPT once to refresh tool discovery. Keep DevLens running after a successful connection test. No database schema/data changes run automatically during upgrade.

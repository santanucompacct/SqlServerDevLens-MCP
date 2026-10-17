# Release notes

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

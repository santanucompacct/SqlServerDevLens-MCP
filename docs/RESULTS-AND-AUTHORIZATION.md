# Large results and session authorization (0.12.0)

This update targets the application, desktop runtime and stdio adapter together. It does not modify production procedures or table data. Stable release downloads remain at their existing version until a new release is explicitly published.

## Full large procedure results, executed once

The old execution reader clipped text cells and output parameters at 2,000 characters and stopped at 500 rows, ten result sets or 200,000 value characters. Those result truncation thresholds are removed for procedure execution. SQL text/output parameters retain their actual declared SQL sizes, including MAX; the old 20,000-character parameter binding cap is removed. Procedure parameter JSON now supports up to 1,000,000 characters, replacing 64,000.

Large SQL SELECT/table-page text cells are no longer clipped at 2,000 characters either. Postman application HTTP bodies are no longer clipped at 128,000 bytes/characters. Known Postman secrets are masked before a result is captured or returned. Binary/complex SQL values remain explicitly omitted and flagged; this is not a binary export feature.

Execution still follows prepare → approved status → apply/execute once. DevLens captures the returned result privately in **desktop-session memory**, with a random ID, content revision and expiry. No result is uploaded to GitHub or saved as a local data export by this feature.

Small results remain inline for compatibility. A large response has `ReadCapturedResult: true`, a `Capture` reference, and empty inline rows/body. Empty inline data in this case does **not** mean an empty database result. Read the captured content:

1. Use `read_result_page(resultId=Capture.ResultId, offset=0)`.
2. Append the returned **Json string**, preserving it exactly.
3. Follow `NextOffset` using the returned `Revision` as `expectedRevision` until `Complete` is true.
4. Concatenate the strings first, then parse the full JSON. A page may end inside a JSON escape/string and need not parse by itself.
5. Inspect all result sets, output parameters, return code, completion and omission flags in the reconstructed object. A JSON string column such as `resultreturn` may then need a second JSON parse.
6. Use `release_result` when finished. Never call the procedure/HTTP run again merely to obtain the next page.

The page format uses camelCase over MCP. Page offsets/counts are UTF-16 code units; valid surrogate pairs are not split. Result IDs belong to the active desktop session and are readable by authorized clients of that same session. This is not per-chat data isolation. Stop, process exit, release or the 15-minute expiry makes them unavailable. Expiry does not authorize a replay of a side-effecting procedure/HTTP call.

## Automatic authorization checkboxes

The MCP server screen has ten independent checkboxes, **all unchecked by default**:

- Execute, create and modify stored procedures (three separate permissions).
- Create tables and modify table structure (two).
- INSERT, UPDATE and DELETE table rows (three).
- Run Postman HTTP requests and create/edit Postman resources (two).

Enabling a checkbox delegates that category to the active SQL database or Postman collection for this running desktop session. Prepare still validates and freezes the exact payload; it immediately returns `status: approved`, `authorization: session_policy` when the matching checkbox is enabled. The caller may then apply that exact single-use request without asking for another DevLens click. This does not remove a host application's independent tool-confirmation controls.

There is no MCP tool to enable/change these permissions. Disabling a checkbox invalidates unused automatic authorizations, including after re-enabling it. It does not undo an action that already started. Existing manual requests are not silently converted into automatic ones. Stop, connection restart and application restart reset all checkboxes. They are not persisted to JSON, the INI or Credential Manager. The activity log records category enable/disable/use without recording SQL payloads, parameters, keys or results. `get_server_status.authorization` reports the category flags.

Procedure execution may itself INSERT, UPDATE or DELETE, and any HTTP method can have side effects. Therefore enabling procedure execution or HTTP runs is broader than granting read access; leaving the table DELETE box unchecked does not sandbox a procedure. SQL-account permissions remain the underlying boundary. Unknown actions always require manual review or fail closed. Revision checks, mandatory pre-change backups, single-use consumption, SQL validation and the prohibition on automatic retries remain in force.

## Scoped DELETE

`prepare_table_sql` now supports one direct `DELETE schema.table WHERE ...`, alongside existing INSERT/UPDATE/schema changes. It uses the same serializable transaction, schema/matching-row rechecks, selected-database restriction and maximum 1–500 directly affected rows. No WHERE, joined DELETE, TOP, OUTPUT, subqueries, aliases, cross-database targets, DROP and TRUNCATE remain unsupported. The count is not a backup; there is no automatic table-row restore. Triggers/cascades may have additional effects.

## Limits audit: what remains deliberately bounded

Removing silent text clipping does **not** mean an unlimited request/response or unlimited memory. The application was searched for size/count/time checks across SQL, procedures, Postman, approvals and transport. The remaining material limits are:

| Area | Current boundary / behavior |
| --- | --- |
| Captured JSON | 16 Mi UTF-16 characters per serialized result; 64 Mi characters across retained captures plus reservations. Roughly twice these counts in bytes just for retained UTF-16 text; transient/provider allocations add memory. Oversize fails explicitly, never returns clipped JSON as complete. |
| SQL reader | Aggregate text/row/column overhead budget of 16 Mi characters. Provider output parameters may be materialized before this check. No separate 500-row/ten-set/100-column **procedure** cutoff remains. |
| Memory reservation | Reserve one maximum-sized capture before starting execution. If retained captures fill the session, release old results first; the new action is not started. Oversize discovered during/after an action may follow committed changes; do not retry. |
| Result pages | 32,000 characters default; configurable 256–128,000. Fifteen-minute lifetime; Stop clears captures. This bounds each transport response, not total readable text. |
| SELECT/table pages | `query_read_only` maxRows 1–500; table pageSize 1–500, max 100 selected columns. Table paging continues by primary key; live pages are not snapshots. SQL text validation remains at 20,000 characters. |
| Object/legacy schema lists | Up to 1,000 rows with `Truncated` flags. For a large individual table schema use revision-checked `get_table_schema`; full procedure/table schema text is paged without the old full-definition character cap. |
| Procedure inputs | Parameter JSON transport budget 1,000,000 characters. Scalar-only types and the procedure's declared SQL types/lengths/precision still apply; table-valued/user-defined/binary/XML parameters remain unsupported. |
| Approved table changes | At most 500 direct rows per action, default 100, exact supported SQL. This is not bulk deletion/update authorization; do not batch/retry automatically. |
| Postman HTTP response | 16 MiB raw response budget, then masked serialized capture budget above. No silent 128K clipping; redirects/scripts remain disabled. Response encoding behavior is UTF-8 as before. |
| Postman control API | Existing 4 MiB response limit; up to 1,000 workspaces/collections/items/variables and 128,000 metadata-path characters. Oversize is rejected. This update does not add control-API pagination. |
| Postman request editing | Existing 64,000-character definition/review/resolved-body limit, 20,000-character replacement JSON body, 10,000-character header-change object, 8,000 per header value. Inputs are rejected, not clipped. These are distinct from response capture. |
| Timeouts | SQL command/HTTP client 15 seconds; SQL/Postman operation budget 25 seconds; adapter tool budget 35 seconds and handshake/status 5 seconds. Large/slow results can still time out. |
| Approvals / sessions | Five pending requests per workflow; five-minute single-use expiry. Up to 16 client sessions. Activity log retains 200 events; it is not an unlimited audit archive. |
| Platform / provider | SQL Server type/identifier limits, available RAM, permissions, network and the OpenAI tunnel's own traffic/message/deadline limits still apply. |

These bounds are reported as limits or explicit errors. A procedure/HTTP request may already have committed when capture or transport fails. Preparation/automatic authorization does not make that action transactional or retryable. This candidate does not claim that all existing limits have been removed.

## Upgrade and verify

Stop DevLens and the tunnel before replacing executables. Build/publish **both** desktop and adapter from master after this update is merged. Source users should publish the adapter into the registered plugin folder, reinstall/refresh it, then restart the host/refresh remote connector discovery. The 0.12.0 adapter advertises 27 tools, including `read_result_page` and `release_result`; an old adapter cannot forward those new tools. Tunnel users should replace the complete tunnel package and retain their private INI separately.

First verify status and both new tool names. Test large data with a synthetic fixture before using an actual report. Leave all checkboxes off for the first run. Enabling a checkbox is the operator's local authorization decision; no production checkbox is enabled by these tests or source changes. Live reproduction of the three purchase-report calls requires rebuilding, starting DevLens and separate approval (or the intended checkbox) for each exact execution. No real company hashes, report definitions or result payloads are committed in the test fixtures.


## Connector discovery after upgrading

The updated adapter advertises **27 tools**, including `read_result_page` and `release_result`. Updating the desktop alone is insufficient. Keep the desktop and tunnel running from the new package, open the existing custom MCP connection in ChatGPT Plugins and select **Refresh**. Confirm both new names appear, then start a new chat with that plugin selected. See [OpenAI metadata refresh instructions](https://developers.openai.com/plugins/deploy/connect-chatgpt#refresh-metadata). Preserve the existing plugin identity and tunnel selection.

If a chat still sees 25 tools, do not rerun a procedure just to retrieve its output. Check the adapter path in the active tunnel profile, refresh the correct connector and recheck discovery. Captures expire after 15 minutes and Stop/restart clears them. Once expired, a new execution is a new action and needs its own authorization.

## Verification recorded 20 October 2026

The owner reported successful live testing and approved merging this update. Independently, the remote connector in this development chat exposed all 27 tools after refresh/restart and `get_server_status` returned running. The packaged adapter also passed isolated initialization/tool discovery with 27 tools. This status/discovery check did not execute SQL or verify the full contents of a live captured procedure result. No production definitions, result payloads or credentials are included here.

Automated validation at application commit `fea5025c07e112e785bc071b35d1e18b41c6a9cf`: Windows, Linux and macOS build/tests, disposable SQL integration and Postman simulation passed in [run 39365983041](https://github.com/santanucompacct/SqlServerDevLens-MCP/actions/runs/39365983041). Both [standard package](https://github.com/santanucompacct/SqlServerDevLens-MCP/actions/runs/39365983226) and [tunnel package](https://github.com/santanucompacct/SqlServerDevLens-MCP/actions/runs/39365983048) workflows succeeded. Download links are maintained in the [README](../README.md#latest-0120-windows-packages); these are test artifacts, not a new published Release.

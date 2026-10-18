# SQL workflow

Use current MCP signatures for parameters and limits. Stay in the configured single database; SQL permissions still apply after approval.

## Inspect

Use `get_database_info` for database details, `list_database_objects` to discover visible objects and `describe_table(schema, name)` before querying unfamiliar columns. Reuse schema already inspected in the current context unless it may have changed.

Use `query_read_only(sql, maxRows)` for one schema-qualified SELECT/CTE. Default maximum is 100 rows; supported range is 1–500. Avoid SELECT * when a few fields answer the question. Use aggregates for counts rather than counting a truncated sample. A simple example with synthetic identifiers is `SELECT COUNT_BIG(*) AS item_count FROM dbo.Example WHERE status = 'READY'`. Confirm actual columns and requested filters first. Explain all-date/all-company scope when no filters were requested.

This query tool does not allow writes, EXEC, database switching, arbitrary batches, custom functions or external sources. Check `truncated` and `valuesTruncated`; empty metadata can mean missing or not visible. Do not work around validation with direct SQL or another connection.

## Procedures

Read `get_procedure_definition(schema, name)` for the first definition page, full-definition revision and scalar parameters. Follow `nextOffset` using `offset` and the first page's `expectedRevision` until `complete` is true. Pages default to 32,000 UTF-16 code units; `maxCharacters` supports 256-128,000. On revision mismatch discard the pages and restart; never claim parity or prepare an edit from incomplete source. The full procedure has no application character cap. Do not infer required parameters or report branches from a procedure's name.

| Intent | Prepare | Status | Apply once after approval |
| --- | --- | --- | --- |
| Execute | `prepare_procedure_execution(schema, name, parametersJson)` | `get_procedure_request(requestId)` | `execute_prepared_procedure(requestId)` |
| Create/alter | `prepare_procedure_change(schema, name, sql, create)` | `get_procedure_request(requestId)` | `apply_procedure_change(requestId)` |

`parametersJson` is a JSON object of actual scalar parameter names/values. A procedure parameter containing JSON must be a JSON **string** within that object, not an invented nested parameter object. Derive report names and JSON shapes from the current definition or explicit business requirements. Procedure execution may write data even if named "get" or "report"; it always uses approval.

For a modification, read the full current definition, preserve unrelated branches and show the exact proposed diff through preparation. Use CREATE with `create=true` for a new procedure or ALTER with `create=false` for an existing one. GO, extra top-level statements and CREATE OR ALTER are unsupported. ALTER saves a private previous-definition backup before applying. Verify the current definition/read-back result after apply. SQL Server may normalize a stored ALTER header to CREATE; do not reapply merely because the header is normalized.

Review all returned result sets, output parameters, return code, errors and bounds. Multiple result sets are not one flat table. A returned execution result does not imply a read-only operation or unrestricted output.

## Tables and row changes

For large tables use `read_table_page(schema, name, columns, offset, pageSize)` with explicit exact columns; it requires a visible enabled primary key and orders by all key columns. Follow `nextOffset` (null ends the read), at most 500 rows per page. This is live offset paging, not a snapshot; concurrent writes may skip/repeat rows. Do not claim a consistent export. ValuesTruncated remains meaningful; long text/binary/complex values are still bounded or omitted.

Use `get_table_schema` for paged columns/index/check/foreign-key/trigger metadata. Follow nextOffset with expectedRevision until complete, restarting on schema revision changes. Complete metadata is used internally for approval/rechecks and private ALTER backups without the old 128K cap. Metadata is not a row backup or a full restore script. SQL command length has no application cap for approved table actions; memory, transport and timeout limits remain. Never automatically split or apply bulk writes: each exact batch needs separate approval and at most 500 direct affected rows.

Read current schema and, for updates, inspect the exact intended row selection first. Use `prepare_table_sql(schema, name, sql, maxAffectedRows)` → `get_table_request(requestId)` → `apply_table_sql(requestId)` after DevLens approval. Set a tight row limit matching user intent (supported 1–500, default 100); use an exact key/WHERE predicate for a single-row edit.

Supported subset: regular CREATE TABLE, ALTER ADD/ALTER COLUMN, INSERT VALUES/default values and UPDATE with WHERE. DELETE, DROP, TRUNCATE, MERGE, unrestricted batches, joined/subquery changes and OUTPUT are unsupported. Trigger/cascade effects need attention. Row changes use a transaction and recheck row counts/schema. A private ALTER catalog backup is not a table-row backup or full restoration script.

When the user requires per-row triggers, prepare/apply INSERTs one at a time, then read back each generated identifier before preparing the next action. Do not assume a placeholder such as a generated registration code is the final stored value. Use the verified code only for the corresponding report/request. Read back the affected records/schema after a known successful change.

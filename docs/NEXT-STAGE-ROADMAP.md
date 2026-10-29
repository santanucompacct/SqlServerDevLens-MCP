# Next-stage roadmap — SQL dependency analysis and procedure versioning

**Roadmap recorded:** 29 October 2026  
**Status:** Planning / not yet implemented  
**Baseline:** The project owner has confirmed that testing of the current DevLens workflows is complete for the agreed development phase. This is an owner acceptance milestone, **not** a new release, universal certification, or a claim that future capabilities have been tested.

## Current baseline — completed scope

- Windows DevLens desktop and MCP integration: SQL Server metadata, complete paged procedure definitions and parameters, controlled SQL/procedure changes, per-action review or explicitly delegated session authorization, results paging, and supported Postman workflows.
- Secure MCP Tunnel connectivity for ChatGPT web use and multiple independent tunnel channels/databases. Each connection must be identified and validated against its intended database before an operation.
- Linux headless, multi-instance deployment as systemd services, with independent runtime/tunnel units, private Unix sockets, a localhost-only administration portal, and operator-selected time-limited authorization. The project owner reports successful Linux service implementation and use; the repository's experimental packaging and security boundaries remain applicable.
- Existing procedure protections: revision checking, explicit review or scoped authorization, and secure backup of the **previous complete procedure definition** before an approved ALTER.

Documentation: [Windows tunnel setup](../Tunnel/QUICKSTART.md), [Linux headless setup](LINUX-HEADLESS.md), [Linux operator portal](LINUX-ADMIN-PORTAL.md), and [large results and authorization](RESULTS-AND-AUTHORIZATION.md).

**Important distinction:** Existing pre-ALTER backups and revision checks are **not** a complete historical version-control or automatic rollback system. Neither a dependency graph nor full procedure version management is currently shipped.

## Priority 1 — SQL object dependency and change-impact analysis

**Goal:** Before modifying a stored procedure or related object, help a developer understand which other objects could be affected.

Planned capabilities:

1. Build a **read-only dependency inventory** for stored procedures, tables, views and functions in the currently authorized database, with clear database/schema-qualified object identities.
2. Show both **outbound dependencies** (objects this procedure uses) and **inbound dependents** (objects referencing it), including direct and transitive relationships, cycles and unresolved references.
3. Use SQL Server metadata such as `sys.sql_expression_dependencies` and relevant dependency DMFs, supplemented by ScriptDom parsing where useful. Validate the coverage and permission requirements against real, disposable test fixtures before choosing an implementation.
4. Provide a bounded, paged and machine-readable dependency result suitable for an eventual graph view, with object type, edge kind, resolution status and scope.
5. Expose a **possible change-impact report** before proposed procedure/table modifications, distinguishing confirmed references from possible or unresolved relationships.

**Limitations to disclose:** Dynamic SQL, runtime-generated object names, synonyms, cross-database/server references, encrypted objects, permissions and stale/ambiguous dependency metadata can make the graph incomplete. A dependency edge signals *potential impact*, not proof that a change will break a caller. Do not label absence of a recorded edge as a guarantee of safety.

**Security and scope:** Inventory/graph reads must not execute business procedures or change the database; honor the connected principal's visibility. Keep object definitions, business data and sensitive metadata inside the authorized DevLens/MCP context; do not upload them to a public repository.

**Acceptance criteria:** Test a disposable database containing nested procedure calls, shared tables, views, functions, cycles, renames, dynamic SQL and out-of-scope references. Check both dependency directions, bounded paging, missing-permission behavior, false-positive/false-negative disclosures, multi-instance isolation and no-write behavior.

## Priority 2 — stored procedure revision history and version management

**Goal:** Move from the existing single pre-change backup safeguard to traceable, reviewable procedure history.

Planned capabilities:

1. Capture a controlled **version record** for each DevLens-approved CREATE/ALTER, including target database/schema/object, before/after revision hashes, complete definitions, relevant SET options, timestamp, operation identity and optional change reason.
2. Provide **history and diff inspection** with immutable records, consistent version identifiers and validation for out-of-band changes made outside DevLens. No claim that local history is complete if external DDL was not observed.
3. Support explicit **export** of a reviewed version or a proposed restore script. A restore is a *new* controlled ALTER, never an automatic rollback: show exact diff/target and require the current revision check, appropriate SQL permissions and the existing approval policy.
4. Use secure, private, owner-restricted storage with defined retention/backup and failure behavior. Do not commit customer SQL definitions, procedure bodies, credentials or operational backups to public GitHub by default.
5. Evaluate optional Git-based change tracking later, with explicit opt-in and data-governance safeguards; Git integration is not a prerequisite for safe local version history.

**Acceptance criteria:** Verify multi-version history, same-name object recreation, collisions, concurrency/external edits, failure to save history, restoration after schema/signature changes, approval expiry and single-use behavior, and tenant isolation. A history-store failure must not silently report a change as fully protected.

## Delivery sequence and gates

| Stage | Status | Exit gate |
| --- | --- | --- |
| Current SQL/Postman/tunnel/Linux implementation | Owner-confirmed accepted baseline | Preserve existing features, source and service deployments |
| P1A Dependency inventory prototype | Planned | Accurate bounded read-only results against synthetic fixtures |
| P1B Impact reports and graph-ready output | Planned | Clearly labeled resolved/unresolved edges; cross-object regression tests |
| P2A Secure procedure history and comparisons | Planned | Immutable per-change records, private storage, external-change detection |
| P2B Explicit restore preparation and approval | Planned | Never auto-restore; recheck current revision, permissions and approvals |
| Cross-platform/security/release validation | Required per new stage | Windows + Linux regression, tunnel isolation, authorization tests, updated docs and release checks |

### Implementation guardrails

- Start on a development branch with tests; preserve existing Windows builds, Linux systemd instances, tunnel profiles, credentials and current release assets.
- No migrations, database changes, service restarts or live customer procedure executions are authorized merely by this roadmap.
- Validate metadata edge cases and graph size bounds before adding a visualization or promising automatic breakage detection.
- Do not describe future features as delivered until implemented, reviewed and tested on the intended supported platforms.
- Update the README, changelog, technical docs and public roadmap only as each feature actually ships.

**Out of scope for this next feature pair:** Automatic rewriting of dependent procedures, autonomous production migrations, unrestricted schema changes, guaranteed rollback of data-side effects, and publishing proprietary SQL definitions to GitHub.

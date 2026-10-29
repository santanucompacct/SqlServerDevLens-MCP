# Troubleshooting Postman collection writes in DevLens

This guide describes the **proposed write-diagnostics update** on the `fix/postman-write-stage-diagnostics` branch. The existing Windows and Linux installations do **not** gain these messages until their matching desktop/runtime and MCP components are rebuilt and deployed. No live Postman collection is changed by this documentation.

## Recognizing the stage

A successful `get_postman_selection`, `list_postman_items` or `get_postman_request` proves that the configured key can **read** the collection; it does not establish that the key has edit permission or that a write payload is accepted.

The old generic error:

```text
Postman action failed or timed out. It may already have been saved or executed.
Verify state; never retry automatically. Approval was consumed.
```

did not reveal whether the error occurred before or after an HTTP write attempt.

The proposed diagnostic reports a safe stage:

| Stage | What it establishes | Recommended check |
| --- | --- | --- |
| `preflight` | Could not validate current collection/revision before writing | Recheck workspace, collection and access; **no write was sent** |
| `private backup` | Could not safely write the mandatory local backup | Check service identity and private backup directory permissions; **no write was sent** |
| `Postman write submission` | A POST/PUT was attempted; provider may have rejected it or a network failure occurred | Look for an HTTP status, inspect the collection before any new attempt |
| `Postman readback` | The write request returned, but collection/item readback could not be verified | Inspect the collection for the new/edited item before doing anything else |
| `Postman field verification` | The request was submitted and returned, but approved fields could not be confirmed in the saved item | Compare method, URL, headers, body and auth in Postman; **do not assume the save failed** |

HTTP status codes are reported without raw API response bodies, secrets, authorization headers, or payloads. Examples: HTTP 400 may mean Postman rejected a payload, HTTP 401 means authentication is denied, HTTP 403 may indicate insufficient write permissions, and HTTP 429 indicates rate limiting. The precise provider-side reason may still require operator-side investigation.

**Never automatically rerun `apply_postman_change` after an uncertain outcome.** Its approval is consumed even on failure. First inspect the intended item using the read-only list/get tools or the Postman UI and establish whether it already exists or was modified.

## Linux-specific authorization

In headless Linux, `Postman review could not be opened` at preparation is distinct from an apply failure: no desktop review dialog exists, so the instance requires an active, expiring `postman_change` operator grant. Save policy changes through the localhost-only admin portal and restart the intended instance to load them. Confirm the selected instance/database and `get_server_status.authorizedCategories.postman_change` rather than assuming the last saved portal setting is active.

A running service with readable Postman metadata and `postman_change: true` can **still fail at apply time** because of backup permissions, Postman write permissions, payload validation, network errors or save verification.

## Contract and test boundaries

The item endpoint uses the Postman collection's **ID**, not its UID:

- `POST /collections/{collectionId}/folders` with optional `folder` property in the JSON body.
- `POST /collections/{collectionId}/requests?folder={folderId}` (optional `folder` query parameter).
- `PUT /collections/{collectionId}/requests/{requestId}` for editing without moving the request.

The `folder` query parameter is documented by Postman; do **not** rename it to `folderId` in the HTTP request. Use only the explicitly selected workspace/collection.

Reference: [create request](https://learning.postman.com/api-docs/api-reference/collection-items/create-collection-request), [update request](https://learning.postman.com/api-docs/api-reference/collection-items/update-collection-request), [create folder](https://learning.postman.com/api-docs/api-reference/collection-items/create-collection-folder).

Synthetic Postman simulation tests verify error-stage classification and ensure a simulated response body containing a sentinel secret is **never** surfaced. These tests do not prove that any particular user's Postman plan/key has write permission, nor do they authorize a live collection modification.

## Deployment and verification

1. Review the patch and run Windows/Linux builds plus synthetic Postman integration tests.
2. Deploy matching versions of the Windows desktop/MCP adapter and Linux headless service/bridge only through the normal release workflow; preserve encrypted/private credentials, configuration and tunnel IDs.
3. Before any new controlled test, verify whether the prior failed operation already saved its intended change.
4. On a disposable collection, test exactly one authorized create/edit operation, inspect its new stage-specific result, and verify the saved item read-only. Never use a chargeable application API execution to test collection editing.

No Postman API key or raw Postman response should be copied into GitHub issues, CI logs or chats.

# Postman workflow

Use the DevLens tools for its selected workspace/collection. Do not substitute a local Postman application's unsynced state or use another account/collection to bypass DevLens approval.

## Inspect the current selection

`get_postman_selection` and `list_postman_requests` are startup snapshots. Use `list_postman_items` for fresh folder/request IDs and paths, then `get_postman_request(requestId)` for a masked saved definition. Match the full folder path when names repeat. Use the selected item ID, not a guessed name or a remembered account-specific ID.

Collection variables use their **shared** values available through the Postman API; local-only values in the desktop Postman app are not automatically available. Never ask the user to paste secret values into chat. Ask them to update shared values in Postman or enter the control API key in DevLens.

## Create or edit

Use `prepare_postman_change` with the current signature. Actions:

- `create_folder`: `name` plus optional `folderId`; empty folder ID means collection root.
- `create_request`: new `name`, existing `templateRequestId`, optional destination `folderId`, optional `headersJson`/`bodyJson`. Inspect a suitable user-selected template first. Inherited authentication must remain compatible with the destination.
- `edit_request`: existing resource `requestId`, optional new `name`, `headersJson` and/or `bodyJson`. A rename should supply only the name and resource ID. Do not silently rewrite the body, report name or SQL registration.

`headersJson` merges string header values; `bodyJson` replaces the entire raw JSON object/array. Preserve unrequested fields and existing variable references. Sensitive header changes require shared-variable references. URL/method/authentication are preserved; moving/deleting resources or replacing a collection is unsupported.

Read the prepared masked before/proposed review. Never copy redacted display values back into an edit. Use the **approval ID returned by preparation** in `get_postman_request_status` and, only after approval, `apply_postman_change`. It is not the request resource ID used to select an item. Saving does not execute the API. The server saves a private pre-change collection backup and reads back saved fields; use that result or fresh read-only inspection to verify.

## Run a saved request

Use `prepare_postman_run(resourceRequestId)` to resolve the exact saved method, URL, supported headers/authentication and raw JSON body for review. This does not send the application request. After DevLens approval, check `get_postman_request_status(approvalRequestId)`, then call `run_prepared_postman_request(approvalRequestId)` once.

Nonempty scripts/tests, environments, Vault, folder/request/path variables, file uploads and unsupported protocol settings fail closed. Supported authentication is no-auth, header API key or bearer. Do not claim to run Postman scripts or a collection test suite: this sends one supported HTTP request. Requests do not follow redirects or retain cookies. Even GET may have side effects; every run needs review.

Interpret HTTP status and response body together. A 200 response can contain an application error or no data; distinguish these. Check `truncated` and masked fields. SQL and API results can differ because of date/company/status filters or application transformations; inspect evidence before concluding either is wrong.

The collection/auth/variables revision is rechecked before an approved action. A changed revision means prepare/review again, not force the old approval through. Timeout, disconnect or post-save verification failure can leave a completed action. Do not automatically retry. Verify the saved resource or business state read-only where possible; if a run's side effects remain uncertain, explain that and wait for user direction.

Large HTTP responses are captured once after known-secret masking. For ReadCapturedResult, read all read_result_page pages using Capture.ResultId and expectedRevision; concatenate Json strings then parse the reconstructed result/body. Never run HTTP again to fetch a page. The 16 MiB raw-response and captured-result budgets are explicit errors, not complete clipped responses. Operator-enabled postman_run or postman_change session authorization can approve the exact prepared request automatically; apply/run it once without another DevLens click only when its status confirms approval. Scripts, redirect/retry restrictions and revision checks remain.

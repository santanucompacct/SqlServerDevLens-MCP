# Linux DevLens Admin Portal — experimental operator mode

This is an **unmerged, unverified development addition** to the Linux branch. Windows desktop code, binaries and release workflows have not been changed. Only deploy to a disposable/test Azure Ubuntu 24.04 VM until live testing passes.

## Purpose

One local-only web administration portal manages **several separately isolated instances**. Each instance has its own database, SQL password, Postman key/selected workspace+collection, OpenAI tunnel ID, services, captured results and authorization categories. No GUI session is required on the server.

The existing ten-tool read-only behavior remains the **default** for instances without `operator.json`. The operator can opt a specific instance into the existing full SQL tools, safe preparation/single-use application workflow and Postman APIs.

**Security warning:** Stored procedure execution and Postman requests can change production data even if their HTTP/SQL entry point looks like a read. Do not enable automatic authorization for live production credentials unless you have explicitly accepted the exact scope and consequences. Unlike Windows session checkboxes, this Linux policy is persisted until its explicit expiry (maximum 24 hours when set from the UI). Categories remain off by default. If an action category is off, the current Linux mode does **not** implement a manual remote approval workflow; prepare is rejected/denied. Do not enable global blanket grants to work around that.

## Install

Build artifact includes `SqlServerDevLens.Admin` as a self-contained ASP.NET Core executable under `/opt/devlens/admin`.

1. Install and configure instances with `AZURE-VM-OPERATIONS.md` as before.
2. Install services: `sudo bash /opt/devlens/linux/install-services.sh`.
3. Set a unique admin secret and enable boot service: `sudo bash /opt/devlens/linux/enable-admin.sh`. This requests the secret only once, at setup.
4. From your Windows PC, with SSH access to the VM, run:
   `ssh -L 8090:127.0.0.1:8090 USER@VM_IP`
5. Keep that SSH window open; in your **Windows** browser open `http://127.0.0.1:8090`.
6. Enter the admin secret. Select an instance to configure SQL details, passwords, Postman key, Postman collection, exact authorization categories and service operations.

**No Azure NSG inbound rule for port 8090, 8080 or 8081.** The portal listens only to 127.0.0.1:8090 on the VM; you reach it over your authenticated SSH tunnel. Port 443 must be available **outbound** to Postman/OpenAI, and the VM needs authorized **outbound** SQL connectivity. Only restrict/permit incoming SSH from an appropriate trusted source (or use Azure Bastion), not public SQL inbound ports.

Admin portal uses its own protected `/etc/devlens-admin/admin-token` (systemd LoadCredential). The HTTP portal must never be exposed publicly, even with a password. Login session lasts six hours or until service restart. Cookies are HttpOnly and SameSite Strict, with a separate form CSRF token. It intentionally uses HTTP over the private SSH local port forward, not public TLS.

## Credentials and configuration

- Per-instance SQL settings: `/etc/devlens/NAME/instance.json`, private, contains host/port/database/username, **not password**.
- SQL password: `/etc/devlens/NAME/sql-password`, root-owned 0400. Only systemd delivers it to `devlens@NAME`.
- OpenAI tunnel runtime key: `/etc/devlens/NAME/tunnel-key`, root-owned 0400. Only the tunnel service receives it.
- Postman key: `/etc/devlens/NAME/postman-key`, root-owned, mode 0440 with only that instance's dedicated Unix group. Required if Postman collection is selected; never included in JSON.
- Postman selection, tool mode and operator policy: `/etc/devlens/NAME/operator.json`, root-owned, mode 0440 with only that instance's group, managed through the portal.
- Admin portal credential: `/etc/devlens-admin/admin-token`, root-owned 0400.

No live password is echoed into the portal or returned through an MCP tool. Protect disk snapshots, root access and backups. Linux root owns all tenants and is inside the trust boundary.

## Postman selection

In the instance page, first save the Postman API key, then choose **Select workspace & collection**. The portal reads workspaces and collections from the official Postman HTTPS API, validates the selected IDs and saves their IDs. After restarting the instance, its headless runtime validates the selection again and enables existing read, prepare and apply Postman tools. Each instance selects one collection at a time; no collection scripts/environments/Vault/cookie persistence are newly introduced. Existing Postman API restrictions remain.

## SQL and Postman automatic authorization

The instance page has a "Tool mode" dropdown. Select **SQL + Postman + controlled writes**. Then enable only the categories you require: `procedure_execute`, `procedure_create`, `procedure_alter`, `table_create`, `table_alter`, `table_insert`, `table_update`, `table_delete`, `postman_run`, `postman_change`.

Set expiry to 1–24 hours and save. **Restart that instance**. Categories are loaded only at runtime startup and automatically reset at expiry; an existing staged action must still pass the authorization revision check before applying. Existing prepare/validate/backup/row limits and one-time apply are reused. No blind SQL batch execution or procedure retries are added. The grant is bounded by time/category/instance, not by SQL object or parameter; use **development databases only** until a more restrictive object/operation-scoped approval system is implemented.

The Portal does not enable categories globally. An instance with no `operator.json` or `mode: read-only` still advertises exactly ten read tools and does not load Postman.

After changing anything, use the instance **Restart** control. Saving a form does not silently restart live SQL/tunnel sessions.

## Validation still required

The workflow must compile/test this new code and Windows regressions must pass. Before production, perform actual Ubuntu deployment, root-owned file permission checks, real SQL TLS and stored procedure tests on disposable data, Postman sandbox requests and collection modification verification, credential rotation, crash and VM deallocate/start tests, SSH browser tests, CSRF/authentication tests, instance A-vs-B isolation and a measured VPS load test.

Keep PR #12 **draft/unmerged** until these checks pass and the owner approves. If a CI workflow is red or a new deployment cannot meet this checklist, do not claim production readiness.

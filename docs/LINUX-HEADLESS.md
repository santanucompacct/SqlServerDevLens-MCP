# Linux headless read-only milestone (separate build)

Target: Ubuntu Server 24.04 LTS, Linux x64. Experimental branch `feature/linux-headless-multi-instance`; review before deployment. This is an additional executable in the existing repository, not a replacement for Windows. The Windows solution, desktop, adapter, plugin manifests, vendor locks, installers and release workflows are unchanged. No VPS or production database is configured by the build.

## Architecture

`ChatGPT connection A -> devlens-tunnel@A (stdio bridge) -> private Unix socket -> devlens@A -> database A`

`ChatGPT connection B -> devlens-tunnel@B (stdio bridge) -> private Unix socket -> devlens@B -> database B`

Each instance has independent `devlens@NAME.service` (SQL runtime) and `devlens-tunnel@NAME.service` (tunnel/stdio bridge) units under its dedicated `devlens-NAME` Linux user. The runtime uses a private 0700 directory and 0600 Unix socket: no Windows computer, Avalonia, desktop named pipe, Redis or public MCP listener. Existing Core/Runtime provide SqlClient, ScriptDom validation, metadata, table/schema/definition paging, per-operation connection disposal, timeouts and memory-bounded captures. Only a positive allowlist of ten tools is registered. There are no execute/prepare/apply/approval/Postman tools and no auto-authorization flag. MCP returns unknown-tool errors for these names. Existing desktop workflow classes are dependencies internally but cannot be invoked through this headless catalog; no approval callback/policy is created.

The ten tools are `get_server_status`, `get_database_info`, `list_database_objects`, `describe_table`, `get_table_schema`, `read_table_page`, `query_read_only`, `get_procedure_definition`, `read_result_page`, `release_result`.

Instance ID and configured database are explicit in status. Status reports process readiness only, not a successful SQL test; use `get_database_info` for live verification. Each persistent runtime owns its sessions and captures. Runtime/VM restart clears captures; do not resume an old result ID after runtime restart. A tunnel/bridge restart does not itself restart the runtime or clear captures, but clients must initialize a fresh session after disconnect; transparent replay is not promised. Clients authorized for the same tunnel may access that instance; a tunnel is the tenant boundary, not an individual chat.

## Build and package

No Visual Studio is needed. On Linux with .NET 10 SDK, bash, Python 3, curl, unzip and tar:

```bash
dotnet test SqlServerDevLens.Headless.Tests/SqlServerDevLens.Headless.Tests.csproj -c Release
bash linux/build-package.sh
```

The separate `Linux headless distribution` Actions workflow publishes a self-contained Linux x64 tarball and SHA256SUMS.txt under its run artifacts. It does not publish a Release or replace Windows assets. The archive includes the headless app, scripts, systemd template, this guide, dependency inventory and the official Linux tunnel-client v0.0.16 archive contents, including upstream notices. Its archive SHA256 is pinned independently of Windows: `d60cdba019bce451bcc3a15478cc5b9cb11270b049f5b56ea39a80b517f8b117`. Upstream [release and provenance](https://github.com/openai/tunnel-client/releases/tag/v0.0.16); [tunnel operation guide](https://github.com/openai/tunnel-client/blob/v0.0.16/docs/end-user-guide.md). This pin is reproducibility, not a claim that the vendor has no vulnerabilities. Review newer vendor versions separately.

## Prepare an Ubuntu host

Use a clean authorized test host first. Install Ubuntu security updates and the native .NET prerequisites (including ICU/OpenSSL/CA certificates); self-contained means no separate .NET runtime, not a static binary with no OS dependencies. Allow outbound HTTPS to the OpenAI tunnel service and outbound connectivity to the intended SQL host. Do not publish SQL port 1433, tunnel dashboard ports or a new MCP HTTP port. Restrict administrative SSH appropriately.

Verify the downloaded tarball against SHA256SUMS.txt, extract it and install the full contents under `/opt/devlens` as root-owned files, not writable by service users. This milestone uses that fixed installation path. Do not extract over an active installation.

```bash
sha256sum --check SHA256SUMS.txt
tar -xzf SqlServerDevLens-linux-headless-0.1.0-linux-x64.tar.gz
sudo install -d -m 0755 /opt/devlens
sudo cp -a SqlServerDevLens-linux-headless-0.1.0-linux-x64/. /opt/devlens/
sudo chown -R root:root /opt/devlens
sudo bash /opt/devlens/linux/install-services.sh
```

## Configure two independent instances

1. Create **two distinct** tunnel IDs in the intended Platform organization/workspace. Use a restricted runtime key with Tunnels Read + Use; management requires Read + Manage. Never use the Windows developer's profile/key. Assign dashboard ports 8080 and 8081; no shared tunnel IDs or profile directories. Only one service should poll each tunnel.
2. Provision each name (replace the tunnel placeholders; these are IDs, not keys):

```bash
sudo bash /opt/devlens/linux/configure-instance.sh tenant-a YOUR_FIRST_TUNNEL_ID 8080
sudo bash /opt/devlens/linux/configure-instance.sh tenant-b YOUR_SECOND_TUNNEL_ID 8081
```

The script accepts IDs beginning `tunnel_`; replace the placeholder fully. It creates dedicated system users, root-owned 0750 configuration directories with tenant-specific group traversal, 0700 private state directories, a 0400 config template and private tunnel profiles. Service users cannot replace the root credential source files or their containing directories. It refuses existing users/directories rather than overwriting them and does not start services. A partial provisioning failure requires administrator inspection before retrying. Port conflicts fail at tunnel startup; choose unique ports.

3. With `sudoedit`, fill `/etc/devlens/tenant-a/instance.json` and `/etc/devlens/tenant-b/instance.json` using each tenant's host, port, database and SQL username. Preserve the matching `instanceId`; the config directory must have that same name. No password/connection string/extra fields are accepted. TLS is always Encrypt=true, TrustServerCertificate=false. Use a SQL certificate trusted by Ubuntu with a matching DNS name; install your organization's CA correctly, not a trust-certificate bypass.
4. A DBA must supply a least-privilege **SQL authentication** login for each database: SELECT on permitted objects and the required metadata visibility, no EXECUTE, INSERT, UPDATE, DELETE, DDL, CONTROL, db_owner/sysadmin or cross-database grants. Windows/Kerberos authentication is not implemented here. ApplicationIntent.ReadOnly is routing intent, not a SQL permission boundary. The application does not audit every inherited SQL privilege; DBA verification is required.
5. Create root-owned 0400 source credential files, without putting keys in command arguments/history. For each tenant, use the prompts below (substitute the instance name for tenant-b):

```bash
sudo sh -c 'umask 077; systemd-ask-password "SQL password for tenant-a" > /etc/devlens/tenant-a/sql-password; chmod 0400 /etc/devlens/tenant-a/sql-password'
sudo sh -c 'umask 077; systemd-ask-password "Tunnel runtime key for tenant-a" > /etc/devlens/tenant-a/tunnel-key; chmod 0400 /etc/devlens/tenant-a/tunnel-key'
```

Systemd LoadCredential supplies SQL credentials only to the runtime unit and the tunnel key only to the tunnel unit. The bridge does not read SQL credentials. SQL passwords stay in files/memory, never JSON or MCP arguments. The tunnel wrapper exports the runtime key for its child process, not system-wide. Host root can access all tenants; this is Unix user isolation, not protection against a compromised administrator/kernel. Protect backups and don't copy credential files into release artifacts. The launcher and profile are administrator-managed; a malicious same-UID process is inside the same tenant boundary.

6. Start the two services:

```bash
sudo bash /opt/devlens/linux/enable-instance.sh tenant-a
sudo bash /opt/devlens/linux/enable-instance.sh tenant-b
sudo devlens-health
sudo systemctl status devlens@tenant-a devlens-tunnel@tenant-a
sudo journalctl -u devlens@tenant-a -n 50 --no-pager
```

Each service uses its own process tree, profile and secret mount. `KillMode=control-group` and the headless SIGTERM handler stop children and dispose database/result state. Runtime retries are spaced by 10 seconds and tunnel retries by 20 seconds, with no permanent start-limit lockout after a network outage. Explicit service/VM stops do not trigger restart. Runtime/tunnel limits are 512/256 MiB respectively, not a capacity guarantee. Captures are RAM-only and never persisted.

## Health and acceptance

Inspect loopback dashboards locally or through SSH forwarding, never public binding:

```bash
/opt/devlens/tunnel/tunnel-client health --port 8080 --require-control-plane-poll --json
/opt/devlens/tunnel/tunnel-client health --port 8081 --require-control-plane-poll --json
```

`/readyz` alone does not prove successful authentication or SQL access. Create/select each separate Tunnel connection in ChatGPT and verify ten-tool discovery. Ask for status then `get_database_info` separately for each tenant, confirming exact instance/database. Test permitted table/schema and definition paging against disposable data before using business data. Mutation/procedure/Postman names must be unavailable. Confirm that a result ID from tenant-a is rejected by tenant-b. Avoid exporting live definitions/results as diagnostics.

Optional local SQL-only health test using the service's credentials:

```bash
sudo systemd-run --wait --pipe --collect --property=User=devlens-tenant-a \
  --property=LoadCredential=sql-password:/etc/devlens/tenant-a/sql-password \
  /opt/devlens/app/SqlServerDevLens.Headless --check /etc/devlens/tenant-a/instance.json
```

This opens/disposes one read-only metadata operation and prints only a generic success/failure on stderr. Normal stdio mode reserves stdout exclusively for MCP. The initial version logs lifecycle only, no raw SQL arguments, credentials, definitions or result rows. Do not enable upstream raw-payload logging.

## Troubleshooting, upgrade and uninstall

- Startup failure: check config name/schema, private modes and systemd credentials, then SQL permissions, host reachability and TLS. Provider exceptions are intentionally not printed. No automatic SQL retries.
- Process running but no database access: status isn't a SQL connection test; call database metadata or use `--check`.
- Wrong tenant: stop the service and inspect its name, config, tunnel/profile selection. Do not change databases by prompt.
- Dashboard bind failure: another process owns the selected port. Stop the duplicate and review the generated profile; do not publish the port externally.
- Connector missing tools: refresh the correct remote connection, then use a new chat. This read-only product intentionally has ten tools, unlike the Windows adapter's 27.
- Upgrade: stop all affected services, back up private configs separately, verify/extract a new package in a staging directory, replace only root-owned `/opt/devlens`, daemon-reload if the unit changed, start and independently revalidate each tenant. Preserve private `/etc/devlens` and `/var/lib/devlens-*`; do not overwrite profiles with another tenant's files. Roll back application files if acceptance fails.
- Uninstall: disable/stop each service, revoke its tunnel key/connection in Platform/ChatGPT, remove the unit and daemon-reload. Keep private configuration/state until its owner explicitly authorizes deletion. Remove dedicated accounts only after stopping their entire process trees. No script deletes credentials or unrelated users automatically.

## Tested scope and remaining work

Offline tests cover strict configuration/TLS, the ten-tool allowlist, blocked SQL shapes, independent captures and real concurrent stdio processes with distinct identities. Package CI repeats stdio smoke tests against the self-contained Linux binary and reports idle RSS for two processes. These offline tests do not establish live SQL connectivity, systemd hardening across two service users, actual remote tunnel reconnection, tenant authorization in ChatGPT or load capacity. Those require an authorized Ubuntu acceptance environment; no live VPS is deployed automatically.

Plan 2 vCPU/4 GB/40 GB for 2–5 lightly used instances only as a hypothesis. Measure complete tunnel+child process trees, concurrent SQL operations, peak capture memory and latency before accepting that target. Existing per-capture 16 Mi-character and aggregate 64 Mi-character budgets are reused; .NET strings/serialization and transient allocations can require substantially more RAM. More than one child per tunnel can multiply this. The service memory limit may terminate the tenant under pressure and clear captures. Do not size from idle RSS alone.

Future approvals: add a separately authenticated, tenant-scoped operator control plane with immutable exact-action review, nonce/expiry, single-use apply, revision checks and private backups. Bind decisions to tenant, actor and request hash and audit without payload secrets. Do not expose approval grants to the agent or add unrestricted auto-approval. Postman and write tools remain absent until that design is reviewed and implemented.


## Azure unattended boot and daily deallocation

Follow the complete [Azure VM startup/shutdown guide](AZURE-VM-OPERATIONS.md). Both services are enabled once by `enable-instance.sh`; starting the VM then starts them without an interactive login. `/etc/devlens`, `/var/lib/devlens-*` and `/opt/devlens` must be on persistent managed disks. Only sockets/credential mounts under `/run` are ephemeral and rebuilt on each boot. No SQL write tools are enabled by these changes. The Azure auto-shutdown schedule is configured in Azure, not by a Linux shutdown cron job.

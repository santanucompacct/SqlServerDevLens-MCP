# Azure Ubuntu VM: configure once, start from Portal

> **New experimental option:** This branch now also includes a Linux-only full SQL + Postman operator mode and a single localhost web portal with per-instance settings. The original ten-tool read-only mode remains the default. See [LINUX-ADMIN-PORTAL.md](LINUX-ADMIN-PORTAL.md). Manual remote approval is not yet available in headless mode; only categories activated in the private operator portal can automatically execute staged sensitive operations, and those category grants expire. Validate on disposable SQL/Postman data first. The Windows desktop and workflows are unchanged.\n\n
This is the experimental **Linux read-only** build, targeting Ubuntu 24.04 LTS x64. It changes no Windows functionality. There are two independent system services per instance: `devlens@NAME` owns SQL and captured results; `devlens-tunnel@NAME` owns the OpenAI tunnel and a stdio-to-Unix-socket bridge. No Linux login session is needed after initial setup. No execute, approval, mutation or Postman tools are enabled.

## 1. Prepare the VM and persistent storage

Use a persistent Azure **managed OS disk**, or mount persistent managed data disks at the deployment paths before the services start. Do not put configuration, credentials or the application on an ephemeral OS disk, Azure temporary disk, `/mnt`, `/tmp` or a container's ephemeral layer. Keep `/etc/devlens`, `/var/lib/devlens-*`, `/opt/devlens` and installed unit files on persistent storage. Root-only files survive normal deallocation/reboot; RAM captures do not. Maintain encrypted, access-controlled backups separately. Host root can read all tenants.

Install Ubuntu updates, Python 3, curl, unzip, the Ubuntu `acl` package and the .NET native dependencies (ICU/OpenSSL/CA certificates). The self-contained application does not require a separate .NET installation. Allow outbound access to the intended SQL endpoints and OpenAI tunnel service. Use authorized private routing/VPN and correct DNS where necessary. Do not expose 1433 or dashboard ports publicly. A public SSH IP may change after deallocation; the outbound tunnel does not require a fixed public VM IP.

## 2. Install the complete Linux package

Download the Linux workflow artifact, extract the outer GitHub ZIP and enter the folder containing the tarball and SHA256SUMS.txt. Verify and extract:

```bash
sha256sum --check SHA256SUMS.txt
tar -xzf SqlServerDevLens-linux-headless-0.1.0-linux-x64.tar.gz
sudo install -d -m 0755 /opt/devlens
sudo cp -a SqlServerDevLens-linux-headless-0.1.0-linux-x64/. /opt/devlens/
sudo chown -R root:root /opt/devlens
sudo bash /opt/devlens/linux/install-services.sh
```

Install only into a new/stopped root-owned deployment. Scripts and binaries must not be writable by service users. Installation writes the two system unit templates and `/usr/local/sbin/devlens-health`; it does not start any tenant or require Azure credentials. Service units wait for `network-online.target`; that target is not a guarantee that remote DNS, SQL or the tunnel endpoint are reachable. Keep Ubuntu's appropriate wait-online service enabled for your network manager. Fixed-delay process retries and per-operation SQL connections handle later availability without blocking boot indefinitely.

## 3. Configure each instance once

Create your own distinct Platform tunnels in the intended organization/workspace, with runtime keys authorized for the intended tunnels. The IDs are not API keys. See [official tunnel setup](https://github.com/openai/tunnel-client/blob/v0.0.16/docs/end-user-guide.md).

Example with two names and separate local dashboard ports (replace the ID placeholders fully with actual `tunnel_...` IDs):

```bash
sudo bash /opt/devlens/linux/configure-instance.sh tenant-a YOUR_FIRST_TUNNEL_ID 8080
sudo bash /opt/devlens/linux/configure-instance.sh tenant-b YOUR_SECOND_TUNNEL_ID 8081
sudoedit /etc/devlens/tenant-a/instance.json
sudoedit /etc/devlens/tenant-b/instance.json
```

Enter the correct host, port, database and restricted SQL login for each instance. Preserve its matching `instanceId`. Have the DBA grant SELECT and required metadata visibility only, without EXECUTE, DDL, writes, cross-database grants or administrative roles. SQL TLS certificate validation is mandatory; install the correct CA and use a matching hostname. No trust-certificate override is offered. Read-only routing intent is not a substitute for SQL permissions.

Configure stores without putting passwords in command arguments or shell history. These prompts are **initial administrator setup only**, never service startup:

```bash
sudo sh -c 'umask 077; systemd-ask-password "tenant-a SQL password" > /etc/devlens/tenant-a/sql-password; chmod 0400 /etc/devlens/tenant-a/sql-password'
sudo sh -c 'umask 077; systemd-ask-password "tenant-a tunnel key" > /etc/devlens/tenant-a/tunnel-key; chmod 0400 /etc/devlens/tenant-a/tunnel-key'
sudo sh -c 'umask 077; systemd-ask-password "tenant-b SQL password" > /etc/devlens/tenant-b/sql-password; chmod 0400 /etc/devlens/tenant-b/sql-password'
sudo sh -c 'umask 077; systemd-ask-password "tenant-b tunnel key" > /etc/devlens/tenant-b/tunnel-key; chmod 0400 /etc/devlens/tenant-b/tunnel-key'
sudo bash /opt/devlens/linux/enable-instance.sh tenant-a
sudo bash /opt/devlens/linux/enable-instance.sh tenant-b
sudo devlens-health
```

Source secrets are protected root-owned files on the encrypted persistent disk, not plaintext JSON, shell arguments or Git. Systemd LoadCredential gives each unit a private read-only credential mount at boot. The Ubuntu `acl` package supplies getfacl for validating systemd’s per-user credential ACLs; configuration files and persistent source credentials remain protected by strict file modes. The SQL service receives only `sql-password`; the tunnel receives only `tunnel-key`. Service users cannot replace the root source directory or files. This is not Key Vault integration or protection against host root. Never share a configured VM image, credential files or private profiles as a distribution.

Both system units are now **enabled for boot**. Verify with `sudo systemctl is-enabled devlens@tenant-a devlens-tunnel@tenant-a`. Profiles, credentials and enabled-unit symlinks persist; the `/run/devlens-NAME` socket directory is recreated automatically. Configure additional names/ports the same way. The script refuses existing instance directories instead of overwriting credentials. Earlier draft packages used a tunnel-owned SQL process; upgrade those only after stopping both components and regenerating their profile to use `--bridge /run/devlens-NAME/mcp.sock`, preserving the intended tunnel ID, port and key reference.

## 4. Create each ChatGPT connection once

Keep both services healthy, then in ChatGPT add a custom MCP connection using **Connection: Tunnel**, selecting the intended tunnel. Select the connection in the chat and confirm `get_server_status`, `get_database_info` and exactly ten read tools. Check each instance/database explicitly. The dashboard at `127.0.0.1:8080/ui` or `8081/ui` is local management, not the MCP URL. Inspect through SSH forwarding if needed. Refresh the existing ChatGPT tool catalog after software upgrades; a VM start does not require recreating the connector/key.

## 5. Daily operation from Azure Portal

**Start:** Azure Portal -> Virtual machines -> your VM -> Start. Allow Ubuntu/networking/services to boot. No SSH login, terminal, password prompt or manual script is needed. Use the existing ChatGPT connections. If reachable but not ready, allow process retry time and run `sudo devlens-health` during troubleshooting.

**Automatic stop:** In Azure Portal -> VM -> Operations -> Auto-shutdown, enable the schedule for **01:00**, time zone **India Standard Time (UTC+05:30)**. Keep automatic startup disabled if you want manual starts only. Confirm after the first scheduled stop that Azure shows **Stopped (deallocated)**. This repository does not modify your Azure subscription or schedule. Follow [Microsoft's schedule instructions](https://learn.microsoft.com/azure/virtual-machines/auto-shutdown-vm).

Do not replace Azure deallocation with a Linux cron `shutdown`: guest shutdown alone may leave a VM allocated and billed. Storage/networking charges may continue while deallocated; see [Azure states and billing](https://learn.microsoft.com/en-us/azure/virtual-machines/states-billing). Changing Linux timezone is not required for the systemd boot services; the Azure schedule has its own timezone.

During normal OS shutdown, systemd stops the tunnel before its SQL runtime, sends SIGTERM, cancels active operations, closes sockets and disposes capture memory. Services have a 45-second stop budget; forced power-off, host failure or an exceeded deadline cannot guarantee graceful completion. No business writes are exposed. Config/credentials stay on disk, but in-memory results and MCP sessions do not survive VM/runtime restart. Reinitialize a client session after reconnect; do not replay a previous SQL operation automatically.

## Health, logs and recovery

`sudo devlens-health` checks every configured instance in parallel (up to four): both units active, actual runtime identity/database, a live read-only SQL metadata operation, and authenticated tunnel control-plane polling. It returns exit code 0 only when all checks pass, otherwise 1 and a generic per-instance summary. It does not run procedures or HTTP application requests. It does not prove that ChatGPT's selected connector/workspace is correct, nor does it print rows, keys or connection strings.

```bash
sudo devlens-health
sudo systemctl status devlens@tenant-a devlens-tunnel@tenant-a
sudo journalctl -u devlens@tenant-a -n 50 --no-pager
sudo journalctl -u devlens-tunnel@tenant-a -n 50 --no-pager
```

Each instance has a dedicated Unix user, configuration/state/profile/home/cache paths and per-unit journal identity. Service accounts are not members of `adm` or `systemd-journal`; privileged administrators can inspect all units. Ubuntu journal retention is a host policy: configure persistent journald storage and retention if reboot-surviving audit logs are required. Never enable raw MCP/SQL payload logging or include secret files in support bundles.

Crash recovery uses systemd `Restart=always`, 10-second SQL-runtime and 20-second tunnel delays, with start-limit lockout disabled. A manual stop or shutdown does not restart the unit. A tunnel restart leaves the SQL runtime/captures alive; a SQL runtime restart changes `runtimeId` and clears captures. A live bridge disconnected by a crash cannot preserve an MCP session; a fresh bridge/session is required. `network-online.target` orders startup but cannot ensure external service availability. Tunnel-client handles its own bounded polling/backoff. Each SQL read opens/disposes its own connection: a failed read is returned as an error, and a later explicitly requested read can reconnect. There is no automatic replay of agent SQL calls.

Permanent errors (expired key, wrong password, wrong TLS CA/name, missing permissions) need administrator correction; retries do not repair credentials. Keep credentials valid and unexpired for unattended boot. To rotate keys, replace the appropriate root-owned 0400 source file privately and restart only the affected service. For SQL credentials restart `devlens@NAME`; its `PartOf` tunnel unit also stops/restarts on an explicit runtime restart. Verify health afterwards.

## Upgrades and uninstall

Stop both units for affected tenants, verify/extract the new archive separately, replace root-owned application/scripts, rerun install-services.sh, then start both units and verify health. Preserve `/etc/devlens`, `/var/lib/devlens-*` and enabled units. Never copy one tenant's profiles/credentials to another. Use OS/data-disk backups for recovery; RAM captures are intentionally not backed up.

To remove an instance, `sudo systemctl disable --now devlens-tunnel@NAME devlens@NAME`, revoke its Platform/ChatGPT access, and retain private files until their owner authorizes deletion. Remove dedicated users/files manually after checking all their processes have stopped. If removing the entire product, remove both unit templates and health command, then daemon-reload. No uninstall script silently deletes credentials or unrelated users.

## Verification limits

CI uses synthetic credentials, offline Unix-socket/bridge tests and a fake tunnel process for systemd crash/stop/start tests. It cannot establish authenticated Azure/ChatGPT connectivity, live SQL/TLS availability or an actual Azure deallocation cycle. Complete a start -> health -> scheduled deallocation -> Portal start -> health acceptance cycle on an authorized test VM before relying on unattended use. Load-test full runtime+tunnel process trees before accepting the proposed small-VM capacity.

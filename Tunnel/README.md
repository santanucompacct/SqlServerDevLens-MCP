# DevLens Secure MCP Tunnel package

This Windows x64 portable package is a **private, user-configured OpenAI Secure MCP Tunnel deployment**, not a hosted public connector or a Cloudflare public website. It includes .NET runtimes: Visual Studio and a separate .NET installation are unnecessary.

1. Download an explicitly approved `tunnel-*` release ZIP and SHA256SUMS.txt, verify with `Get-FileHash -Algorithm SHA256`, and extract the complete ZIP to a permanent writable folder. Ordinary branch builds are test artifacts, not approved releases.
2. Run **Start-DevLens.cmd**, enter your own SQL/optional Postman credentials inside DevLens, complete Test Connection and leave the server Running.
3. In your own OpenAI Platform organization, create a tunnel associated with your intended ChatGPT workspace. Creating/managing requires Tunnels Read + Manage; runtime use requires Tunnels Read + Use. Custom MCP access also depends on your ChatGPT account/workspace. Do not reuse the developer's tunnel ID, key or profile.
4. Run **Configure-Tunnel.cmd**. Enter your own tunnel ID. It creates an environment-reference profile in `%APPDATA%\tunnel-client`; it does not authenticate or overwrite an existing profile. Use `Tunnel/Configure-Tunnel.ps1 -Profile another-name` when a profile already exists. Keep this folder stable: the profile records an absolute MCP executable path.
5. Run **Start-Tunnel.cmd** and enter your restricted runtime API key locally at the masked prompt. It is placed in this process environment for the client and is not saved by the launcher. A key already supplied through `CONTROL_PLANE_API_KEY` can also be used. It is never passed literally in CLI arguments. Trusted child processes may inherit environment secrets; do not use untrusted MCP commands. The default launcher uses profile `devlens`; use `Tunnel/Start-Tunnel.ps1 -Profile another-name` for a different profile.
6. Check `http://127.0.0.1:8080/ui`. From a separate terminal use `Tunnel\tunnel-client.exe health --port 8080 --require-control-plane-poll --json`. Keep both DevLens and the tunnel terminal running. No inbound internet port or SQL Server port forwarding is needed.
7. In ChatGPT Plugins, use Add custom MCP server, **Connection: Tunnel**, select your own associated tunnel and finish the authentication/review flow. Then request `get_server_status`, `get_database_info`, and optionally `get_postman_selection` only. Tool discovery must succeed; a local plugin skill alone is not a remote connection test.

Full architecture, verification, troubleshooting, upgrade and release gates: **TUNNEL_SETUP_AND_RELEASE.md** at the package root, or `docs/TUNNEL_SETUP_AND_RELEASE.md` in the source checkout. Official setup: https://developers.openai.com/api/docs/guides/secure-mcp-tunnels . Permissions and account availability must be checked independently; this ZIP does not grant them.

## Roles and shutdown

## One-click startup with a local INI

Run **Start-All.cmd**. On the first run it opens `Tunnel/launcher.ini` in Notepad. Enter your own `TunnelId` and `ApiKey`, save, close Notepad and run Start-All.cmd again. It starts DevLens (or uses an already running instance), starts the tunnel, and opens `http://127.0.0.1:8080/ui` once readiness succeeds. Complete SQL/Postman setup inside DevLens until Running. The launcher does not automate database login or approvals.

For later starts, use Start-All.cmd only. Keep the app and tunnel terminal open. Stop the old tunnel before launching; an occupied dashboard port causes a clear error. To change the tunnel ID or key, stop the tunnel, edit the INI and start again. No credential-management commands are required. Changes to the tunnel ID or extraction path create a separate generated profile without overwriting the existing `devlens` profile.

The INI deliberately stores the key in plaintext for this development workflow. Anyone able to read it can use the key. Keep it local and private. The source repository ignores completed INIs; release packaging includes only the blank example and rejects completed INIs. The key goes to the tunnel through its process environment, never a literal command-line argument. The existing separate setup/start commands remain available.

- `published_app/`: fresh self-contained Desktop; owns credentials, approvals and the local MCP runtime.
- `published_mcp/`: fresh self-contained stdio adapter launched by tunnel-client with `--desktop`.
- `Tunnel/`: pinned client 0.0.16 and upstream companion/license/SPDX files, plus safe setup/start scripts. Cloudflared is bundled upstream but unused in the verified outbound-polling setup.
- `plugins/` + `.agents/`: optional **local** desktop plugin and skill. Run Install-Plugin.cmd only for local plugin use; it is not the web tunnel connector. The plugin has an identical contained adapter copy because plugin executable paths must remain inside its root.

Ctrl+C in the tunnel terminal stops forwarding and its child adapter. Stop DevLens separately through its confirmation UI. Stopping processes does not revoke your OpenAI key or remove a tunnel: revoke/rotate those in your own Platform settings.

## Upgrade safely

Stop both processes, preserve per-user credentials and profile files outside the ZIP, replace the complete verified package, and restart. If the extraction path changes, create a new profile pointing at the new MCP executable; never overwrite a working profile blindly. Local plugin users also refresh their registered source and restart the chat app/use a new local chat for changed tools. A source checkout and portable folder are different registrations.

Never ship completed INIs, YAML profiles, API keys, logs, certificates, SQL exports or private backups. The original local launcher used a plaintext-key INI; this package deliberately does not copy that file or launcher. Do not expose the loopback dashboard remotely, enable raw HTTP logging, or share diagnostic exports without reviewing them for sensitive content.

## Release validation checklist

- [ ] Tagged commit and dedicated workflow identified; standard master/v* release unchanged.
- [ ] Complete archive, checksum, runtime/dependency and vendor hashes verified.
- [ ] Fresh extraction with spaces: stdio initialize/discovery/status and isolated profile creation pass.
- [ ] No developer identity, credentials, debug symbols, private endpoints or data in package.
- [ ] Recipient creates their own tunnel/key/workspace association and verifies live remote metadata calls.
- [ ] Human approves the exact first public tunnel asset; environment `tunnel-release` should require repository reviewers.

Offline tests cannot establish a new recipient's account permissions, authentication, Windows policy or end-to-end remote session. Those remain manual acceptance steps.

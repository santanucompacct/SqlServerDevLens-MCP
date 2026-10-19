# Tunnel setup, verification and separate release

Verification date: **19 October 2026**. Branch: `Tunnel_Installation`; standard `master`, its v0.11.0 release and existing release workflow are preserved. This document records inspected facts and explicitly distinguishes offline package tests from account-dependent remote validation.

## Verified new installation

| Check | Observed result |
|---|---|
| Client | OpenAI `tunnel-client.exe` 0.0.16, source commit `5f99daabd4aa4a77049e6d81d54a0d8c18335397` |
| Distribution/install | Extracted Windows amd64 upstream ZIP, not an observed Windows service/MSI install; local copies exist in the package's `Tunnel/` folder and development deployment |
| Provenance | Official v0.0.16 ZIP SHA256 `edef7241b0c647fcb30f1a80ff376b6b25c51927960f257a3f01e21b17c2aba6`; extracted executable hashes match the inspected installed files |
| Client SHA256 | `4226bb74a72388147882881f29f79801b95e285d731a138071812e3aa6766571` |
| Companion | Cloudflared 2026.8.2; SHA256 `cd0bc49aec24d0d62a37b6e842dad46000f7b401f6167b4cbace4e170819c9a2`; bundled but not configured/running in this connection |
| Authenticode | Both inspected upstream EXEs are unsigned; checksum/provenance verification is required. Cryptographic Sigstore attestation validation is NOT VERIFIED |
| Runtime config | User-owned YAML under `%APPDATA%\tunnel-client`, one stdio MCP command ending in `SqlServerDevLens.McpHost.exe --desktop`; API key is an environment reference |
| Control plane | Official `https://api.openai.com`, outbound HTTPS polling; no custom public hostname or inbound listener is required for this setup |
| Management | `127.0.0.1:8080` is the actual listener owned by tunnel-client. `/healthz`, `/readyz` and `/ui` returned HTTP 200; PID-checked health plus successful-control-plane-poll test returned `ok` |
| Exposure/auth | UI responds locally without a login, bound to loopback only. Do not enable remote UI; local OS account isolation remains important. TLS follows the official HTTPS control plane; optional control-plane mTLS is not configured/verified here |
| Processes | Running tunnel-client spawns the MCP host; DevLens Desktop is separate. Windows withheld protected process image paths, so exact live executable-image paths are NOT VERIFIED; profile targets and on-disk binary hashes were inspected |
| Startup | Existing batch → PowerShell launcher → user YAML/INI → tunnel-client → stdio adapter. No matching service or scheduled task was discovered. Exact elevated parent-process command line/working directory is NOT VERIFIED |
| Connected remote tools | The connected remote `SQL Server DevLens MCP` tool namespace returned live get_server_status, get_database_info and get_postman_selection. Remote tool discovery is available through that connector. This is distinct from the local `SQL Server DevLens` plugin |
| Remote client scope | These calls used the remote connector from this desktop chat. A browser-only ChatGPT Web session on a second computer was NOT independently tested; prior user-reported web success is not a clean-recipient acceptance test |
| Business safety | No procedure execution, DDL/DML, Postman edits or application HTTP runs during inspection |

The active path is:

```text
ChatGPT remote connector (authorized user/workspace)
  -> OpenAI-hosted Secure MCP Tunnel service over HTTPS
  <- outbound HTTPS polling / response posting by local tunnel-client
  -> local stdio published_mcp/SqlServerDevLens.McpHost.exe --desktop
  -> per-user desktop named pipe
  -> DevLens Desktop MCP runtime and its selected SQL/Postman context
```

`desktop-pipe` in a status response describes the inner adapter/runtime hop; it does not imply the outer remote call was a local plugin call. This installation does not expose a local HTTP/SSE MCP server on port 8080: that port is the operator dashboard. The user must keep Desktop Running and the tunnel daemon active. SQL connections open/dispose per operation; approval requirements remain enforced by Desktop.

### Differences and installation findings

- Historical local-only deployment: ChatGPT desktop local plugin launches a stdio adapter. New remote deployment: the OpenAI client launches that same adapter through a private outbound tunnel. The cloudflared manifest alone never established which path was used.
- The legacy launcher auto-opened the browser after readiness and read a real API key from an INI beside the binaries. The inspected INI had inherited broad local-user permissions. Do not distribute it. Its owner should restrict/remove that file and rotate a key if exposure is suspected; the live file was not modified by this task.
- New distributable scripts prompt locally or accept an environment key, preserve existing profiles, bind the management UI to loopback and never embed an identity. They intentionally do not auto-launch the dashboard watcher or silently start another Desktop instance. Open `/ui` manually after readiness.
- Both existing `published_app/` and `published_mcp/` were framework-dependent net10.0 publishes, missing coreclr.dll. They require an installed .NET runtime and cannot serve as the no-Visual-Studio/no-runtime package. The new builder publishes fresh self-contained win-x64 outputs instead.
- The developer checkout was on a feature branch with 130 local deleted tracked publish-output files. It was left untouched. Work uses an isolated clean checkout based on `Tunnel_Installation`; no reset/clean/force-push is needed.

## New Windows user without Visual Studio

Follow the package's **README-FIRST.md** (source: `Tunnel/README.md`). Download ZIP and SHA256SUMS only from an explicitly approved `tunnel-*` release, verify and extract the entire archive. Do not run inside the ZIP. The package includes .NET; ChatGPT/OpenAI accounts, SQL permissions, internet access and Windows policy are separate prerequisites.

1. Run Start-DevLens.cmd and complete your SQL or SQL + Postman setup locally. Keep Running.
2. Create your own tunnel in [Platform tunnel settings](https://platform.openai.com/settings/organization/tunnels), associated with your intended ChatGPT workspace. Creator: Tunnels Read + Manage. Restricted runtime key and connector operator: Tunnels Read + Use. ChatGPT custom MCP permissions are separate. Do not assume a subscription alone grants organization/workspace access.
3. Run Configure-Tunnel.cmd, enter your own tunnel ID, and preserve the generated profile in `%APPDATA%\tunnel-client`. Configuration is not authentication. Existing profiles are not replaced. Use a different `-Profile` value when required.
4. Run Start-Tunnel.cmd. Enter the restricted runtime key at the local masked prompt or supply CONTROL_PLANE_API_KEY through your approved local environment. It is never written by these scripts or passed literally in arguments. Trusted children can inherit environment secrets; this is not a Windows Credential Manager persistence feature.
5. Check localhost readiness and successful polling. In ChatGPT Plugins → Add custom MCP server → Connection: Tunnel, choose the same user-owned tunnel, finish authentication/risk review and create the private connection. No developer public URL, profile or certificate is required.
6. Ask for status, SQL database metadata and optional Postman selection. Do not run SQL writes/procedures or HTTP requests as an installation test. Enabled execution is not evidence that business requests work.

For CLI details, use the installed client's `init --help`, `help quickstart`, `doctor --help` and the [official Secure MCP Tunnel guide](https://developers.openai.com/api/docs/guides/secure-mcp-tunnels). Check the [upstream release and provenance instructions](https://github.com/openai/tunnel-client/releases/tag/v0.0.16). Secure tunnels support private integrations; distributing this installer is not publishing a public OpenAI plugin listing.

## Layout and start/stop

```text
published_app/              self-contained Desktop and dependencies
published_mcp/              self-contained stdio adapter and dependencies
Tunnel/                     pinned upstream binaries/licenses/SPDX + safe scripts
plugins/ + .agents/          optional local stdio plugin/skill/marketplace
Start-DevLens.cmd            opens Desktop only
Configure-Tunnel.cmd        creates a non-secret user-owned profile
Start-Tunnel.cmd            prompts locally and starts foreground tunnel
Install-Plugin.cmd          optional local plugin setup, not remote registration
BUILD-INFO.json              source commit, version and upstream checksum
licenses/                   .NET dependency notices
```

The plugin keeps an identical adapter copy inside `plugins/.../server/` for the required contained `./server/SqlServerDevLens.McpHost.exe` launch path. The remote profile uses `published_mcp`. Do not mix versions. Stop forwarding with Ctrl+C, then stop/close Desktop through its confirmation UI. Revoke keys or tunnel identities separately in Platform; stopping does not revoke them.

## Troubleshooting and upgrade

- **No readiness/poll:** use `tunnel-client health --port 8080 --require-control-plane-poll --json`; check outbound HTTPS/DNS/proxy policy, own runtime permissions, workspace association and whether another client owns port 8080. Do not expose port 8080 publicly.
- **Desktop unavailable:** complete Desktop connection setup and leave Running; adapter reconnects on its next call. Check both processes use the same Windows user/trust boundary. Do not launch a second tunnel to repair a SQL login error.
- **Profile path moved:** profile MCP command is absolute. Stop the old daemon and create a new profile for the new extraction location; preserve existing config rather than forcing replacement.
- **Tools missing:** check remote connector discovery versus local plugin discovery separately. Refresh/reconnect the intended connector and use a new chat when necessary. A skill alone is not tool access.
- **Wrong .NET/runtime:** use the fresh complete self-contained ZIP. Existing local publish folders are not authoritative. Do not copy just an EXE.
- **Logs:** inspect local UI/terminal only as needed. Raw HTTP logging was off in the inspected profile; keep it off. UI/log exports may contain private request details. Do not upload them unredacted.
- **Upgrade:** stop daemon and Desktop; keep user profiles/credentials outside the ZIP; replace all package files together; regenerate a profile if the path changed. Local plugin users separately refresh their registered source. Re-test read-only remote metadata before business work.

## Build, tests and release gates

`scripts/Build-TunnelDistribution.ps1 -Version 0.11.0 -Revision 1` reuses the unchanged standard publisher from the checked-out commit. It never copies local published_app, published_mcp or completed Tunnel configs. It downloads a pinned official ZIP, verifies its archive and individual file hashes, retains license/NOTICE/SPDX files, records git SHA, strips PDBs, scans staged paths/text, validates stdio/profile setup, then emits a versioned ZIP and SHA256SUMS. This is reproducible from pinned inputs; ZIP timestamps are not promised byte-for-byte reproducible across builds.

`scripts/Test-TunnelDistribution.ps1 -PackageDirectory <artifact-folder>` verifies a fresh extraction with spaces, runtime inventory, matching adapter copies, contained plugin command, vendor hashes, private-file/identity scanning, stdio protocol and isolated profile setup. It does not authenticate or poll a real recipient tunnel. Scanning cannot prove absence of all secrets; source/config review is also required.

`.github/workflows/tunnel-distribution.yml` handles PRs targeting/pushes to Tunnel_Installation and manual builds as **Actions artifacts only**. An intentional `tunnel-0.11.0-1` tag enables a separate contents-write release job after package tests. Configure the `tunnel-release` environment with required reviewers before using it. Releases are prereleases with `make_latest: false`; standard master/v* automation is unchanged. Future versions must update the explicit script defaults/tag validation deliberately.

**Initial public upload remains gated:** review the exact artifact inventory/hash, recipient identity isolation, license/provenance and remaining acceptance items, then obtain explicit owner approval before creating the first tunnel tag. No tag/release is implied merely by a green artifact build.

### Verification boundaries / release record

- Verified before packaging: versions/hashes, profile reference mode, loopback health/UI, PID-checked successful poll, remote connector status/SQL/Postman metadata, named-pipe/stdio topology and current framework-dependent local outputs.
- Package build/test and Actions run/commit/PR: record the actual results with the reviewed change; do not substitute this document for workflow evidence.
- NOT VERIFIED: second independent Windows account/computer with its own authenticated tunnel, browser-only web session, corporate execution policy, optional mTLS/Cloudflare mode, cryptographic Sigstore attestation verification and protected live process image paths. These are not claimed by offline tests.
- Preserve current user profile and live session. No original user's key, tunnel ID, authenticated endpoint, SQL host, database content or Postman payload is included in documentation or artifacts.

### Implementation and local test record

Reviewed implementation: [PR #10](https://github.com/santanucompacct/SqlServerDevLens-MCP/pull/10), targeting Tunnel_Installation only. Local .NET regression run: 139 passed, five environment-gated tests skipped. Fresh staging and extracted package checks passed for bundled runtimes/vendor hashes, private-file/path scan, headless MCP status, desktop-mode 25-tool discovery using an isolated nonexistent pipe, and recipient profile creation under Windows PowerShell 5.1. An extraction path containing spaces and rejection of an existing profile were exercised. Tests found and fixed both Windows backslash parsing and PowerShell 5.1 nested-quote loss; the generated MCP command uses a quoted normalized forward-slash path.

The recipient launcher enforces an environment-reference key, loopback health address and raw HTTP logging off. Exact Actions results/artifacts are available from the PR checks and the [dedicated workflow](https://github.com/santanucompacct/SqlServerDevLens-MCP/actions/workflows/tunnel-distribution.yml). An Actions artifact is not a public-release approval or evidence of a second recipient's authenticated setup. The actual scope of the current user's runtime key, key revocation, and environment required-reviewer configuration were NOT independently verified; account owners must check them before public release.

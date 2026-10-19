# DevLens Secure MCP Tunnel package

**Start here: [Illustrated first-time setup and daily-use guide](QUICKSTART.md).** It covers creating your own tunnel, creating a restricted runtime API key, filling the INI, starting everything and adding the custom MCP connection in ChatGPT.

This is a Windows x64 portable package with bundled .NET runtimes. Visual Studio and a separate .NET installation are unnecessary. Tunnel connectivity is a private, user-configured integration; distributing the application does not publish a public ChatGPT directory plugin or grant account permissions.

## One-time setup

1. Obtain the complete tunnel ZIP from a successful [dedicated workflow build](https://github.com/santanucompacct/SqlServerDevLens-MCP/actions/workflows/tunnel-distribution.yml), or an explicitly approved `tunnel-*` release when available. Branch artifacts are temporary test builds. Check the checksum and extract the inner package ZIP to a permanent, private folder.
2. Create your own [Platform tunnel](https://platform.openai.com/settings/organization/tunnels), associated with the intended organization and ChatGPT workspace.
3. Create your restricted runtime key in [Platform API keys](https://platform.openai.com/settings/organization/api-keys). Check organization-level Tunnels permissions with your administrator.
4. Run **Start-All.cmd**. It opens a newly created `Tunnel/launcher.ini`. Enter your own TunnelId and ApiKey, save and close it, then run Start-All.cmd again.
5. Finish SQL/optional Postman setup in DevLens until Running. The launcher starts the tunnel and automatically opens `http://127.0.0.1:8080/ui` after readiness. Check successful control-plane polling too.
6. In ChatGPT **Plugins → + → Add custom MCP server → Connection: Tunnel**, choose your tunnel, review the authentication/risk settings and create/enable the connection. Verify real status and read-only metadata calls as shown in the guide.

## Every later start

Run **Start-All.cmd** only, complete connection setup in DevLens, and keep the desktop and tunnel terminal open. Do not run Configure-Tunnel.cmd daily. To change the tunnel ID/key, stop the tunnel, edit `Tunnel/launcher.ini` and restart. The ID and package path determine a generated profile; existing `devlens` profiles are preserved. Stop an existing tunnel before launching; the launcher refuses an occupied port 8080.

The completed INI stores the key in **plaintext**, by the owner's development-workflow choice. Restrict folder access and never share it or commit it. Only a blank example is packaged; completed INIs are ignored by Git and rejected by package checks. The runtime key is passed through the process environment, never literally in command arguments. Trusted child processes may inherit that environment. Enter SQL/Postman credentials only inside DevLens.

**Security status:** Chrome blocked the tested one-click build. Defender scans found no threats, but Google's classification is unresolved. See [exact candidate scan results and limitations](DOWNLOAD-VALIDATION.md). Do not disable protection or describe the build as cleared for colleague distribution.

## Folder roles and shutdown

- `published_app/`: self-contained desktop; owns connection context, credentials, approvals and the MCP runtime.
- `published_mcp/`: self-contained stdio adapter, started automatically by the tunnel client.
- `Tunnel/`: pinned client 0.0.16, upstream companion/license/SPDX files, launcher and this guide. Cloudflared is bundled upstream but unused in this outbound-polling deployment.
- `plugins/` and `.agents/`: optional local desktop plugin and skill. Install-Plugin.cmd is a separate local-plugin setup, not the remote tunnel connection.

Ctrl+C in the tunnel terminal stops forwarding and its child adapter. Stop/close DevLens separately through its confirmation UI. This does not revoke the key. Keep the dashboard bound to loopback; do not publish it remotely or enable raw HTTP logging.

## Manual launch alternative

The separate Start-DevLens.cmd, Configure-Tunnel.cmd and Start-Tunnel.cmd remain available. Configure once; the default profile is `devlens`, and existing profiles are preserved. Start-Tunnel.cmd prompts locally for a key or accepts `CONTROL_PLANE_API_KEY`, without saving it. For another profile use `Tunnel/Start-Tunnel.ps1 -Profile another-name`. These manual commands do not consume launcher.ini.

The [full guide](QUICKSTART.md) covers upgrades, troubleshooting, both chat prompts and verification. Maintainer setup/release evidence is in TUNNEL_SETUP_AND_RELEASE.md at the package root (docs/TUNNEL_SETUP_AND_RELEASE.md in source). The standard master/v* release workflow remains separate; only explicit `tunnel-*` tags can publish tunnel prereleases. Account access and a second recipient's authenticated installation are separate acceptance checks.

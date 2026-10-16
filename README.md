# SQL Server DevLens MCP

**Your SQL Server, understood by AI.** An MIT-licensed .NET 10 / Avalonia desktop application with owl branding.

## Current milestone

The two-mode startup screen now opens a SQL Server login form: TCP host, port (1433), one required database, SQL authentication or Windows integrated authentication, SQL username/password, encryption (on), and trust-server-certificate (off). Test Connection uses Microsoft.Data.SqlClient and opens/disposes a connection per attempt. It does not maintain a session or execute inspection queries. Provider errors are reduced to safe messages/error numbers, without displaying passwords or connection strings.

Remember profile saves only connection metadata after a successful test. Profiles live in `Environment.SpecialFolder.LocalApplicationData/SqlServerDevLens/connections.json`, outside the repository. **Host, database and username are not encrypted**; use only an appropriate OS account/location. Forget saved profile removes it immediately. Leaving Remember profile unchecked removes a previous profile after a successful test. Password is cleared after every test and when returning to mode selection.

Remember password is visibly disabled. The core includes an `ICredentialStore` boundary with a fail-closed implementation; Windows Credential Manager, macOS Keychain and Linux Secret Service adapters are staged for a later milestone. There is no plaintext-password fallback. Passwords are briefly held as managed strings in memory and cannot be guaranteed zeroed by .NET.

The separate official C# SDK MCP host exposes **only `get_server_status`**. It reports transport readiness and explicitly reports SQL as unconfigured/disconnected. It neither reads desktop profiles nor shares the desktop's test results. All host logs go to stderr; stdout is reserved for MCP JSON messages.

**Postman integration remains planned**, including when SQL Server + Postman is selected. SQL metadata/data tools, desktop-to-host configuration, secure password persistence and distributable cross-platform installers are not implemented.

## Pull and run in Visual Studio 2026

Install Visual Studio 2026 with .NET desktop development and a .NET 10 SDK. Avalonia packages restore from NuGet; a paid extension is not required. From your existing clone:

```powershell
git fetch origin
git switch feature/sql-login-mcp-status
git pull --ff-only origin feature/sql-login-mcp-status
dotnet restore SqlServerDevLens.slnx
dotnet build SqlServerDevLens.slnx -c Release
dotnet test SqlServerDevLens.slnx -c Release --no-build
```

For a new clone, first run `git clone https://github.com/santanucompacct/SqlServerDevLens-MCP.git` and enter the cloned directory. After the PR is merged, use `master` instead of the feature branch.

Open `SqlServerDevLens.slnx` in Visual Studio 2026. Set `SqlServerDevLens.Desktop` as the startup project, then press F5. Choose either mode and Continue. Enter a TCP host/IP, port and a single database. Named instances must be resolved to their TCP port; do not enter a connection string or `server\instance` in Host. SQL authentication is the default; check Windows authentication to use the current process identity. On macOS/Linux integrated authentication requires correctly configured Kerberos and is not equivalent to entering Windows credentials.

Use Test Connection with an account authorized for the chosen database. Keep encryption enabled and certificate trust disabled with a valid server certificate. For an intentionally self-signed test server, choosing Trust server certificate bypasses certificate validation. A success means only that SqlClient opened the selected database; no SQL tool or MCP connection was established. Test wrong credentials, invalid database, unreachable port, and certificate failure on your own test server. Do not paste credentials into chats, JSON, command arguments or source files.

## Test the local MCP plugin

From the repository root in PowerShell 7:

```powershell
dotnet build SqlServerDevLens.slnx -c Release
./scripts/Test-Mcp.ps1
dotnet publish SqlServerDevLens.McpHost -c Release --no-self-contained -o plugins/sql-server-devlens/server
./scripts/Test-Mcp.ps1 -HostDll ./plugins/sql-server-devlens/server/SqlServerDevLens.McpHost.dll
```

The smoke script launches the compiled host directly, sends initialize, tools/list and tools/call messages, checks the status response, and shuts it down. It fails on non-JSON stdout and bounds response waits. The published server directory is ignored by Git. This is a framework-dependent local package requiring .NET 10 on the target machine; do not launch through `dotnet run`, which can print build messages onto the protocol stream.

The repository includes `plugins/sql-server-devlens/plugin.json`, `mcp.json`, and a repo-scoped `.agents/plugins/marketplace.json`. The MCP configuration uses `dotnet` with `${PLUGIN_ROOT}/server/SqlServerDevLens.McpHost.dll`, so the published server and dependencies must be present **before installation**.

1. Open this repository as a local project in the ChatGPT desktop app's Work/Codex local execution surface.
2. Restart the desktop app to discover the repo marketplace. In Plugins Directory select **DevLens Local** and install **sql-server-devlens**. Availability depends on the account/client; absence of this source is not proof the MCP host failed.
3. In a locally executed chat enable/mention the plugin and ask: “Call get_server_status from SQL Server DevLens and show the result.”
4. Expect `status: ready`, `transport: stdio`, `sqlConfigured: false`, `sqlConnected: false`, `postmanImplemented: false`. A successful call verifies this client's plugin transport only.
5. After republishing/changing plugin files, refresh/reinstall the local plugin and restart the app; installed plugins are cached copies.

These steps follow the official [local plugin packaging/marketplace guide](https://developers.openai.com/plugins/build/plugins) and [stdio configuration guide](https://developers.openai.com/plugins/deploy/desktop-only). Public-directory submission is a separate approval process; this repository does not claim account eligibility or a verified ChatGPT installation. No Azure endpoint or OpenAI API key is required by this host.

## Validation and boundaries

Verified locally on Windows with .NET SDK 10.0.402: restore; Release solution build with zero warnings/errors; five core tests covering validation, safe connection-string construction, integrated-auth credential omission, profile round trip/deletion, and unavailable secure storage; real stdio initialize/list/call smoke test. No real SQL Server connection or ChatGPT Desktop installation has been verified. The UI compiles, but visual/interactive QA is pending.

GitHub Actions runs restore/build/tests and the stdio smoke script on Windows, Ubuntu and macOS. Results must be checked on the PR before claiming those platforms pass. CI does not contact a SQL Server.

## Structure and next milestones

- `SqlServerDevLens.Desktop`: Avalonia MVVM startup and login UI.
- `SqlServerDevLens.Core`: connection/profile DTOs and interfaces, SqlClient tester, credential-free JSON storage.
- `SqlServerDevLens.McpHost`: independent stdio host, status tool only.
- `SqlServerDevLens.Tests`: core safety tests.
- `plugins/`, `.agents/plugins/`: local plugin package and marketplace.
- `scripts/Test-Mcp.ps1`: protocol smoke test.

Next: verified SQL integration tests and UI QA; OS credential adapters; explicit desktop-to-host profile selection; controlled read-only metadata tools; optional Postman work later. Use least-privileged SQL accounts and enforce database access in SQL Server. Never commit credentials, customer data, local profiles or tokens. There is no unrestricted SQL execution tool.

Licensed under [MIT](LICENSE.txt). Third-party tools retain their own licenses.

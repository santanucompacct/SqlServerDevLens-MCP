# Install DevLens for another user or account

The SQL/Postman instructions are already bundled at `skills/devlens-workflow/SKILL.md`, with supporting references. Installing this complete plugin installs the tools and skill together. Do not copy the skill into a chat or create a second plugin for Postman.

Each recipient needs ChatGPT Desktop with local-plugin/local-execution access, a compatible DevLens desktop application on the same computer, and their own authorized SQL/Postman setup. Plus or higher is the supported DevLens setup, subject to account/workspace availability. The adapter needs the .NET 10 runtime; building from source needs the .NET 10 SDK and, for the Visual Studio workflow, Visual Studio 2026.

## Recommended: install from the source project

Until the preview is merged, use `feature/postman-setup`. On the recipient's computer, open a local Work/Codex chat with access to the folder where they want the project, then use the setup prompt below. It can carry out the following steps when local tools/permissions are available. Manual PowerShell alternative:

```powershell
git clone --branch feature/postman-setup --single-branch https://github.com/santanucompacct/SqlServerDevLens-MCP.git
cd SqlServerDevLens-MCP
dotnet restore SqlServerDevLens.slnx
dotnet build SqlServerDevLens.slnx -c Release
dotnet publish SqlServerDevLens.McpHost -c Release --no-self-contained -o plugins/sql-server-devlens/server
codex plugin marketplace add .
codex plugin add sql-server-devlens@devlens-local
```

If the repository already exists, pull the latest feature branch instead of cloning over it. `codex` must be installed and on PATH for the last two commands. If it is unavailable, open this repository as a local project, use its **DevLens Local** source in Plugins, and install/enable **SQL Server DevLens** after the adapter is published. Do not overwrite another personal marketplace to make this plugin visible.

Fully quit/reopen ChatGPT Desktop once after installation. In a local chat, enable/select the plugin and check for **DevLens SQL and Postman** (`sql-server-devlens:devlens-workflow`) in the skill picker. Existing chats may need a fresh local chat to see the newly installed components.

Open `SqlServerDevLens.slnx` in Visual Studio 2026, select `SqlServerDevLens.Desktop` as startup project and start without debugging (Ctrl+F5). Enter credentials in DevLens, choose SQL-only or SQL + Postman, complete setup and leave the server Running.

## Copyable setup prompt for the recipient

Replace `<FULL_LOCAL_REPOSITORY_PATH>` with the recipient's local project folder, such as `C:\Projects\SqlServerDevLens-MCP`.

```text
Install the existing SQL Server DevLens local plugin for use in my local ChatGPT desktop chats from <FULL_LOCAL_REPOSITORY_PATH>. Read its README and plugins/sql-server-devlens/INSTALL.md. Use the current feature/postman-setup preview until merged. Publish the .NET 10 stdio adapter, register the existing DevLens Local marketplace if needed, and install/enable sql-server-devlens@devlens-local. Preserve other marketplace entries and the plugin identity. Include the entire skills/devlens-workflow folder and its references/agents metadata; do not create a separate skill or hosted connector. Verify the installed package version, enabled plugin and discovery of sql-server-devlens:devlens-workflow. Tell me any local permissions, installation tools or ChatGPT restart steps still needed. Credentials must be entered only in DevLens. Once I have completed DevLens setup, call get_server_status and report the actual selected database and configuration; do not execute SQL or application requests as an installation test. If tools/skill are unavailable, explain the limitation instead of claiming success.
```

**Plugins → + / Add** may offer a creation assistant or an HTTP connector form, depending on the host. Use the prompt only in a local setup assistant/chat. If Add asks for a server URL, this stdio package is not that type of connector. Use the local marketplace; do not enter a SQL host, SQL port or invented localhost URL.

## If you distribute a prebuilt package

Provide the compatible DevLens desktop application and the **whole plugin folder**, including hidden `.codex-plugin`, root `plugin.json`, `mcp.json`, `INSTALL.md`, all `skills/` files, and the complete published `server/` output (DLLs, dependency/runtime files and subdirectories). One EXE or the host DLL alone does not contain the plugin skill. GitHub source checkouts omit `server/`; build/publish it before installing or sharing a prepared plugin.

The recipient can place that folder under a local project as `plugins/sql-server-devlens`, with this file at the project root's `.agents/plugins/marketplace.json`:

```json
{
  "name": "devlens-local",
  "interface": { "displayName": "DevLens Local" },
  "plugins": [{
    "name": "sql-server-devlens",
    "source": { "source": "local", "path": "./plugins/sql-server-devlens" },
    "policy": { "installation": "AVAILABLE", "authentication": "ON_INSTALL" },
    "category": "Productivity"
  }]
}
```

Register/open that local project root, install the plugin, restart ChatGPT, and verify the skill as above. If a marketplace already exists, merge the entry rather than replacing the catalog. A ZIP is a transport for these files; do not assume every ChatGPT Add screen imports a local stdio ZIP. No public-directory submission is required for this local marketplace workflow.

Do not distribute saved profiles, Credential Manager secrets, private database/Postman exports, backups or live response files. Recipients enter their own authorized connection details. Plugin discovery is a local installation/host setting, not an automatic transfer from this developer's ChatGPT account. If another account on the same computer already sees the correct installed plugin and skill, enable/verify them rather than creating duplicate copies. Changing ChatGPT sign-in does not establish a new SQL/Postman connection.

## Verify before normal work

Ask: **Use the DevLens workflow skill to call get_server_status and check my connection.** Expect the actual server state; `sqlConnected: false` is normal per-operation connection handling. For combined mode, also check `get_postman_selection`. These checks do not execute a procedure or application request. DevLens approvals still apply to procedure executions, changes and Postman runs.

Local installation follows [official marketplace guidance](https://developers.openai.com/plugins/build/plugins#install-a-local-plugin-manually). Local stdio plugins require [desktop/local execution](https://developers.openai.com/plugins/deploy/desktop-only). Skill discovery on this development host was verified; another account/computer must verify its own installation and access.

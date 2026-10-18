# Connect SQL Server DevLens to ChatGPT Desktop

This guide covers first-time local-plugin installation and starting SQL or SQL + Postman work in an existing chat. Checked against official OpenAI documentation on 17 October 2026.

For first installation or an existing source/portable upgrade, follow [Install and upgrade](https://github.com/santanucompacct/SqlServerDevLens-MCP/blob/master/docs/INSTALL-AND-UPGRADE.md). A build alone does not refresh the installed adapter/tool catalog.

## Before you start

For a machine without Visual Studio/.NET, use the [Windows x64 portable distribution](https://github.com/santanucompacct/SqlServerDevLens-MCP/releases/tag/v0.11.0): extract it, run Install-Plugin.cmd, restart ChatGPT once, then Start-DevLens.cmd. Skip the source-build prompt below when using that package. Its setup installs the bundled adapter EXE and skill.

- Use the ChatGPT desktop app on the same computer as DevLens, with a chat running locally in Work/Codex. This stdio plugin needs local execution; opening a desktop window alone does not make every chat local.
- ChatGPT Plus or higher is the supported setup for this DevLens guide. This is not a claim that Plus is OpenAI's universal minimum. Local-plugin access also depends on the app, account and workspace policy; check availability before subscribing.
- Build DevLens with .NET 10. Start without debugging (Visual Studio: Ctrl+F5). Debugger handling of parsing exceptions can slow large Postman collections enough to cause timeouts.
- Enter passwords and Postman API keys only inside DevLens. The prompts below do not need credentials.

## 1. First-time installation: Plugins → Add

If SQL Server DevLens is already installed and working, skip to section 2. Both operating modes use the **same plugin**.

1. Keep a local checkout of this repository. Use the default branch `master` for SQL, Postman and the bundled workflow skill.
2. Open that folder as a local Work/Codex project. It contains `SqlServerDevLens.slnx`, `plugins/sql-server-devlens` and `.agents/plugins/marketplace.json` (the **DevLens Local** catalog).
3. Open **Plugins**. If **+ / Add** offers an assistant or plugin-creation prompt, paste the setup prompt below. Labels/options vary by app version. If the local plugin is already listed, select **DevLens Local → SQL Server DevLens → Install/Enable** instead.
4. Replace `<FULL_LOCAL_REPOSITORY_PATH>` with your actual checkout folder, for example `C:\Projects\SqlServerDevLens-MCP`. Send the prompt in a local chat with access to that folder.

### Copy this setup prompt

```text
Set up the existing SQL Server DevLens local plugin from <FULL_LOCAL_REPOSITORY_PATH> on this computer. Read its README and plugins/sql-server-devlens/INSTALL.md. Use its existing plugin package and DevLens Local marketplace. Build/publish the .NET 10 stdio adapter and install/enable sql-server-devlens@devlens-local using the supported local-plugin workflow. Include the bundled skills/devlens-workflow folder and its references/agents metadata. Preserve the plugin identity and other marketplace entries. Verify the installed version, enabled plugin and discovery of sql-server-devlens:devlens-workflow. Do not create a separate skill or hosted connector, or ask for credentials in chat. Tell me any restart/manual steps that remain. After DevLens setup, call get_server_status and report the actual result; do not claim success if tools or skill are unavailable. Do not execute SQL or application requests during setup.
```

This is a request for the assistant to perform local setup, not a magic activation command. It depends on available installation tools and local-folder access. A prompt alone cannot grant plugin permissions or install a plugin in an unsupported chat.

**If Add opens a form asking for an MCP server URL:** cancel that form for this setup. DevLens supplies a local stdio adapter, not a public HTTP endpoint. Do not enter the SQL host, SQL port, a localhost URL or credentials there. Use the local marketplace workflow above; the exact build/publish/install commands are in [Publish/install and test the local plugin](../README.md#publishinstall-and-test-the-local-plugin).

After first installation or an adapter upgrade, fully quit/reopen ChatGPT Desktop once to refresh discovery. Then choose/enable the installed plugin in a local chat. Ordinary DevLens Stop/Start reconnects on the next tool call and does not require a ChatGPT restart.

OpenAI documents [local marketplaces and installation](https://developers.openai.com/plugins/build/plugins#install-a-local-plugin-manually), [desktop-only stdio plugins](https://developers.openai.com/plugins/deploy/desktop-only) and [using installed plugins](https://learn.chatgpt.com/docs/plugins). These establish the local workflow; the setup prompt above is DevLens-specific guidance.

## 2. Use SQL only in an existing chat

### Sharing with another user/account

Give the recipient the source-project link or a complete prepared plugin plus the compatible DevLens desktop application. **[Installation instructions are included inside the plugin](../plugins/sql-server-devlens/INSTALL.md)**, including a recipient setup prompt, manual commands, required package contents and skill-discovery checks. Installing the whole plugin brings its skill along automatically. The recipient does not recreate the skill or copy this conversation. They enable/verify the plugin in their local host and enter their own authorized SQL/Postman details. An account using an existing correct installation can verify/enable it rather than making duplicate copies.

Plugin **0.11.0** includes the **DevLens SQL and Postman** skill (`devlens-workflow`). The host can choose it for relevant DevLens tasks. You can explicitly select it in the skill picker; Codex can display the plugin-qualified name `sql-server-devlens:devlens-workflow`. Select the entry shown by your host. If it is missing, update/reinstall the plugin and restart ChatGPT once. The prompts below also work as ordinary instructions; they cannot force a missing skill or tool to load. Installing on a different account does not copy your business rules or credentials.

In DevLens choose **SQL Server Only**, enter one database and select **Test Connection**. Leave its server screen showing **Running**.

Open the existing chat in a locally executed desktop Work/Codex context. Select/enable **SQL Server DevLens** if offered, or type `@` and select its installed plugin entry when available. Paste:

```text
Use the installed SQL Server DevLens local plugin for SQL-only work in this chat. First call get_server_status and confirm the server is running, SQL is configured and the last connection test succeeded. Tell me the selected database. Work only with that database and start with read-only inspection. Before executing a stored procedure or applying any SQL change, prepare the exact action for approval in DevLens and wait for my approval there. Do not use Postman tools, expose credentials or claim an operation succeeded without its tool result. If the plugin tools are not available in this chat, tell me that clearly.
```

Then ask for the task, for example:

```text
List the tables in the connected database. Do not change anything.
```

## 3. Use SQL + Postman in an existing chat

In DevLens choose **SQL Server + Postman** → **Test Connection** → enter the Postman API key locally → **Load Workspaces** → select your workspace → **Load Collections** → select your collection → **Start MCP Server**. Collection variables must have the shared values needed for execution. Optional secure key saving is separate from SQL password saving.

Enable/select the same installed plugin in the local chat, then paste:

```text
Use the installed SQL Server DevLens local plugin for SQL and Postman work in this chat. First call get_server_status and get_postman_selection. Confirm the selected database, workspace and collection, and whether Postman request execution is enabled. Work only within that selected database and collection. Begin with read-only inspection. Prepare each procedure execution, SQL change, Postman change or HTTP run for separate approval in DevLens, and wait for my approval there before applying or running it. Do not expose credentials or automatically retry an action with an uncertain outcome. If tools are missing or Postman is not configured, explain what setup is required instead of claiming a connection.
```

Then ask for the task, for example:

```text
List the Postman folders and request names in the selected collection. Do not run or edit any request yet.
```

These prompts choose how the assistant should work; they do not switch the application's mode. To add Postman to a SQL-only session, stop DevLens, return to mode selection and complete combined setup. Enabling Postman does not require reinstalling a second plugin.

## 4. Verify the connection and troubleshoot

The shortest check is:

```text
Call get_server_status from SQL Server DevLens.
```

Expect `status: running`, `sqlConfigured: true` and `lastSqlTestSucceeded: true`. `sqlConnected: false` is normal: each SQL operation opens/disposes its own connection. In combined mode also check `postmanConfigured` and `postmanRequestExecutionEnabled`, then confirm the selection with `get_postman_selection`. Status alone does not test an application endpoint.

| What happens | Next step |
| --- | --- |
| Tools are absent in an existing chat | Check local execution and plugin selection. After installation/upgrades, restart ChatGPT once. If the old chat still cannot see the tools, use a new local chat with the plugin enabled and bring over the relevant context. There is no prompt that guarantees adding tools to every existing chat. |
| `desktop_unavailable` | Start DevLens, complete setup until Running, then ask for status again. Do not reinstall merely because the server was stopped. |
| Postman is not configured | Complete SQL Server + Postman setup in DevLens. A chat prompt cannot supply this active selection. |
| No approval window | Preparation may have failed before review. Check the activity log and the tool's error in chat. Text approval in chat does not replace approval in DevLens. |
| Postman preparation times out | Start DevLens without debugging; check Postman connectivity and try the read-only status/selection checks first. Do not retry an uncertain write/run automatically. |

The activity log shows serving/completed/failed events. Completed means the tool call finished, not proof that a change committed. Approval is required only for the exact reviewed action and expires after five minutes. A normal read-only count or schema inspection does not need a DevLens approval window.

Never paste secrets into prompts, commit private database exports or put credentials in plugin JSON. Local secure-storage availability varies by OS. For current account/plan availability, see [official plan details](https://learn.chatgpt.com/docs/pricing).

# SQL Server DevLens MCP

**Your SQL Server, understood by AI.**

SQL Server DevLens MCP is an open-source, cross-platform .NET project aiming to help developers inspect Microsoft SQL Server databases and validate API behavior with AI assistants through the [Model Context Protocol (MCP)](https://modelcontextprotocol.io/).

The goal is a developer-friendly desktop application: choose an operating mode, enter SQL Server connection details (similar to SQL Server Management Studio), and expose approved SQL inspection and, optionally, API testing tools to an MCP-compatible client.

> **Project status: early development / initial scaffold.** The repository currently contains a running Avalonia desktop application template targeting .NET 10. SQL connectivity, the MCP server, Postman integration, and automated testing **are planned and are not implemented yet**.

## Planned features

### SQL Server Only

- SSMS-style connection setup: host, port (default 1433), **one required database name**, authentication, TLS encryption, and certificate-trust settings.
- Reusable saved connection profiles, with credentials stored securely outside configuration JSON.
- Inspect tables, columns, keys, relationships, indexes, and stored procedure definitions.
- Search database objects and retrieve limited, authorized sample data.
- Expose controlled, **read-only-by-default** SQL exploration tools through MCP.
- Open SQL connections per operation and dispose of them afterward, using standard ADO.NET connection pooling.

### SQL Server + Postman (optional)

- Reuse all SQL inspection functionality.
- Work with existing Postman collections and add or update requests containing sample JSON and headers.
- Optionally integrate with Postman CLI or compatible local collection runners.
- Run approved API tests against explicitly configured **test environments**.
- Verify API responses **and** expected database state changes using deterministic assertions.
- Produce combined API/database test results for debugging and frontend handoff.

The two modes will share **one MCP server** with separately organized tools. The SQL-only mode will not require Postman.

## Technology

| Area | Technology |
| --- | --- |
| Language | C# |
| Framework | .NET 10 |
| Desktop UI | Avalonia UI (currently 12.1.4) |
| Architecture | MVVM with CommunityToolkit.Mvvm |
| SQL access (planned) | Microsoft.Data.SqlClient |
| MCP (planned) | Official C# MCP SDK |
| Postman (planned, optional) | Postman collection integration and CLI / compatible runner |
| Target platforms | Windows, macOS, and Linux |
| License | MIT |

Avalonia supports cross-platform desktop development. Platform-specific builds and packaging will be addressed as the project matures.

## Getting started (current scaffold)

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- A supported .NET development environment, such as Visual Studio 2026 or a compatible editor

The Avalonia dependencies are restored automatically through NuGet. You do **not** need a paid Avalonia extension to build this project.

### Clone and run

```bash
git clone https://github.com/santanucompacct/SqlServerDevLens-MCP.git
cd SqlServerDevLens-MCP
dotnet restore SqlServerDevLens.slnx
dotnet run --project SqlServerDevLens.Desktop/SqlServerDevLens.Desktop.csproj
```

**Expected result today:** the default Avalonia welcome window opens. Database login and MCP functionality will be added in future milestones.

You can also open `SqlServerDevLens.slnx` in Visual Studio 2026, set `SqlServerDevLens.Desktop` as the startup project, and press **F5**.

## Repository structure

```text
SqlServerDevLens-MCP/
├── SqlServerDevLens.Desktop/   # Avalonia desktop app (implemented scaffold)
├── SqlServerDevLens.slnx       # .NET solution
├── README.md
└── LICENSE.txt
```

Planned future modules include a shared core/configuration library, SQL inspection engine, MCP host, optional Postman adapter, and API/database testing engine. These are *not yet present* in the repository.

## Roadmap

- [x] Create the public MIT-licensed repository and runnable Avalonia/.NET 10 starter application.
- [ ] **v0.1** — Operating mode selection and single-database connection manager.
- [ ] **v0.2** — Read-only SQL metadata tools and MCP integration.
- [ ] **v0.3** — Optional Postman collection management.
- [ ] **v0.4** — API execution, response assertions, and database verification.
- [ ] **v0.5** — Test reports and automation support.
- [ ] **v1.0** — Cross-platform packaging, documentation, and stable release.

Roadmap items describe intended work, not shipped features or guaranteed dates. Compatibility with specific AI assistants and ChatGPT subscription tiers will be verified during integration testing.

## Security principles

- Never commit SQL passwords, connection profiles containing sensitive host details, API tokens, or customer data.
- Store passwords in an operating system credential store or other approved secure secret storage—not as plaintext in JSON.
- Default to read-only SQL permissions and narrowly scoped metadata/data inspection tools; avoid unrestricted SQL execution.
- Limit access to the explicitly selected database and enforce permissions at the **SQL Server account** level, not only in the UI.
- Use encrypted connections where supported and make certificate validation explicit.
- Keep API write tests separate from production databases. Require authorization, isolate fixtures, and validate both successful and failed transactions.
- Restrict tool inputs, command execution, and local MCP endpoint access.

## Contributing

Issues, feature suggestions, documentation improvements, and pull requests are welcome. For larger changes, please open an issue first so the proposal can be discussed. Keep contributions generic and free of customer-specific code, credentials, or data.

## License

Licensed under the [MIT License](LICENSE.txt). Postman and other optional third-party tools remain subject to their own licenses and terms.

---

Created and maintained as an open-source initiative. Follow this repository for progress as the SQL, MCP, and API testing components are developed.

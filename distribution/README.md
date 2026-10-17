# Run DevLens without Visual Studio

Download **SqlServerDevLens-0.9.0-win-x64.zip** and **SHA256SUMS.txt** from [release v0.9.0](https://github.com/santanucompacct/SqlServerDevLens-MCP/releases/tag/v0.9.0). The release publishes after package checks succeed. Compiled files are release assets rather than large binaries in Git; this folder contains setup files and reproducible build instructions.

The ZIP includes the self-contained desktop app and MCP adapter, plugin manifests, SQL/Postman workflow skill, marketplace, setup/launch scripts, help and dependency notices. Both programs bundle .NET. This first distribution targets **Windows x64**; ChatGPT Desktop/local-plugin access is a separate prerequisite.

1. Extract the complete ZIP to a permanent folder.
2. Run **Install-Plugin.cmd**; it locates Codex CLI or explains the manual Plugins Directory alternative, preserving other sources.
3. Restart ChatGPT Desktop once and enable the plugin in a local chat.
4. Run **Start-DevLens.cmd**, configure SQL/Postman and leave the server Running.
5. Ask: **Use the DevLens workflow skill to call get_server_status and check my connection.**

See [README-FIRST.txt](README-FIRST.txt) for conflicts, prerequisites, checksum/unsigned-build information and privacy. Another account/computer must verify its own access and setup.

## Build the package from source

Maintainers need .NET 10 SDK and PowerShell 7 on Windows:

```powershell
./scripts/Build-WindowsDistribution.ps1
```

The script uses fresh staging under ignored `distribution/packages/`, publishes without trimming/single-file bundling and rewrites only the distribution copy of `mcp.json` to launch its EXE. Development wiring stays `dotnet` plus DLL. It creates a ZIP/checksum and smoke-tests the EXE protocol.

CI uploads tested packages as Actions artifacts. Tag `v0.9.0` after merging the checked commit publishes release downloads. Keep the extracted marketplace folder in place after registration. No private settings or database/Postman exports belong in the distribution.

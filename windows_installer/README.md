# Windows installer / portable setup

**[Download the complete Windows x64 setup ZIP](https://github.com/santanucompacct/SqlServerDevLens-MCP/releases/download/v0.11.0/SqlServerDevLens-0.11.0-win-x64.zip)** · [Release and checksum](https://github.com/santanucompacct/SqlServerDevLens-MCP/releases/tag/v0.11.0)

This is a portable ZIP, not an MSI. It includes the desktop application, bundled .NET runtime, MCP adapter EXE, workflow skill, help and plugin setup files. **Visual Studio and a separate .NET installation are not needed.** ChatGPT Desktop with local-plugin access and your authorized SQL/Postman details are still required.

## Easiest way

1. Download the ZIP above and extract it completely to a permanent folder.
2. Run **Install-Plugin.cmd** from the extracted folder.
3. Fully restart ChatGPT Desktop once and enable SQL Server DevLens in a local chat.
4. Run **Start-DevLens.cmd**, enter SQL/Postman settings and leave the server Running.

The package's README-FIRST.txt explains an existing DevLens marketplace conflict, the Codex CLI prerequisite/manual alternative, unsigned-build Windows warnings and privacy.

## Download from this folder

If you have this repository locally, double-click **Download-Setup.cmd**. Keep it beside Download-Setup.ps1. It downloads the released ZIP, verifies the pinned SHA256 checksum and extracts it under:

```text
%LOCALAPPDATA%\SqlServerDevLens\Portable\v0.11.0
```

The script prints the exact extracted folder and next steps. It does not install/register the plugin or start SQL/Postman work. Existing destinations are preserved. To choose another short, permanent path:

```powershell
.\Download-Setup.ps1 -Destination 'C:\Tools\DevLens-0.11.0'
```

Compiled binaries stay on GitHub Releases rather than adding the 120 MB ZIP to Git history. This `windows_installer` folder is the setup entry point on **master**; [distribution](../distribution/README.md) contains the reproducible package build and setup templates. The initial package supports **Windows x64**, not ARM64/macOS/Linux.

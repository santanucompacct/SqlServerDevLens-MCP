[CmdletBinding()]
param([string]$CodexPath)
$ErrorActionPreference = 'Stop'
$packageRoot = $PSScriptRoot
$pluginRoot = Join-Path $packageRoot 'plugins/sql-server-devlens'
$manifest = Get-Content -Raw -LiteralPath (Join-Path $pluginRoot 'plugin.json') | ConvertFrom-Json
foreach ($relative in @('server/SqlServerDevLens.McpHost.exe', 'skills/devlens-workflow/SKILL.md', 'skills/devlens-workflow/agents/openai.yaml', 'skills/devlens-workflow/references/sql.md', 'skills/devlens-workflow/references/postman.md', 'INSTALL.md')) {
    if (-not (Test-Path -LiteralPath (Join-Path $pluginRoot $relative) -PathType Leaf)) { throw "Incomplete package: $relative. Extract the complete ZIP first." }
}
if (-not $CodexPath) {
    $command = Get-Command codex.exe -ErrorAction SilentlyContinue
    if ($command) { $CodexPath = $command.Source }
    else {
        $candidate = Join-Path $env:LOCALAPPDATA 'Programs/OpenAI/Codex/bin/codex.exe'
        if (Test-Path -LiteralPath $candidate) { $CodexPath = $candidate }
    }
}
if (-not $CodexPath -or -not (Test-Path -LiteralPath $CodexPath -PathType Leaf)) {
    throw 'Codex CLI not found. Install/open ChatGPT Desktop with local Work/Codex access, or rerun with -CodexPath "full path to codex.exe". See README-FIRST.txt for the Plugins Directory alternative. No Visual Studio/.NET required.'
}
$catalog = & $CodexPath plugin marketplace list --json
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect marketplaces. No sources were changed.' }
$existing = @((($catalog -join "`n") | ConvertFrom-Json).marketplaces | Where-Object name -eq 'devlens-local')
if ($existing.Count -gt 0) {
    $currentRoot = [IO.Path]::GetFullPath($existing[0].root).TrimEnd('\', '/')
    $wantedRoot = [IO.Path]::GetFullPath($packageRoot).TrimEnd('\', '/')
    if (-not $currentRoot.Equals($wantedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "DevLens Local already points to: $currentRoot. Setup preserves existing sources. Update/use that installation, or ask your local setup assistant to review migration to this permanent folder. Do not delete unrelated marketplaces."
    }
} else {
    & $CodexPath plugin marketplace add $packageRoot
    if ($LASTEXITCODE -ne 0) { throw 'Could not register DevLens Local. See README-FIRST.txt.' }
}
$installed = & $CodexPath plugin add sql-server-devlens@devlens-local --json
if ($LASTEXITCODE -ne 0) { throw 'Plugin installation failed. No SQL or Postman operation was attempted.' }
$result = ($installed -join "`n") | ConvertFrom-Json
if ($result.pluginId -ne 'sql-server-devlens@devlens-local' -or $result.version -ne $manifest.version) { throw 'Installed plugin identity/version does not match this package. Check the marketplace source.' }
if (-not (Test-Path -LiteralPath (Join-Path $result.installedPath 'skills/devlens-workflow/SKILL.md'))) { throw 'Installed workflow skill is missing.' }
Write-Host "Installed SQL Server DevLens $($result.version), including its workflow skill."
Write-Host 'Fully quit/reopen ChatGPT Desktop once, enable the plugin in a local chat and look for DevLens SQL and Postman in the skill picker.'
Write-Host 'Run Start-DevLens.cmd, configure SQL/Postman locally until Running, then ask: Call get_server_status from SQL Server DevLens.'
Write-Host 'Setup did not query SQL, run application requests or read credentials. Keep this extracted folder in place.'

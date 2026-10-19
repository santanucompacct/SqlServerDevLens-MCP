[CmdletBinding()]
param([string]$PackageRoot, [string]$PackageDirectory)
$ErrorActionPreference='Stop'
$temporary=$null
try {
    if ($PackageDirectory) {
        $zip=@(Get-ChildItem -LiteralPath $PackageDirectory -Filter 'SqlServerDevLens-Tunnel-*.zip')
        if ($zip.Count -ne 1) { throw 'Expected one tunnel archive.' }
        $expected=(Get-Content -LiteralPath (Join-Path $PackageDirectory 'SHA256SUMS.txt')).Split(' ')[0]
        if ((Get-FileHash $zip[0].FullName).Hash.ToLowerInvariant() -ne $expected) { throw 'Archive checksum mismatch.' }
        $temporary=Join-Path ([IO.Path]::GetTempPath()) ('dvl-tunnel-'+[guid]::NewGuid().ToString('N')+' test')
        [IO.Compression.ZipFile]::ExtractToDirectory($zip[0].FullName,$temporary)
        $PackageRoot=(Get-ChildItem -LiteralPath $temporary -Directory | Select-Object -First 1).FullName
    }
    if (-not $PackageRoot) { throw 'Provide PackageRoot or PackageDirectory.' }
    $root=[IO.Path]::GetFullPath($PackageRoot)
    foreach ($path in @('published_app/SqlServerDevLens.Desktop.exe','published_mcp/SqlServerDevLens.McpHost.exe',
        'published_app/coreclr.dll','published_mcp/coreclr.dll','Tunnel/tunnel-client.exe','Tunnel/cloudflared.exe',
        'Tunnel/LICENSE','Tunnel/NOTICE','Tunnel/Configure-Tunnel.ps1','Tunnel/Start-Tunnel.ps1',
        'Configure-Tunnel.cmd','Start-Tunnel.cmd','Start-DevLens.cmd','TUNNEL_SETUP_AND_RELEASE.md','BUILD-INFO.json',
        'Install-Plugin.cmd','Setup-Plugin.ps1','.agents/plugins/marketplace.json','licenses/dependencies.json',
        'plugins/sql-server-devlens/skills/devlens-workflow/SKILL.md',
        'plugins/sql-server-devlens/skills/devlens-workflow/agents/openai.yaml',
        'plugins/sql-server-devlens/skills/devlens-workflow/references/sql.md',
        'plugins/sql-server-devlens/skills/devlens-workflow/references/postman.md')) {
        if (-not(Test-Path -LiteralPath (Join-Path $root $path) -PathType Leaf)) { throw "Missing package component: $path" }
    }
    $files=@(Get-ChildItem -LiteralPath $root -File -Recurse -Force)
    foreach ($file in $files) {
        if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Links are forbidden in release files.' }
        if ($file.Name -match '(?i)^(connections|credentials|devlens-tunnel)\.(json|ini)$|^\.env|\.(pem|key|pfx|p12|sql|xlsx|csv|pdb)$' -or
            $file.FullName -match '(?i)[/\\](ProcedureBackups|TableSchemaBackups|PostmanBackups|profiles|state)[/\\]') { throw 'Forbidden private/generated file in package.' }
        if ($file.Extension -in @('.json','.md','.txt','.ps1','.cmd','.bat','.yaml','.yml','.ini','.config')) {
            $text=[IO.File]::ReadAllText($file.FullName)
            if ($text -match '(?i)sk-[a-z0-9_-]{16,}|-----BEGIN (?:RSA |EC )?PRIVATE KEY|tunnel_[a-z0-9]{16,}|[A-Z]:[\\/]+Users[\\/]') {
                throw 'Potential credential, tunnel identity or personal source path found; contents withheld.'
            }
            if ($file.Extension -in @('.ini','.yaml','.yml') -and $text -match '(?i)(password|api_key|token)\s*[:=]\s*(?!env:|file:|<)[^\s]+') { throw 'Unreferenced credential configuration found.' }
        }
    }
    $lock=Get-Content -Raw (Join-Path $root 'Tunnel/tunnel-client-lock.json') | ConvertFrom-Json
    foreach ($entry in $lock.files.PSObject.Properties) {
        if ((Get-FileHash (Join-Path $root "Tunnel/$($entry.Name)")).Hash.ToLowerInvariant() -ne $entry.Value) { throw 'Bundled tunnel file differs from pinned upstream.' }
    }
    $mcp=Get-Content -Raw (Join-Path $root 'plugins/sql-server-devlens/mcp.json')|ConvertFrom-Json
    if ($mcp.mcpServers.'sql-server-devlens'.command -ne './server/SqlServerDevLens.McpHost.exe') { throw 'Invalid contained local plugin command.' }
    if ((Get-FileHash (Join-Path $root 'published_mcp/SqlServerDevLens.McpHost.dll')).Hash -ne
        (Get-FileHash (Join-Path $root 'plugins/sql-server-devlens/server/SqlServerDevLens.McpHost.dll')).Hash) { throw 'Adapter copies differ.' }
    & (Join-Path $PSScriptRoot 'Test-Mcp.ps1') -HostExe (Join-Path $root 'published_mcp/SqlServerDevLens.McpHost.exe')
    & (Join-Path $PSScriptRoot 'Test-TunnelProtocol.ps1') -HostExe (Join-Path $root 'published_mcp/SqlServerDevLens.McpHost.exe')
    # Real clean-account profile materialization, isolated from the user's APPDATA.
    $profiles=Join-Path ([IO.Path]::GetTempPath()) ('dvl-profile-'+[guid]::NewGuid().ToString('N'))
    try {
        & (Join-Path $root 'Tunnel/Configure-Tunnel.ps1') -Profile 'package-test' -TunnelId ('tunnel_'+('0'*32)) -ProfileDirectory $profiles
        $config=Get-Content -Raw (Join-Path $profiles 'package-test.yaml')
        if ($config -notmatch 'env:CONTROL_PLANE_API_KEY' -or $config -notmatch '127.0.0.1:8080' -or $config -notmatch 'published_mcp') { throw 'Profile configuration not isolated or correctly wired.' }
        $existing=$false
        try { & (Join-Path $root 'Tunnel/Configure-Tunnel.ps1') -Profile 'package-test' -TunnelId ('tunnel_'+('0'*32)) -ProfileDirectory $profiles }
        catch { $existing=$true }
        if (-not $existing) { throw 'Setup overwrote an existing profile.' }
    } finally {
        $p=[IO.Path]::GetFullPath($profiles); $temp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if (-not $p.StartsWith($temp,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($p) -notlike 'dvl-profile-*') { throw 'Unsafe profile cleanup target.' }
        if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Recurse -Force }
    }
    Write-Host 'PASS: inventory, self-contained runtime, contained plugin path, vendor integrity, secret/path scan, stdio MCP and isolated profile setup. No authenticated poll, SQL or application request was performed.'
} finally {
    if ($temporary) {
        $p=[IO.Path]::GetFullPath($temporary); $temp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if (-not $p.StartsWith($temp,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($p) -notlike 'dvl-tunnel-* test') { throw 'Unsafe extraction cleanup target.' }
        if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Recurse -Force }
    }
}

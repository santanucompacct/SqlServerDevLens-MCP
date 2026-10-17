param([Parameter(Mandatory)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
$zip = Get-ChildItem -LiteralPath $PackageDirectory -Filter '*.zip' -File | Select-Object -First 1
if (-not $zip) { throw 'Package ZIP missing.' }
$checksum = (Get-Content -LiteralPath (Join-Path $PackageDirectory 'SHA256SUMS.txt')).Split(' ')[0]
if ((Get-FileHash -LiteralPath $zip.FullName).Hash.ToLowerInvariant() -ne $checksum) { throw 'Package checksum mismatch.' }
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('dvl-dist-' + [Guid]::NewGuid().ToString('N').Substring(0,8) + ' test')
try {
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($zip.FullName, $testRoot)
$root = (Get-ChildItem -LiteralPath $testRoot -Directory | Select-Object -First 1).FullName
$plugin = Join-Path $root 'plugins/sql-server-devlens'
$manifest = Get-Content -Raw (Join-Path $plugin 'plugin.json') | ConvertFrom-Json
$mcp = Get-Content -Raw (Join-Path $plugin 'mcp.json') | ConvertFrom-Json
$adapterCommand = $mcp.mcpServers.'sql-server-devlens'.command
if ($adapterCommand -ne './server/SqlServerDevLens.McpHost.exe') { throw 'Distribution must use an Agent Plugins contained ./ executable command; ${PLUGIN_ROOT} is not valid in command.' }
$adapterPath = Join-Path $plugin $adapterCommand.Substring(2)
if (-not (Test-Path -LiteralPath $adapterPath -PathType Leaf)) { throw 'Packaged MCP command does not resolve to an executable.' }
foreach ($relative in @('.agents/plugins/marketplace.json', 'plugins/sql-server-devlens/.codex-plugin/plugin.json', 'plugins/sql-server-devlens/skills/devlens-workflow/SKILL.md', 'desktop/SqlServerDevLens.Desktop.exe', 'Setup-Plugin.ps1', 'licenses/dependencies.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $relative))) { throw "Archive missing $relative" }
}
if (@(Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object { $_.Name -match '^(connections|credentials)\.json$|\.secrets\.json$|^\.env$' -or $_.FullName -match '[/\\](ProcedureBackups|TableSchemaBackups|PostmanBackups)[/\\]' }).Count -gt 0) { throw 'Private settings found in distribution.' }
$stub = Join-Path $testRoot 'fake-codex.ps1'
@'
$root = $env:DEVLENS_SETUP_TEST_ROOT
if ($args[0] -eq 'plugin' -and $args[1] -eq 'marketplace' -and $args[2] -eq 'list') {
    $entries = @(@{name='unrelated-marketplace';root='C:\ExampleOtherSource'})
    if ($env:DEVLENS_SETUP_TEST_MODE -eq 'same') { $entries += @{name='devlens-local';root=$root} }
    if ($env:DEVLENS_SETUP_TEST_MODE -eq 'conflict') { $entries += @{name='devlens-local';root='C:\ExistingDevLensSource'} }
    @{marketplaces=$entries} | ConvertTo-Json -Depth 5 -Compress
} elseif ($args[1] -eq 'marketplace' -and $args[2] -eq 'add') {
    if ($args[3] -ne $root) { throw 'Package path with spaces was not preserved.' }
    Add-Content -LiteralPath $env:DEVLENS_SETUP_TEST_LOG -Value 'register'
} elseif ($args[0] -eq 'plugin' -and $args[1] -eq 'add') {
    if ($args[2] -ne 'sql-server-devlens@devlens-local') { throw 'Wrong plugin identity.' }
    $plugin=Join-Path $root 'plugins/sql-server-devlens'
    $version=(Get-Content -Raw (Join-Path $plugin 'plugin.json') | ConvertFrom-Json).version
    @{pluginId='sql-server-devlens@devlens-local';version=$version;installedPath=$plugin} | ConvertTo-Json -Compress
    Add-Content -LiteralPath $env:DEVLENS_SETUP_TEST_LOG -Value 'install'
} else { throw 'Setup attempted an unexpected CLI action.' }
$global:LASTEXITCODE=0
'@ | Set-Content -LiteralPath $stub
foreach ($mode in @('fresh', 'same', 'conflict')) {
    $log = Join-Path $testRoot "$mode.txt"
    $info = [Diagnostics.ProcessStartInfo]::new((Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'))
    foreach ($arg in @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $root 'Setup-Plugin.ps1'), '-CodexPath', $stub)) { $info.ArgumentList.Add($arg) }
    $info.UseShellExecute=$false; $info.CreateNoWindow=$true; $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
    $info.Environment['DEVLENS_SETUP_TEST_ROOT']=$root; $info.Environment['DEVLENS_SETUP_TEST_MODE']=$mode; $info.Environment['DEVLENS_SETUP_TEST_LOG']=$log
    $process=[Diagnostics.Process]::Start($info); $stdout=$process.StandardOutput.ReadToEndAsync(); $stderr=$process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(20000)) { $process.Kill($true); throw 'Setup test timed out.' }
    $commands=if(Test-Path -LiteralPath $log){ @(Get-Content -LiteralPath $log) } else { @() }
    if ($mode -eq 'conflict') {
        if ($process.ExitCode -eq 0 -or $commands.Count -gt 0) { throw 'Conflicting marketplace was not preserved.' }
    } else {
        if ($process.ExitCode -ne 0) { throw ($stderr.Result + $stdout.Result) }
        if ('install' -notin $commands) { throw 'Setup did not request installation.' }
        if (($mode -eq 'fresh') -ne ('register' -in $commands)) { throw 'Marketplace registration was not idempotent.' }
    }
    $process.Dispose()
}
& (Join-Path $PSScriptRoot 'Test-Mcp.ps1') -HostExe $adapterPath
Write-Host 'PASS: extracted archive/checksum, EXE protocol, runtime packaging and Windows PowerShell 5.1 setup (fresh, repeat, conflicting source; paths with spaces). No real marketplace or SQL/Postman changes.'
} finally {
    $absolute = [IO.Path]::GetFullPath($testRoot)
    $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $absolute.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($absolute) -notlike 'dvl-dist-* test') { throw 'Refusing cleanup outside this test temporary directory.' }
    if (Test-Path -LiteralPath $absolute) { Remove-Item -LiteralPath $absolute -Recurse -Force }
}

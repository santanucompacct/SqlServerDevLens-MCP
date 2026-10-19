[CmdletBinding()]
param([string]$Profile='devlens', [string]$TunnelId, [string]$ProfileDirectory=(Join-Path $env:APPDATA 'tunnel-client'))
$ErrorActionPreference='Stop'
if ($Profile -notmatch '^[A-Za-z0-9_.-]+$') { throw 'Invalid profile name.' }
if (-not $TunnelId) { $TunnelId=Read-Host 'Your own Platform tunnel ID (not an API key)' }
if ($TunnelId -notmatch '^tunnel_[A-Za-z0-9]+$') { throw 'Invalid tunnel ID.' }
$client=Join-Path $PSScriptRoot 'tunnel-client.exe'
$hostExe=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../published_mcp/SqlServerDevLens.McpHost.exe'))
if (-not(Test-Path -LiteralPath $client) -or -not(Test-Path -LiteralPath $hostExe)) { throw 'Extract the complete package first.' }
if (Test-Path -LiteralPath (Join-Path $ProfileDirectory "$Profile.yaml")) { throw 'Profile exists; preserve it. Choose a new name or review its configuration locally.' }
New-Item -ItemType Directory -Path $ProfileDirectory -Force | Out-Null
# Upstream command parsing treats backslashes as escapes, including inside double quotes.
# Windows accepts forward slashes; preserve spaces with quotes around this normalized path.
$commandPath=$hostExe.Replace('\','/')
# No key is needed to materialize an environment-reference profile. init does not authorize the account.
& $client init --sample sample_mcp_stdio_local --profile $Profile --profile-dir $ProfileDirectory --tunnel-id $TunnelId `
    --control-plane-api-key-ref 'env:CONTROL_PLANE_API_KEY' --health-listen-addr '127.0.0.1:8080' --mcp-command ('"'+$commandPath+'" --desktop')
if ($LASTEXITCODE -ne 0) { throw 'Profile creation failed; review local CLI output.' }
Write-Host 'Profile created with a key reference only. Start DevLens and finish SQL/Postman setup, then run Start-Tunnel.cmd.'

[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$ini=Join-Path $PSScriptRoot 'launcher.ini'
if (-not(Test-Path -LiteralPath $ini)) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'launcher.example.ini') -Destination $ini
    Write-Host 'Enter your own TunnelId and ApiKey in Tunnel/launcher.ini, save it, then run Start-All.cmd again. This file stores the key in plaintext; keep it private.'
    Start-Process notepad.exe -ArgumentList ('"'+$ini+'"')
    exit 0
}
$settings=@{}
foreach ($line in Get-Content -LiteralPath $ini) {
    $line=$line.Trim()
    if (-not $line -or $line.StartsWith(';') -or $line.StartsWith('#') -or $line -eq '[Tunnel]') { continue }
    if ($line -notmatch '^(TunnelId|ApiKey)\s*=(.*)$') { throw 'Invalid INI setting. Use TunnelId and ApiKey only.' }
    $name=$matches[1]; $value=$matches[2].Trim()
    if ($settings.ContainsKey($name)) { throw 'Duplicate INI setting.' }
    $settings[$name]=$value
}
if ($settings.TunnelId -notmatch '^tunnel_[A-Za-z0-9]+$' -or [string]::IsNullOrWhiteSpace($settings.ApiKey)) { throw 'Fill TunnelId and ApiKey in Tunnel/launcher.ini locally.' }
$desktop=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../published_app/SqlServerDevLens.Desktop.exe'))
if (-not(Test-Path -LiteralPath $desktop)) { throw 'Extract the complete package first.' }
# Avoid starting a competing tunnel or opening another process's dashboard.
$listener=New-Object Net.Sockets.TcpClient
try {
    $pending=$listener.BeginConnect('127.0.0.1',8080,$null,$null)
    if ($pending.AsyncWaitHandle.WaitOne(500) -and $listener.Connected) { throw 'Port 8080 is already in use. Stop the previous tunnel first.' }
} finally { $listener.Dispose() }
# A profile belongs to this tunnel ID and package path. Changes create a new
# generated profile without overwriting the user's existing devlens profile.
$hash=[Security.Cryptography.SHA256]::Create()
try { $bytes=$hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($PSScriptRoot.ToLowerInvariant()+'|'+$settings.TunnelId)) }
finally { $hash.Dispose() }
$profile='launcher-'+([BitConverter]::ToString($bytes).Replace('-','').Substring(0,24).ToLowerInvariant())
$profiles=Join-Path $env:APPDATA 'tunnel-client'
if (-not(Test-Path -LiteralPath (Join-Path $profiles ($profile+'.yaml')))) {
    & (Join-Path $PSScriptRoot 'Configure-Tunnel.ps1') -Profile $profile -TunnelId $settings.TunnelId -ProfileDirectory $profiles
}
if (-not(Get-Process -Name 'SqlServerDevLens.Desktop' -ErrorAction SilentlyContinue)) { Start-Process -FilePath $desktop | Out-Null }
Write-Host 'Complete SQL/Postman setup in DevLens and leave the MCP server Running. Keep this tunnel window open; Ctrl+C stops forwarding.'
$oldKey=$env:CONTROL_PLANE_API_KEY
$watcher=$null
try {
    $env:CONTROL_PLANE_API_KEY=$settings.ApiKey
    $watcher=Start-Job -ScriptBlock {
        for ($attempt=0; $attempt -lt 60; $attempt++) {
            try {
                $ready=Invoke-WebRequest -UseBasicParsing -Uri 'http://127.0.0.1:8080/readyz' -TimeoutSec 1
                if ($ready.StatusCode -eq 200) { Start-Process 'http://127.0.0.1:8080/ui'; return }
            } catch { }
            Start-Sleep -Seconds 1
        }
    }
    & (Join-Path $PSScriptRoot 'Start-Tunnel.ps1') -Profile $profile -ProfileDirectory $profiles
} finally {
    if ($watcher) { Stop-Job $watcher -ErrorAction SilentlyContinue; Remove-Job $watcher -Force -ErrorAction SilentlyContinue }
    $env:CONTROL_PLANE_API_KEY=$oldKey
    $settings.Clear()
}

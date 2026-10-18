# One-click SQL Server DevLens Secure MCP Tunnel launcher.
# This script reads the secret locally; it never prints the API key.
# Requires an existing tunnel-client profile named in devlens-tunnel.ini.
$ErrorActionPreference = 'Stop'

function Read-TunnelIni([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "INI file not found: $Path"
    }
    $items = @{}
    $inTunnelSection = $false
    foreach ($raw in [System.IO.File]::ReadAllLines($Path)) {
        $line = $raw.Trim()
        if ($line.Length -eq 0 -or $line.StartsWith(';') -or $line.StartsWith('#')) { continue }
        if ($line -match '^\[([^\]]+)\]$') {
            $inTunnelSection = ($Matches[1].Trim() -ieq 'Tunnel')
            continue
        }
        if (-not $inTunnelSection) { continue }
        $separator = $line.IndexOf('=')
        if ($separator -lt 1) { continue }
        $key = $line.Substring(0, $separator).Trim().ToLowerInvariant()
        $value = $line.Substring($separator + 1).Trim()
        $items[$key] = $value
    }
    return $items
}

try {
    $settingsPath = Join-Path $PSScriptRoot 'devlens-tunnel.ini'
    $settings = Read-TunnelIni $settingsPath
    $tunnelId = [string]$settings['tunnel_id']
    $profile = [string]$settings['profile']
    $apiKey = [string]$settings['api_key']

    if ($tunnelId -notmatch '^tunnel_[A-Za-z0-9]+$') {
        throw 'Invalid or missing tunnel_id in the INI file.'
    }
    if ([string]::IsNullOrWhiteSpace($profile)) { $profile = 'devlens' }
    if ($profile -notmatch '^[A-Za-z0-9_.-]+$') { throw 'Invalid profile name in the INI file.' }
    if ([string]::IsNullOrWhiteSpace($apiKey) -or $apiKey -like '*PASTE_YOUR*') {
        throw 'Enter the NEW replacement API key as api_key=... in devlens-tunnel.ini.'
    }

    $clientExe = Join-Path $PSScriptRoot 'tunnel-client.exe'
    if (-not (Test-Path -LiteralPath $clientExe -PathType Leaf)) {
        throw "tunnel-client.exe not found: $clientExe. Copy these launcher files beside your existing tunnel-client.exe."
    }
    $profileFile = Join-Path (Join-Path $env:APPDATA 'tunnel-client') ($profile + '.yaml')
    if (-not (Test-Path -LiteralPath $profileFile -PathType Leaf)) {
        throw "Existing tunnel profile not found: $profileFile. Create it first using tunnel-client init."
    }

    $mcpExe = [string]$settings['mcp_host_exe']
    if (-not [string]::IsNullOrWhiteSpace($mcpExe) -and -not (Test-Path -LiteralPath $mcpExe -PathType Leaf)) {
        throw "MCP Host executable does not exist: $mcpExe"
    }

    # Do not start a second daemon using the same local tunnel client.
    $running = Get-Process -Name 'tunnel-client' -ErrorAction SilentlyContinue
    if ($running) {
        throw 'A tunnel-client process is already running. Check http://127.0.0.1:8080/ui or stop the existing daemon with Ctrl+C before starting another.'
    }

    $startDesktop = [string]$settings['start_desktop']
    if ($startDesktop -ieq 'true') {
        $desktopExe = [string]$settings['desktop_exe']
        if (-not (Test-Path -LiteralPath $desktopExe -PathType Leaf)) {
            throw "DevLens desktop executable does not exist: $desktopExe"
        }
        if (-not (Get-Process -Name 'SqlServerDevLens.Desktop' -ErrorAction SilentlyContinue)) {
            Write-Host 'Starting SQL Server DevLens Desktop...'
            Start-Process -FilePath $desktopExe -WorkingDirectory (Split-Path -Parent $desktopExe)
        } else {
            Write-Host 'SQL Server DevLens Desktop is already running.'
        }
    }

    # Launch a separate, short-lived browser watcher BEFORE setting the secret.
    # It waits for the local /readyz endpoint to succeed, then opens /ui.
    # This avoids opening the browser before tunnel-client is actually ready.
    $dashboardWatcher = Join-Path $PSScriptRoot 'Open-DevLensDashboard.ps1'
    if (Test-Path -LiteralPath $dashboardWatcher -PathType Leaf) {
        # Do not pass API key to the browser-watcher process (even via inheritance).
        Remove-Item Env:\CONTROL_PLANE_API_KEY -ErrorAction SilentlyContinue
        $watcherArgs = '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + $dashboardWatcher + '"'
        Start-Process -FilePath 'powershell.exe' -ArgumentList $watcherArgs -WindowStyle Hidden
        Write-Host 'Dashboard will open automatically when the tunnel is ready.'
    } else {
        Write-Host 'Dashboard watcher not installed; open http://127.0.0.1:8080/ui manually.'
    }

    # Set secrets only in this launcher process and its tunnel-client child.
    # Never pass the key literally as a command-line argument.
    $env:CONTROL_PLANE_API_KEY = $apiKey
    $env:CONTROL_PLANE_TUNNEL_ID = $tunnelId
    $apiKey = $null
    $settings.Remove('api_key') | Out-Null

    Write-Host ('Starting tunnel profile: ' + $profile)
    Write-Host ('Tunnel ID: ' + $tunnelId)
    Write-Host 'Keep this window open. Press Ctrl+C to stop the tunnel.'
    Write-Host 'Dashboard: http://127.0.0.1:8080/ui'
    Write-Host ''

    & $clientExe run --profile $profile --control-plane.tunnel-id $tunnelId --control-plane.api-key 'env:CONTROL_PLANE_API_KEY'
    if ($LASTEXITCODE -ne 0) {
        throw "Tunnel client exited with code $LASTEXITCODE."
    }
}
catch {
    Write-Host ('ERROR: ' + $_.Exception.Message) -ForegroundColor Red
    exit 1
}
finally {
    # Does not revoke the key or remove it from the INI file.
    Remove-Item Env:\CONTROL_PLANE_API_KEY -ErrorAction SilentlyContinue
    Remove-Item Env:\CONTROL_PLANE_TUNNEL_ID -ErrorAction SilentlyContinue
}

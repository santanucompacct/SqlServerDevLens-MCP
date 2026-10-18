# Called by Start-DevLens-Tunnel.ps1 in a separate hidden PowerShell process.
# Does not read the INI or receive the runtime API key.
$ErrorActionPreference = 'Stop'
$readyUri = 'http://127.0.0.1:8080/readyz'
$dashboardUri = 'http://127.0.0.1:8080/ui'

# Wait up to 90 seconds. A failed startup will NOT open a broken dashboard.
for ($attempt = 0; $attempt -lt 45; $attempt++) {
    try {
        $response = Invoke-WebRequest -Uri $readyUri -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop
        if ($response.StatusCode -eq 200) {
            Start-Process -FilePath $dashboardUri
            exit 0
        }
    }
    catch {
        # The tunnel is either not listening yet or not ready. Retry.
    }
    Start-Sleep -Seconds 2
}

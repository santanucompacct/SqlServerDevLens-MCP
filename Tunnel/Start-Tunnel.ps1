[CmdletBinding()]
param([string]$Profile='devlens', [string]$ProfileDirectory=(Join-Path $env:APPDATA 'tunnel-client'), [switch]$DoctorOnly)
$ErrorActionPreference='Stop'
if ($Profile -notmatch '^[A-Za-z0-9_.-]+$') { throw 'Invalid profile name.' }
if (-not(Test-Path -LiteralPath (Join-Path $ProfileDirectory "$Profile.yaml"))) { throw 'Create your own profile with Configure-Tunnel.cmd first.' }
$client=Join-Path $PSScriptRoot 'tunnel-client.exe'
$oldKey=$env:CONTROL_PLANE_API_KEY; $pointer=[IntPtr]::Zero
try {
    if (-not $oldKey) {
        $secure=Read-Host 'Your restricted runtime API key (local entry; not saved)' -AsSecureString
        $pointer=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
        $env:CONTROL_PLANE_API_KEY=[Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    }
    if ($DoctorOnly) { & $client doctor --profile $Profile --profile-dir $ProfileDirectory }
    else {
        Write-Host 'Keep DevLens Running. Loopback dashboard: http://127.0.0.1:8080/ui. Ctrl+C stops this tunnel; it does not revoke your key.'
        & $client run --profile $Profile --profile-dir $ProfileDirectory
    }
    if ($LASTEXITCODE -ne 0) { throw 'Tunnel command failed; inspect local output without sharing credentials or log archives.' }
} finally {
    if ($pointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
    $env:CONTROL_PLANE_API_KEY=$oldKey
}

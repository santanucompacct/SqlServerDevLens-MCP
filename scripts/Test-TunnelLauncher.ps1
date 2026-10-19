$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$sandbox=Join-Path ([IO.Path]::GetTempPath()) ('dvl-launcher-'+[guid]::NewGuid().ToString('N'))
$oldAppData=$env:APPDATA; $oldKey=$env:CONTROL_PLANE_API_KEY
try {
    $tunnel=Join-Path $sandbox 'Tunnel'; New-Item -ItemType Directory $tunnel -Force | Out-Null
    $app=Join-Path $sandbox 'published_app'; New-Item -ItemType Directory $app | Out-Null
    Set-Content (Join-Path $app 'SqlServerDevLens.Desktop.exe') 'test placeholder'
    Copy-Item (Join-Path $repo 'Tunnel/Start-All.ps1') $tunnel
    @'
param($Profile,$TunnelId,$ProfileDirectory)
New-Item -ItemType Directory $ProfileDirectory -Force | Out-Null
Set-Content (Join-Path $ProfileDirectory ($Profile+'.yaml')) $TunnelId
'@ | Set-Content (Join-Path $tunnel 'Configure-Tunnel.ps1')
    @'
param($Profile,$ProfileDirectory)
if ($env:CONTROL_PLANE_API_KEY -ne 'synthetic-value=with-equals') { throw 'Key was not read intact.' }
Set-Content (Join-Path $ProfileDirectory 'called.txt') $Profile
'@ | Set-Content (Join-Path $tunnel 'Start-Tunnel.ps1')
    Set-Content (Join-Path $tunnel 'launcher.ini') @('[Tunnel]','TunnelId=tunnel_synthetic','ApiKey=synthetic-value=with-equals')
    $env:APPDATA=$sandbox; $env:CONTROL_PLANE_API_KEY='previous-value'
    # Mock external effects: no actual app, tunnel, dashboard or network is used.
    function Get-Process { [pscustomobject]@{Id=1} }
    function Start-Job { $null }
    function New-Object {
        param($TypeName)
        if ($TypeName -ne 'Net.Sockets.TcpClient') { throw 'Unexpected construction.' }
        $client=[pscustomobject]@{Connected=$false}
        $client | Add-Member ScriptMethod BeginConnect {
            $handle=New-ObjectForTest
            [pscustomobject]@{AsyncWaitHandle=$handle}
        }
        $client | Add-Member ScriptMethod Dispose {}
        $client
    }
    function New-ObjectForTest {
        $handle=[pscustomobject]@{}
        $handle | Add-Member ScriptMethod WaitOne { $false }
        $handle
    }
    & (Join-Path $tunnel 'Start-All.ps1')
    $profile=Get-Content (Join-Path $sandbox 'tunnel-client/called.txt')
    & (Join-Path $tunnel 'Start-All.ps1')
    if (@(Get-ChildItem (Join-Path $sandbox 'tunnel-client') -Filter '*.yaml').Count -ne 1) { throw 'Profile was not reused.' }
    (Get-Content (Join-Path $tunnel 'launcher.ini')) -replace 'tunnel_synthetic','tunnel_changed' | Set-Content (Join-Path $tunnel 'launcher.ini')
    & (Join-Path $tunnel 'Start-All.ps1')
    if (@(Get-ChildItem (Join-Path $sandbox 'tunnel-client') -Filter '*.yaml').Count -ne 2) { throw 'Changed ID did not create a separate profile.' }
    if ($env:CONTROL_PLANE_API_KEY -ne 'previous-value') { throw 'Caller environment was not restored.' }
    Write-Host 'PASS: INI parsing, profile reuse/identity change and environment restoration (external effects mocked).'
} finally {
    $env:APPDATA=$oldAppData; $env:CONTROL_PLANE_API_KEY=$oldKey
    $target=[IO.Path]::GetFullPath($sandbox)
    if (-not $target.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($target) -notlike 'dvl-launcher-*') { throw 'Unsafe test cleanup path.' }
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
}

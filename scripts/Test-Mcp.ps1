param([string]$HostDll = "$PSScriptRoot/../SqlServerDevLens.McpHost/bin/Release/net10.0/SqlServerDevLens.McpHost.dll")
$ErrorActionPreference = 'Stop'
$start = [System.Diagnostics.ProcessStartInfo]::new('dotnet')
$start.ArgumentList.Add([System.IO.Path]::GetFullPath($HostDll))
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$process = [System.Diagnostics.Process]::Start($start)
$errors = $process.StandardError.ReadToEndAsync()
function Read-Response($id) {
    while ($true) {
        $read = $process.StandardOutput.ReadLineAsync()
        if (-not $read.Wait(15000)) { throw 'MCP response timed out' }
        if ($null -eq $read.Result) { throw 'MCP host closed stdout' }
        $response = $read.Result | ConvertFrom-Json
        if ($response.error) { throw ($response.error | ConvertTo-Json -Compress) }
        if ($response.id -eq $id) { return $response }
    }
}
try {
    $process.StandardInput.WriteLine('{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"devlens-smoke","version":"1.0"}}}')
    $null = Read-Response 1
    $process.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}')
    $process.StandardInput.WriteLine('{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}')
    $list = Read-Response 2
    if ('get_server_status' -notin $list.result.tools.name) { throw 'Status tool missing' }
    $process.StandardInput.WriteLine('{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"get_server_status","arguments":{}}}')
    $call = Read-Response 3
    if ($call.result.isError) { throw 'Status tool returned an error' }
    $status = $call.result.content[0].text | ConvertFrom-Json
    if ($status.status -ne 'ready' -or $status.sqlConnected -ne $false) { throw 'Unexpected host status' }
    Write-Output 'PASS: initialize, tools/list, get_server_status; stdout contained JSON protocol responses.'
}
finally {
    $process.StandardInput.Close()
    if (-not $process.WaitForExit(5000)) { $process.Kill($true); $process.WaitForExit() }
    $process.Dispose()
}

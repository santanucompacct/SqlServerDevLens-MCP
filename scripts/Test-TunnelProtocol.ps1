param([Parameter(Mandatory)][string]$HostExe)
$ErrorActionPreference='Stop'
$info=[Diagnostics.ProcessStartInfo]::new([IO.Path]::GetFullPath($HostExe))
foreach ($argument in @('--desktop','--pipe-name',('dvl-package-'+[guid]::NewGuid().ToString('N')))) { $info.ArgumentList.Add($argument) }
$info.UseShellExecute=$false; $info.CreateNoWindow=$true
$info.RedirectStandardInput=$true; $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
$child=[Diagnostics.Process]::Start($info); $stderr=$child.StandardError.ReadToEndAsync()
function Request($id,$method,$parameters) {
    $child.StandardInput.WriteLine((@{jsonrpc='2.0';id=$id;method=$method;params=$parameters}|ConvertTo-Json -Depth 8 -Compress))
    while ($true) {
        $line=$child.StandardOutput.ReadLineAsync()
        if (-not $line.Wait(15000) -or $null -eq $line.Result) { throw 'Packaged MCP protocol timed out/closed.' }
        $reply=$line.Result|ConvertFrom-Json
        if ($reply.id -eq $id) { if ($reply.error) { throw 'Packaged MCP returned a protocol error.' }; return $reply.result }
    }
}
try {
    $null=Request 1 'initialize' @{protocolVersion='2025-11-25';capabilities=@{};clientInfo=@{name='tunnel-package-test';version='1.0'}}
    $child.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}')
    $tools=Request 2 'tools/list' @{}
    if ($tools.tools.Count -ne 27 -or 'read_table_page' -notin $tools.tools.name -or 'get_table_schema' -notin $tools.tools.name -or 'read_result_page' -notin $tools.tools.name -or 'release_result' -notin $tools.tools.name) { throw 'Expected tunnel adapter tool schemas missing.' }
    $result=Request 3 'tools/call' @{name='get_server_status';arguments=@{}}
    if ($result.isError -or ($result.content[0].text|ConvertFrom-Json).status -ne 'desktop_unavailable') { throw 'Isolated adapter status failed.' }
    Write-Host 'PASS: desktop-mode initialize, 27 tools including table paging, isolated unavailable status. No live pipe was used.'
} finally {
    $child.StandardInput.Close()
    if (-not $child.WaitForExit(5000)) { $child.Kill($true); $child.WaitForExit() }
    $child.Dispose()
}

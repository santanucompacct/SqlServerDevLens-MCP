[CmdletBinding()]
param([string]$Version = '0.12.0')
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Build/test this Windows package on Windows with PowerShell 7.' }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must be a numeric semantic version.' }
if ($env:GITHUB_REF_TYPE -eq 'tag' -and $env:GITHUB_REF_NAME -ne "v$Version") { throw 'Release tag and package versions must match.' }
$repo = Split-Path $PSScriptRoot -Parent
$output = Join-Path $repo 'distribution/packages'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$buildRoot = Join-Path $output ('build-' + [Guid]::NewGuid().ToString('N'))
$package = Join-Path $buildRoot "SqlServerDevLens-$Version-win-x64"
$desktop = Join-Path $package 'desktop'
$plugin = Join-Path $package 'plugins/sql-server-devlens'
New-Item -ItemType Directory -Force -Path $desktop, $plugin | Out-Null
foreach ($project in @('SqlServerDevLens.Desktop', 'SqlServerDevLens.McpHost')) {
    $target = if ($project.EndsWith('.Desktop')) { $desktop } else { Join-Path $plugin 'server' }
    & dotnet publish (Join-Path $repo $project) -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false "-p:Version=$Version" -o $target
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $project" }
}
$sourcePlugin = Join-Path $repo 'plugins/sql-server-devlens'
foreach ($item in @('plugin.json', 'mcp.json', 'INSTALL.md', '.codex-plugin', 'skills')) {
    Copy-Item -LiteralPath (Join-Path $sourcePlugin $item) -Destination $plugin -Recurse -Force
}
$metadata = Get-Content -Raw (Join-Path $plugin 'plugin.json') | ConvertFrom-Json
if ($metadata.version -ne $Version) { throw 'Plugin and distribution versions must match.' }
$mcpPath = Join-Path $plugin 'mcp.json'
$mcp = Get-Content -Raw $mcpPath | ConvertFrom-Json
$server = $mcp.mcpServers.'sql-server-devlens'
# Agent Plugins resolves a contained ./ command against the installed plugin root.
# ${PLUGIN_ROOT} is supported in arguments/cwd, but is rejected in command.
$server.command = './server/SqlServerDevLens.McpHost.exe'
$server.args = @('--desktop')
$mcp | ConvertTo-Json -Depth 12 | Set-Content -Encoding utf8 $mcpPath
$catalog = Join-Path $package '.agents/plugins'
New-Item -ItemType Directory -Force -Path $catalog | Out-Null
Copy-Item -LiteralPath (Join-Path $repo '.agents/plugins/marketplace.json') -Destination $catalog
foreach ($name in @('Setup-Plugin.ps1', 'Install-Plugin.cmd', 'Start-DevLens.cmd', 'README-FIRST.txt')) {
    Copy-Item -LiteralPath (Join-Path $repo "distribution/$name") -Destination $package
}
Copy-Item -LiteralPath (Join-Path $repo 'docs/CONNECTING-TO-CHATGPT.md') -Destination (Join-Path $package 'HELP.md')
Copy-Item -LiteralPath (Join-Path $repo 'docs/RESULTS-AND-AUTHORIZATION.md') -Destination $package
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE.txt') -Destination $package

# Preserve declared licenses and license/notice files available in dependency packages.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$notices = Join-Path $package 'licenses'
New-Item -ItemType Directory -Force -Path $notices | Out-Null
$dependencies = @{}
foreach ($project in @('SqlServerDevLens.Desktop', 'SqlServerDevLens.McpHost')) {
    $assets = Get-Content -Raw (Join-Path $repo "$project/obj/project.assets.json") | ConvertFrom-Json -AsHashtable
    foreach ($entry in $assets.libraries.GetEnumerator()) {
        if ($entry.Value.type -eq 'package') { $dependencies[$entry.Key] = $entry.Value.path }
    }
}
$nugetRoot = @($assets.packageFolders.Keys)[0]
$runtimeConfig = Get-Content -Raw (Join-Path $desktop 'SqlServerDevLens.Desktop.runtimeconfig.json') | ConvertFrom-Json
foreach ($framework in $runtimeConfig.runtimeOptions.includedFrameworks) {
    $id = "$($framework.name).Runtime.win-x64".ToLowerInvariant()
    $dependencies["$id/$($framework.version)"] = "$id/$($framework.version)"
}
$licenseIndex = [Collections.Generic.List[object]]::new()
foreach ($entry in $dependencies.GetEnumerator() | Sort-Object Key) {
    $directory = Join-Path $nugetRoot $entry.Value
    $archiveFile = Get-ChildItem -LiteralPath $directory -Filter '*.nupkg' -File | Select-Object -First 1
    if (-not $archiveFile) { throw "Dependency archive unavailable: $($entry.Key)" }
    $archive = [IO.Compression.ZipFile]::OpenRead($archiveFile.FullName)
    try {
        $nuspec = $archive.Entries | Where-Object FullName -Like '*.nuspec' | Select-Object -First 1
        $reader = [IO.StreamReader]::new($nuspec.Open())
        try { [xml]$spec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $licenseIndex.Add([pscustomobject]@{package=$entry.Key; license=[string]$spec.package.metadata.license.InnerText; licenseUrl=[string]$spec.package.metadata.licenseUrl; projectUrl=[string]$spec.package.metadata.projectUrl})
        foreach ($file in $archive.Entries | Where-Object { $_.Name -match '(?i)^(license|licence|notice|third.?party.?notices)(\..*)?$' }) {
            if ($file.Length -gt 4MB) { throw 'Unexpectedly large license file.' }
            $name = ($entry.Key + '-' + $file.FullName) -replace '[^a-zA-Z0-9._-]', '_'
            [IO.Compression.ZipFileExtensions]::ExtractToFile($file, (Join-Path $notices $name), $false)
        }
    } finally { $archive.Dispose() }
}
$licenseIndex | ConvertTo-Json -Depth 6 | Set-Content -Encoding utf8 (Join-Path $notices 'dependencies.json')
foreach ($target in @($desktop, (Join-Path $plugin 'server'))) {
    foreach ($name in @('coreclr.dll', 'hostfxr.dll', 'System.Private.CoreLib.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $target $name))) { throw "Missing bundled runtime: $name" }
    }
}
& (Join-Path $PSScriptRoot 'Test-Mcp.ps1') -HostExe (Join-Path $plugin 'server/SqlServerDevLens.McpHost.exe')
# Launch only this new test process; startup does not test SQL or load credentials.
$gui = Start-Process -FilePath (Join-Path $desktop 'SqlServerDevLens.Desktop.exe') -WorkingDirectory $desktop -WindowStyle Hidden -PassThru
try {
    $ready = $false
    for ($i = 0; $i -lt 30; $i++) {
        if ($gui.WaitForExit(500)) { throw "Published desktop exited before showing its window ($($gui.ExitCode))." }
        $gui.Refresh()
        if ($gui.MainWindowHandle -ne [IntPtr]::Zero -and $gui.Responding) { $ready = $true; break }
    }
    if (-not $ready) { throw 'Published desktop did not create a responsive window.' }
} finally { if (-not $gui.HasExited) { $gui.Kill($true); $gui.WaitForExit() }; $gui.Dispose() }
Write-Host 'PASS: self-contained desktop created a responsive startup window; no SQL/Postman setup was performed.'
$zip = Join-Path $buildRoot "SqlServerDevLens-$Version-win-x64.zip"
[IO.Compression.ZipFile]::CreateFromDirectory($package, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($zip))" | Set-Content -Encoding ascii (Join-Path $buildRoot 'SHA256SUMS.txt')
Write-Host "Package: $zip"
if ($env:GITHUB_OUTPUT) { "package_dir=$buildRoot" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8 }
Write-Output $buildRoot

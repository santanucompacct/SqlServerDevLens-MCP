[CmdletBinding()]
param([string]$Version='0.11.0', [int]$Revision=1)
$ErrorActionPreference='Stop'
if (-not $IsWindows -or $Revision -lt 1 -or $Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use Windows PowerShell 7, a semantic version and positive revision.' }
$tag="tunnel-$Version-$Revision"
if ($env:GITHUB_REF_TYPE -eq 'tag' -and $env:GITHUB_REF_NAME -ne $tag) { throw 'Tunnel tag/version mismatch.' }
$repo=Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'Test-TunnelLauncher.ps1')
$lock=Get-Content -Raw (Join-Path $repo 'Tunnel/tunnel-client-lock.json') | ConvertFrom-Json
$output=Join-Path $repo ('Tunnel/packages/build-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output -Force | Out-Null
$inputs=Join-Path $output 'inputs'; New-Item -ItemType Directory -Path $inputs | Out-Null
$archive=Join-Path $inputs 'upstream.zip'
Invoke-WebRequest -Uri $lock.archiveUrl -OutFile $archive
if ((Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $lock.archiveSha256) { throw 'Untrusted tunnel archive.' }
$vendor=Join-Path $output 'verified-upstream'
Expand-Archive -LiteralPath $archive -DestinationPath $vendor
foreach ($entry in $lock.files.PSObject.Properties) {
    if ($entry.Name -ne [IO.Path]::GetFileName($entry.Name)) { throw 'Invalid vendor inventory.' }
    $file=Join-Path $vendor $entry.Name
    if (-not(Test-Path -LiteralPath $file -PathType Leaf) -or (Get-FileHash $file).Hash.ToLowerInvariant() -ne $entry.Value) { throw 'Tunnel dependency integrity failure.' }
}
# Reuse the standard fresh self-contained publisher without changing its workflow or tag policy.
$refType=$env:GITHUB_REF_TYPE; $githubOutput=$env:GITHUB_OUTPUT
try {
    $env:GITHUB_REF_TYPE='branch'; Remove-Item Env:GITHUB_OUTPUT -ErrorAction SilentlyContinue
    $baseline=@(& (Join-Path $PSScriptRoot 'Build-WindowsDistribution.ps1') -Version $Version)
} finally { $env:GITHUB_REF_TYPE=$refType; $env:GITHUB_OUTPUT=$githubOutput }
$base=Join-Path ([string]$baseline[-1]) "SqlServerDevLens-$Version-win-x64"
if (-not(Test-Path -LiteralPath (Join-Path $base 'desktop/SqlServerDevLens.Desktop.exe'))) { throw 'Fresh standard publish was not produced.' }
$name="SqlServerDevLens-Tunnel-$Version-$Revision-win-x64"
$package=Join-Path $output $name
New-Item -ItemType Directory -Path $package | Out-Null
foreach ($item in @('plugins','.agents','licenses','LICENSE.txt','HELP.md','Install-Plugin.cmd','Setup-Plugin.ps1')) {
    Copy-Item -LiteralPath (Join-Path $base $item) -Destination $package -Recurse
}
Copy-Item -LiteralPath (Join-Path $base 'desktop') -Destination (Join-Path $package 'published_app') -Recurse
Copy-Item -LiteralPath (Join-Path $base 'plugins/sql-server-devlens/server') -Destination (Join-Path $package 'published_mcp') -Recurse
$tunnel=Join-Path $package 'Tunnel'; New-Item -ItemType Directory -Path $tunnel | Out-Null
foreach ($entry in $lock.files.PSObject.Properties) { Copy-Item -LiteralPath (Join-Path $vendor $entry.Name) -Destination $tunnel }
foreach ($item in @('tunnel-client-lock.json','Configure-Tunnel.ps1','Start-Tunnel.ps1','Start-All.ps1','launcher.example.ini','README.md','QUICKSTART.md','DOWNLOAD-VALIDATION.md')) {
    Copy-Item -LiteralPath (Join-Path $repo "Tunnel/$item") -Destination $tunnel
}
Copy-Item -LiteralPath (Join-Path $repo 'Tunnel/images') -Destination (Join-Path $tunnel 'images') -Recurse
Copy-Item -LiteralPath (Join-Path $repo 'docs/TUNNEL_SETUP_AND_RELEASE.md') -Destination $package
$record=Join-Path $package 'TUNNEL_SETUP_AND_RELEASE.md'
[IO.File]::WriteAllText($record,[IO.File]::ReadAllText($record).Replace('(../Tunnel/','(Tunnel/'))
'@echo off', 'powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Tunnel\Start-All.ps1"', 'if errorlevel 1 pause' | Set-Content -Encoding ascii (Join-Path $package 'Start-All.cmd')
'@echo off', 'start "" "%~dp0published_app\SqlServerDevLens.Desktop.exe"' | Set-Content -Encoding ascii (Join-Path $package 'Start-DevLens.cmd')
foreach ($action in @('Configure','Start')) {
    '@echo off', "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"%~dp0Tunnel\$action-Tunnel.ps1`"", 'pause' |
        Set-Content -Encoding ascii (Join-Path $package "$action-Tunnel.cmd")
}
@{version=$Version;revision=$Revision;tag=$tag;commit=(& git -C $repo rev-parse HEAD);tunnelVersion=$lock.version;
    tunnelArchiveSha256=$lock.archiveSha256;identityIncluded=$false;validation='Offline package validation; recipient account setup required'} |
    ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $package 'BUILD-INFO.json')
Copy-Item -LiteralPath (Join-Path $repo 'Tunnel/README.md') -Destination (Join-Path $package 'README-FIRST.md')
$firstReadme=Join-Path $package 'README-FIRST.md'
$readmeText=[IO.File]::ReadAllText($firstReadme).Replace('(QUICKSTART.md)','(Tunnel/QUICKSTART.md)').Replace('(DOWNLOAD-VALIDATION.md)','(Tunnel/DOWNLOAD-VALIDATION.md)')
[IO.File]::WriteAllText($firstReadme,$readmeText)
# The package keeps this maintainer record at its root; preserve the guide link after copying.
$quickstart=Join-Path $tunnel 'QUICKSTART.md'
[IO.File]::WriteAllText($quickstart,[IO.File]::ReadAllText($quickstart).Replace('(../docs/TUNNEL_SETUP_AND_RELEASE.md)','(../TUNNEL_SETUP_AND_RELEASE.md)'))
# Debug symbols can disclose local source paths; remove only files inside this fresh staging root.
Get-ChildItem -LiteralPath $package -File -Recurse -Filter '*.pdb' | ForEach-Object {
    if (-not $_.FullName.StartsWith($package+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe symbol removal target.' }
    Remove-Item -LiteralPath $_.FullName
}
& (Join-Path $PSScriptRoot 'Test-TunnelDistribution.ps1') -PackageRoot $package
$zip=Join-Path $output "$name.zip"
[IO.Compression.ZipFile]::CreateFromDirectory($package,$zip,[IO.Compression.CompressionLevel]::Optimal,$true)
"$((Get-FileHash $zip).Hash.ToLowerInvariant())  $name.zip" | Set-Content -Encoding ascii (Join-Path $output 'SHA256SUMS.txt')
if ($env:GITHUB_OUTPUT) { "package_dir=$output" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append }
Write-Output $output

[CmdletBinding()]
param([string]$Destination = (Join-Path $env:LOCALAPPDATA 'SqlServerDevLens/Portable/v0.9.0'))
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$version = '0.9.0'
$fileName = "SqlServerDevLens-$version-win-x64.zip"
$expectedHash = '57f369e92729096ec9a091dd17ef9c1a211423a7be879920624b95c1f8aa2190'
$destinationPath = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $destinationPath) {
    throw "Destination already exists: $destinationPath. Existing files were not changed. Use that installation or choose a new folder with -Destination."
}
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('devlens-dl-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $zipPath = Join-Path $temporary $fileName
    Write-Host "Downloading DevLens $version for Windows x64 (about 120 MB)..."
    Invoke-WebRequest -UseBasicParsing -TimeoutSec 600 -Uri "https://github.com/santanucompacct/SqlServerDevLens-MCP/releases/download/v$version/$fileName" -OutFile $zipPath
    if ((Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedHash) {
        throw 'Checksum verification failed. No package was extracted. Download again from the official release.'
    }
    Write-Host 'Checksum verified. Extracting the complete package...'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $destinationPath)
    $package = Join-Path $destinationPath "SqlServerDevLens-$version-win-x64"
    foreach ($required in @('Install-Plugin.cmd', 'Start-DevLens.cmd', 'desktop/SqlServerDevLens.Desktop.exe', 'plugins/sql-server-devlens/server/SqlServerDevLens.McpHost.exe', 'plugins/sql-server-devlens/skills/devlens-workflow/SKILL.md')) {
        if (-not (Test-Path -LiteralPath (Join-Path $package $required) -PathType Leaf)) { throw "Extracted package is incomplete: $required" }
    }
    Write-Host "Ready: $package"
    Write-Host 'Next: run Install-Plugin.cmd in that folder, restart ChatGPT Desktop once, then run Start-DevLens.cmd.'
    Write-Host 'No Visual Studio or separately installed .NET is required. ChatGPT Desktop/local-plugin access is still required.'
    Write-Host 'Download only: no plugin registration, SQL connection or application request was performed.'
    Write-Output $package
} finally {
    $absolute = [IO.Path]::GetFullPath($temporary)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $absolute.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($absolute) -notlike 'devlens-dl-*') { throw 'Refusing cleanup outside this download temporary directory.' }
    if (Test-Path -LiteralPath $absolute) { Remove-Item -LiteralPath $absolute -Recurse -Force }
}

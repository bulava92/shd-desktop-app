$ErrorActionPreference = 'Stop'

$windowsRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $windowsRoot 'SHD.Windows\SHD.Windows.csproj'
$publishDir = Join-Path $windowsRoot 'publish'
$distDir = Join-Path $windowsRoot 'dist'
$bundleDir = Join-Path $distDir 'SHD-Windows-x64'
$zipPath = Join-Path $distDir 'SHD-Windows-x64.zip'

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
if (Test-Path $bundleDir) { Remove-Item $bundleDir -Recurse -Force }
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $bundleDir -Force | Out-Null

dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

Copy-Item (Join-Path $publishDir '*') $bundleDir -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot 'install.ps1') $bundleDir -Force
Copy-Item (Join-Path $PSScriptRoot 'uninstall.ps1') $bundleDir -Force
Copy-Item (Join-Path $windowsRoot 'README.md') $bundleDir -Force
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $bundleDir '*') -DestinationPath $zipPath -CompressionLevel Optimal
Write-Host "Created $zipPath"

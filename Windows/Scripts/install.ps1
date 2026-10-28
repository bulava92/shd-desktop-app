param(
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
$bundleDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceExe = Join-Path $bundleDir 'SHD.Windows.exe'
if (-not (Test-Path $sourceExe)) {
    throw 'SHD.Windows.exe is missing. Extract the complete SHD-Windows-x64.zip before installing.'
}

$installDir = Join-Path $env:LOCALAPPDATA 'Programs\SHD'
New-Item -ItemType Directory -Path $installDir -Force | Out-Null
$bundleFullPath = [System.IO.Path]::GetFullPath($bundleDir).TrimEnd('\')
$installFullPath = [System.IO.Path]::GetFullPath($installDir).TrimEnd('\')
if (-not $bundleFullPath.Equals($installFullPath, [System.StringComparison]::OrdinalIgnoreCase)) {
    Get-ChildItem -LiteralPath $bundleDir -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $installDir -Recurse -Force
    }
}

$installedExe = Join-Path $installDir 'SHD.Windows.exe'
$protocolKey = 'HKCU:\Software\Classes\shd-auth'
New-Item -Path $protocolKey -Force | Out-Null
Set-Item -Path $protocolKey -Value 'URL:SHD Desktop authentication'
New-ItemProperty -Path $protocolKey -Name 'URL Protocol' -Value '' -PropertyType String -Force | Out-Null
$commandKey = Join-Path $protocolKey 'shell\open\command'
New-Item -Path $commandKey -Force | Out-Null
Set-Item -Path $commandKey -Value ('"{0}" "%1"' -f $installedExe)

$startMenuDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\SHD'
New-Item -ItemType Directory -Path $startMenuDir -Force | Out-Null
$shortcutPath = Join-Path $startMenuDir 'SHD.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $installedExe
$shortcut.WorkingDirectory = $installDir
$shortcut.IconLocation = "$installedExe,0"
$shortcut.Save()

Write-Host "SHD installed to $installDir"
Write-Host 'The shd-auth:// callback is registered for passkey sign-in.'
if (-not $NoLaunch) { Start-Process -FilePath $installedExe }

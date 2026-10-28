$ErrorActionPreference = 'Stop'
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\SHD'
$startMenuDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\SHD'
$protocolKey = 'HKCU:\Software\Classes\shd-auth'

if (Test-Path $protocolKey) {
    $commandKey = Join-Path $protocolKey 'shell\open\command'
    $registeredCommand = if (Test-Path $commandKey) { (Get-Item -Path $commandKey).GetValue('') } else { '' }
    if ($registeredCommand -like '*\Programs\SHD\SHD.Windows.exe*') {
        Remove-Item -Path $protocolKey -Recurse -Force
    }
}
if (Test-Path $startMenuDir) { Remove-Item -Path $startMenuDir -Recurse -Force }
if (Test-Path $installDir) { Remove-Item -Path $installDir -Recurse -Force -ErrorAction SilentlyContinue }

Write-Host 'SHD has been uninstalled. Your WebView profile and site data remain under %LOCALAPPDATA%\SHDDesktop.'

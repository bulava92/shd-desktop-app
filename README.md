# SHD Desktop — скачать и установить

## macOS

Готовое приложение уже собрано из актуального кода и лежит в репозитории:

**[Скачать ZIP из Gitea](https://git.xyz.su/shd/shd-desktop-app/raw/branch/main/Apple/downloads/SHD-macOS-arm64.zip)** · **[скачать из GitHub](https://github.com/bulava92/shd-desktop-app/raw/refs/heads/main/Apple/downloads/SHD-macOS-arm64.zip)**

В файле репозитория: [`Apple/downloads/SHD-macOS-arm64.zip`](Apple/downloads/SHD-macOS-arm64.zip).

Нужен Mac с процессором Apple Silicon и macOS 14 или новее. Для установки не нужно скачивать исходники или что-либо собирать.

1. Скачайте ZIP по ссылке выше.
2. Откройте ZIP двойным щелчком — появится `SHD.app`.
3. Закройте старую версию SHD, если она запущена.
4. Перетащите `SHD.app` в папку «Программы» и согласитесь заменить старую версию, если macOS спросит.
5. Откройте SHD из папки «Программы».

Сборка не нотарифицирована Apple, поэтому macOS может предупредить о неизвестном разработчике. Если вы скачали приложение из этого репозитория и доверяете ему, после первой попытки запуска откройте «Системные настройки» → «Конфиденциальность и безопасность» → «Открыть всё равно». [Инструкция Apple](https://support.apple.com/en-gb/102445).

## Windows

Готового Windows ZIP пока нет. Его можно собрать на Windows 10 или 11 x64.

### 1. Установите Git и .NET 10 SDK

Откройте PowerShell и выполните:

```powershell
winget install --id Git.Git -e --source winget
winget install --id Microsoft.DotNet.SDK.10 --exact --source winget
```

Закройте PowerShell и откройте его снова.

### 2. Скачайте проект

Создайте папку `Projects` в домашней папке, скачайте репозиторий и перейдите в него:

```powershell
New-Item -ItemType Directory -Force -Path "$HOME\Projects" | Out-Null
Set-Location "$HOME\Projects"
git clone https://git.xyz.su/shd/shd-desktop-app.git
Set-Location .\shd-desktop-app
```

Оба репозитория открыты: войти в аккаунт не нужно. Если Gitea недоступен, используйте GitHub: `git clone https://github.com/bulava92/shd-desktop-app.git`.

### 3. Соберите и установите приложение

В том же PowerShell выполните:

```powershell
$ErrorActionPreference = 'Stop'
powershell -NoProfile -ExecutionPolicy Bypass -File .\Windows\Scripts\build.ps1
if ($LASTEXITCODE -ne 0) { throw 'Сборка не удалась.' }
$packageDir = Join-Path $env:TEMP ("SHD-Windows-" + [guid]::NewGuid())
Expand-Archive -LiteralPath .\Windows\dist\SHD-Windows-x64.zip -DestinationPath $packageDir
powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $packageDir 'install.ps1')
```

Установщик добавит SHD в меню «Пуск» и запустит приложение. Для запуска нужен Microsoft Edge WebView2 Runtime; если его нет, SHD предложит установить [WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/). Дополнительные сведения — в [`Windows/README.md`](Windows/README.md).

## Вход

Вход по passkey в приложении пока работает только на DEV. Для PROD используйте Safari или другой браузер.

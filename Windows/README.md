# Установка SHD для Windows

Если у вас есть только исходники, выполните раздел «Windows» в [основной инструкции](../README.md), чтобы установить инструменты, скачать проект и собрать установочный ZIP.

## Если у вас уже есть установочный ZIP

1. Распакуйте `SHD-Windows-x64.zip` в любую папку.
2. Откройте PowerShell в этой папке.
3. Выполните команду:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
```

SHD скопируется в `%LOCALAPPDATA%\Programs\SHD`, появится в меню «Пуск» и запустится.

## Если ZIP ещё нет

Пока готовый установочный ZIP не опубликован. На Windows с установленным .NET 10 SDK выполните из корня скачанного проекта:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Windows\Scripts\build.ps1
```

Файл будет создан по адресу `Windows\dist\SHD-Windows-x64.zip`. Затем распакуйте его и выполните команду установки выше.

Для запуска приложения нужен Microsoft Edge WebView2 Runtime. Обычно он уже установлен вместе с Microsoft Edge. Если Runtime отсутствует, SHD покажет ссылку на его установку.

## Удаление

В PowerShell выполните:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\Programs\SHD\uninstall.ps1"
```

SHD исчезнет из меню «Пуск». Профиль сайта и сохранённые данные останутся на компьютере.

В приложении выберите «Контур» → «SHD Production» или «SHD Dev». Вход по passkey пока работает только на DEV; для PROD используйте браузер.

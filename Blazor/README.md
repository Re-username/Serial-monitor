# Serial Plotter — Blazor

Новая версия интерфейса Serial Plotter на **MAUI Blazor Hybrid**. Окно Windows нативное,
весь UI написан на Blazor/HTML/CSS, графики рисуются в SVG.

Старый WinForms-проект в корне репозитория не изменялся.

## Требования

- Windows 10/11 x64
- .NET 10 SDK с workload `maui-windows`

## Сборка и запуск

Из корня репозитория:

```bash
dotnet build Blazor/SerialPlotter.Blazor.csproj -c Release
dotnet run --project Blazor/SerialPlotter.Blazor.csproj -c Release
```

Или из этой папки:

```bash
dotnet build SerialPlotter.Blazor.csproj -c Release
dotnet run --project SerialPlotter.Blazor.csproj -c Release
```

## Публикация

```bash
dotnet publish SerialPlotter.Blazor.csproj -c Release -o publish
```

Результат — `publish/SerialPlotter.exe` рядом с DLL и ресурсами MAUI / Windows App SDK.

## Что перенесено

- COM-порт: подключение, бодрейт, DTR, кодировки, отправка строк.
- Симулятор сигналов и совместная работа порта с симулятором.
- Каналы: имена, цвета, видимость, толщина, тип линии, сглаживание.
- Режимы: время/XY, полярный, 3D, FFT.
- Навигация: зум, панорама, маркеры A/B, сброс вида.
- Консоль входящих/исходящих строк.
- Экспорт PNG/SVG/CSV.
- Настройки в `%AppData%/SerialPlotter/settings.json`.
- Темы Cyberpunk/Dark/Light и язык RU/EN.

## Demo-запуск для визуальной проверки

Можно сразу открыть приложение с запущенным симулятором в нужном режиме:

```powershell
$env:SERIALPLOTTER_DEMO="Time"      # Time / Polar / ThreeD / Fft
$env:SERIALPLOTTER_THEME="cyberpunk" # cyberpunk / dark / light
.\bin\Release\net10.0-windows10.0.19041.0\win-x64\SerialPlotter.exe
```

В Git Bash:

```bash
SERIALPLOTTER_DEMO=Time SERIALPLOTTER_THEME=cyberpunk \
  ./bin/Release/net10.0-windows10.0.19041.0/win-x64/SerialPlotter.exe
```

Скриншоты последнего визуального прогона лежат в [`screenshots/`](screenshots/).

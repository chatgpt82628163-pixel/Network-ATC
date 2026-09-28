<div align="center">
  <img src="docs/logo.png" width="96" alt="Network-ATC">
  <h1>Network-ATC</h1>
  <p>Диспетчерская станция сети SkyNetwork</p>

  [![CI](https://github.com/chatgpt82628163-pixel/Network-ATC/actions/workflows/ci.yml/badge.svg)](https://github.com/chatgpt82628163-pixel/Network-ATC/actions/workflows/ci.yml)
  [![Release](https://img.shields.io/github/v/release/chatgpt82628163-pixel/Network-ATC)](https://github.com/chatgpt82628163-pixel/Network-ATC/releases/latest)

  [Сайт](https://sky.network.npzy2.us) · [Скачать](https://sky.network.npzy2.us/docs/software) · [Поддержка](https://sky.network.npzy2.us/support)
</div>

---

## Что это

Network-ATC — программа для диспетчеров авиационной сети SkyNetwork. Она показывает радарную картину в реальном времени, позволяет управлять воздушным движением и взаимодействовать с другими диспетчерами. Программа написана на WPF (.NET 8) и работает только на Windows.

---

## Возможности

### Радар и воздушное пространство

- Загрузка секторных файлов EuroScope (`.sct` + `.ese`) и собственного формата Network-ATC (`.natc`, JSON).
- Настраиваемые слои карты: каждый слой — произвольный набор полилиний с именем и цветом.
- Метки воздушных судов с гибким шаблоном: стандартное, активное и детальное состояние. Поля задаются текстом (`{callsign}`, `{alt}`, `{cfl}`, `{gs10}` и другие).
- STCA (краткосрочное предупреждение о конфликте): текущее нарушение эшелонирования и прогноз на 2 минуты вперёд (5 NM / 1000 ft по умолчанию, только воздушные суда выше 1500 ft).
- Сигналы тревоги по коду ответчика: EMERG, CLAM, DUPE, SQ.

### Списки воздушного движения

- Список вылетов: воздушные суда у активных аэропортов со статусом (на стоянке, рулит, взлётный разбег, в воздухе).
- Список прилётов: воздушные суда, следующие в активные аэропорты, с расстоянием и расчётным временем прибытия.

### ATIS

- Несколько станций ATIS с произвольным текстом и поддержкой переменных (`$metar(...)`, `$atiscode(...)`).
- Автоматическая смена буквы при поступлении новой METAR (опция `AutoLetter`).
- Озвучивание через системный синтез речи Windows (System.Speech).

### Координация

- Подключение к серверу SkyNetwork по протоколу FSD.
- Список онлайн-диспетчеров с позывными и частотами.
- Текстовые каналы: Unicom, частотный, частный.
- Приём и отправка координации (handoff, CFL, scratchpad, assigned squawk, heading, speed).

### SID/STAR и активные ВПП

- Привязка воздушных судов к SID/STAR из секторного файла.
- Указание активных ВПП для аэропортов.

### Плагины и интеграция

- **EuroScope-плагины**: мост к 32-битным DLL-плагинам EuroScope (нативный компонент на C++/Win32). Плагины загружаются из папки профиля и работают с тегами, всплывающими списками и отрисовкой.
- **Собственный API**: .NET 8 плагины (`IAtcPlugin`, API версии 1.2). Плагин — класс-библиотека, реализующая `IAtcPlugin`; DLL кладётся в папку плагинов и загружается при старте.

### Профили и темы

- Настройки хранятся в профилях (JSON); смена профиля без перезапуска.
- Поддержка тем оформления.

### Обновления

- Автоматическая проверка обновлений через GitHub Releases: при появлении новой версии программа скачивает установщик и запускает его тихо, без вопросов.

---

## Скриншоты

<div align="center">
  <img src="https://sky.network.npzy2.us/screenshots/atc-main.png" alt="Радарный экран" width="700">
</div>

---

## Установка

Скачайте установщик со страницы [sky.network.npzy2.us/docs/software](https://sky.network.npzy2.us/docs/software) и запустите его. Права администратора не нужны.

**Требования:**
- Windows 10 или новее (64-bit).
- Подключение к интернету для связи с сервером SkyNetwork.

Установщик не затрагивает профили и настройки при обновлении и удалении. Обновления устанавливаются автоматически: программа сама проверяет новые релизы на GitHub и перезапускается после обновления.

---

## Сборка и запуск

### Зависимости

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [CMake](https://cmake.org/) (для моста EuroScope-плагинов)
- Windows (сборка всего проекта); Linux подходит для запуска тестов ядра

### Основная сборка

```
dotnet build NetworkAtc.sln -c Release
```

### Запуск приложения

```
dotnet run --project src/NetworkAtc.App -c Release
```

### Мост EuroScope-плагинов (Win32)

```
cmake -S native/EsBridge -B build/esbridge -A Win32
cmake --build build/esbridge --config Release
```

### Тесты

```
dotnet test tests/NetworkAtc.Core.Tests -c Release
dotnet test tests/NetworkAtc.EsHost.Tests -c Release
```

Для тестов хоста EuroScope нужна переменная окружения `NATC_ESBRIDGE_DIR`, указывающая на папку с собранными нативными библиотеками (`build/esbridge/Release`).

---

## Устройство

```
src/
  NetworkAtc.App/         — WPF-приложение: окна, элементы управления, сервисы
    Controls/             — пользовательские элементы (радар, теги, панели)
    Radar/                — отображение радара
    Views/                — основные окна
  NetworkAtc.Core/        — логика без UI
    Atis/                 — ATIS: текст, буква, автоматическая смена
    Customization/        — профили и темы
    EsPlugins/            — мост к EuroScope-плагинам
    Fsd/                  — FSD-клиент (протокол связи)
    Geo/                  — геодезические расчёты
    Import/               — импорт настроек EuroScope (.asr)
    Plugins/              — собственный API плагинов
    Radar/                — треки, STCA, списки движения
    Sectors/              — парсер .sct/.ese и формат .natc
    Session/              — сессия диспетчера (подключение, треки, сообщения)
    Tags/                 — шаблоны меток
    Updates/              — проверка и установка обновлений
    Voice/                — голосовая связь
  NetworkAtc.PluginApi/   — публичный API для .NET-плагинов
  SkyNetwork.Voice/       — голосовой клиент
native/
  EsBridge/               — нативный Win32-компонент для загрузки EuroScope DLL
tests/
  NetworkAtc.Core.Tests/  — тесты ядра (кросс-платформа)
  NetworkAtc.EsHost.Tests/— тесты хоста EuroScope-плагинов (Windows)
demo/                     — демо-сектор UUEE (Шереметьево)
examples/
  SamplePlugin/           — пример .NET-плагина
installer/                — скрипт Inno Setup
docs/                     — документация
```

---

## Часть SkyNetwork

Network-ATC — один из компонентов экосистемы SkyNetwork:

| Репозиторий | Описание |
|---|---|
| [SkyNetwork-site](https://github.com/chatgpt82628163-pixel/SkyNetwork-site) | Основной сайт сети |
| [SkyNetwork-FSD](https://github.com/chatgpt82628163-pixel/SkyNetwork-FSD) | FSD-сервер |
| [Network-ATC](https://github.com/chatgpt82628163-pixel/Network-ATC) | Диспетчерская станция |
| [SkyPilot](https://github.com/chatgpt82628163-pixel/SkyPilot) | Пилотский клиент |
| [Skynetwork-voice](https://github.com/chatgpt82628163-pixel/Skynetwork-voice) | Голосовой сервер |
| [SkyRUS-site](https://github.com/chatgpt82628163-pixel/SkyRUS-site) | Сайт SkyRUS |
| [Skynetwork-bot](https://github.com/chatgpt82628163-pixel/Skynetwork-bot) | Бот Discord |

---

## English

**Network-ATC** is the ATC client for the SkyNetwork simulation network. Built with WPF on .NET 8, it runs on Windows 10 and later.

**Features:** real-time radar with configurable map layers; sector files in EuroScope format (`.sct`/`.ese`) and the native `.natc` JSON format; customizable aircraft tags; STCA with look-ahead; departure and arrival lists; text ATIS with automatic letter advance and Windows speech synthesis; FSD connection to SkyNetwork; SID/STAR and active runway assignment; EuroScope plugin bridge (32-bit Win32 DLL); native .NET plugin API.

**Install:** download the installer from [sky.network.npzy2.us/docs/software](https://sky.network.npzy2.us/docs/software). No administrator rights required. The program updates itself silently from GitHub Releases.

**Build:**
```
dotnet build NetworkAtc.sln -c Release
dotnet test tests/NetworkAtc.Core.Tests -c Release
```
The EuroScope bridge requires CMake and a Win32 toolchain: `cmake -S native/EsBridge -B build/esbridge -A Win32 && cmake --build build/esbridge --config Release`.

**Support:** [sky.network.npzy2.us/support](https://sky.network.npzy2.us/support)

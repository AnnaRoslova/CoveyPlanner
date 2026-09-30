# CoveyPlanner

Личный планировщик по мотивам недельного листа Стивена Кови («7 навыков высокоэффективных людей»)
плюс лента дел «когда-нибудь», из которой дела перетаскиваются в неделю.
Техническое задание — [CLAUDE.md](CLAUDE.md), образец дизайна — [docs/design-mockup.html](docs/design-mockup.html).

Все команды ниже — для PowerShell, из корня репозитория. Нужен .NET 10 SDK.

## Запуск

```powershell
dotnet run --project src/Planner.Server
```

Открыть http://localhost:5070. Приложение можно установить как PWA
(в Edge/Chrome: меню → «Приложения» → «Установить»).

## Где лежат данные

| Что | Где |
|---|---|
| База (один файл SQLite) | `%LOCALAPPDATA%\CoveyPlanner\planner.db` |
| Резервные копии | `%LOCALAPPDATA%\CoveyPlanner\backups\` |

Рядом с базой во время работы лежат `planner.db-wal` и `planner.db-shm` — это журнал SQLite, он нужен:
не удаляйте его при работающем сервере.

Пути меняются в `src/Planner.Server/appsettings.json`, раздел `Storage`:
`DatabasePath` (файл базы), `BackupPath` (папка копий), `BackupsToKeep` (сколько копий хранить, 30),
`DailyBackup` (копия раз в день при запуске, `true`).

## Резервные копии

**Автоматически** сервер делает копию:
- раз в день — при первом запуске за день;
- перед обновлением схемы базы (когда в новой версии есть миграции) — на случай, если обновление что-то испортит.

Хранятся последние 30 копий, старые удаляются сами. Имя копии: `planner-2026-09-30_221500-ежедневная.db`.

**Вручную**:

```powershell
powershell -File tools/backup.ps1
```

Если сервер запущен, копию делает он (целостная копия средствами SQLite, останавливать работу не нужно).
Если остановлен — скрипт копирует файл базы в `backups`.

**Восстановить из копии**:
1. Остановить сервер (Ctrl+C в его окне).
2. Удалить `planner.db-wal` и `planner.db-shm`, если они есть.
3. Скопировать нужную копию на место `planner.db`:
   ```powershell
   $dir = Join-Path $env:LOCALAPPDATA 'CoveyPlanner'
   Copy-Item "$dir\backups\planner-2026-09-30_221500-ежедневная.db" "$dir\planner.db"
   ```
   (Если рядом с копией лежит файл `…-wal` — скопировать и его, как `planner.db-wal`.)
4. Запустить сервер.

Копию можно просто открыть и посмотреть любым просмотрщиком SQLite (например, DB Browser for SQLite).

## Демо

Отдельная демо-база с примерами — основная база не затрагивается:

```powershell
powershell -File tools/demo.ps1
```

Скрипт удаляет прежнюю демо-базу (`$env:TEMP\planner-demo.db`), собирает проект, запускает сервер
на демо-базе, наполняет её и открывает браузер. Остановить — Ctrl+C.
Сервер запускается из копии сборки во временной папке, поэтому `dotnet build` / `dotnet test`
можно запускать, не останавливая демо.

## Проверки

```powershell
dotnet test                                # модульные тесты и тесты API
powershell -File tools/e2e.ps1             # проверки в браузере (Edge без окна)
powershell -File tools/e2e.ps1 -Only dnd   # только сценарии, в имени которых есть «dnd»
```

Для проверок в браузере нужен Node.js (зависимости ставятся сами). Сценарии — `tests/e2e/*.mjs`:
лента, неделя, роли и цели, раскладка, перетаскивание, клики. У каждого сценария своя чистая база
и сервер на порту 5071; демо и основная база не затрагиваются.

## Устройство

| Проект | Что внутри |
|---|---|
| `src/Planner.Core` | Доменная модель и сценарии (дела, категории, роли, цели), без ASP.NET и EF |
| `src/Planner.Shared` | DTO и контракты API |
| `src/Planner.Server` | Minimal API, EF Core + SQLite, миграции, резервные копии; раздаёт клиент |
| `src/Planner.Client` | Blazor WebAssembly (PWA): экран, состояние, перетаскивание (`wwwroot/js/dnd.js`) |
| `tests/Planner.Tests` | xUnit: домен, API (WebApplicationFactory), миграции, копии |
| `tests/e2e` | Проверки в браузере (puppeteer-core + установленный Edge) |
| `tools` | `demo.ps1`, `seed-demo.ps1`, `backup.ps1`, `e2e.ps1` |

Новая миграция после изменения модели:

```powershell
dotnet ef migrations add <Имя> --project src/Planner.Server --output-dir Data/Migrations
```

(`dotnet-ef` подключён локальным инструментом: при первом запуске — `dotnet tool restore`.)

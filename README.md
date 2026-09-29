# CoveyPlanner

Личный планировщик по мотивам недельного листа Стивена Кови плюс лента дел «когда-нибудь».
Техническое задание — в [CLAUDE.md](CLAUDE.md).

## Запуск

```powershell
dotnet run --project src/Planner.Server
```

Открыть http://localhost:5070. База — `%LOCALAPPDATA%\CoveyPlanner\planner.db`
(путь меняется в `src/Planner.Server/appsettings.json`, ключ `Storage:DatabasePath`).

## Проверки

```powershell
dotnet test                          # модульные тесты и тесты API
powershell -File tools/e2e.ps1       # проверки в браузере (Edge без окна): лента, неделя, роли, перетаскивание
powershell -File tools/e2e.ps1 -Only dnd   # только сценарии перетаскивания
```

Для проверок в браузере нужен Node.js (зависимости ставятся сами). Сценарии — в `tests/e2e/*.mjs`,
у каждого своя чистая база и сервер на порту 5071; демо и основная база не затрагиваются.

## Демо

Отдельная демо-база с примерами, основная база не затрагивается:

```powershell
powershell -File tools/demo.ps1
```

Скрипт удаляет прежнюю демо-базу (`$env:TEMP\planner-demo.db`), собирает проект, запускает сервер
на демо-базе, наполняет её и открывает браузер. Остановить — Ctrl+C.
Сервер запускается из копии сборки во временной папке, поэтому `dotnet build` / `dotnet test`
можно запускать, не останавливая демо.

# CoveyPlanner

Личный планировщик по мотивам недельного листа Стивена Кови плюс лента дел «когда-нибудь».
Техническое задание — в [CLAUDE.md](CLAUDE.md).

## Запуск

```powershell
dotnet run --project src/Planner.Server
```

Открыть http://localhost:5070. База — `%LOCALAPPDATA%\CoveyPlanner\planner.db`
(путь меняется в `src/Planner.Server/appsettings.json`, ключ `Storage:DatabasePath`).

## Демо

Отдельная демо-база с примерами, основная база не затрагивается:

```powershell
powershell -File tools/demo.ps1
```

Скрипт удаляет прежнюю демо-базу (`$env:TEMP\planner-demo.db`), собирает проект, запускает сервер
на демо-базе, наполняет её и открывает браузер. Остановить — Ctrl+C.
Сервер запускается из копии сборки во временной папке, поэтому `dotnet build` / `dotnet test`
можно запускать, не останавливая демо.

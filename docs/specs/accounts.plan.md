# План: аккаунты и вход

Спека: [accounts.md](accounts.md) · журнал: [accounts.metrics.md](accounts.metrics.md). Спека утверждена 2026-10-22 (ответы — раздел 18). Этап 1 — сначала перенос на копии и отчёт Анне, установка — после её «ок».
Каждый этап: `dotnet build` без предупреждений, `dotnet test` и e2e (`tools/e2e.ps1`) зелёные, коммит (номера правил), push,
`tools/backup.ps1`, миграция — сначала на свежей копии со сверкой чисел, `tools/install.ps1`. Рабочий сервер :5070 — только эти два скрипта.
⚠-решения — в metrics, пункты «что сделать → что получится» — в manual-checklist.md (раздел «accounts»), CLAUDE.md — в коммите этапа.

Порядок выбран так, чтобы самый рискованный шаг — перенос данных — шёл отдельно и раньше входа: после этапа 1 приложение работает как сейчас
(один пользователь, без входа), но данные уже лежат в файле администратора.

| Этап | Что | Модель / миграция | Тесты | Установка |
|---|---|---|---|---|
| 1 D+X. Хранилище по пользователям | `Storage:DataDirectory`, `accounts.db` (`AccountsDbContext`, таблица `Users`), `users\{id}.db`; `PlannerDbContext` на файл пользователя через `IUserContext` (пока — всегда администратор); мигратор файлов (D4); бэкап снимком-папкой — все базы, `keys`, `local.key`, `settings.json` (D5); `.gitignore` + `GitIgnoreTests` (D8); `local.key` + `backup.ps1`/`install.ps1` (D6); `settings.json` как источник настроек (D1); `--migrate-accounts` и перенос `planner.db` (X1–X3); `install.ps1 -AdminEmail` (I7) | `accounts.db` — новая миграция `Users`; схема данных пользователя **не меняется** | unit/API: мигратор (T5), сверка, повторный запуск, прерванный перенос; `PlannerApiFactory` — папка данных вместо файла; весь прежний набор зелёный | **да**: сначала `--migrate-accounts` на копии → отчёт Анне (числа до/после) → `install.ps1 -AdminEmail` |
| 2 L+S+M+C+Q. Вход | `Planner.Core/Accounts` (правила кода, частоты — чистые функции); `LoginCodes`, `Sessions`; cookie-аутентификация, Data Protection в `keys\`; `FallbackPolicy`, `X-Planner` + Origin (C), rate limiting, заголовки; `IEmailSender` (`Folder`, `Smtp`), `/dev/mailbox`; `/api/dev/login` (Development); клиент: экран входа, окно входа поверх экрана, 401 в `ConnectionQueue` (Q1–Q5), «Выйти»; `/privacy` (P4) | `accounts.db`: `LoginCodes`, `Sessions` | T1 (все маршруты закрыты), T2 (А/Б по всем маршрутам — таблица примеров), T3 (код, попытки, срок, частота, CSRF, сессии); `ApiTestBase` входит сам; e2e: `lib/nav.mjs` входит через `/api/dev/login`, новые `login.mjs`, `session-expired.mjs` | **да**: Анна входит кодом из почтового ящика разработки |
| 3 I+U+N. Приглашения и пользователи | `Invites`; режим `Invite`/`Open` (I1, I6); экран «Пользователи и приглашения» (U1–U4, I2–I5); стартовый набор нового пользователя (N1–N2) | `accounts.db`: `Invites`; `Users.IsDisabled` | API: приглашения (T3), отключение — сессии отозваны, `Open`; стартовый набор; T1/T2 — новые маршруты в таблице; e2e: `invite.mjs` | да |
| 4 P. Аккаунт | «Аккаунт» в «⚙ Настройки»: почта, устройства, выйти везде (S4–S5, P1); выгрузка JSON (P2); удаление — «корзина аккаунта» 7 дней, восстановление входом, стирание по сроку (P3); `/privacy` про сроки (P4) | `Users.DeleteAfter` | API: выгрузка содержит всё и только своё, удаление убирает файл и сессии, единственный администратор не удаляется; e2e: `account.mjs` | да |
| 5. Финал | полный e2e (≈60 сценариев) на текущем коде; README (папка данных, вход, почта, SMTP, бэкап/восстановление снимка); CLAUDE.md §1, §2, §5 + раздел «Аккаунты»; manual-checklist | — | 3 полных прогона `dotnet test`; полный e2e | да (контрольная) |

## Затрагиваемые места кода (оценка)

- **Без изменений**: `Planner.Core/Services/*`, `Planner.Core/Entities/*`, репозитории `EfRepositories.cs`, все миграции данных.
- **Сервер**: `Program.cs` (аутентификация, политики, лимиты, заголовки), `DataServiceCollectionExtensions` (папка данных, контекст по пользователю, мигратор),
  `BackupService` (снимок папкой), `SeedData` → стартовый набор пользователя, `BackupEndpoints` (D6), новые `AccountEndpoints`, `AdminEndpoints`, `Data/Accounts/*`.
- **Клиент**: `PlannerApi` (заголовок `X-Planner`, 401), `ConnectionQueue` (401 = ожидание входа), новые `Pages/Login`, `Invite`, `Privacy`,
  окна «Аккаунт», «Пользователи и приглашения» в «⚙ Настройки».
- **Тесты**: `PlannerApiFactory`/`ApiTestBase` (папка данных, вход), `MigrationTests` (контекст на файл), новые `Accounts/*`; e2e `lib/nav.mjs`.
- **Скрипты**: `backup.ps1`, `install.ps1` (ключ, `-AdminEmail`, снимок), `e2e.ps1` (папка данных вместо файла).

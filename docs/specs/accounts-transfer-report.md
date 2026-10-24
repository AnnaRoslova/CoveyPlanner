# Перенос данных Анны в аккаунт администратора — отчёты сверки

Спека: [accounts.md](accounts.md), правила X1–X3. Оба прогона — на **копиях** базы (свежая копия сервером через `tools/backup.ps1`),
командой `Planner.Server.exe --MigrateAccounts:Report=… --Storage:DataDirectory=<копия> --Accounts:AdminEmail=… --Storage:DailyBackup=false` —
это тот же код, что выполняет сервер при первом запуске. «Сумма» — SHA-256 всех значений таблицы (первые 12 знаков), строки — по всем столбцам.
«Строк после запуска» — в файле администратора после миграций.

## 1. Первая копия (2026-10-22, почта-заглушка)


- Когда: 2026-10-22 22:03:43
- Администратор: anna@example.com
- Откуда: `<копия 1>\planner.db`
- Куда: `<копия 1>\users\8fc9df0efa034429a0b8b2076423c557.db`
- Итог: **всё совпадает** ✓

| Таблица | Строк до | Строк после | Сумма до | Сумма после | | Строк после запуска |
|---|---:|---:|---|---|---|---:|
| AppSettings | 1 | 1 | 958c11eae1e3 | 958c11eae1e3 | ✓ | 1 |
| Categories | 16 | 16 | c6a508d0fad8 | c6a508d0fad8 | ✓ | 16 |
| GoalRoles | 69 | 69 | dddb82284a1d | dddb82284a1d | ✓ | 69 |
| GoalSeries | 10 | 10 | 5d38be22c519 | 5d38be22c519 | ✓ | 10 |
| GoalSeriesRoles | 11 | 11 | fdc50160fa6a | fdc50160fa6a | ✓ | 11 |
| Roles | 10 | 10 | f3833bd67578 | f3833bd67578 | ✓ | 10 |
| TaskGoals | 176 | 176 | 064e808c6337 | 064e808c6337 | ✓ | 176 |
| TaskRoles | 285 | 285 | 229990a5b6d3 | 229990a5b6d3 | ✓ | 285 |
| Tasks | 364 | 364 | 2eab32af6add | 2eab32af6add | ✓ | 364 |
| VacationHabits | 0 | 0 | e3b0c44298fc | e3b0c44298fc | ✓ | 0 |
| Vacations | 0 | 0 | e3b0c44298fc | e3b0c44298fc | ✓ | 0 |
| WeeklyGoals | 69 | 69 | 9fc0fded649e | 9fc0fded649e | ✓ | 69 |
| __EFMigrationsHistory | 22 | 22 | f17ddce7c594 | f17ddce7c594 | ✓ | 22 |
| __EFMigrationsLock | 0 | 0 | e3b0c44298fc | e3b0c44298fc | ✓ | 0 |
| sqlite_sequence | 5 | 5 | 4af15dd5b257 | 4af15dd5b257 | ✓ | 5 |

Всего строк: 1038 → 1038.
После запуска (миграции файла пользователя): 1038 строк.

## 2. Свежая копия перед установкой (2026-10-24, почта администратора)


- Когда: 2026-10-24 04:18:43
- Администратор: h***@gmail.com
- Откуда: `<копия 2>\planner.db`
- Куда: `<копия 2>\users\1f3d2441e24e486b9deb38ab2678fda0.db`
- Итог: **всё совпадает** ✓

| Таблица | Строк до | Строк после | Сумма до | Сумма после | | Строк после запуска |
|---|---:|---:|---|---|---|---:|
| AppSettings | 1 | 1 | 958c11eae1e3 | 958c11eae1e3 | ✓ | 1 |
| Categories | 16 | 16 | c6a508d0fad8 | c6a508d0fad8 | ✓ | 16 |
| GoalRoles | 81 | 81 | b87c2a3d62a7 | b87c2a3d62a7 | ✓ | 81 |
| GoalSeries | 10 | 10 | 4ff78375c3d1 | 4ff78375c3d1 | ✓ | 10 |
| GoalSeriesRoles | 11 | 11 | fdc50160fa6a | fdc50160fa6a | ✓ | 11 |
| Roles | 10 | 10 | f3833bd67578 | f3833bd67578 | ✓ | 10 |
| TaskGoals | 184 | 184 | bd2b1b67da6f | bd2b1b67da6f | ✓ | 184 |
| TaskRoles | 294 | 294 | 3661178fa73b | 3661178fa73b | ✓ | 294 |
| Tasks | 374 | 374 | a21c2fd8227c | a21c2fd8227c | ✓ | 374 |
| VacationHabits | 0 | 0 | e3b0c44298fc | e3b0c44298fc | ✓ | 0 |
| Vacations | 0 | 0 | e3b0c44298fc | e3b0c44298fc | ✓ | 0 |
| WeeklyGoals | 81 | 81 | 3c57c84897e8 | 3c57c84897e8 | ✓ | 81 |
| __EFMigrationsHistory | 22 | 22 | f17ddce7c594 | f17ddce7c594 | ✓ | 22 |
| __EFMigrationsLock | 0 | 0 | e3b0c44298fc | e3b0c44298fc | ✓ | 0 |
| sqlite_sequence | 5 | 5 | d2d16824281c | d2d16824281c | ✓ | 5 |

Всего строк: 1089 → 1089.
После запуска (миграции файла пользователя): 1089 строк.

## 3. Установка на настоящих данных (2026-10-24)

`tools/backup.ps1` → перенос на свежей копии (раздел 2) → `tools/install.ps1 -AdminEmail h***@gmail.com`. Сервер при запуске сделал снимок
`backups\planner-2026-10-24_041956-перед-обновлением` (с `planner.db` внутри) и перенёс данные: отчёт в папке данных — **всё совпадает** ✓,
**1089 → 1089** строк; `planner.db` → `planner-before-accounts.db`.

## 4. Сверка миграций этапов 2–4 на свежей копии (2026-10-24)

Снимок `backups\planner-2026-10-24_064133-вручную` → копия → `Planner.Server.exe --CheckMigrations:Report=…`: прежние таблицы и столбцы до и после миграций.

### accounts.db

**всё совпадает** ✓ · строк 1 → 1 · новое: Invites.Id, Invites.TokenHash, Invites.Email, Invites.CreatedAt, Invites.ExpiresAt, Invites.UsedAt, Invites.UsedByUserId, Invites.RevokedAt, LoginCodes.Id, LoginCodes.Email, LoginCodes.CodeHash, LoginCodes.CreatedAt, LoginCodes.ExpiresAt, LoginCodes.Attempts, LoginCodes.UsedAt, Sessions.Id, Sessions.UserId, Sessions.Device, Sessions.CreatedAt, Sessions.LastSeenAt, Sessions.ExpiresAt, Sessions.RevokedAt, Users.DeleteAfter, Users.IsDisabled, sqlite_sequence.name, sqlite_sequence.seq

| Таблица | Строк до | Строк после | |
|---|---:|---:|---|
| Users | 1 | 1 | ✓ |

### users\611076ed959e403d97b82b70a38673c0.db

**всё совпадает** ✓ · строк 1067 → 1067

| Таблица | Строк до | Строк после | |
|---|---:|---:|---|
| AppSettings | 1 | 1 | ✓ |
| Categories | 16 | 16 | ✓ |
| GoalRoles | 81 | 81 | ✓ |
| GoalSeries | 10 | 10 | ✓ |
| GoalSeriesRoles | 11 | 11 | ✓ |
| Roles | 10 | 10 | ✓ |
| TaskGoals | 184 | 184 | ✓ |
| TaskRoles | 294 | 294 | ✓ |
| Tasks | 374 | 374 | ✓ |
| VacationHabits | 0 | 0 | ✓ |
| Vacations | 0 | 0 | ✓ |
| WeeklyGoals | 81 | 81 | ✓ |
| sqlite_sequence | 5 | 5 | ✓ |


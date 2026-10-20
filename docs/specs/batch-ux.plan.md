# План: пачка UX (разделы 1–9)

Спека: [batch-ux.md](batch-ux.md) · макет: [batch-ux-mockup.html](batch-ux-mockup.html) · журнал: [batch-ux.metrics.md](batch-ux.metrics.md).
Перед ней — [ui-declutter.plan.md](ui-declutter.plan.md) (этапы 0–4). **Код — после одного «ок» Анны** на обе спеки, дальше — без остановок.
Каждый этап: `dotnet build` без предупреждений, `dotnet test` и e2e (`tools/e2e.ps1`) зелёные, коммит (номера правил), push,
`tools/backup.ps1`, миграция — сначала на свежей копии со сверкой чисел, `tools/install.ps1`. Рабочий сервер :5070 — только эти два скрипта.
⚠-решения — в metrics, пункты «что сделать → что получится» — в manual-checklist.md, CLAUDE.md — в коммите этапа.

| Этап | Что | Модель / миграция | Тесты |
|---|---|---|---|
| 1 BP | вкладки «Мысли · N \| Проекты · N» (`SomedayPanel`, `RightPanelState`), окно мысли без «+ шаг» + «🛠 Сделать проектом», пустая вкладка, плашки BP2/BP5, 🛠 у проекта везде | — | `project-tabs.mjs` (новый), `projects`, `feed-done`, `steps`, `project-field` |
| 2 BG | словарь: правка слов по таблице BG2 во всём клиенте; экран «Как это устроено» (словарь, SVG-схема, пояснения из D5, клавиши) | — | `glossary.mjs` (новый) + правка текстов в e2e |
| 3 BI | экран «📈 Итоги» с вкладками (каркас над `HabitsScreen`, `DoneScreen`, `ReportScreen`), «⚙ Настройки» (тема), шапка 📈 ⚙ | — | `totals.mjs` (новый), `habits`, `done-screen`, `report`, `themes`, `themes-contrast` |
| 4 BD | дашборд: `GET /api/dashboard`, `BestWeekStreak` в `HabitStatsCalculator`, компоненты-графики (SVG/CSS) | — | unit: лучшая серия; API: дашборд; `dashboard.mjs` (новый), контраст |
| 5 BS | `UpdatedAt`/`KeptAt`, `AppSettings` (срок), метка возраста, плашка раз в день, режим ленты «давно лежат», срок в ⚙ | **`StaleThoughts`**: `Tasks.UpdatedAt` (= `CreatedAt`), `Tasks.KeptAt`, таблица `AppSettings`; сверка на копии + отчёт «сколько сразу старых» | unit/API: кто меняет `UpdatedAt`, «Оставить»; миграция; `stale.mjs` (новый) |
| 6 BK | `js/hotkeys.js` + сервис `Hotkeys`, быстрый поиск (`GET /api/search`), подсказки клавиш | — | API: поиск; `hotkeys.mjs` (новый) |
| 7 BN | очередь изменений в `PlannerState` (порядок, повтор 2/5/10/30 с, `online`, фокус), плашка «нет связи», `beforeunload` | — | unit: очередь (клиент); `offline.mjs` (новый, перехват запросов) |
| 8 BR | экран «Недельный обзор» (5 шагов), напоминание воскресенья, вход из «Итогов» и W | — | `weekly-review.mjs` (новый) |
| 9 BM | «Моя миссия», строки ролей, подсказка на роли, блок в левой панели, шаг 4 обзора | **`Mission`**: `AppSettings.Mission`, `AppSettings.ShowMissionInPanel`, `Roles.Vision`; сверка на копии | API: настройки и роли; миграция; `mission.mjs` (новый) |

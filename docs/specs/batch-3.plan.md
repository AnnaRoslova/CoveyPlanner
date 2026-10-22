# План: пачка 3 (плавающие тесты, переход к дате, «Пять колец», мелочи)

Спека: [batch-3.md](batch-3.md) · журнал: [batch-3.metrics.md](batch-3.metrics.md). «Ок» на спеку не ждём (решения Анны — в задании), этапы — подряд.
Каждый этап: `dotnet build` без предупреждений, `dotnet test` и e2e (`tools/e2e.ps1`) зелёные, коммит (номера правил), push,
`tools/backup.ps1`, миграция (если есть) — сначала на свежей копии со сверкой чисел, `tools/install.ps1`. Рабочий сервер :5070 — только эти два скрипта.
⚠-решения — в metrics, пункты «что сделать → что получится» — в manual-checklist.md (раздел «batch-3»), CLAUDE.md — в коммите этапа.

| Этап | Что | Модель / миграция | Тесты |
|---|---|---|---|
| 1 FT | причина падений; `TestDatabase.ClearPool` вместо `ClearAllPools`; часы сервера в `PlannerApiFactory` (FT1) | — | стресс-тест (временный, не в git); 3 полных прогона подряд |
| 2 J | `DateQuery` (клиент), `GET /api/calendar` (`GetBusyDaysAsync`), `WeekJump` (Popover под подписью недели), G в `hotkeys.js`, пункт «перейти к неделе» в `SearchBox` | — | unit: `DateQueryTests`; API: `CalendarApiTests`; e2e: `week-jump.mjs` (новый), `hotkeys` |
| 3 G | шрифты Kurale, PT Serif (+ Caveat 700); темы `earth`, `water`, `void` в `themes.css` и `ThemeState`; `GorinDecor` (SVG-символы и места); танзаку, круг кистью | — | `themes-contrast` (+3 темы), `themes`; снимки → `batch-3-shots/` |
| 4 M | M1 «×» у редких полей и возврат в «+ …»; M2 дни цели — в подсказку и окно; M3 «+» у роли, «дела роли на неделе» в «⋯»; M4 разделы-папки | — | e2e: `small-fixes.mjs` (новый), правка `declutter`, `goal-fold`, `role-tasks` и др. |

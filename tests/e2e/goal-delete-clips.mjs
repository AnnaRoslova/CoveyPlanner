// Этап 10 (docs/specs/tasks-steps-done.md: C1–C3, S28; e2e п. 24): удаление цели привычки не уносит скрепки.
// Прошлая неделя: занятие отмечено галочкой цели (без дня) → цель удалили «Только на этой неделе» →
// дело встало в понедельник той недели, выполнено; счёт 🫙 не изменился. Данные — через API (чистая база), удаление — мышью.
import puppeteer from 'puppeteer-core';

const BASE = process.env.PLANNER_URL ?? 'http://localhost:5071/';
const SHOTS = process.argv[2] ?? '.';
const results = [];
const consoleErrors = [];
const browser = await puppeteer.launch({
  executablePath: process.env.PLANNER_BROWSER ?? 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  headless: true, defaultViewport: { width: 1600, height: 1000 },
});
const page = await browser.newPage();
page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text()); });
page.on('pageerror', e => consoleErrors.push(e.message));
const sleep = ms => new Promise(r => setTimeout(r, ms));
const settle = () => sleep(600);
function assert(c, m) { if (!c) throw new Error(m); }
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-goal-delete-clips-${results.length}.png` }); }
}
const api = (method, path, body) => page.evaluate(async (m, p, b) => {
  const r = await fetch(`/api/${p}`, { method: m, headers: { 'Content-Type': 'application/json' }, body: b ? JSON.stringify(b) : undefined });
  if (!r.ok) throw new Error(`${m} ${p}: ${r.status} ${await r.text()}`);
  const text = await r.text();
  return text ? JSON.parse(text) : null;
}, method, path, body);
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day'); await settle(); }

const iso = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
const now = new Date();
const monday = new Date(now); monday.setDate(now.getDate() - ((now.getDay() + 6) % 7));
const dayIso = i => { const d = new Date(monday); d.setDate(monday.getDate() + i); return iso(d); };
const today = iso(now);
const GOAL = 'Бег';

await load();
const body = (await api('GET', 'board')).roles.find(r => r.name === 'Тело');
const run = await api('POST', 'goals', { roleId: body.id, weekStart: dayIso(-7), title: GOAL, isRecurring: true });
await api('POST', `goals/${run.id}/toggle`, { today });
const total = async () => (await api('GET', `habits?today=${today}`)).habits.find(h => h.id === run.seriesId)?.stats.total;
await load();

await check('24. прошлая неделя: занятие отмечено галочкой цели (без дня); удалили цель «Только на этой неделе» — дело в понедельнике, выполнено; 🫙 тот же', async () => {
  const goal = (await api('GET', `week/${dayIso(-7)}`)).goals.find(g => g.id === run.id);
  assert(goal.sessions.length === 1 && goal.sessions[0].isDone && goal.sessions[0].plannedDate === null, JSON.stringify(goal.sessions));
  const sessionId = goal.sessions[0].taskId;
  const before = await total();
  assert(before === 1, `скрепок до удаления: ${before}`);

  await page.click('.nav[title="Предыдущая неделя"]'); await settle();
  const row = (await page.evaluateHandle(t => [...document.querySelectorAll('.role .item')].find(i => i.querySelector('.title')?.textContent.trim() === t), GOAL)).asElement();
  assert(row, 'нет цели на прошлой неделе');
  await row.hover();
  await (await row.$('.row-action[title="Удалить цель"]')).click(); await settle();
  await page.evaluate(() => [...document.querySelectorAll('.confirm-delete .btn')].find(b => b.textContent.trim() === 'Только на этой неделе').click());
  await settle();
  await sleep(6500); // удаление уходит на сервер через 6 с

  const task = await api('GET', `tasks/${sessionId}`);
  assert(task.state === 'Done' && task.plannedDate === dayIso(-7), `дело: ${JSON.stringify(task)}`);
  const mon = await page.evaluate(() => [...document.querySelectorAll('.day')[0].querySelectorAll(':scope > .item')].map(i => ({ t: i.querySelector('.title').textContent.trim(), done: i.classList.contains('done') })));
  assert(mon.some(x => x.t === GOAL && x.done), `понедельник: ${JSON.stringify(mon)}`);
  assert(await total() === before, `скрепок после удаления: ${await total()}`);
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);

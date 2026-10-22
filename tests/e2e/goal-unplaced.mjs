// N1 (docs/specs/next-batch.md, N1.1–N1.7): подсветка неразложенного — у цели не все занятия стоят в днях недели.
// «не разложено: 2 из 2» → бросили на день → «1 из 2» → на второй день — подсветки нет; занятие в корзину — вернулась, «Вернуть» — исчезла;
// галочка цели закрыла норму — нет; прошлая неделя — нет; «свернуть все цели» — остаётся. Данные — через API (чистая база).
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
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-goal-unplaced-${results.length}.png` }); }
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

/** Строка цели в левой панели: подсвечена ли и её подсказка. */
const goalState = title => page.evaluate(t => {
  const el = [...document.querySelectorAll('.role .item')].find(i => i.querySelector('.title')?.textContent.trim() === t);
  return el ? { unplaced: el.classList.contains('unplaced'), hint: el.getAttribute('title') } : null;
}, title);
const toastAction = text => page.evaluate(t => [...document.querySelectorAll('.toast button.action')].find(b => b.textContent.trim() === t)?.click(), text);
async function dragGoalToDay(title, dayIndex) {
  const el = (await page.evaluateHandle(t => [...document.querySelectorAll('.role .item')].find(i => i.querySelector('.title')?.textContent.trim() === t), title)).asElement();
  const from = await el.boundingBox();
  const day = await (await page.evaluateHandle(i => document.querySelectorAll('.day')[i], dayIndex)).asElement().boundingBox();
  const sx = from.x + 40, sy = from.y + 10, tx = day.x + day.width / 2, ty = day.y + day.height - 80;
  await page.mouse.move(sx, sy); await page.mouse.down();
  for (let i = 1; i <= 16; i++) { await page.mouse.move(sx + (tx - sx) * i / 16, sy + (ty - sy) * i / 16); await sleep(16); }
  await sleep(200); await page.mouse.up(); await settle();
}

await load();
const work = (await api('GET', 'board')).roles.find(r => r.name === 'Работа');
const goal = await api('POST', 'goals', { roleId: work.id, weekStart: dayIso(0), title: 'Отчёт', isRecurring: false });
await api('PUT', `goals/${goal.id}`, { title: 'Отчёт', roleIds: [work.id], timesPerWeek: 2, startTime: null, quadrant: null, notes: null, durationMinutes: null });
await load();

await check('N1.1, N1.3: цель «2 раза» без занятий — подсвечена, «не разложено: 2 из 2»; на день — «1 из 2»; на второй — подсветки нет', async () => {
  let s = await goalState('Отчёт');
  assert(s?.unplaced && s.hint.endsWith('не разложено: 2 из 2'), JSON.stringify(s));
  await (await page.$('.roles-panel')).screenshot({ path: `${SHOTS}/goal-unplaced.png` });
  await dragGoalToDay('Отчёт', 1);
  s = await goalState('Отчёт');
  assert(s?.unplaced && s.hint.endsWith('не разложено: 1 из 2'), JSON.stringify(s));
  await dragGoalToDay('Отчёт', 3);
  s = await goalState('Отчёт');
  assert(s && !s.unplaced && !s.hint.includes('не разложено'), JSON.stringify(s));
});

await check('N1: занятие в корзину — подсветка вернулась; «Вернуть» — исчезла', async () => {
  const session = (await api('GET', `week/${dayIso(0)}`)).goals.find(g => g.id === goal.id).sessions[0];
  const row = (await page.evaluateHandle(id => [...document.querySelectorAll('.day .item')].find(i => i.dataset.id === String(id)), session.taskId)).asElement();
  await row.hover(); await sleep(200);
  await (await row.$('.trash-btn')).click(); await settle();
  assert((await goalState('Отчёт')).unplaced, 'нет подсветки после удаления занятия');
  await toastAction('Вернуть'); await settle();
  assert(!(await goalState('Отчёт')).unplaced, 'подсветка осталась после «Вернуть»');
});

await check('N1: галочка цели (выполненное без дня) закрыла норму — подсветки нет', async () => {
  const g2 = await api('POST', 'goals', { roleId: work.id, weekStart: dayIso(0), title: 'Звонок', isRecurring: false });
  await load();
  assert((await goalState('Звонок')).unplaced, 'новая цель не подсвечена');
  await api('POST', `goals/${g2.id}/toggle`, { today: iso(now) });
  await load();
  assert(!(await goalState('Звонок')).unplaced, 'выполненная цель подсвечена');
});

await check('N1.3: «свернуть все цели» — подсветка и подсказка остаются', async () => {
  await api('POST', 'goals', { roleId: work.id, weekStart: dayIso(0), title: 'Планёрка', isRecurring: false });
  await load();
  await page.click('.head-tools .fold'); await settle();
  const s = await goalState('Планёрка');
  assert(s?.unplaced && s.hint.endsWith('не разложено: 1 из 1'), JSON.stringify(s));
  await page.click('.head-tools .fold'); await settle();
});

await check('N1.4: прошлая неделя — подсветки нет', async () => {
  await api('POST', 'goals', { roleId: work.id, weekStart: dayIso(-7), title: 'Старое', isRecurring: false });
  await page.click('.nav[title^="Предыдущая неделя"]'); await settle();
  const s = await goalState('Старое');
  assert(s && !s.unplaced, JSON.stringify(s));
  await page.click('.nav[title^="Следующая неделя"]'); await settle();
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);

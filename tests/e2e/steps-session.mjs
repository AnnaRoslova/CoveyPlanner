// Баги ручной проверки этапа 6 (docs/specs/tasks-steps-done.metrics.md): занятие цели с шагами —
// 1) отметка одного шага не должна отмечать само дело (S13: дело меняется само только по S12), удаление шагов — тоже;
// 2) «▸» раскрывает шаги в дне — и у занятия цели, и у обычного дела (клик по стрелке, а не по чекбоксу).
// Данные создаются здесь же через API (чистая база).
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
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-steps-session-${results.length}.png` }); }
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

const rowEl = (i, title) => page.evaluateHandle((i, t) =>
  [...document.querySelectorAll('.day')[i].querySelectorAll(':scope > .item')]
    .find(it => it.querySelector('.title')?.textContent.trim() === t), i, title);
const rowInfo = (i, title) => page.evaluate((i, t) => {
  const it = [...document.querySelectorAll('.day')[i].querySelectorAll(':scope > .item')]
    .find(x => x.querySelector('.title')?.textContent.trim() === t);
  return it ? { done: it.classList.contains('done'), project: it.querySelector('.project-label')?.textContent.trim() ?? null } : null;
}, i, title);
const stepsShown = i => page.evaluate(i => [...document.querySelectorAll('.day')[i].querySelectorAll('.steps .item .title')].map(t => t.textContent.trim()), i);

/** Клик мышью по «▸» строки — в его видимой точке (центр значка), как это делает человек. */
async function clickFold(i, title) {
  const fold = await (await rowEl(i, title)).asElement().$('.fold');
  assert(fold, 'нет «▸»');
  const b = await fold.boundingBox();
  await page.mouse.click(b.x + b.width / 2, b.y + b.height / 2);
  await settle();
}

await load();
const roles = (await api('GET', 'board')).roles;
const work = roles.find(r => r.name === 'Работа');
const goal = await api('POST', 'goals', { roleId: work.id, weekStart: dayIso(0), title: 'Подготовка к собесу', isRecurring: false });
const session = (await api('POST', `goals/${goal.id}/schedule`, { kind: 'day', date: dayIso(1), index: 0 })).task;
const plain = await api('POST', 'tasks', { title: 'Ремонт', target: { kind: 'day', date: dayIso(2), index: 0 } });
await api('POST', `tasks/${plain.id}/steps`, { title: 'Обои' });
await load();

await check('занятие цели: два шага через окно дела — «☑ 0/2», дело не отмечено', async () => {
  await (await (await rowEl(1, 'Подготовка к собесу')).asElement().$('.tx')).click(); await settle();
  for (const t of ['Задачи', 'Резюме']) {
    await page.type('.editor .step-new', t); await page.keyboard.press('Enter'); await settle();
  }
  await page.keyboard.press('Escape'); await settle();
  const info = await rowInfo(1, 'Подготовка к собесу');
  assert(info?.project === '☑ 0/2' && !info.done, JSON.stringify(info));
});

await check('баг 2: «▸» у занятия цели раскрывает шаги (клик мышью по значку)', async () => {
  await clickFold(1, 'Подготовка к собесу');
  assert(JSON.stringify(await stepsShown(1)) === '["Задачи","Резюме"]', JSON.stringify(await stepsShown(1)));
  assert(!(await rowInfo(1, 'Подготовка к собесу')).done, 'клик по «▸» отметил дело');
});

await check('баг 2: «▸» у обычного дела раскрывает шаги', async () => {
  await clickFold(2, 'Ремонт');
  assert(JSON.stringify(await stepsShown(2)) === '["Обои"]', JSON.stringify(await stepsShown(2)));
  assert(!(await rowInfo(2, 'Ремонт')).done, 'клик по «▸» отметил дело');
});

await check('баг 1: отметили один шаг в окне занятия — дело не отмечено, «☑ 1/2»', async () => {
  await (await (await rowEl(1, 'Подготовка к собесу')).asElement().$('.tx')).click(); await settle();
  await page.click('.editor .step-ck'); await settle();
  await page.keyboard.press('Escape'); await settle();
  const info = await rowInfo(1, 'Подготовка к собесу');
  assert(info?.project === '☑ 1/2' && !info.done, JSON.stringify(info));
  const server = await api('GET', `tasks/${session.id}`);
  assert(server.state === 'Planned', `на сервере: ${server.state}`);
});

await check('баг 1: удалили все шаги — дело не отмечено', async () => {
  await (await (await rowEl(1, 'Подготовка к собесу')).asElement().$('.tx')).click(); await settle();
  while (await page.$('.editor .steps .row-action')) { await page.click('.editor .steps .row-action'); await settle(); }
  await page.keyboard.press('Escape'); await settle();
  const info = await rowInfo(1, 'Подготовка к собесу');
  assert(info && !info.done && info.project === null, JSON.stringify(info));
  assert((await api('GET', `tasks/${session.id}`)).state === 'Planned', 'на сервере отмечено');
});

await check('«▸» отдельно от чекбокса: вся стрелка — стрелка, зона чекбокса до неё не доходит', async () => {
  await load();
  const g = await page.evaluate(() => {
    const it = [...document.querySelectorAll('.day > .item')].find(x => x.querySelector('.fold'));
    const f = it.querySelector('.fold').getBoundingClientRect();
    const c = it.querySelector('.ck-hit').getBoundingClientRect();
    const misses = [];
    for (let x = Math.ceil(f.left) + 2; x < f.right - 2; x++) for (let y = Math.ceil(f.top) + 2; y < f.bottom - 2; y += 3) {
      const el = document.elementFromPoint(x, y); if (!el?.closest('.fold')) misses.push(`${x - f.left|0},${y - f.top|0}:${el?.className}`);
    }
    return { gap: f.left - c.right, misses: misses.length, where: misses.join(' ') };
  });
  assert(g.gap >= 0 && g.misses === 0, JSON.stringify(g));
});

await check('клик по «☑ 0/1» раскрывает и сворачивает шаги', async () => {
  const label = await (await rowEl(2, 'Ремонт')).asElement().$('.project-label');
  const was = (await stepsShown(2)).length;
  await label.click(); await settle();
  assert((await stepsShown(2)).length !== was, 'не переключилось');
  assert(!(await rowInfo(2, 'Ремонт')).done, 'отметилось дело');
});

await check('длинное название проекта в дне: обрезано на 2 строках, через ~0,5 с — подсказка с полным текстом (clamp.js)', async () => {
  const long = 'Очень длинное название проекта, которое точно не помещается в две строки узкой колонки дня';
  const p = await api('POST', 'tasks', { title: long, target: { kind: 'day', date: dayIso(4), index: 0 } });
  await api('POST', `tasks/${p.id}/steps`, { title: 'Шаг' });
  await load();
  await page.mouse.move(5, 5); await settle();
  const row = (await rowEl(4, long)).asElement();
  assert(await row.$eval('.title', t => t.hasAttribute('data-long')), 'нет data-long');
  const t = await row.$eval('.title', e => e.getBoundingClientRect().toJSON());
  await page.mouse.move(t.left + 30, t.top + 6); await sleep(900);
  const tip = await page.evaluate(() => document.querySelector('.clamp-tip')?.isConnected ? document.querySelector('.clamp-tip').textContent : null);
  assert(tip === long, `подсказка: ${tip}`);
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);

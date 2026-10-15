// N2 (docs/specs/next-batch.md, N2.1–N2.7): дело на дело = шаг. Середина строки — «сделать шагом «…»» (строка подсвечена, подпись),
// край — порядок (линия вставки). Плашка «„…“ теперь шаг „…“ · Вернуть» — на прежнее место. Запреты N2.4. Данные — через API (чистая база).
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
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-dnd-make-step-${results.length}.png` }); }
}
const api = (method, path, body) => page.evaluate(async (m, p, b) => {
  const r = await fetch(`/api/${p}`, { method: m, headers: { 'Content-Type': 'application/json' }, body: b ? JSON.stringify(b) : undefined });
  if (!r.ok) throw new Error(`${m} ${p}: ${r.status} ${await r.text()}`);
  const text = await r.text();
  return text ? JSON.parse(text) : null;
}, method, path, body);
async function load() {
  await page.goto(BASE, { waitUntil: 'networkidle0' });
  await page.evaluate(() => localStorage.setItem('planner.right-mode', 'feed'));
  await page.goto(BASE, { waitUntil: 'networkidle0' });
  await page.waitForSelector('.day'); await settle();
}

const iso = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
const now = new Date();
const monday = new Date(now); monday.setDate(now.getDate() - ((now.getDay() + 6) % 7));
const dayIso = i => { const d = new Date(monday); d.setDate(monday.getDate() + i); return iso(d); };

const dayTitles = i => page.evaluate(i => [...document.querySelectorAll('.day')[i].querySelectorAll(':scope > .item')].map(it => it.querySelector('.title').textContent.trim()), i);
const dayRow = (i, title) => page.evaluateHandle((i, t) => [...document.querySelectorAll('.day')[i].querySelectorAll(':scope > .item')].find(it => it.querySelector('.title').textContent.trim() === t), i, title);
const feed = name => page.evaluate(n => {
  const cat = [...document.querySelectorAll('.cat')].find(c => c.querySelector('.cat-h .name')?.textContent.trim() === n);
  return cat ? [...cat.querySelectorAll('.cat-b > .item .title')].map(t => t.textContent.trim()) : null;
}, name);
const feedRow = (name, title) => page.evaluateHandle((n, t) => {
  const cat = [...document.querySelectorAll('.cat')].find(c => c.querySelector('.cat-h .name')?.textContent.trim() === n);
  return [...cat.querySelectorAll('.cat-b > .item')].find(i => i.querySelector('.title')?.textContent.trim() === t);
}, name, title);
const toast = () => page.evaluate(() => document.querySelector('.toast')?.textContent.replace(/\s+/g, ' ').trim() ?? null);
const toastAction = t => page.evaluate(t => [...document.querySelectorAll('.toast button.action')].find(b => b.textContent.trim() === t)?.click(), t);

/**
 * Перетащить запись from на строку to: part — доля высоты строки-цели (0.5 — середина, 0.1 — верхний край).
 * Перед отпусканием — что подсвечено: строка (drop-hover и её подпись) и видна ли линия вставки.
 */
async function dragOnto(from, to, part = 0.5) {
  const a = await from.boundingBox(), b = await to.boundingBox();
  const sx = a.x + a.width / 2, sy = a.y + Math.min(10, a.height / 2), tx = b.x + b.width / 2, ty = b.y + b.height * part;
  await page.mouse.move(sx, sy); await page.mouse.down();
  for (let i = 1; i <= 16; i++) { await page.mouse.move(sx + (tx - sx) * i / 16, sy + (ty - sy) * i / 16); await sleep(16); }
  await sleep(200);
  const seen = await to.evaluate(el => ({
    hover: el.classList.contains('drop-hover'),
    hint: el.classList.contains('drop-hover') ? getComputedStyle(el, '::after').content : null,
    line: !document.querySelector('.drop-line')?.hidden,
  }));
  await page.mouse.up(); await settle();
  return seen;
}

await load();
const ideas = await api('POST', 'categories', { name: 'Идеи' });
for (const t of ['Кисти', 'Краска', 'Обои']) await api('POST', 'tasks', { title: t, target: { kind: 'someday', categoryId: ideas.id, index: 99 } });
for (const t of ['Ремонт', 'Плитка', 'Звонок']) await api('POST', 'tasks', { title: t, target: { kind: 'day', date: dayIso(1), index: 99 } });
const move = await api('POST', 'tasks', { title: 'Переезд', target: { kind: 'day', date: dayIso(2), index: 0 } });
await api('POST', `tasks/${move.id}/steps`, { title: 'Коробки' });
const work = (await api('GET', 'board')).roles.find(r => r.name === 'Работа');
const goal = await api('POST', 'goals', { roleId: work.id, weekStart: dayIso(0), title: 'Отчёт', isRecurring: false });
await api('POST', `goals/${goal.id}/schedule`, { kind: 'day', date: dayIso(3), index: 0 });
await load();

await check('N2.1, N2.3: дело из дня на середину другого — подсветка строки и «сделать шагом «Ремонт»»; шаг, «↳ Ремонт», «☑ 0/1»', async () => {
  const seen = await dragOnto((await dayRow(1, 'Плитка')).asElement(), (await dayRow(1, 'Ремонт')).asElement(), 0.5);
  assert(seen.hover && seen.hint.includes('сделать шагом «Ремонт»') && !seen.line, JSON.stringify(seen));
  const rows = await page.evaluate(() => [...document.querySelectorAll('.day')[1].querySelectorAll(':scope > .item')].map(i => ({
    t: i.querySelector('.title').textContent.trim(), parent: i.querySelector('.parent-of span')?.textContent.trim() ?? null,
    label: i.querySelector('.project-label')?.textContent.trim() ?? null })));
  assert(rows.find(r => r.t === 'Плитка')?.parent === 'Ремонт' && rows.find(r => r.t === 'Ремонт')?.label === '☑ 0/1', JSON.stringify(rows));
  assert((await toast())?.includes('«Плитка» теперь шаг «Ремонт»'), JSON.stringify(await toast()));
});

await check('N2.6: «Вернуть» — снова отдельное дело на прежнем месте, «Ремонт» — снова не проект', async () => {
  await toastAction('Вернуть'); await settle();
  assert(JSON.stringify(await dayTitles(1)) === '["Ремонт","Плитка","Звонок"]', JSON.stringify(await dayTitles(1)));
  const repair = (await api('GET', `week/${dayIso(0)}`)).tasks.find(t => t.title === 'Ремонт');
  assert(!repair.isProject, 'Ремонт остался проектом');
});

await check('N2.1, N6: мысль на середину мысли — шаг; та стала проектом и ушла в «🛠 Проекты»; «Вернуть» — обе на прежних местах', async () => {
  await dragOnto((await feedRow('Идеи', 'Краска')).asElement(), (await feedRow('Идеи', 'Кисти')).asElement(), 0.5);
  assert(JSON.stringify(await feed('Идеи')) === '["Обои"]', JSON.stringify(await feed('Идеи')));
  const brushes = (await api('GET', 'board')).somedayTasks.find(t => t.title === 'Кисти');
  assert(brushes.isProject && (await api('GET', `tasks/${brushes.id}/steps`)).some(s => s.title === 'Краска'), JSON.stringify(brushes));
  assert((await toast())?.includes('🛠 Проекты'), JSON.stringify(await toast()));
  await toastAction('Вернуть'); await settle();
  assert(JSON.stringify(await feed('Идеи')) === '["Кисти","Краска","Обои"]', JSON.stringify(await feed('Идеи')));
});

await check('N2.2: на верхний край строки — порядок (линия вставки), шагом не стала', async () => {
  const seen = await dragOnto((await dayRow(1, 'Звонок')).asElement(), (await dayRow(1, 'Ремонт')).asElement(), 0.12);
  assert(!seen.hover && seen.line, JSON.stringify(seen));
  assert(JSON.stringify(await dayTitles(1)) === '["Звонок","Ремонт","Плитка"]', JSON.stringify(await dayTitles(1)));
  const call = (await api('GET', `week/${dayIso(0)}`)).tasks.find(t => t.title === 'Звонок');
  assert(call.parentId === null, 'стал шагом');
});

await check('N2.4: на занятие цели и с делом-проектом — середина не подсвечивается, шагом не становится', async () => {
  let seen = await dragOnto((await dayRow(1, 'Звонок')).asElement(), (await dayRow(3, 'Отчёт')).asElement(), 0.5);
  assert(!seen.hint?.includes('сделать шагом'), JSON.stringify(seen));
  assert((await api('GET', `week/${dayIso(0)}`)).tasks.find(t => t.title === 'Звонок').parentId === null, 'стал шагом занятия');
  seen = await dragOnto((await dayRow(2, 'Переезд')).asElement(), (await dayRow(1, 'Ремонт')).asElement(), 0.5);
  assert(!seen.hover, `проект подсветил середину: ${JSON.stringify(seen)}`);
  assert((await api('GET', `tasks/${move.id}`)).parentId === null, 'проект стал шагом');
});

await check('N2.5: шаг на строку своего проекта — «снять с дня — в список шагов»: шаг снят с дня, остался шагом', async () => {
  const boxes = (await api('GET', `tasks/${move.id}/steps`))[0];
  await api('POST', `tasks/${boxes.id}/move`, { kind: 'day', date: dayIso(4), index: 0 });
  await load();
  const project = (await page.evaluateHandle(() => [...document.querySelectorAll('.day .item')].find(i => i.querySelector('.title').textContent.trim() === 'Переезд'))).asElement();
  const seen = await dragOnto((await dayRow(4, 'Коробки')).asElement(), project, 0.5);
  assert(seen.hover && seen.hint.includes('снять с дня'), JSON.stringify(seen));
  const step = await api('GET', `tasks/${boxes.id}`);
  assert(step.plannedDate === null && step.parentId === move.id, JSON.stringify(step));
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);

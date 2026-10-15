// Этап 7 (docs/specs/tasks-steps-done.md: D1–D7, S21, поиск по шагам, плашка у корзины; e2e п. 12–13):
// галочка у каждой мысли ленты, отметка — мысль уходит, плашка «выполнено · в корзину · вернуть» у значка корзины;
// «вернуть» — на прежнее место; «в корзину» — в корзину, оттуда — снова «выполнено в ленте»; поиск находит шаг — проект развёрнут;
// шаг из раскрытого списка в другую категорию — стал мыслью, плашка «больше не шаг · Вернуть». Данные — через API (чистая база).
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
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-feed-done-${results.length}.png` }); }
}
const api = (method, path, body) => page.evaluate(async (m, p, b) => {
  const r = await fetch(`/api/${p}`, { method: m, headers: { 'Content-Type': 'application/json' }, body: b ? JSON.stringify(b) : undefined });
  if (!r.ok) throw new Error(`${m} ${p}: ${r.status} ${await r.text()}`);
  const text = await r.text();
  return text ? JSON.parse(text) : null;
}, method, path, body);
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.someday'); await settle(); }

/** Названия мыслей категории по порядку (без шагов раскрытых проектов). */
const feed = name => page.evaluate(n => {
  const cat = [...document.querySelectorAll('.cat')].find(c => c.querySelector('.cat-h .name')?.textContent.trim() === n);
  return cat ? [...cat.querySelectorAll('.cat-b > .item .title')].map(t => t.textContent.trim()) : null;
}, name);
const feedRow = (name, title) => page.evaluateHandle((n, t) => {
  const cat = [...document.querySelectorAll('.cat')].find(c => c.querySelector('.cat-h .name')?.textContent.trim() === n);
  return [...cat.querySelectorAll('.cat-b > .item')].find(i => i.querySelector('.title')?.textContent.trim() === t);
}, name, title);
const toast = () => page.evaluate(() => {
  const t = document.querySelector('.toast');
  if (!t) return null;
  const r = t.getBoundingClientRect();
  const feed = document.querySelector('.someday').getBoundingClientRect();
  const b = document.querySelector('.someday .trash').getBoundingClientRect();
  const coversTrash = !(r.right <= b.left || r.left >= b.right || r.bottom <= b.top || r.top >= b.bottom);
  return { text: t.textContent.replace(/\s+/g, ' ').trim(), nearTrash: t.classList.contains('near-trash'), overFeed: r.left + r.width / 2 > feed.left, coversTrash };
});
const toastAction = text => page.evaluate(t => [...document.querySelectorAll('.toast button.action')].find(b => b.textContent.trim() === t)?.click(), text);

await load();
const ideas = await api('POST', 'categories', { name: 'Идеи' });
const shop = await api('POST', 'categories', { name: 'Покупки' });
for (const t of ['А', 'Б', 'В']) await api('POST', 'tasks', { title: t, target: { kind: 'someday', categoryId: ideas.id, index: 99 } });
const repair = await api('POST', 'tasks', { title: 'Ремонт', target: { kind: 'someday', categoryId: shop.id, index: 0 } });
await api('POST', `tasks/${repair.id}/steps`, { title: 'Купить краску' });
await api('POST', `tasks/${repair.id}/steps`, { title: 'Валики' });
await api('POST', 'tasks', { title: 'Хлеб', target: { kind: 'someday', categoryId: shop.id, index: 99 } });
// Шаг «Купить краску» стоит сегодня — его тащат из дня в ленту (S21): проекты в ленте не показываются (N6.2)
const today = await page.evaluate(() => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`; });
const paintStep = (await api('GET', `tasks/${repair.id}/steps`)).find(s => s.title === 'Купить краску');
await api('POST', `tasks/${paintStep.id}/move`, { kind: 'day', date: today, index: 0 });
await load();

await check('D1: у каждой мысли ленты — квадратный чекбокс; проекта «Ремонт» в ленте нет (N6.2)', async () => {
  const n = await page.$$eval('.someday .cat-b > .item', items => items.map(i => !!i.querySelector('.ck-hit')));
  assert(n.length >= 4 && n.every(Boolean), JSON.stringify(n));
  assert(!(await feed('Покупки')).includes('Ремонт'), JSON.stringify(await feed('Покупки')));
  await (await page.$('.someday')).screenshot({ path: `${SHOTS}/feed-done.png` });
});

await check('D2, D3: отметили «Б» — ушла из ленты; плашка «выполнено · в корзину · вернуть» у корзины; «вернуть» — на прежнее место (D6)', async () => {
  await (await (await feedRow('Идеи', 'Б')).asElement().$('.ck-hit')).click(); await settle();
  assert(JSON.stringify(await feed('Идеи')) === '["А","В"]', JSON.stringify(await feed('Идеи')));
  const t = await toast();
  assert(t?.text.includes('«Б» выполнено') && t.text.includes('в корзину') && t.text.includes('вернуть'), JSON.stringify(t));
  assert(t.nearTrash && t.overFeed && !t.coversTrash, `плашка не у корзины или закрывает её: ${JSON.stringify(t)}`);
  await toastAction('вернуть'); await settle();
  assert(JSON.stringify(await feed('Идеи')) === '["А","Б","В"]', JSON.stringify(await feed('Идеи')));
});

await check('«в корзину» на плашке — в корзине; вернули из корзины — снова «выполнено в ленте» (К20)', async () => {
  const b = (await api('GET', 'board')).somedayTasks.find(t => t.title === 'Б');
  await (await (await feedRow('Идеи', 'Б')).asElement().$('.ck-hit')).click(); await settle();
  await toastAction('в корзину'); await settle();
  assert((await toast())?.text.includes('Удалено «Б»'), JSON.stringify(await toast()));
  await sleep(6500); // удаление уходит на сервер
  assert((await api('GET', 'trash')).some(t => t.id === b.id), 'не в корзине');
  const restored = await api('POST', `trash/${b.id}/restore`);
  assert(restored.state === 'Done' && restored.plannedDate === null, JSON.stringify(restored));
  await load();
  assert(!(await feed('Идеи')).includes('Б'), 'вернулась в ленту, а не в «Выполненное»');
});

await check('поиск по шагу: «валик» — в ленте мыслей нет, строка «🛠 в проектах: 1» (N6.12)', async () => {
  await page.type('.someday .search input', 'валик'); await settle();
  const r = await page.evaluate(() => ({
    rows: [...document.querySelectorAll('.someday .cat-b > .item .title')].map(t => t.textContent.trim()),
    line: document.querySelector('.someday .in-projects')?.textContent.trim() ?? null,
  }));
  assert(r.rows.length === 0 && r.line === '🛠 в проектах: 1', JSON.stringify(r));
  await page.click('.someday .search input', { clickCount: 3 }); await page.keyboard.press('Backspace'); await settle();
});

await check('S21: шаг из дня на «Идеи» — стал мыслью там; плашка «больше не шаг · Вернуть» — снова шаг (и снова в дне)', async () => {
  const step = (await page.evaluateHandle(() => [...document.querySelectorAll('.day .item')].find(i => i.querySelector('.title').textContent.trim() === 'Купить краску'))).asElement();
  const from = await step.boundingBox();
  const header = await (await page.evaluateHandle(() => [...document.querySelectorAll('.cat-h')].find(h => h.querySelector('.name')?.textContent.trim() === 'Идеи'))).asElement().boundingBox();
  const sx = from.x + from.width / 2, sy = from.y + 8, tx = header.x + header.width / 2, ty = header.y + header.height / 2;
  await page.mouse.move(sx, sy); await page.mouse.down();
  for (let i = 1; i <= 14; i++) { await page.mouse.move(sx + (tx - sx) * i / 14, sy + (ty - sy) * i / 14); await sleep(16); }
  await sleep(150); await page.mouse.up(); await settle();
  assert((await feed('Идеи'))[0] === 'Купить краску', JSON.stringify(await feed('Идеи')));
  assert((await toast())?.text.includes('больше не шаг «Ремонт»'), JSON.stringify(await toast()));
  await toastAction('Вернуть'); await settle();
  assert(!(await feed('Идеи')).includes('Купить краску'), 'осталась мыслью');
  const steps = await api('GET', `tasks/${repair.id}/steps`);
  assert(steps.some(s => s.title === 'Купить краску' && s.plannedDate === today), JSON.stringify(steps));
});

await check('удаление — плашка «Удалено «…» · Вернуть» тоже у значка корзины', async () => {
  const row = (await feedRow('Идеи', 'А')).asElement();
  const b = await row.boundingBox();
  const trash = await (await page.$('.someday .trash')).boundingBox();
  const sx = b.x + b.width / 2, sy = b.y + 8, tx = trash.x + trash.width / 2, ty = trash.y + trash.height / 2;
  await page.mouse.move(sx, sy); await page.mouse.down();
  for (let i = 1; i <= 14; i++) { await page.mouse.move(sx + (tx - sx) * i / 14, sy + (ty - sy) * i / 14); await sleep(16); }
  await sleep(150); await page.mouse.up(); await settle();
  const t = await toast();
  assert(t?.text.includes('Удалено «А»') && t.nearTrash && t.overFeed && !t.coversTrash, JSON.stringify(t));
  await toastAction('Вернуть'); await settle();
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);

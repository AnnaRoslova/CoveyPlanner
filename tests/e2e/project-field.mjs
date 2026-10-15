// Этап 8 — карточка дела (docs/specs/tasks-steps-done.md: S6–S10, S25, 6.3; e2e п. 9–11): поле «Проект» с поиском в окне мысли и дела,
// неактивно у дела со своими шагами, без шагов и занятий в списке; шаги в окне — «☑ N/M», метка дня, клик — окно шага с «↳ проект»,
// 🗑 — в корзину с «Вернуть», порядок перетаскиванием; «N занятий ▸» у книги с занятием «Чтения». Данные — через API (чистая база).
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
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-project-field-${results.length}.png` }); }
}
const api = (method, path, body) => page.evaluate(async (m, p, b) => {
  const r = await fetch(`/api/${p}`, { method: m, headers: { 'Content-Type': 'application/json' }, body: b ? JSON.stringify(b) : undefined });
  if (!r.ok) throw new Error(`${m} ${p}: ${r.status} ${await r.text()}`);
  const text = await r.text();
  return text ? JSON.parse(text) : null;
}, method, path, body);
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day'); await settle(); }
async function closeEditor() {
  for (let i = 0; i < 3 && await page.$('.editor'); i++) { await page.keyboard.press('Escape'); await settle(); }
}

const iso = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
const now = new Date();
const monday = new Date(now); monday.setDate(now.getDate() - ((now.getDay() + 6) % 7));
const dayIso = i => { const d = new Date(monday); d.setDate(monday.getDate() + i); return iso(d); };

const feedTitles = name => page.evaluate(n => {
  const cat = [...document.querySelectorAll('.cat')].find(c => c.querySelector('.cat-h .name')?.textContent.trim() === n);
  return cat ? [...cat.querySelectorAll('.cat-b > .item .title')].map(t => t.textContent.trim()) : null;
}, name);
async function openFeed(name, title) {
  const row = (await page.evaluateHandle((n, t) => {
    const cat = [...document.querySelectorAll('.cat')].find(c => c.querySelector('.cat-h .name')?.textContent.trim() === n);
    return [...cat.querySelectorAll('.cat-b > .item')].find(i => i.querySelector('.title')?.textContent.trim() === t);
  }, name, title)).asElement();
  await (await row.$('.title')).click(); await settle();
}
async function openDay(i, title) {
  const row = (await page.evaluateHandle((i, t) => [...document.querySelectorAll('.day')[i].querySelectorAll(':scope > .item')]
    .find(it => it.querySelector('.title')?.textContent.trim() === t), i, title)).asElement();
  await (await row.$('.title')).click(); await settle();
}
/** Выбрать проект в поле «Проект» окна (с поиском). */
async function chooseProject(label, search) {
  await page.click('.editor .project-row .select-field'); await sleep(300);
  if (search) { await page.type('.editor .popover .option-search', search); await sleep(200); }
  await page.evaluate(l => [...document.querySelectorAll('.editor .popover .option')].find(o => o.querySelector('.option-name')?.textContent.trim() === l).click(), label);
  await sleep(200);
}
const save = () => page.evaluate(() => [...document.querySelectorAll('.editor .btn')].find(b => b.textContent.trim() === 'Сохранить').click());
const windowSteps = () => page.$$eval('.editor .task-steps .steps .item', items => items.map(i => ({
  title: i.querySelector('.title').textContent.trim(), mark: i.querySelector('.day-mark')?.textContent.trim() ?? null,
})));
const toastText = () => page.evaluate(() => document.querySelector('.toast')?.textContent.replace(/\s+/g, ' ').trim() ?? null);

await load();
const ideas = await api('POST', 'categories', { name: 'Идеи' });
const repair = await api('POST', 'tasks', { title: 'Ремонт', target: { kind: 'day', date: dayIso(0), index: 0 } });
const walls = await api('POST', `tasks/${repair.id}/steps`, { title: 'Стены' });
await api('POST', `tasks/${repair.id}/steps`, { title: 'Полы' });
await api('POST', `tasks/${walls.id}/move`, { kind: 'day', date: dayIso(3), index: 0 });
await api('POST', 'tasks', { title: 'Краска', target: { kind: 'someday', categoryId: ideas.id, index: 0 } });
const book = await api('POST', 'tasks', { title: 'Атомные привычки', target: { kind: 'someday', categoryId: ideas.id, index: 1 }, isProject: true });
await load();

await check('9. мысль «Краска»: «Проект: Ремонт» (поиск) — ушла из ленты, в шагах «Ремонта»; «— нет» — снова в «Идеях»', async () => {
  await openFeed('Идеи', 'Краска');
  await chooseProject('Ремонт', 'рем');
  await save(); await settle();
  assert(!(await feedTitles('Идеи')).includes('Краска'), 'осталась в ленте');
  const steps = await api('GET', `tasks/${repair.id}/steps`);
  const paint = steps.find(s => s.title === 'Краска');
  assert(paint, JSON.stringify(steps.map(s => s.title)));
  // Окно шага: «↳ Ремонт», поле «Проект» — «Ремонт»; «— нет» — обратно в ленту
  await openDay(0, 'Ремонт');
  await page.evaluate(() => [...document.querySelectorAll('.editor .task-steps .steps .item')].find(i => i.querySelector('.title').textContent.trim() === 'Краска').querySelector('.title').click());
  await settle();
  assert((await page.$eval('.editor .step-of', e => e.textContent.trim())) === '↳ Ремонт', 'нет «↳ Ремонт»');
  await chooseProject('— нет');
  await save(); await settle();
  assert((await feedTitles('Идеи')).includes('Краска'), `не вернулась: ${JSON.stringify(await feedTitles('Идеи'))}`);
});

await check('10. у дела со своими шагами поле «Проект» неактивно; в списке проектов нет шагов и самого дела', async () => {
  await openDay(0, 'Ремонт');
  assert((await page.$eval('.editor .project-row', e => e.textContent)).includes('это проект'), 'поле активно');
  await closeEditor();
  await openFeed('Идеи', 'Краска');
  await page.click('.editor .project-row .select-field'); await sleep(300);
  const options = await page.$$eval('.editor .popover .option .option-name', os => os.map(o => o.textContent.trim()));
  assert(!options.includes('Стены') && !options.includes('Полы') && !options.includes('Краска') && options.includes('Ремонт'), JSON.stringify(options));
  await closeEditor();
});

await check('шаги в окне: «☑ 0/2», метка дня у «Стены»; клик — окно шага с «↳ Ремонт», клик по «↳» — окно «Ремонта»', async () => {
  await openDay(0, 'Ремонт');
  assert((await page.$eval('.editor .task-steps .hint', e => e.textContent.trim())) === '☑ 0/2', 'нет «☑ 0/2»');
  await (await page.$('.editor')).screenshot({ path: `${SHOTS}/task-window.png` });
  const steps = await windowSteps();
  assert(steps.find(s => s.title === 'Стены')?.mark, JSON.stringify(steps));
  await page.evaluate(() => [...document.querySelectorAll('.editor .task-steps .steps .item')].find(i => i.querySelector('.title').textContent.trim() === 'Стены').querySelector('.title').click());
  await settle();
  assert(await page.$eval('.editor .title-input', e => e.value) === 'Стены', 'не окно шага');
  await page.click('.editor .step-of'); await settle();
  assert(await page.$eval('.editor .title-input', e => e.value) === 'Ремонт', 'не окно проекта');
});

await check('порядок шагов в окне — перетаскиванием', async () => {
  const items = await page.$$('.editor .task-steps .steps .item');
  const from = await items[1].boundingBox();
  const to = await items[0].boundingBox();
  const sx = from.x + from.width / 2, sy = from.y + 8;
  await page.mouse.move(sx, sy); await page.mouse.down();
  for (let i = 1; i <= 12; i++) { await page.mouse.move(sx, sy + (to.y + 2 - sy) * i / 12); await sleep(16); }
  await sleep(150); await page.mouse.up(); await settle();
  assert(JSON.stringify((await windowSteps()).map(s => s.title)) === '["Полы","Стены"]', JSON.stringify(await windowSteps()));
  assert(JSON.stringify((await api('GET', `tasks/${repair.id}/steps`)).filter(s => s.title !== 'Краска').map(s => s.title)) === '["Полы","Стены"]', 'на сервере другой порядок');
});

await check('🗑 у шага в окне — в корзину с плашкой «Удалено «Полы» · Вернуть»; «Вернуть» — на месте', async () => {
  const row = (await page.evaluateHandle(() => [...document.querySelectorAll('.editor .task-steps .steps .item')].find(i => i.querySelector('.title').textContent.trim() === 'Полы'))).asElement();
  const b = await row.boundingBox();
  await page.mouse.move(b.x + b.width / 2, b.y + b.height / 2); await sleep(300);
  await (await row.$('.trash-btn')).click(); await settle();
  assert(!(await windowSteps()).some(s => s.title === 'Полы'), 'шаг остался');
  assert((await toastText())?.includes('Удалено «Полы»'), `плашка: ${await toastText()}`);
  await page.evaluate(() => [...document.querySelectorAll('.toast button.action')].find(b => b.textContent.trim() === 'Вернуть').click()); await settle();
  assert((await windowSteps()).some(s => s.title === 'Полы'), 'не вернулся');
  await closeEditor();
});

await check('11. занятие «Чтения» → «Проект: Атомные привычки»: у книги «1 занятие ▸» (после отметки); удалили книгу — занятие осталось, счёт 🫙 тот же', async () => {
  const mind = (await api('GET', 'board')).roles.find(r => r.name === 'Ум');
  const reading = await api('POST', 'goals', { roleId: mind.id, weekStart: dayIso(0), title: 'Чтение', isRecurring: true });
  const session = (await api('POST', `goals/${reading.id}/schedule`, { kind: 'day', date: dayIso(2), index: 0 })).task;
  await api('PUT', `tasks/${session.id}/done`, { isDone: true });
  await load();
  await openDay(2, 'Чтение');
  await chooseProject('Атомные привычки', 'атом');
  await save(); await settle();
  await page.click('.projects-b'); await settle(); // книга — проект, она в «🛠 Проекты» (N6)
  await (await (await page.evaluateHandle(() => [...document.querySelectorAll('.someday .pgroup > .item')].find(i => i.querySelector('.title').textContent.trim() === 'Атомные привычки'))).asElement().$('.title')).click();
  await settle();
  const line = await page.$eval('.editor .task-steps .sessions-line', e => e.textContent.trim());
  assert(line.startsWith('1 занятие'), `строка занятий: ${line}`);
  await page.click('.editor .task-steps .sessions-line'); await sleep(200);
  assert((await page.$eval('.editor .task-steps .session-row', e => e.textContent)).includes('Чтение'), 'нет занятия в списке');
  await closeEditor();
  const iso2 = iso(new Date());
  const before = (await api('GET', `habits?today=${iso2}`)).habits.find(h => h.id === reading.seriesId).stats.total;
  await api('DELETE', `tasks/${book.id}`);
  const after = await api('GET', `tasks/${session.id}`);
  assert(after.state === 'Done' && after.parentId === null && after.plannedDate === dayIso(2), JSON.stringify(after));
  assert((await api('GET', `habits?today=${iso2}`)).habits.find(h => h.id === reading.seriesId).stats.total === before, 'счёт привычки изменился');
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);

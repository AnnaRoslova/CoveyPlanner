// N6 «🛠 Проекты» (docs/specs/next-batch.md, N6.1–N6.16): проекты не в ленте, а в панели на её месте (кнопка 🛠 в шапке недели).
// «Сделать проектом» и первый «+ шаг» — с плашкой «теперь проект · 🛠 Проекты · Вернуть»; «+ проект» с фильтром категории;
// шаг из панели — в день; проект — в день и обратно в группу; 🛠 и полоска в дне; поиск ленты «🛠 в проектах: N»;
// проект цели (G3) — сразу в панели; «больше не проект». Данные — через API (чистая база).
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
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-projects-${results.length}.png` }); }
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
async function closeEditor() {
  for (let i = 0; i < 3 && await page.$('.editor'); i++) { await page.keyboard.press('Escape'); await settle(); }
}

const iso = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
const now = new Date();
const monday = new Date(now); monday.setDate(now.getDate() - ((now.getDay() + 6) % 7));
const dayIso = i => { const d = new Date(monday); d.setDate(monday.getDate() + i); return iso(d); };

/** Мысли ленты в категории (null — нет такой категории). */
const feed = name => page.evaluate(n => {
  const cat = [...document.querySelectorAll('.someday .cat')].find(c => c.querySelector('.cat-h .name')?.textContent.trim() === n);
  return cat ? [...cat.querySelectorAll('.cat-b > .item .title')].map(t => t.textContent.trim()) : null;
}, name);
const allFeedTitles = () => page.$$eval('.someday:not(.projects-mode) .item .title', ts => ts.map(t => t.textContent.trim()));
/** Панель проектов: группы — имя и проекты. */
const panel = () => page.evaluate(() => [...document.querySelectorAll('.someday.projects-mode .pgroup')].map(g => ({
  name: g.querySelector('.pg-h .name').textContent.trim(),
  projects: [...g.querySelectorAll(':scope > .item')].map(i => ({
    title: i.querySelector('.title').textContent.trim(), day: i.querySelector('.day-mark')?.textContent.trim() ?? null,
    label: i.querySelector('.project-label')?.textContent.trim() ?? null,
  })),
})));
const projectRow = title => page.evaluateHandle(t => [...document.querySelectorAll('.someday.projects-mode .pgroup > .item')]
  .find(i => i.querySelector('.title').textContent.trim() === t), title);
const dayRows = i => page.evaluate(i => [...document.querySelectorAll('.day')[i].querySelectorAll(':scope > .item')].map(it => ({
  title: it.querySelector('.title').textContent.trim(), project: it.classList.contains('is-project'),
  mark: !!it.querySelector('.proj-mark'), bar: it.querySelector('.proj-bar')?.style.getPropertyValue('--p') ?? null,
})), i);
const toast = () => page.evaluate(() => document.querySelector('.toast')?.textContent.replace(/\s+/g, ' ').trim() ?? null);
const toastAction = text => page.evaluate(t => [...document.querySelectorAll('.toast button.action')].find(b => b.textContent.trim() === t)?.click(), text);
const showProjects = async () => { if (!(await page.$('.someday.projects-mode'))) { await page.click('.projects-b'); await settle(); } };
const showFeed = async () => { if (await page.$('.someday.projects-mode')) { await page.click('.projects-b'); await settle(); } };
async function openFeed(cat, title) {
  const row = (await page.evaluateHandle((n, t) => [...[...document.querySelectorAll('.cat')].find(c => c.querySelector('.cat-h .name')?.textContent.trim() === n)
    .querySelectorAll('.cat-b > .item')].find(i => i.querySelector('.title').textContent.trim() === t), cat, title)).asElement();
  await (await row.$('.title')).click(); await settle();
  await page.waitForSelector('.editor', { timeout: 3000 });
}
async function drag(from, tx, ty) {
  const sx = from.x + from.width / 2, sy = from.y + Math.min(10, from.height / 2);
  await page.mouse.move(sx, sy); await page.mouse.down();
  for (let i = 1; i <= 16; i++) { await page.mouse.move(sx + (tx - sx) * i / 16, sy + (ty - sy) * i / 16); await sleep(16); }
  await sleep(200); await page.mouse.up(); await settle();
}

await load();
const ideas = await api('POST', 'categories', { name: 'Идеи' });
const home = await api('POST', 'categories', { name: 'Дом', emoji: '🏠' });
for (const t of ['Сад', 'Хлеб']) await api('POST', 'tasks', { title: t, target: { kind: 'someday', categoryId: ideas.id, index: 99 } });
const repair = await api('POST', 'tasks', { title: 'Ремонт', target: { kind: 'someday', categoryId: home.id, index: 0 } });
for (const t of ['Стены', 'Полы']) await api('POST', `tasks/${repair.id}/steps`, { title: t });
await load();

await check('N6.2: проект с шагами в ленте не показывается; кнопка 🛠 — панель «🛠 Проекты» вместо ленты, «Ремонт» в группе «Дом»', async () => {
  assert(!(await allFeedTitles()).includes('Ремонт'), 'проект в ленте');
  await showProjects();
  assert(!(await page.$('.someday .quick')) && await page.$('.someday.projects-mode'), 'лента не сменилась панелью');
  const p = await panel();
  const g = p.find(x => x.name === 'Дом');
  assert(g?.projects[0]?.title === 'Ремонт' && g.projects[0].label === '☑ 0/2', JSON.stringify(p));
  assert(await page.$eval('.projects-b', b => b.getAttribute('aria-pressed')) === 'true', 'кнопка не нажата');
  await (await page.$('.someday')).screenshot({ path: `${SHOTS}/projects.png` });
});

await check('N6.3: «🛠 сделать проектом» в окне мысли — мысль ушла из ленты, плашка «теперь проект»; «Вернуть» — снова мысль', async () => {
  await showFeed();
  await openFeed('Идеи', 'Сад');
  await page.click('.editor .project-toggle'); await settle();
  assert(!(await feed('Идеи')).includes('Сад'), 'осталась в ленте');
  assert((await toast())?.includes('«Сад» теперь проект'), JSON.stringify(await toast()));
  await closeEditor();
  await toastAction('Вернуть'); await settle();
  assert(JSON.stringify(await feed('Идеи')) === '["Сад","Хлеб"]', JSON.stringify(await feed('Идеи')));
});

await check('N6.3, N6.14: первый «+ шаг» у мысли — проект; «Вернуть» убирает и проект, и этот шаг', async () => {
  await openFeed('Идеи', 'Хлеб');
  await page.click('.editor .task-steps .add-step'); await sleep(200);
  await page.keyboard.type('Закваска'); await page.keyboard.press('Enter'); await settle();
  assert(!(await feed('Идеи')).includes('Хлеб'), 'осталась в ленте');
  assert((await toast())?.includes('«Хлеб» теперь проект'), JSON.stringify(await toast()));
  await closeEditor();
  await toastAction('Вернуть'); await settle();
  assert((await feed('Идеи')).includes('Хлеб'), 'не вернулась');
  const bread = (await api('GET', 'board')).somedayTasks.find(t => t.title === 'Хлеб');
  assert(!bread.isProject && (await api('GET', `tasks/${bread.id}/steps`)).length === 0, 'шаг или флаг остались');
});

await check('N6.11: «+ проект» с фильтром «Дом» — новый проект наверху группы «Дом»; фильтр оставляет одну группу', async () => {
  await showProjects();
  await page.click('.projects-tools .select-field'); await sleep(300);
  await page.evaluate(() => [...document.querySelectorAll('.projects-tools .popover .option')].find(o => o.textContent.includes('Дом')).click());
  await settle();
  await page.click('.add-project'); await sleep(200);
  await page.keyboard.type('Переезд'); await page.keyboard.press('Enter'); await settle();
  await page.keyboard.press('Escape'); await settle();
  const p = await panel();
  assert(p.length === 1 && p[0].name === 'Дом' && p[0].projects[0].title === 'Переезд', JSON.stringify(p));
  await page.click('.projects-tools .select-field'); await sleep(300);
  await page.evaluate(() => [...document.querySelectorAll('.projects-tools .popover .option')].find(o => o.textContent.includes('все категории')).click());
  await settle();
});

await check('N6.1, N6.10: шаг из развёрнутого проекта панели — в среду; в дне «↳ Ремонт», у проекта метка дня шага', async () => {
  const row = (await projectRow('Ремонт')).asElement();
  await (await row.$('.expand')).click(); await settle();
  const step = (await page.evaluateHandle(() => [...document.querySelectorAll('.someday.projects-mode .steps .item')].find(i => i.querySelector('.title').textContent.trim() === 'Стены'))).asElement();
  const wed = await (await page.evaluateHandle(() => document.querySelectorAll('.day')[2])).asElement().boundingBox();
  await drag(await step.boundingBox(), wed.x + wed.width / 2, wed.y + wed.height - 60);
  const rows = await page.evaluate(() => [...document.querySelectorAll('.day')[2].querySelectorAll(':scope > .item')].map(i => ({
    t: i.querySelector('.title').textContent.trim(), parent: i.querySelector('.parent-of span')?.textContent.trim() ?? null })));
  assert(rows.some(r => r.t === 'Стены' && r.parent === 'Ремонт'), JSON.stringify(rows));
});

await check('N6.10, N6.16: проект перетащили в пятницу — в дне 🛠 и полоска; в панели — с меткой дня; обратно в группу «Дом» — снят с дня', async () => {
  const row = (await projectRow('Ремонт')).asElement();
  const fri = await (await page.evaluateHandle(() => document.querySelectorAll('.day')[4])).asElement().boundingBox();
  await drag(await row.boundingBox(), fri.x + fri.width / 2, fri.y + fri.height - 60);
  await api('PUT', `tasks/${(await api('GET', `tasks/${repair.id}/steps`)).find(s => s.title === 'Полы').id}/done`, { isDone: true });
  await load(); await showProjects();
  const r = (await dayRows(4)).find(x => x.title === 'Ремонт');
  assert(r?.project && r.mark && r.bar === '50%', JSON.stringify(r));
  const inPanel = (await panel()).find(g => g.name === 'Дом').projects.find(x => x.title === 'Ремонт');
  assert(inPanel?.day, `нет метки дня: ${JSON.stringify(inPanel)}`);
  await (await page.$('.day')).screenshot({ path: `${SHOTS}/projects-day.png` }).catch(() => {});
  // Обратно в панель: проект из пятницы — на заголовок группы «Дом»
  const dayRow = (await page.evaluateHandle(() => [...document.querySelectorAll('.day')[4].querySelectorAll(':scope > .item')].find(i => i.querySelector('.title').textContent.trim() === 'Ремонт'))).asElement();
  const head = await (await page.evaluateHandle(() => [...document.querySelectorAll('.pgroup')].find(g => g.querySelector('.pg-h .name').textContent.trim() === 'Дом').querySelector('.pg-h'))).asElement().boundingBox();
  await drag(await dayRow.boundingBox(), head.x + head.width / 2, head.y + head.height / 2);
  const task = await api('GET', `tasks/${repair.id}`);
  assert(task.state === 'Someday' && task.plannedDate === null && task.categoryId === home.id && task.isProject, JSON.stringify(task));
});

await check('N6.12: поиск ленты «пол» — мыслей нет, строка «🛠 в проектах: 1» открывает панель с тем же поиском', async () => {
  await showFeed();
  await page.type('.someday .search input', 'пол'); await settle();
  const line = await page.$eval('.someday .in-projects', b => b.textContent.trim());
  assert(line === '🛠 в проектах: 1', line);
  await page.click('.someday .in-projects'); await settle();
  assert(await page.$eval('.someday.projects-mode .search input', i => i.value) === 'пол', 'поиск не перенёсся');
  const p = await panel();
  assert(p.length === 1 && p[0].projects.length === 1 && p[0].projects[0].title === 'Ремонт', JSON.stringify(p));
  await page.click('.someday.projects-mode .search input', { clickCount: 3 }); await page.keyboard.press('Escape'); await settle();
});

await check('N6.4, N6.15: «+ шаг» в окне цели без проекта — проект сразу в «🛠 Проекты» (без категории), не в ленте; плашка', async () => {
  const work = (await api('GET', 'board')).roles.find(r => r.name === 'Работа');
  await api('POST', 'goals', { roleId: work.id, weekStart: dayIso(0), title: 'Собес', isRecurring: false });
  await load();
  const goalEl = (await page.evaluateHandle(() => [...document.querySelectorAll('.role .item')].find(i => i.querySelector('.title')?.textContent.trim() === 'Собес'))).asElement();
  await (await goalEl.$('.title')).click(); await settle();
  await page.focus('.editor .goal-steps .inline-input').catch(async () => { await page.click('.editor .goal-steps .add-step'); });
  await sleep(200);
  await page.keyboard.type('Резюме'); await page.keyboard.press('Enter'); await settle();
  assert((await toast())?.includes('Проект «Собес» — в 🛠 Проектах'), JSON.stringify(await toast()));
  await closeEditor();
  assert(!(await allFeedTitles()).includes('Собес'), 'проект цели в ленте');
  await showProjects();
  const g = (await panel()).find(x => x.name === 'без категории');
  assert(g?.projects.some(x => x.title === 'Собес'), JSON.stringify(await panel()));
});

await check('N6.7: у проекта без шагов в окне — «🛠 больше не проект» → снова мысль в своей категории', async () => {
  const row = (await projectRow('Переезд')).asElement();
  await (await row.$('.title')).click(); await settle();
  await page.click('.editor .project-toggle'); await settle();
  await closeEditor();
  await showFeed();
  assert((await feed('Дом')).includes('Переезд'), JSON.stringify(await feed('Дом')));
});

await check('N6.13: поле «Проект» в окне мысли предлагает только проекты', async () => {
  await openFeed('Идеи', 'Сад');
  await page.click('.editor .project-row .select-field'); await sleep(300);
  const options = await page.$$eval('.editor .popover .option .option-name', os => os.map(o => o.textContent.trim()));
  assert(options.includes('Ремонт') && options.includes('Собес') && !options.includes('Хлеб') && !options.includes('Переезд'), JSON.stringify(options));
  await closeEditor();
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await page.evaluate(() => localStorage.setItem('planner.right-mode', 'feed'));
await browser.close();
console.log(results.join('\n'));
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);

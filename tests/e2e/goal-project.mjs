// Этап 10 (docs/specs/tasks-steps-done.md: P1–P9, G4; e2e п. 20–23): «Проект» в окне цели.
// Новые занятия цели — шаги выбранного проекта (у занятия своего проекта «↳» нет, G4); постоянная цель — тот же проект на следующей неделе;
// смена проекта — только для новых занятий; проект выполнен — новые занятия без проекта, в поле пометка «(выполнен)».
// Данные — через API (чистая база), выбор проекта и бросок цели на день — мышью.
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
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-goal-project-${results.length}.png` }); }
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
const BOOK = 'Атомные привычки';
const GOAL = 'Чтение';

async function openGoal(title) {
  const el = (await page.evaluateHandle(t => [...document.querySelectorAll('.role .item')].find(i => i.querySelector('.title')?.textContent.trim() === t), title)).asElement();
  assert(el, `нет цели ${title}`);
  await (await el.$('.title')).click(); await settle();
  await page.waitForSelector('.editor[aria-label="Цель недели"]', { timeout: 3000 });
}
async function chooseProject(label, search) {
  await page.click('.editor .project-row .select-field'); await sleep(300);
  if (search) { await page.type('.editor .popover .option-search', search); await sleep(200); }
  await page.evaluate(l => [...document.querySelectorAll('.editor .popover .option')].find(o => o.querySelector('.option-name')?.textContent.trim() === l).click(), label);
  await sleep(200);
}
const projectField = () => page.$eval('.editor .project-row .select-label', e => e.textContent.trim());
const save = () => page.evaluate(() => [...document.querySelectorAll('.editor .btn')].find(b => b.textContent.trim() === 'Сохранить').click());
const dayRow = (i, title) => page.evaluateHandle((i, t) =>
  [...document.querySelectorAll('.day')[i].querySelectorAll(':scope > .item')].find(it => it.querySelector('.title')?.textContent.trim() === t), i, title);
const goalOf = async week => (await api('GET', `week/${week}`)).goals.find(g => g.title === GOAL);
const newSession = (goal, date) => api('POST', `goals/${goal.id}/schedule`, { kind: 'day', date, index: 99 }).then(r => r.task);

await load();
const books = await api('POST', 'categories', { name: 'Книги' });
const book = await api('POST', 'tasks', { title: BOOK, target: { kind: 'someday', categoryId: books.id, index: 0 } });
const mind = (await api('GET', 'board')).roles.find(r => r.name === 'Ум');
const reading = await api('POST', 'goals', { roleId: mind.id, weekStart: dayIso(0), title: GOAL, isRecurring: true });
await load();

let wedSession;
await check('20. окно цели: «Проект: Атомные привычки»; цель брошена на среду — занятие стало шагом книги (без «↳», G4); отметили — у книги «1 занятие»', async () => {
  await openGoal(GOAL);
  await chooseProject(BOOK, 'атом');
  assert(await projectField() === BOOK, `поле: ${await projectField()}`);
  await save(); await settle();
  const goalEl = (await page.evaluateHandle(t => [...document.querySelectorAll('.role .item')].find(i => i.querySelector('.title')?.textContent.trim() === t), GOAL)).asElement();
  const from = await goalEl.boundingBox();
  const wed = await (await page.evaluateHandle(() => document.querySelectorAll('.day')[2])).asElement().boundingBox();
  const sx = from.x + 40, sy = from.y + 10, tx = wed.x + wed.width / 2, ty = wed.y + 140;
  await page.mouse.move(sx, sy); await page.mouse.down();
  for (let i = 1; i <= 16; i++) { await page.mouse.move(sx + (tx - sx) * i / 16, sy + (ty - sy) * i / 16); await sleep(16); }
  await sleep(200); await page.mouse.up(); await settle();
  const g = await goalOf(dayIso(0));
  assert(g.sessions.length === 1 && g.sessions[0].plannedDate === dayIso(2), JSON.stringify(g.sessions));
  wedSession = await api('GET', `tasks/${g.sessions[0].taskId}`);
  assert(wedSession.parentId === book.id, `занятие не шаг книги: ${JSON.stringify(wedSession)}`);
  const row = (await dayRow(2, GOAL)).asElement();
  assert(row, 'нет занятия в среде');
  assert(!(await row.$('.parent-of')), 'у занятия своего проекта есть «↳» (G4)');
  await (await row.$('.ck-hit')).click(); await settle();
  await (await (await page.evaluateHandle(t => [...document.querySelectorAll('.someday .cat-b > .item')].find(i => i.querySelector('.title')?.textContent.trim() === t), BOOK)).asElement().$('.title')).click();
  await settle();
  const line = await page.$eval('.editor .task-steps .sessions-line', e => e.textContent.trim());
  assert(line.startsWith('1 занятие'), `строка занятий: ${line}`);
  await closeEditor();
});

await check('21. постоянная цель: на следующей неделе — тот же проект; новое занятие там — шаг книги', async () => {
  const next = await goalOf(dayIso(7));
  assert(next?.projectId === book.id, JSON.stringify(next));
  const s = await newSession(next, dayIso(8));
  assert(s.parentId === book.id, JSON.stringify(s));
});

await check('22. следующая неделя: «— нет» — старые занятия остались шагами, новое — без проекта; цель этой недели — с книгой', async () => {
  await page.click('.nav[title="Следующая неделя"]'); await settle();
  await openGoal(GOAL);
  await chooseProject('— нет');
  await save(); await settle();
  const next = await goalOf(dayIso(7));
  assert(!next.projectId, `у цели следующей недели проект: ${next.projectId}`);
  const s = await newSession(next, dayIso(10));
  assert(s.parentId === null, JSON.stringify(s));
  const older = next.sessions.filter(x => x.taskId !== s.id);
  for (const o of older) assert((await api('GET', `tasks/${o.taskId}`)).parentId === book.id, 'старое занятие перестало быть шагом');
  assert((await api('GET', `tasks/${wedSession.id}`)).parentId === book.id, 'занятие этой недели перестало быть шагом');
  assert((await goalOf(dayIso(0))).projectId === book.id, 'у цели этой недели сменился проект (P4: прошлые недели не меняются)');
  await page.click('.nav[title="Предыдущая неделя"]'); await settle();
});

await check('23. книга выполнена — новое занятие этой недели без проекта; в окне цели — «(выполнен)»', async () => {
  await api('PUT', `tasks/${book.id}/done`, { isDone: true });
  const g = await goalOf(dayIso(0));
  const s = await newSession(g, dayIso(4));
  assert(s.parentId === null, JSON.stringify(s));
  await load();
  await openGoal(GOAL);
  const label = await projectField();
  assert(label.includes(BOOK) && label.includes('выполнен'), `поле: ${label}`);
  await closeEditor();
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);

// N4 (docs/specs/next-batch.md, вариант Б): «→ на следующую неделю (без дня)». Кнопка в окне дела и мысли; дело исчезло из дня/ленты,
// видно слева на следующей неделе «на неделе: 1» под своей ролью, без роли — «на неделе без роли»; перетаскивание оттуда в день;
// бросок на «на неделе» роли; «›» + роль = камень, дело из дня теряет день (исправление дыры). Данные — через API (чистая база).
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
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-next-week-${results.length}.png` }); }
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
/** «На неделе» под ролью (null — без роли): подпись и названия дел. */
const weekBlock = role => page.evaluate(r => {
  const block = r
    ? [...document.querySelectorAll('.role')].find(x => x.querySelector('.role-h .name')?.textContent.trim() === r)?.querySelector('.week-nd')
    : [...document.querySelectorAll('.week-nd')].find(b => b.querySelector('.wnd-h')?.textContent.includes('без роли'));
  if (!block) return null;
  return { empty: block.classList.contains('empty'), head: block.querySelector('.wnd-h').textContent.replace(/\s+/g, ' ').trim(),
    tasks: [...block.querySelectorAll('.item .title')].map(t => t.textContent.trim()) };
}, role);
const toast = () => page.evaluate(() => document.querySelector('.toast')?.textContent.replace(/\s+/g, ' ').trim() ?? null);
const toastAction = t => page.evaluate(t => [...document.querySelectorAll('.toast button.action')].find(b => b.textContent.trim() === t)?.click(), t);
async function drag(from, tx, ty) {
  const a = await from.boundingBox();
  const sx = a.x + a.width / 2, sy = a.y + Math.min(10, a.height / 2);
  await page.mouse.move(sx, sy); await page.mouse.down();
  for (let i = 1; i <= 16; i++) { await page.mouse.move(sx + (tx - sx) * i / 16, sy + (ty - sy) * i / 16); await sleep(16); }
  await sleep(200); await page.mouse.up(); await settle();
}

await load();
const family = (await api('GET', 'board')).roles.find(r => r.name === 'Семья');
const work = (await api('GET', 'board')).roles.find(r => r.name === 'Работа');
const gift = await api('POST', 'tasks', { title: 'Купить подарок', target: { kind: 'day', date: dayIso(1), index: 0 } });
await api('PUT', `tasks/${gift.id}`, { title: 'Купить подарок', notes: null, roleIds: [family.id], quadrant: null, startTime: null, durationMinutes: null });
const ideas = await api('POST', 'categories', { name: 'Идеи' });
await api('POST', 'tasks', { title: 'Шкаф', target: { kind: 'someday', categoryId: ideas.id, index: 0 } });
await load();

await check('N4.1: окно дела — «→ след. неделя, без дня» → дело ушло из дня; плашка «Перенесено на неделю … · Показать»', async () => {
  await page.evaluate(() => [...document.querySelectorAll('.day')[1].querySelectorAll(':scope > .item')].find(i => i.querySelector('.title').textContent.trim() === 'Купить подарок').querySelector('.title').click());
  await settle();
  await page.click('.editor .chip.next-week'); await sleep(200);
  assert((await page.$eval('.editor .date-hint', e => e.textContent)).includes('без дня'), 'нет подсказки');
  await page.evaluate(() => [...document.querySelectorAll('.editor .btn')].find(b => b.textContent.trim() === 'Сохранить').click()); await settle();
  assert(!(await dayTitles(1)).includes('Купить подарок'), 'осталось в дне');
  assert((await toast())?.includes('Перенесено на неделю'), JSON.stringify(await toast()));
});

await check('N4.2: «Показать» — следующая неделя: слева под «Семьёй» «на неделе: 1» с делом; в днях его нет', async () => {
  await toastAction('Показать'); await settle();
  const b = await weekBlock('Семья');
  assert(b && !b.empty && b.head.startsWith('на неделе: 1') && JSON.stringify(b.tasks) === '["Купить подарок"]', JSON.stringify(b));
  for (let i = 0; i < 7; i++) assert(!(await dayTitles(i)).includes('Купить подарок'), `в дне ${i}`);
  await (await page.$('.roles-panel')).screenshot({ path: `${SHOTS}/next-week.png` });
});

await check('N4.2: из «на неделе» перетащили в среду — встало в день, блок опустел', async () => {
  const row = (await page.evaluateHandle(() => [...document.querySelectorAll('.week-nd .item')].find(i => i.querySelector('.title').textContent.trim() === 'Купить подарок'))).asElement();
  const wed = await (await page.evaluateHandle(() => document.querySelectorAll('.day')[2])).asElement().boundingBox();
  await drag(row, wed.x + wed.width / 2, wed.y + wed.height - 60);
  assert((await dayTitles(2)).includes('Купить подарок'), JSON.stringify(await dayTitles(2)));
  assert((await weekBlock('Семья'))?.empty, JSON.stringify(await weekBlock('Семья')));
});

await check('N4.1: мысль без роли — «→ на след. неделю» в окне мысли — на следующей неделе в «на неделе без роли»', async () => {
  await page.click('.nav[title="Предыдущая неделя"]'); await settle();
  await page.evaluate(() => [...document.querySelectorAll('.cat-b > .item')].find(i => i.querySelector('.title').textContent.trim() === 'Шкаф').querySelector('.title').click());
  await settle();
  await page.click('.editor .next-week'); await settle();
  assert(!(await page.$$eval('.cat-b > .item .title', ts => ts.map(t => t.textContent.trim()))).includes('Шкаф'), 'осталась в ленте');
  await page.click('.nav[title="Следующая неделя"]'); await settle();
  const b = await weekBlock(null);
  assert(b && JSON.stringify(b.tasks) === '["Шкаф"]', JSON.stringify(b));
});

await check('N4.3: дело из дня бросили на «на неделе» роли (пустой виден при перетаскивании) — без дня, получило роль', async () => {
  const call = await api('POST', 'tasks', { title: 'Звонок', target: { kind: 'day', date: dayIso(10), index: 0 } });
  await load(); await page.click('.nav[title="Следующая неделя"]'); await settle();
  const row = (await page.evaluateHandle(() => [...document.querySelectorAll('.day .item')].find(i => i.querySelector('.title').textContent.trim() === 'Звонок'))).asElement();
  const a = await row.boundingBox();
  await page.mouse.move(a.x + a.width / 2, a.y + 8); await page.mouse.down();
  await page.mouse.move(a.x + a.width / 2 - 30, a.y + 20, { steps: 4 }); await sleep(100);
  const zone = await page.evaluate(() => {
    const role = [...document.querySelectorAll('.role')].find(x => x.querySelector('.role-h .name')?.textContent.trim() === 'Работа');
    const r = role.querySelector('.week-nd').getBoundingClientRect();
    return { x: r.x + r.width / 2, y: r.y + r.height / 2, h: r.height };
  });
  assert(zone.h > 10, `пустой блок не виден при перетаскивании: ${JSON.stringify(zone)}`);
  for (let i = 1; i <= 16; i++) { await page.mouse.move(a.x + (zone.x - a.x) * i / 16, a.y + (zone.y - a.y) * i / 16); await sleep(16); }
  await sleep(200); await page.mouse.up(); await settle();
  const t = await api('GET', `tasks/${call.id}`);
  assert(t.plannedDate === null && t.plannedWeek === dayIso(7) && t.roleIds.includes(work.id), JSON.stringify(t));
  assert((await weekBlock('Работа'))?.tasks.includes('Звонок'), JSON.stringify(await weekBlock('Работа')));
});

await check('N4: дыра закрыта — дело из дня, ставшее целью другой недели, теряет день', async () => {
  const report = await api('POST', 'tasks', { title: 'Отчёт', target: { kind: 'day', date: dayIso(1), index: 0 } });
  await api('POST', 'goals/from-task', { taskId: report.id, roleId: work.id, weekStart: dayIso(7), index: 0 });
  const t = await api('GET', `tasks/${report.id}`);
  assert(t.plannedDate === null && t.goalId, JSON.stringify(t));
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);

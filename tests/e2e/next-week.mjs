// «→ на след. неделю» (docs/specs/next-batch.md, N4; доработка — docs/specs/fixes-2026-10.md, B1–B5): заметная кнопка в окнах дела и мысли;
// дело с ролью — на следующей неделе слева под ролью (список не свёрнут), плашка «… → след. неделя, «Роль» · Показать · Вернуть»;
// без роли — меню ролей (и «в ленту» у дела); бросок на заголовок роли — то же; между целями — по-прежнему новая цель. Данные — через API.
// E2 (fixes-2026-10.md): дело «на неделе» без дня на текущей и будущих неделях подсвечено «не назначено на день».
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
/** «На неделе» под ролью (null — без роли): подпись и названия дел; списка нет — null. */
const weekBlock = role => page.evaluate(r => {
  const block = r
    ? [...document.querySelectorAll('.role')].find(x => x.querySelector('.role-h .name')?.textContent.trim() === r)?.querySelector('.week-nd')
    : [...document.querySelectorAll('.week-nd')].find(b => b.querySelector('.wnd-h')?.textContent.includes('без роли'));
  if (!block) return null;
  return { head: block.querySelector('.wnd-h').textContent.replace(/\s+/g, ' ').trim(),
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
const openDayTask = (i, t) => page.evaluate((i, t) => [...document.querySelectorAll('.day')[i].querySelectorAll(':scope > .item')]
  .find(x => x.querySelector('.title').textContent.trim() === t).querySelector('.title').click(), i, t);
const roleHeader = name => page.evaluate(n => {
  const r = [...document.querySelectorAll('.role')].find(x => x.querySelector('.role-h .name')?.textContent.trim() === n).querySelector('.role-h').getBoundingClientRect();
  return { x: r.x + r.width / 3, y: r.y + r.height / 2 };
}, name);
const menuItems = () => page.$$eval('.date-menu .option-name', os => os.map(o => o.textContent.trim()));
/** «→ на след. неделю» — последний пункт меню поля «Дата» (ui-declutter.md, U2.5). */
async function nextWeekItem() { await page.click('.editor .date-field'); await sleep(300); await page.click('.date-menu .option.next-week'); await sleep(300); }
const giftBody = roleId => ({ title: 'Купить подарок', notes: null, roleIds: [roleId], quadrant: null, startTime: '15:00:00', durationMinutes: null });

await load();
const board = await api('GET', 'board');
const family = board.roles.find(r => r.name === 'Семья');
const work = board.roles.find(r => r.name === 'Работа');
const gift = await api('POST', 'tasks', { title: 'Купить подарок', target: { kind: 'day', date: dayIso(1), index: 0 } });
await api('PUT', `tasks/${gift.id}`, giftBody(family.id));
await api('POST', 'tasks', { title: 'Чай', target: { kind: 'day', date: dayIso(1), index: 0 } });
await api('POST', 'tasks', { title: 'Почта', target: { kind: 'day', date: dayIso(3), index: 0 } });
const ideas = await api('POST', 'categories', { name: 'Идеи' });
await api('POST', 'tasks', { title: 'Шкаф', target: { kind: 'someday', categoryId: ideas.id, index: 0 } });
await load();

await check('B1, B4: окно дела с ролью — «→ на след. неделю» в меню «Дата» → дела нет в дне; плашка «… → след. неделя, «Семья» · Показать · Вернуть»', async () => {
  await openDayTask(1, 'Купить подарок'); await settle();
  assert(!(await page.$('.editor .next-week-row')), 'отдельная кнопка осталась');
  await nextWeekItem(); await settle();
  assert(!(await page.$('.editor')), 'окно не закрылось');
  assert(!(await dayTitles(1)).includes('Купить подарок'), 'осталось в дне');
  const t = await toast();
  assert(t?.includes('«Купить подарок» → след. неделя, «Семья»') && t.includes('Показать') && t.includes('Вернуть'), JSON.stringify(t));
});

await check('B2: «Показать» — следующая неделя: под «Семьёй» «на неделе · 1» и само дело (без сворачивания); в днях его нет', async () => {
  await toastAction('Показать'); await settle();
  const b = await weekBlock('Семья');
  assert(b && b.head === 'на неделе · 1' && JSON.stringify(b.tasks) === '["Купить подарок"]', JSON.stringify(b));
  assert(!(await page.$('.week-nd button.wnd-h')), 'заголовок «на неделе» сворачивает список');
  for (let i = 0; i < 7; i++) assert(!(await dayTitles(i)).includes('Купить подарок'), `в дне ${i}`);
  await (await page.$('.roles-panel')).screenshot({ path: `${SHOTS}/next-week.png` });
});

await check('B4: «Вернуть» — дело снова во вторнике, на своём месте и с 15:00', async () => {
  await api('POST', `tasks/${gift.id}/move`, { kind: 'day', date: dayIso(1), index: 0 });
  await api('PUT', `tasks/${gift.id}`, giftBody(family.id));
  await load();
  await openDayTask(1, 'Купить подарок'); await settle();
  await nextWeekItem(); await settle();
  await toastAction('Вернуть'); await settle();
  const t = await api('GET', `tasks/${gift.id}`);
  assert(t.plannedDate === dayIso(1) && t.startTime?.startsWith('15:00') && t.plannedWeek === null, JSON.stringify(t));
  assert((await dayTitles(1)).includes('Купить подарок'), JSON.stringify(await dayTitles(1)));
});

await check('B3: мысль без роли — меню ролей без «в ленту»; «в роль: Дом» → ушла из ленты, на следующей неделе под «Домом»', async () => {
  await page.evaluate(() => [...document.querySelectorAll('.cat-b > .item')].find(i => i.querySelector('.title').textContent.trim() === 'Шкаф').querySelector('.title').click());
  await settle();
  await nextWeekItem();
  const items = await menuItems();
  assert(items.includes('в роль: Дом') && !items.includes('в ленту'), JSON.stringify(items));
  await page.evaluate(() => [...document.querySelectorAll('.date-menu .option')].find(o => o.textContent.includes('в роль: Дом')).click()); await settle();
  assert(!(await page.$$eval('.cat-b > .item .title', ts => ts.map(t => t.textContent.trim()))).includes('Шкаф'), 'осталась в ленте');
  assert((await toast())?.includes('«Шкаф» → след. неделя, «Дом»'), JSON.stringify(await toast()));
  await page.click('.nav[title^="Следующая неделя"]'); await settle();
  assert(JSON.stringify((await weekBlock('Дом'))?.tasks) === '["Шкаф"]', JSON.stringify(await weekBlock('Дом')));
  assert(!(await weekBlock(null)), 'появился блок «без роли»');
  await page.click('.nav[title^="Предыдущая неделя"]'); await settle();
});

await check('B3: дело в дне без роли — в меню есть «в ленту» → дело в ленте', async () => {
  await openDayTask(1, 'Чай'); await settle();
  await nextWeekItem();
  assert((await menuItems()).includes('в ленту'), JSON.stringify(await menuItems()));
  await page.evaluate(() => document.querySelector('.date-menu .option.to-feed').click()); await settle();
  assert(!(await dayTitles(1)).includes('Чай'), 'осталось в дне');
  assert((await api('GET', 'board')).somedayTasks.some(t => t.title === 'Чай'), 'не в ленте');
});

await check('B5: дело тащат на «›» (неделя листается), потом на заголовок роли — на той неделе под ролью, плашка B4', async () => {
  const row = (await page.evaluateHandle(() => [...document.querySelectorAll('.day')[3].querySelectorAll(':scope > .item')]
    .find(i => i.querySelector('.title').textContent.trim() === 'Почта'))).asElement();
  const a = await row.boundingBox();
  const next = await (await page.$('.nav[title^="Следующая неделя"]')).boundingBox();
  const nx = next.x + next.width / 2, ny = next.y + next.height / 2;
  await page.mouse.move(a.x + a.width / 2, a.y + 8); await page.mouse.down();
  for (let i = 1; i <= 12; i++) { await page.mouse.move(a.x + (nx - a.x) * i / 12, a.y + (ny - a.y) * i / 12); await sleep(16); }
  await sleep(800); // ~0,5 с над «›» — неделя перелистнулась
  const h = await roleHeader('Работа');
  for (let i = 1; i <= 12; i++) { await page.mouse.move(nx + (h.x - nx) * i / 12, ny + (h.y - ny) * i / 12); await sleep(16); }
  const hint = await page.evaluate(() => getComputedStyle(document.querySelector('.role-h'), '::after').content);
  assert(hint.includes('на неделе'), `нет подписи на заголовке роли: ${hint}`);
  await sleep(200); await page.mouse.up(); await settle();
  const t = (await api('GET', `week/${dayIso(7)}`)).tasks.find(x => x.title === 'Почта');
  assert(t && t.plannedDate === null && t.plannedWeek === dayIso(7) && t.roleIds.includes(work.id), JSON.stringify(t));
  assert((await weekBlock('Работа'))?.tasks.includes('Почта'), JSON.stringify(await weekBlock('Работа')));
  assert((await toast())?.includes('«Почта» → след. неделя, «Работа»'), JSON.stringify(await toast()));
});

await check('B5: мысль, брошенная у нижнего края строки цели, — по-прежнему новая цель', async () => {
  await api('POST', 'goals', { roleId: work.id, weekStart: dayIso(7), title: 'Квартальный план', isRecurring: false });
  await api('POST', 'tasks', { title: 'Созвон', target: { kind: 'someday', categoryId: ideas.id, index: 0 } });
  await load(); await page.click('.nav[title^="Следующая неделя"]'); await settle();
  const row = (await page.evaluateHandle(() => [...document.querySelectorAll('.cat-b > .item')].find(i => i.querySelector('.title').textContent.trim() === 'Созвон'))).asElement();
  const target = await page.evaluate(() => {
    const r = [...document.querySelectorAll('.role .item')].find(i => i.querySelector('.title')?.textContent.trim() === 'Квартальный план').getBoundingClientRect();
    return { x: r.x + r.width / 2, y: r.bottom - 2 };
  });
  await drag(row, target.x, target.y);
  const goals = (await api('GET', `week/${dayIso(7)}`)).goals.map(g => g.title);
  assert(goals.includes('Созвон'), JSON.stringify(goals));
});

await check('N4.5: окно занятия цели — пункт неактивен, подсказка', async () => {
  const g = (await api('GET', `week/${dayIso(7)}`)).goals.find(x => x.title === 'Квартальный план');
  await api('POST', `goals/${g.id}/schedule`, { kind: 'day', date: dayIso(9), index: 0 });
  await load(); await page.click('.nav[title^="Следующая неделя"]'); await settle();
  await openDayTask(2, 'Квартальный план'); await settle();
  await page.click('.editor .date-field'); await sleep(300);
  assert(await page.$eval('.date-menu .option.next-week', b => b.disabled), 'пункт активен');
  assert((await page.$eval('.date-menu .option.next-week', e => e.textContent)).includes('своя неделя'), 'нет подсказки');
  await page.keyboard.press('Escape'); await sleep(200); await page.keyboard.press('Escape'); await settle();
});

await check('N4: дыра закрыта — дело из дня, ставшее целью другой недели, теряет день', async () => {
  const report = await api('POST', 'tasks', { title: 'Отчёт', target: { kind: 'day', date: dayIso(1), index: 0 } });
  await api('POST', 'goals/from-task', { taskId: report.id, roleId: work.id, weekStart: dayIso(7), index: 0 });
  const t = await api('GET', `tasks/${report.id}`);
  assert(t.plannedDate === null && t.goalId, JSON.stringify(t));
});

await check('E2: дело «на неделе» текущей недели подсвечено «не назначено на день»; отметили — нет; прошлая неделя — нет; на день — ушло из списка', async () => {
  const gym = await api('POST', 'tasks', { title: 'Записаться в зал', target: { kind: 'week', weekStart: dayIso(0), roleId: family.id } });
  const past = await api('POST', 'tasks', { title: 'Старое', target: { kind: 'week', weekStart: dayIso(-7), roleId: family.id } });
  await load();
  const row = t => page.evaluate(t => {
    const el = [...document.querySelectorAll('.week-nd .item')].find(i => i.querySelector('.title')?.textContent.trim() === t);
    return el ? { unplaced: el.classList.contains('unplaced'), hint: el.getAttribute('title') } : null;
  }, t);
  let s = await row('Записаться в зал');
  assert(s?.unplaced && s.hint === 'не назначено на день', JSON.stringify(s));
  await (await page.$('.roles-panel')).screenshot({ path: `${SHOTS}/week-unassigned.png` });
  await api('PUT', `tasks/${gym.id}/done`, { isDone: true }); await load();
  s = await row('Записаться в зал');
  assert(s && !s.unplaced && !s.hint, `выполненное: ${JSON.stringify(s)}`);
  await api('PUT', `tasks/${gym.id}/done`, { isDone: false }); await load();
  await page.click('.nav[title^="Предыдущая неделя"]'); await settle();
  s = await row('Старое');
  assert(s && !s.unplaced, `прошлая неделя: ${JSON.stringify(s)}`);
  await page.click('.nav[title^="Следующая неделя"]'); await settle();
  const el = (await page.evaluateHandle(() => [...document.querySelectorAll('.week-nd .item')].find(i => i.querySelector('.title')?.textContent.trim() === 'Записаться в зал'))).asElement();
  const day = await (await page.evaluateHandle(() => document.querySelectorAll('.day')[4])).asElement().boundingBox();
  await drag(el, day.x + day.width / 2, day.y + day.height - 60);
  assert(!(await row('Записаться в зал')), 'осталось в «на неделе»');
  assert((await dayTitles(4)).includes('Записаться в зал'), JSON.stringify(await dayTitles(4)));
  await api('DELETE', `tasks/${past.id}`);
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);

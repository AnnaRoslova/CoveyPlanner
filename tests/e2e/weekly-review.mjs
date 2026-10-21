// @seed — нужны демо-данные (tools/seed-demo.ps1)
// Недельный обзор (docs/specs/batch-ux.md, BR): W и кнопка в «Итогах»; шаги 1–5, «Назад / Дальше»; шаг 1 — каждое действие
// (на след. неделю, в роль…, в ленту, в корзину, оставить; занятие разовой цели — к той же цели на след. неделе, у постоянной — без переноса);
// шаг 5 — следующая неделя, «у роли … нет камней», «+ цель»; «Готово» — на следующую неделю; напоминание в воскресенье (часы подменяются).
import puppeteer from 'puppeteer-core';
import { openTotals } from './lib/nav.mjs';

const BASE = process.env.PLANNER_URL ?? 'http://localhost:5071/';
const SHOTS = process.argv[2] ?? '.';
const results = [];
const consoleErrors = [];
const browser = await puppeteer.launch({
  executablePath: process.env.PLANNER_BROWSER ?? 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  headless: true, defaultViewport: { width: 1600, height: 1000 },
});
let page;
const sleep = ms => new Promise(r => setTimeout(r, ms));
const settle = () => sleep(600);
function assert(c, m) { if (!c) throw new Error(m); }
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-weekly-review-${results.length}.png` }); }
}
const iso = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
const addDays = (d, n) => { const r = new Date(d); r.setDate(r.getDate() + n); return r; };
const now = new Date();
const monday = addDays(new Date(now.getFullYear(), now.getMonth(), now.getDate()), -((now.getDay() + 6) % 7));
const nextMonday = addDays(monday, 7);

/** Открыть приложение «как будто сейчас» fakeNow (часы страницы; данные — настоящие). */
async function openAt(fakeNow) {
  await page?.close();
  page = await browser.newPage();
  page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text()); });
  page.on('pageerror', e => consoleErrors.push(e.message));
  await page.evaluateOnNewDocument(at => {
    const realNow = Date.now.bind(Date);
    const start = realNow();
    Date.now = () => at + (realNow() - start);
  }, fakeNow.getTime());
  await page.goto(BASE, { waitUntil: 'networkidle0' });
  await page.waitForSelector('.day');
  await settle();
}
const api = (method, path, body) => page.evaluate(async (m, p, b) => {
  const r = await fetch(`/api/${p}`, { method: m, headers: { 'Content-Type': 'application/json' }, body: b ? JSON.stringify(b) : undefined });
  if (!r.ok) throw new Error(`${m} ${p}: ${r.status} ${await r.text()}`);
  const text = await r.text();
  return text ? JSON.parse(text) : null;
}, method, path, body);
const toast = async () => (await page.$('.toast')) ? page.$eval('.toast', t => t.textContent.replace(/\s+/g, ' ').trim()) : null;
const rowTitles = () => page.$$eval('.review .row .title', ts => ts.map(t => t.textContent.replace('◆', '').trim()));
async function act(title, label) {
  await page.evaluate((t, l) => {
    const row = [...document.querySelectorAll('.review .row')].find(r => r.querySelector('.title').textContent.replace('◆', '').trim() === t);
    [...row.querySelectorAll('button')].find(b => b.textContent.trim().startsWith(l)).click();
  }, title, label);
  await settle();
}
const allTasks = async week => (await api('GET', `week/${iso(week)}`)).tasks;
const stepOn = () => page.$eval('.review .step.on', s => s.textContent.replace(/\s+/g, ' ').trim());
const next = async () => { await page.click('.review .next'); await settle(); };

// Среда текущей недели, 15:00 — обзор по этой неделе (в понедельник утром он был бы по прошлой)
await openAt(new Date(addDays(monday, 2).setHours(15, 0, 0, 0)));
const roles = Object.fromEntries((await api('GET', 'board')).roles.map(r => [r.name, r.id]));
const run = (await api('GET', `week/${iso(monday)}`)).goals.find(g => g.title === 'Бег');
await api('POST', `goals/${run.id}/schedule`, { kind: 'day', date: iso(addDays(monday, 2)), index: 0 });
await openAt(new Date(addDays(monday, 2).setHours(15, 0, 0, 0)));

await check('BR1: W открывает обзор текущей недели; шаги 1–5', async () => {
  await page.keyboard.press('KeyW'); await settle();
  assert(await page.$('.review'), 'обзор не открылся');
  const steps = await page.$$eval('.review .step', s => s.map(x => x.textContent.replace(/\s+/g, ' ').trim()));
  assert(JSON.stringify(steps) === JSON.stringify(['1 Не сделано', '2 Привычки', '3 Давно лежат', '4 Миссия', '5 Камни']), JSON.stringify(steps));
  const titles = await rowTitles();
  for (const t of ['Оплатить интернет', 'Созвон с заказчиком', 'Написать отчёт', 'Ужин с семьёй', 'Кофе с Олей', 'Демо для команды', 'Бег']) {
    assert(titles.includes(t), `нет «${t}»: ${JSON.stringify(titles)}`);
  }
  assert(!titles.includes('Бег в парке'), 'выполненное в списке');
  await (await page.$('.review')).screenshot({ path: `${SHOTS}/weekly-review-1.png` });
});

await check('BR3: «на след. неделю» — дело с ролью уходит на следующую неделю без дня', async () => {
  await act('Созвон с заказчиком', 'на след. неделю');
  assert(!(await rowTitles()).includes('Созвон с заказчиком'), 'осталось в списке');
  const moved = (await allTasks(nextMonday)).find(t => t.title === 'Созвон с заказчиком');
  assert(moved && moved.plannedWeek === iso(nextMonday) && !moved.plannedDate, JSON.stringify(moved));
});

await check('BR3: у дела без роли — «на след. неделю ▾ → в роль: Дом»', async () => {
  await act('Оплатить интернет', 'на след. неделю');
  await page.evaluate(() => [...document.querySelectorAll('.more-menu .option')].find(o => o.textContent.trim() === 'в роль: Дом').click());
  await settle();
  const moved = (await allTasks(nextMonday)).find(t => t.title === 'Оплатить интернет');
  assert(moved && moved.roleIds.includes(roles['Дом']) && moved.plannedWeek === iso(nextMonday), JSON.stringify(moved));
});

await check('BR3: «в ленту», «в корзину» (плашка «Вернуть»), «оставить»', async () => {
  await act('Написать отчёт', 'в ленту');
  assert((await api('GET', 'board')).somedayTasks.some(t => t.title === 'Написать отчёт'), 'не в ленте');
  await act('Ужин с семьёй', 'в корзину');
  assert((await toast())?.includes('в корзине'), `плашка: ${await toast()}`);
  await act('Кофе с Олей', 'оставить');
  assert(await page.evaluate(() => [...document.querySelectorAll('.review .row.kept .title')].some(t => t.textContent.trim() === 'Кофе с Олей')), 'не «оставлено»');
  assert((await allTasks(monday)).some(t => t.title === 'Кофе с Олей' && t.plannedDate), '«оставить» что-то поменял');
});

await check('BR3: камни — разовая цель переносит занятие к той же цели на след. неделе; у постоянной переноса нет', async () => {
  const runRow = await page.evaluate(() => {
    const row = [...document.querySelectorAll('.review .row')].find(r => r.querySelector('.title').textContent.replace('◆', '').trim() === 'Бег');
    return [...row.querySelectorAll('button')].map(b => b.textContent.trim());
  });
  assert(JSON.stringify(runRow) === JSON.stringify(['в корзину', 'оставить']), JSON.stringify(runRow));
  await act('Демо для команды', 'на след. неделю');
  const week = await api('GET', `week/${iso(nextMonday)}`);
  const demo = week.goals.find(g => g.title === 'Демо для команды');
  assert(demo && demo.sessions.length === 1, JSON.stringify(demo));
});

await check('BR4, BR5: «Дальше» — привычки (🔥, «из»), «давно лежат»', async () => {
  await next();
  assert((await stepOn()).endsWith('Привычки'), await stepOn());
  const habits = await page.$$eval('.review .row.habit', rs => rs.map(r => r.textContent.replace(/\s+/g, ' ').trim()));
  assert(habits.some(h => h.startsWith('Бег') && / из 3/.test(h)), JSON.stringify(habits));
  await next();
  assert((await page.$eval('.review .body', b => b.textContent)).includes('Давно лежащих мыслей нет'), 'шаг 3');
  await next();
  assert((await stepOn()).endsWith('Миссия'), await stepOn());
});

await check('BR7: шаг 5 — следующая неделя на экране, «у роли … нет камней», «+ цель» создаёт цель следующей недели', async () => {
  const before = await page.$eval('.week-h .wt', e => e.textContent.trim());
  await next();
  assert(await page.$eval('.week-h .wt', e => e.textContent.trim()) !== before, 'неделя на экране не сменилась');
  const empty = await page.$$eval('.review .no-rocks', ps => ps.map(p => p.textContent.trim()));
  assert(empty.includes('у роли «Дом» нет камней'), JSON.stringify(empty));
  await page.evaluate(() => {
    const block = [...document.querySelectorAll('.review .role-block')].find(b => b.querySelector('.role-name').textContent.trim() === 'Дом');
    block.querySelector('.add').click();
  });
  await sleep(300);
  await page.keyboard.type('Покрасить забор'); await page.keyboard.press('Enter'); await settle();
  const goal = (await api('GET', `week/${iso(nextMonday)}`)).goals.find(g => g.title === 'Покрасить забор');
  assert(goal && goal.roles.some(r => r.roleId === roles['Дом']), JSON.stringify(goal));
  await (await page.$('.review')).screenshot({ path: `${SHOTS}/weekly-review-5.png` });
});

await check('BR2: «Назад» с шага 5 — снова неделя обзора; «Готово» — следующая неделя, обзор закрыт', async () => {
  const shown = await page.$eval('.week-h .wt', e => e.textContent.trim());
  await page.click('.review .back'); await settle();
  assert(await page.$eval('.week-h .wt', e => e.textContent.trim()) !== shown, 'неделя не вернулась');
  await next();
  await page.click('.review .done'); await settle();
  assert(!(await page.$('.review')), 'обзор не закрылся');
  assert(await page.$eval('.week-h .wt', e => e.textContent.trim()) === shown, 'не следующая неделя');
});

await check('BR1: вход из «Итогов» — кнопка «🗓 Недельный обзор»; Esc — выйти', async () => {
  await openTotals(page, 'Привычки');
  await page.click('.review-b'); await settle();
  assert(await page.$('.review'), 'не открылся');
  await page.keyboard.press('Escape'); await settle();
  assert(!(await page.$('.review')), 'Esc не закрыл');
});

await check('BR1: напоминание в воскресенье после 12:00 — один раз; в понедельник утром той же недели — нет', async () => {
  await page.evaluate(() => localStorage.removeItem('planner.review-reminded'));
  await openAt(new Date(addDays(monday, 6).setHours(13, 0, 0, 0)));
  assert(await toast() === 'Неделя заканчивается· Недельный обзор', `плашка: ${await toast()}`);
  await page.click('.toast .action'); await settle();
  assert(await page.$('.review'), 'обзор не открылся с плашки');
  await openAt(new Date(addDays(monday, 6).setHours(18, 0, 0, 0)));
  assert(await toast() === null, `второй раз: ${await toast()}`);
  await openAt(new Date(addDays(nextMonday, 0).setHours(9, 0, 0, 0)));
  assert(await toast() === null, `понедельник: ${await toast()}`);
});

await check('BR1: пропустили воскресенье — напоминание в понедельник до 12:00; в субботу — нет', async () => {
  await page.evaluate(() => localStorage.removeItem('planner.review-reminded'));
  await openAt(new Date(addDays(monday, 5).setHours(13, 0, 0, 0)));
  assert(await toast() === null, `суббота: ${await toast()}`);
  await openAt(new Date(addDays(nextMonday, 0).setHours(9, 0, 0, 0)));
  assert(await toast() === 'Неделя заканчивается· Недельный обзор', `понедельник: ${await toast()}`);
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);

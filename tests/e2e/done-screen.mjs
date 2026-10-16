// Этап 9 (docs/specs/tasks-steps-done.md: V1–V9; e2e п. 14–16): экран «✓ Выполненное» внизу ленты.
// Группы по категориям со счётом по годам, дата отметки — сегодня; сняли галочку — мысль в ленте на своём месте;
// дело, созданное в неделе, не попадает, мысль, отмеченная в дне, — попадает; удалили категорию — дела в «без категории».
// Данные — через API (чистая база).
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
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-done-screen-${results.length}.png` }); }
}
const api = (method, path, body) => page.evaluate(async (m, p, b) => {
  const r = await fetch(`/api/${p}`, { method: m, headers: { 'Content-Type': 'application/json' }, body: b ? JSON.stringify(b) : undefined });
  if (!r.ok) throw new Error(`${m} ${p}: ${r.status} ${await r.text()}`);
  const text = await r.text();
  return text ? JSON.parse(text) : null;
}, method, path, body);
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.someday'); await settle(); }
const todayIso = () => page.evaluate(() => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`; });

const feed = name => page.evaluate(n => {
  const cat = [...document.querySelectorAll('.cat')].find(c => c.querySelector('.cat-h .name')?.textContent.trim() === n);
  return cat ? [...cat.querySelectorAll('.cat-b > .item .title')].map(t => t.textContent.trim()) : null;
}, name);
/** Экран: группы — имя, годы, строки (название, мета, дата). */
const screen = () => page.evaluate(() => {
  const s = document.querySelector('.done-screen');
  if (!s) return null;
  return [...s.querySelectorAll('.done-group')].map(g => ({
    name: g.querySelector('h3 .name').textContent.trim(),
    years: g.querySelector('h3 .years').textContent.trim(),
    rows: [...g.querySelectorAll('.done-row')].map(r => ({
      title: r.querySelector('.title').textContent.trim(),
      meta: r.querySelector('.meta')?.textContent.replace(/\s+/g, ' ').trim() ?? null,
      date: r.querySelector('.date').textContent.trim(),
    })),
  }));
});
const openScreen = async () => { await page.click('.week-h .done-b'); await page.waitForSelector('.done-screen'); await settle(); };
const doneRow = title => page.evaluateHandle(t => [...document.querySelectorAll('.done-screen .done-row')].find(r => r.querySelector('.title').textContent.trim() === t), title);

await load();
const books = await api('POST', 'categories', { name: 'Книги', emoji: '📚' });
const trip = await api('POST', 'categories', { name: 'Поездки' });
for (const t of ['Дюна', 'Лолита', 'Мастер']) await api('POST', 'tasks', { title: t, target: { kind: 'someday', categoryId: books.id, index: 99 } });
const rome = await api('POST', 'tasks', { title: 'Рим', target: { kind: 'someday', categoryId: trip.id, index: 0 } });
const today = await todayIso();
const weekTask = await api('POST', 'tasks', { title: 'Позвонить в банк', target: { kind: 'day', date: today, index: 0 } });
await api('PUT', `tasks/${weekTask.id}/done`, { isDone: true });
await api('POST', `tasks/${rome.id}/move`, { kind: 'day', date: today, index: 0 });
await api('PUT', `tasks/${rome.id}/done`, { isDone: true });
const repair = await api('POST', 'tasks', { title: 'Ремонт', target: { kind: 'someday', categoryId: trip.id, index: 99 } });
for (const t of ['Стены', 'Полы']) {
  const s = await api('POST', `tasks/${repair.id}/steps`, { title: t });
  await api('PUT', `tasks/${s.id}/done`, { isDone: true });
}
await load();
const lolita = (await api('GET', 'board')).somedayTasks.find(t => t.title === 'Лолита');

await check('V9 (fixes-2026-10 D3): вход — значок «✓» в шапке недели рядом с 🛠 и 🫙; внизу ленты строки нет', async () => {
  const r = await page.evaluate(() => {
    const b = document.querySelector('.week-h .done-b');
    return b && { text: b.textContent.trim(), title: b.title, prev: b.previousElementSibling?.textContent.trim(), next: b.nextElementSibling?.textContent.trim(),
      inFeed: !!document.querySelector('.someday .done-link') };
  });
  assert(r && r.text === '✓' && r.title === 'Выполненное' && r.prev === '🛠' && r.next === '🫙' && !r.inFeed, JSON.stringify(r));
});

await check('п. 14: отметили «Лолиту» в ленте — в группе «📚 Книги», счёт по годам, дата — сегодня', async () => {
  const row = (await page.evaluateHandle(() => [...document.querySelectorAll('.cat-b > .item')].find(i => i.querySelector('.title')?.textContent.trim() === 'Лолита'))).asElement();
  await (await row.$('.ck-hit')).click(); await settle();
  await openScreen();
  const s = await screen();
  const g = s.find(x => x.name === '📚 Книги');
  const year = Number(today.slice(0, 4));
  const dayMonth = await page.evaluate(() => {
    const d = new Date(); const m = ['янв', 'фев', 'мар', 'апр', 'мая', 'июн', 'июл', 'авг', 'сен', 'окт', 'ноя', 'дек'];
    return [`${d.getDate()} ${m[d.getMonth()]}`, `${d.getDate()} ${m[d.getMonth()]}.`];
  });
  assert(g && g.years === `${year} · 1` && g.rows.length === 1 && g.rows[0].title === 'Лолита', JSON.stringify(s));
  assert(dayMonth.includes(g.rows[0].date), `дата ${g.rows[0].date}, ждали ${dayMonth[0]}`);
  await (await page.$('.done-screen')).screenshot({ path: `${SHOTS}/done-screen.png` });
});

await check('V2, п. 15: дело, созданное в неделе, — нет; мысль, отмеченная в дне, — есть (в своей категории)', async () => {
  const s = await screen();
  const titles = s.flatMap(g => g.rows.map(r => r.title));
  assert(!titles.includes('Позвонить в банк'), JSON.stringify(titles));
  assert(s.find(g => g.name === 'Поездки')?.rows.some(r => r.title === 'Рим'), JSON.stringify(s));
});

await check('V6: у проекта — «2 шага», клик раскрывает шаги', async () => {
  await page.keyboard.press('Escape'); await settle();
  await api('PUT', `tasks/${repair.id}/done`, { isDone: true });
  await load(); await openScreen();
  const row = (await doneRow('Ремонт')).asElement();
  const meta = await row.$eval('.meta', m => m.textContent);
  assert(meta.includes('2 шага'), meta);
  await (await row.$('.meta')).click(); await settle();
  const steps = await row.$$eval('.done-steps li', l => l.map(x => x.textContent.trim()));
  assert(JSON.stringify(steps.sort()) === '["Полы","Стены"]', JSON.stringify(steps));
});

await check('V7: клик по названию — окно дела', async () => {
  await (await (await doneRow('Рим')).asElement().$('.title')).click(); await settle();
  assert(!(await page.$('.done-screen')), 'экран не закрылся');
  const title = await page.$eval('.editor input, .editor textarea', e => e.value).catch(() => null);
  assert(title === 'Рим', `окно: ${title}`);
  await page.keyboard.press('Escape'); await settle();
});

await check('D6, п. 14: сняли галочку «Лолиты» — исчезла с экрана, в ленте на своём месте', async () => {
  await openScreen();
  await (await (await doneRow('Лолита')).asElement().$('.ck-hit')).click(); await settle();
  const titles = (await screen()).flatMap(g => g.rows.map(r => r.title));
  assert(!titles.includes('Лолита'), JSON.stringify(titles));
  await page.keyboard.press('Escape'); await settle();
  assert(!(await page.$('.done-screen')), 'Esc не закрыл');
  assert(JSON.stringify(await feed('Книги')) === '["Дюна","Лолита","Мастер"]', JSON.stringify(await feed('Книги')));
});

await check('п. 16: удалили категорию «Поездки» (дела — в неразобранное) — «Рим» и «Ремонт» в группе «без категории», она в конце', async () => {
  await api('DELETE', `categories/${trip.id}?keepTasks=true`);
  await api('PUT', `tasks/${lolita.id}/done`, { isDone: true });
  await load(); await openScreen();
  const s = await screen();
  const last = s[s.length - 1];
  assert(last.name === 'без категории' && ['Рим', 'Ремонт'].every(t => last.rows.some(r => r.title === t)), JSON.stringify(s));
  assert(s[0].name === '📚 Книги', JSON.stringify(s.map(g => g.name)));
  await page.click('.done-screen .close'); await settle();
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);

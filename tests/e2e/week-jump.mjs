// Переход к дате (docs/specs/batch-3.md, J): клик по подписи недели и G — мини-календарь; наведение — строка недели,
// клик по дню, стрелки и Enter, поле ввода («12.03.2027», непонятный ввод), Esc, «Сегодня», точки дней с делами,
// календарь только читает (никаких /api/week, пока он открыт); быстрый поиск «12 окт» — «перейти к неделе …»;
// лента месяцев (polish-3.md, C): колесо листает месяцы подряд, лента дорастает, подпись — месяц в середине, «‹ ›», ↓ за край.
import puppeteer from 'puppeteer-core';

const BASE = process.env.PLANNER_URL ?? 'http://localhost:5071/';
const SHOTS = process.argv[2] ?? '.';
const results = [];
const consoleErrors = [];
const browser = await puppeteer.launch({
  executablePath: process.env.PLANNER_BROWSER ?? 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  headless: true, defaultViewport: { width: 1600, height: 900 },
});
const page = await browser.newPage();
page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text()); });
page.on('pageerror', e => consoleErrors.push(e.message));
const sleep = ms => new Promise(r => setTimeout(r, ms));
const settle = () => sleep(400);
function assert(c, m) { if (!c) throw new Error(m); }
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-week-jump-${results.length}.png` }); }
  for (let i = 0; i < 3 && await page.$('.week-jump, .editor, .search-box'); i++) {
    await page.keyboard.press('Escape'); await settle();
  }
  await page.evaluate(() => document.activeElement?.blur());
}
const api = (method, path, body) => page.evaluate(async (m, p, b) => {
  const r = await fetch(`/api/${p}`, { method: m, headers: { 'Content-Type': 'application/json', 'X-Planner': '1' }, body: b ? JSON.stringify(b) : undefined });
  if (!r.ok) throw new Error(`${m} ${p}: ${r.status} ${await r.text()}`);
  const text = await r.text();
  return text ? JSON.parse(text) : null;
}, method, path, body);

// Подпись недели — как RuDates.WeekRange
const GEN = ['января', 'февраля', 'марта', 'апреля', 'мая', 'июня', 'июля', 'августа', 'сентября', 'октября', 'ноября', 'декабря'];
const NOM = ['январь', 'февраль', 'март', 'апрель', 'май', 'июнь', 'июль', 'август', 'сентябрь', 'октябрь', 'ноябрь', 'декабрь'];
const today = new Date(); today.setHours(0, 0, 0, 0);
const addDays = (d, n) => { const x = new Date(d); x.setDate(x.getDate() + n); return x; };
const mondayOf = d => addDays(d, -((d.getDay() + 6) % 7));
const iso = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
function weekRange(monday) {
  const end = addDays(monday, 6);
  const range = monday.getMonth() === end.getMonth()
    ? `${monday.getDate()} — ${end.getDate()} ${GEN[end.getMonth()]}`
    : `${monday.getDate()} ${GEN[monday.getMonth()]} — ${end.getDate()} ${GEN[end.getMonth()]}`;
  return end.getFullYear() === today.getFullYear() ? range : `${range} ${end.getFullYear()}`;
}

const weekLabel = () => page.$eval('.week-h .wt', e => e.textContent.trim());
const isOpen = async () => !!(await page.$('.week-jump'));
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day'); await settle(); }
async function open() {
  await page.click('.week-h .wt'); await settle();
  assert(await isOpen(), 'календарь не открылся');
}

const weekRequests = [];
page.on('request', r => { if (/\/api\/week\//.test(r.url())) weekRequests.push(r.url()); });

await load();
const current = await weekLabel();
const thisMonday = mondayOf(today);
// День с делом — в этом месяце, но не сегодня (точка)
const busy = today.getDate() > 15 ? new Date(today.getFullYear(), today.getMonth(), 3) : new Date(today.getFullYear(), today.getMonth(), 25);
await api('POST', 'tasks', { title: 'Стоматолог', target: { kind: 'day', date: iso(busy), index: 0 } });
await api('POST', 'tasks', { title: 'Позвонить маме', target: { kind: 'day', date: iso(today), index: 0 } });
await load();

await check('клик по подписи недели — календарь: поле в фокусе, месяц недели, сегодня отмечено', async () => {
  await open();
  assert(await page.evaluate(() => document.activeElement?.matches('.week-jump .jump-input')), 'фокус не в поле');
  const month = await page.$eval('.week-jump .jump-month', e => e.textContent.trim());
  assert(month === `${NOM[today.getMonth()]} ${today.getFullYear()}`, `месяц: ${month}`);
  assert(await page.$(`.week-jump .jump-day.today[data-date="${iso(today)}"]`), 'сегодня не отмечено');
  assert(await page.$('.week-jump .jump-week.shown'), 'неделя на экране не отмечена');
  assert(await page.$eval('.week-h .wt', b => b.title.includes('(G)')), 'нет подсказки клавиши');
});

await check('точка — на дне с делом, на пустом — нет', async () => {
  await open();
  await page.waitForSelector(`.week-jump .jump-day[data-date="${iso(busy)}"] .busy`, { timeout: 2000 });
  const empty = busy.getDate() === 3 ? 4 : 26;
  assert(!(await page.$(`.week-jump .jump-day[data-date="${iso(new Date(today.getFullYear(), today.getMonth(), empty))}"] .busy`)), 'точка на пустом дне');
});

await check('наведение на день подсвечивает всю его неделю', async () => {
  await open();
  // Строки, видимые в окошке ленты (лента длиннее окошка — polish-3.md, C)
  const box = await page.$eval('.week-jump .jump-strip', s => s.getBoundingClientRect().toJSON());
  const rows = [];
  for (const r of await page.$$('.week-jump .jump-week:not(.shown):not(.picked)')) {
    const b = await r.boundingBox();
    if (b && b.y >= box.y - 1 && b.y + b.height <= box.y + box.height + 1) rows.push(r);
  }
  const plain = await rows[1].evaluate(r => getComputedStyle(r).backgroundColor);
  const day = await rows[0].$('.jump-day');
  await day.hover(); await sleep(150);
  const lit = await rows[0].evaluate(r => getComputedStyle(r).backgroundColor);
  assert(lit !== plain && lit !== 'rgba(0, 0, 0, 0)', `строка не подсветилась: ${lit}`);
});

await check('клик по дню — его неделя, календарь закрыт', async () => {
  await open();
  const target = addDays(thisMonday, 15);
  if (target.getMonth() !== today.getMonth() && !(await page.$(`.week-jump .jump-day[data-date="${iso(target)}"]`))) {
    await page.click('.week-jump .jump-head .nav:nth-of-type(2)'); await settle();
  }
  await page.click(`.week-jump .jump-day[data-date="${iso(target)}"]`); await settle();
  assert(!(await isOpen()), 'не закрылся');
  assert(await weekLabel() === weekRange(mondayOf(target)), `неделя: ${await weekLabel()}`);
  await page.keyboard.press('KeyT'); await settle();
});

await check('G — открыть; стрелка ↓ и Enter — следующая неделя', async () => {
  await page.keyboard.press('KeyG'); await settle();
  assert(await isOpen(), 'G не открыл');
  await page.keyboard.press('ArrowDown'); await sleep(100);
  await page.keyboard.press('ArrowRight'); await sleep(100); // пустое поле — по дням
  assert(await page.$(`.week-jump .jump-day.sel[data-date="${iso(addDays(thisMonday, 8))}"]`), 'выбор не сдвинулся на ↓ и →');
  await page.keyboard.press('Enter'); await settle();
  assert(!(await isOpen()) && await weekLabel() === weekRange(addDays(thisMonday, 7)), `неделя: ${await weekLabel()}`);
  await page.keyboard.press('KeyT'); await settle();
});

await check('поле: «12.03.2027» — подсказка и неделя в сетке, Enter — переход', async () => {
  await open();
  await page.type('.week-jump .jump-input', '12.03.2027'); await settle();
  const hint = await page.$eval('.week-jump .jump-hint', e => e.textContent.trim());
  assert(hint === '→ 8 — 14 марта 2027', `подсказка: ${hint}`);
  assert(await page.$eval('.week-jump .jump-month', e => e.textContent.trim()) === 'март 2027', 'месяц сетки не март 2027');
  assert(await page.$('.week-jump .jump-week.picked .jump-day[data-date="2027-03-12"]'), 'неделя не подсвечена');
  await page.keyboard.press('Enter'); await settle();
  assert(await weekLabel() === '8 — 14 марта 2027', `неделя: ${await weekLabel()}`);
});

await check('«Сегодня» в календаре — текущая неделя', async () => {
  await open();
  await page.click('.week-jump .jump-today'); await settle();
  assert(!(await isOpen()) && await weekLabel() === current, `неделя: ${await weekLabel()}`);
});

await check('«март 2027» — неделя 1 марта; непонятный ввод — тихая подсказка, Enter ничего не делает', async () => {
  await open();
  await page.type('.week-jump .jump-input', 'абракадабра'); await settle();
  const hint = await page.$eval('.week-jump .jump-hint', e => e.textContent.trim());
  assert(hint.startsWith('не понимаю'), `подсказка: ${hint}`);
  await page.keyboard.press('Enter'); await settle();
  assert(await isOpen() && await weekLabel() === current, 'Enter что-то сделал');
  await page.click('.week-jump .jump-input', { clickCount: 3 });
  await page.type('.week-jump .jump-input', 'март 2027'); await settle();
  await page.keyboard.press('Enter'); await settle();
  assert(await weekLabel() === weekRange(mondayOf(new Date(2027, 2, 1))), `неделя: ${await weekLabel()}`);
  await page.keyboard.press('KeyT'); await settle();
});

await check('Esc закрывает календарь; G при открытом окне не срабатывает', async () => {
  await open();
  await page.keyboard.press('Escape'); await settle();
  assert(!(await isOpen()), 'Esc не закрыл');
  await page.click('.day.today .item .tx'); await settle();
  assert(await page.$('.editor'), 'окно дела не открылось');
  await page.keyboard.press('KeyG'); await settle();
  assert(!(await isOpen()), 'G открыл календарь поверх окна');
});

await check('календарь только читает: листание месяцев без /api/week', async () => {
  await open();
  weekRequests.length = 0;
  for (let i = 0; i < 4; i++) { await page.click('.week-jump .jump-head .nav:nth-of-type(2)'); await sleep(200); }
  await page.click('.week-jump .jump-head .nav:nth-of-type(1)'); await settle();
  assert(weekRequests.length === 0, `запросы недели: ${weekRequests.join(', ')}`);
  const month = await page.$eval('.week-jump .jump-month', e => e.textContent.trim());
  const m = new Date(today.getFullYear(), today.getMonth() + 3, 1);
  assert(month === `${NOM[m.getMonth()]} ${m.getFullYear()}`, `месяц: ${month}`);
});

// Лента месяцев (polish-3.md, C): колесо и свайп листают её подряд; подпись — месяц в середине окошка
const strip = () => page.$eval('.week-jump', w => {
  const s = w.querySelector('.jump-strip');
  const rows = [...s.querySelectorAll('.jump-week')];
  const box = s.getBoundingClientRect();
  const mid = rows.find(r => r.getBoundingClientRect().bottom > box.top + box.height / 2);
  return { month: w.querySelector('.jump-month').textContent.trim(), weeks: rows.map(r => r.dataset.week), middle: mid.dataset.week };
});
const monthOfWeek = week => { const t = addDays(new Date(`${week}T00:00:00`), 3); return `${NOM[t.getMonth()]} ${t.getFullYear()}`; };
const monthIndex = label => { const [m, y] = label.split(' '); return Number(y) * 12 + NOM.indexOf(m); };
async function wheel(deltaY) {
  const box = await (await page.$('.week-jump .jump-strip')).boundingBox();
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
  await page.mouse.wheel({ deltaY }); await settle();
}

await check('C: колесо вниз — следующий месяц, вверх — снова этот; дни другого месяца бледные', async () => {
  await open();
  const start = (await strip()).month;
  await wheel(34 * 4);
  const next = await strip();
  await (await page.$('.week-jump')).screenshot({ path: `${SHOTS}/week-jump-strip.png` });
  assert(monthIndex(next.month) === monthIndex(start) + 1, `после колеса вниз: ${next.month} (было ${start})`);
  assert(next.month === monthOfWeek(next.middle), `подпись ${next.month} ≠ месяц середины ${monthOfWeek(next.middle)}`);
  const thursday = addDays(new Date(`${next.middle}T00:00:00`), 3);
  const wrong = await page.$$eval('.week-jump .jump-day', (days, m) => days.filter(d => d.classList.contains('out') === d.dataset.date.startsWith(m))
    .map(d => d.dataset.date), iso(thursday).slice(0, 7));
  assert(wrong.length === 0, `бледность не по месяцу подписи: ${wrong.slice(0, 3).join(', ')}`);
  await wheel(-34 * 4);
  assert((await strip()).month === start, `после колеса вверх: ${(await strip()).month}`);
});

await check('C: долгая прокрутка — лента дорастает в обе стороны, недели подряд, подпись без скачков; клик по дню — его неделя', async () => {
  await open();
  const before = await strip();
  let last = monthIndex(before.month);
  for (let i = 0; i < 25; i++) {
    await wheel(34 * 3);
    const now = monthIndex((await strip()).month);
    assert(now >= last && now - last <= 1, `вниз: скачок ${last} → ${now}`);
    last = now;
  }
  let s = await strip();
  assert(s.weeks.length > before.weeks.length, `вниз не доросла: ${s.weeks.length}`);
  assert(last - monthIndex(before.month) >= 15, `пролистали мало: ${s.month}`);
  for (let i = 0; i < 70; i++) {
    await wheel(-34 * 3);
    const now = monthIndex((await strip()).month);
    assert(now <= last && last - now <= 1, `вверх: скачок ${last} → ${now}`);
    last = now;
  }
  s = await strip();
  assert(monthIndex(before.month) - last >= 15, `вверх пролистали мало: ${s.month}`);
  assert(s.weeks[0] < before.weeks[0], 'вверх не доросла');
  for (let i = 1; i < s.weeks.length; i++) {
    assert(iso(addDays(new Date(`${s.weeks[i - 1]}T00:00:00`), 7)) === s.weeks[i], `разрыв: ${s.weeks[i - 1]} → ${s.weeks[i]}`);
  }
  assert(s.month === monthOfWeek(s.middle), `подпись ${s.month} ≠ ${monthOfWeek(s.middle)}`);
  await page.click(`.week-jump .jump-week[data-week="${s.middle}"] .jump-day:nth-child(3)`); await settle();
  assert(!(await isOpen()) && await weekLabel() === weekRange(new Date(`${s.middle}T00:00:00`)), `неделя: ${await weekLabel()}`);
  await page.keyboard.press('KeyT'); await settle();
});

await check('C: стрелки «‹ ›» — к соседнему месяцу лентой; ↓ ниже края — лента подтягивает выбранный день', async () => {
  await open();
  const start = monthIndex((await strip()).month);
  await page.click('.week-jump .jump-head .nav:nth-of-type(2)'); await sleep(900);
  assert(monthIndex((await strip()).month) === start + 1, `› : ${(await strip()).month}`);
  await page.click('.week-jump .jump-head .nav:nth-of-type(1)'); await sleep(900);
  assert(monthIndex((await strip()).month) === start, `‹ : ${(await strip()).month}`);
  for (let i = 0; i < 8; i++) { await page.keyboard.press('ArrowDown'); await sleep(80); }
  await settle();
  const visible = await page.$eval('.week-jump', w => {
    const s = w.querySelector('.jump-strip').getBoundingClientRect();
    const r = w.querySelector('.jump-day.sel').getBoundingClientRect();
    return r.top >= s.top - 1 && r.bottom <= s.bottom + 1;
  });
  assert(visible, 'выбранный день за краем ленты');
});

await check('быстрый поиск «12 окт» — первым «перейти к неделе …», Enter — переход', async () => {
  await page.keyboard.down('Control'); await page.keyboard.press('KeyK'); await page.keyboard.up('Control'); await settle();
  await page.type('.search-box input', '12 окт'); await sleep(700);
  const first = await page.$eval('.search-box .hit', e => e.textContent.trim());
  const expected = `перейти к неделе ${weekRange(mondayOf(new Date(today.getFullYear(), 9, 12)))}`;
  assert(first === expected, `первый пункт: ${first}`);
  assert(!(await page.$('.search-box .nothing')), '«Ничего не нашлось» при пункте недели');
  await page.keyboard.press('Enter'); await settle();
  assert(!(await page.$('.search-box')), 'поиск не закрылся');
  assert(await weekLabel() === weekRange(mondayOf(new Date(today.getFullYear(), 9, 12))), `неделя: ${await weekLabel()}`);
});

for (const theme of ['lotus', 'platinum', 'obsidian']) {
  await check(`окошко стоит под подписью недели (тема ${theme})`, async () => {
    await page.evaluate(t => { localStorage.setItem('planner.theme', t); }, theme);
    await load();
    await open();
    const [title, pop] = await Promise.all([page.$eval('.week-h .wt', e => e.getBoundingClientRect().toJSON()),
      page.$eval('.week-jump', e => e.getBoundingClientRect().toJSON())]);
    assert(Math.abs(pop.top - title.bottom) < 12 && Math.abs(pop.left - title.left) < 12,
      `окошко (${Math.round(pop.left)}, ${Math.round(pop.top)}), подпись (${Math.round(title.left)}, ${Math.round(title.bottom)})`);
  });
}
await page.evaluate(() => localStorage.setItem('planner.theme', 'lotus'));

await browser.close();
results.forEach(r => console.log(r));
if (consoleErrors.length) console.log(`FAIL ошибки в консоли: ${consoleErrors.slice(0, 3).join(' | ')}`);
process.exit(results.some(r => r.startsWith('FAIL')) || consoleErrors.length ? 1 : 0);

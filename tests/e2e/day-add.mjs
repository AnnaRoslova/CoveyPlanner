// Новое дело прямо в дне (docs/specs/polish-2.md, A): клик по пустому месту под последним делом — строка «Новое дело…»,
// Enter — добавить и оставить строку, Esc — закрыть; в пустом дне — подсказка; «+» у названия дня при наведении;
// D — в выделенный (кликом) день, иначе на сегодня; долгое нажатие на пустоте — не клик; перетаскивание в пустую часть дня работает.
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
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-day-add-${results.length}.png` }); }
  if (await page.$('.day .inline-input')) { await page.keyboard.press('Escape'); await settle(); }
}
const api = (method, path, body) => page.evaluate(async (m, p, b) => {
  const r = await fetch(`/api/${p}`, { method: m, headers: { 'Content-Type': 'application/json', 'X-Planner': '1' }, body: b ? JSON.stringify(b) : undefined });
  if (!r.ok) throw new Error(`${m} ${p}: ${r.status} ${await r.text()}`);
  const text = await r.text();
  return text ? JSON.parse(text) : null;
}, method, path, body);
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day'); await settle(); }
const day = async i => (await page.$$('.day'))[i];
const titlesIn = i => page.$$eval('.day', (ds, i) => [...ds[i].querySelectorAll('.item')].map(x => x.textContent), i);
const activeDay = () => page.evaluate(() => [...document.querySelectorAll('.day')].indexOf(document.activeElement?.closest('.day')));
async function clickBlank(i, dy = 30) {
  const b = await (await (await day(i)).$('.day-blank')).boundingBox();
  await page.mouse.click(b.x + b.width / 2, b.y + Math.min(dy, b.height - 4)); await settle();
}

await load();
const week = await api('GET', 'week/' + new Date().toISOString().slice(0, 10));
const todayIndex = await page.$$eval('.day', ds => ds.findIndex(d => d.classList.contains('today')));
// Два дня, не сегодняшние: в одном — дела (непустой), другой — пустой
const busy = todayIndex === 1 ? 2 : 1;
const empty = todayIndex === 5 ? 4 : 5;
const dateOf = i => new Date(week.weekStart + 'T12:00:00Z').getTime() + i * 86400000;
const iso = i => new Date(dateOf(i)).toISOString().slice(0, 10);
await api('POST', 'tasks', { title: 'Дело для перетаскивания', target: { kind: 'day', date: iso(busy), index: 0 } });
await api('POST', 'tasks', { title: 'Второе дело дня', target: { kind: 'day', date: iso(busy), index: 1 } });
await load();

await check('A0: «+» у названия дня невидим без наведения и виден при наведении на непустой не сегодняшний день', async () => {
  await page.mouse.move(0, 0); await settle();
  assert(await (await day(busy)).$eval('.day-plus', e => getComputedStyle(e).opacity) === '0', '«+» виден без наведения');
  const b = await (await day(busy)).boundingBox();
  await page.mouse.move(b.x + b.width / 2, b.y + 70); await settle();
  assert(await (await day(busy)).$eval('.day-plus', e => getComputedStyle(e).opacity) === '1', '«+» не появился при наведении');
  assert(await (await day(busy)).$$eval('.day-plus', ps => ps.length) === 1, 'не один «+»');
  assert(!(await page.$('.day .add')), 'старая «+ дело» осталась');
});

await check('A3: в сегодняшнем дне «+» при наведении — сверху (бейдж «сегодня» его не закрывает)', async () => {
  const b = await (await day(todayIndex)).boundingBox();
  await page.mouse.move(b.x + b.width / 2, b.y + 200); await settle();
  const top = await (await day(todayIndex)).$eval('.day-plus', p => {
    const r = p.getBoundingClientRect();
    return document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2) === p && getComputedStyle(p).opacity === '1';
  });
  assert(top, '«+» сегодняшнего дня закрыт или не виден');
});

await check('A1: клик по пустому месту дня — строка «Новое дело…»; Enter — дело в этом дне, строка остаётся; Esc — закрыть', async () => {
  await clickBlank(busy, 60);
  assert(await activeDay() === busy, `фокус не в строке этого дня: ${await activeDay()}`);
  assert(await page.evaluate(() => document.activeElement.placeholder) === 'Новое дело…', 'не та строка');
  await page.keyboard.type('Позвонить маме'); await page.keyboard.press('Enter'); await settle();
  await page.keyboard.type('Купить хлеб'); await page.keyboard.press('Enter'); await settle();
  assert(await activeDay() === busy, 'строка закрылась после Enter');
  const titles = await titlesIn(busy);
  assert(titles.at(-2).includes('Позвонить маме') && titles.at(-1).includes('Купить хлеб'), JSON.stringify(titles));
  await page.keyboard.press('Escape'); await settle();
  assert(!(await page.$('.day .inline-input')), 'Esc не закрыл');
  const saved = (await api('GET', 'week/' + iso(busy))).tasks.filter(t => t.title === 'Позвонить маме' || t.title === 'Купить хлеб');
  assert(saved.length === 2 && saved.every(t => t.plannedDate === iso(busy)), JSON.stringify(saved));
});

await check('A1: клик по пустому месту при открытой строке — строка остаётся, набранное на месте', async () => {
  await clickBlank(busy, 60);
  await page.keyboard.type('Недописанное');
  await clickBlank(busy, 120);
  assert(await page.$eval('.day .inline-input', i => i.value) === 'Недописанное', 'набранное пропало');
  await page.keyboard.press('Escape'); await settle();
});

await check('A2: в пустом дне — подсказка «щёлкни, чтобы записать»; клик по ней — строка в этом дне', async () => {
  assert(await (await day(empty)).$eval('.blank-hint', h => h.textContent.trim()) === 'щёлкни, чтобы записать', 'нет подсказки');
  assert(!(await (await day(busy)).$('.blank-hint')), 'подсказка в непустом дне');
  const h = await (await (await day(empty)).$('.blank-hint')).boundingBox();
  await page.mouse.click(h.x + h.width / 2, h.y + h.height / 2); await settle();
  assert(await activeDay() === empty, 'строка не в пустом дне');
  assert(!(await (await day(empty)).$('.blank-hint')), 'подсказка не спряталась');
  await page.keyboard.press('Escape'); await settle();
});

await check('A3: «+» у названия дня открывает ту же строку', async () => {
  const b = await (await day(empty)).boundingBox();
  await page.mouse.move(b.x + 40, b.y + 200); await settle();
  await (await (await day(empty)).$('.day-plus')).click(); await settle();
  assert(await activeDay() === empty, 'строка не открылась');
  await page.keyboard.type('Сходить в бассейн'); await page.keyboard.press('Enter'); await settle();
  assert((await titlesIn(empty)).some(t => t.includes('Сходить в бассейн')), 'дело не добавилось');
  await page.keyboard.press('Escape'); await settle();
});

await check('A4: D — в выделенный кликом день; после перезагрузки (выделения нет) — на сегодня', async () => {
  const b = await (await day(empty)).boundingBox();
  await page.mouse.click(b.x + b.width / 2, b.y + 12); await settle(); // заголовок дня — выделить, строку не открывать
  assert(await page.$$eval('.day', (ds, i) => ds[i].classList.contains('picked'), empty), 'день не выделен');
  assert(!(await page.$('.day .inline-input')), 'клик по заголовку открыл строку');
  await page.keyboard.press('KeyD'); await settle();
  assert(await activeDay() === empty, `D: строка в дне ${await activeDay()}, а не в выделенном`);
  await page.keyboard.type('Дело по клавише'); await page.keyboard.press('Enter'); await settle();
  await page.keyboard.press('Escape'); await settle();
  assert((await titlesIn(empty)).some(t => t.includes('Дело по клавише')), 'дело не в выделенном дне');
  await load();
  await page.keyboard.press('KeyD'); await settle();
  assert(await activeDay() === todayIndex, `без выделения D — не сегодня: ${await activeDay()}`);
});

await check('A4: другая неделя — выделение снимается', async () => {
  await clickBlank(busy, 200); await page.keyboard.press('Escape'); await settle();
  assert(await page.$('.day.picked'), 'нет выделения');
  await page.keyboard.press('ArrowRight'); await settle();
  await page.keyboard.press('ArrowLeft'); await settle();
  assert(!(await page.$('.day.picked')), 'выделение пережило смену недели');
});

await check('A5: долгое нажатие на пустом месте — не клик (строка не открывается)', async () => {
  const b = await (await (await day(busy)).$('.day-blank')).boundingBox();
  await page.mouse.move(b.x + b.width / 2, b.y + 80); await page.mouse.down(); await sleep(450); await page.mouse.up(); await settle();
  assert(!(await page.$('.day .inline-input')), 'долгое нажатие открыло строку');
});

await check('A5: дело перетаскивается в пустую часть другого дня; бросок строку не открывает', async () => {
  const item = (await (await day(busy)).$$('.item'))[0];
  const from = await item.boundingBox();
  const to = await (await (await day(empty)).$('.day-blank')).boundingBox();
  await page.mouse.move(from.x + 40, from.y + 8); await page.mouse.down();
  for (let i = 1; i <= 14; i++) {
    await page.mouse.move(from.x + 40 + (to.x + 40 - from.x - 40) * i / 14, from.y + 8 + (to.y + 120 - from.y - 8) * i / 14); await sleep(16);
  }
  await sleep(150); await page.mouse.up(); await sleep(900);
  assert((await titlesIn(empty)).at(-1).includes('Дело для перетаскивания'), JSON.stringify(await titlesIn(empty)));
  assert(!(await page.$('.day .inline-input')), 'бросок открыл строку');
  const moved = (await api('GET', 'week/' + iso(empty))).tasks.find(t => t.title === 'Дело для перетаскивания');
  assert(moved.plannedDate === iso(empty), `на сервере ${moved.plannedDate}`);
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
for (const r of results) console.log(r);
if (results.some(r => r.startsWith('FAIL'))) process.exit(1);

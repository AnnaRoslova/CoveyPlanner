// @seed — нужны демо-данные (tools/seed-demo.ps1)
// Клик против перетаскивания: окно/переименование открывается только коротким нажатием почти без движения;
// после сброса клики полсекунды не принимаются; порог перетаскивания 8px; автопрокрутка с задержкой;
// выделение текста только в полях ввода. Для записей в ленте, днях и целях — одинаково.
import puppeteer from 'puppeteer-core';

const BASE = process.env.PLANNER_URL ?? 'http://localhost:5071/';
const SHOTS = process.argv[2] ?? '.';
const results = [];
const consoleErrors = [];
const browser = await puppeteer.launch({
  executablePath: process.env.PLANNER_BROWSER ?? 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  headless: true, defaultViewport: { width: 1600, height: 1300 },
});
const page = await browser.newPage();
page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text()); });
page.on('pageerror', e => consoleErrors.push(e.message));
const sleep = ms => new Promise(r => setTimeout(r, ms));
function assert(c, m) { if (!c) throw new Error(m); }
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-clicks-${results.length}.png` }); }
  await closeAll();
}
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day .item'); await sleep(500); }
const api = (method, path, body) => page.evaluate(async (method, path, body) => {
  const r = await fetch('/api/' + path, { method, headers: { 'Content-Type': 'application/json' }, body: body ? JSON.stringify(body) : undefined });
  return r.status === 204 ? null : r.json();
}, method, path, body);

const titleOf = el => [...el.querySelector('.tx').childNodes].filter(n => !n.classList?.contains('pills') && !n.classList?.contains('note')).map(n => n.textContent).join('').trim();
const T = `const titleOf = ${titleOf};`;
async function boxOf(src) {
  const el = (await page.evaluateHandle(src)).asElement();
  assert(el, `не найдено: ${src.slice(0, 90)}`);
  await el.evaluate(e => e.scrollIntoView({ block: 'nearest' }));
  return el.boundingBox();
}
const itemIn = (scope, title) => boxOf(`(() => { ${T} return [...document.querySelectorAll(${JSON.stringify(scope)})].find(i => titleOf(i) === ${JSON.stringify(title)}); })()`);

// Три вида записей: где лежит, что открывает клик
const kinds = [
  { name: 'лента', item: () => itemIn('.cat-b .item', 'Сад с травами на балконе'), opened: '.cat-b .inline-input' },
  { name: 'день', item: () => itemIn('.day .item', 'Оплатить интернет'), opened: '.editor' },
  { name: 'цель', item: () => itemIn('.role .item', '100 страниц'), opened: '.role .inline-input' },
];
const isOpen = sel => page.$(sel).then(Boolean);
async function closeAll() {
  await page.keyboard.press('Escape').catch(() => {});
  await sleep(250);
}
// Нажать на запись (в точке x+40), подвигать на dx и отпустить через holdMs
async function press(box, { holdMs = 0, dx = 0 } = {}) {
  const x = box.x + 40, y = box.y + Math.min(box.height / 2, 10);
  await page.mouse.move(x, y);
  await page.mouse.down();
  if (dx) { await page.mouse.move(x + dx / 2, y, { steps: 2 }); await page.mouse.move(x + dx, y, { steps: 2 }); }
  if (holdMs) await sleep(holdMs);
  await page.mouse.up();
  await sleep(350);
}

await load();

for (const k of kinds) {
  await check(`${k.name}: короткий клик открывает`, async () => {
    await press(await k.item());
    assert(await isOpen(k.opened), 'не открылось');
  });
  await check(`${k.name}: долгое нажатие (400 мс) без движения — не открывает`, async () => {
    await press(await k.item(), { holdMs: 400 });
    assert(!(await isOpen(k.opened)), 'открылось');
  });
  await check(`${k.name}: запись «дёрнулась» на 5px — не клик и не перетаскивание`, async () => {
    await press(await k.item(), { dx: 5 });
    assert(!(await isOpen(k.opened)), 'открылось');
    assert(!(await page.$('.drag-ghost')), 'осталось перетаскивание');
  });
  await check(`${k.name}: сдвиг на 7px — ещё не перетаскивание, на 9px — уже да`, async () => {
    const b = await k.item();
    const x = b.x + 40, y = b.y + Math.min(b.height / 2, 10);
    await page.mouse.move(x, y); await page.mouse.down();
    await page.mouse.move(x + 7, y, { steps: 3 }); await sleep(50);
    const at7 = !!(await page.$('.drag-ghost'));
    await page.mouse.move(x + 9, y, { steps: 2 }); await sleep(50);
    const at9 = !!(await page.$('.drag-ghost'));
    await page.keyboard.press('Escape'); await page.mouse.up(); await sleep(300);
    assert(!at7 && at9, `7px: ${at7}, 9px: ${at9}`);
    assert(!(await isOpen(k.opened)), 'после отмены открылось');
  });
}

await check('цель → воскресенье: клик сразу после сброса не открывает окно, через 0,6 с — открывает', async () => {
  const from = await itemIn('.role .item', 'Поменять фильтр для воды');
  const sun = await boxOf(`document.querySelectorAll('.day')[6]`);
  const tx = sun.x + 40, ty = sun.y + 60;
  await page.mouse.move(from.x + 40, from.y + 8); await page.mouse.down();
  for (let i = 1; i <= 12; i++) { await page.mouse.move(from.x + 40 + (tx - from.x - 40) * i / 12, from.y + 8 + (ty - from.y - 8) * i / 12); await sleep(16); }
  await sleep(100);
  await page.mouse.up();
  await sleep(80);
  await page.mouse.click(tx, ty); // «лишний» клик по месту сброса, где уже стоит новое дело
  await sleep(300);
  assert(!(await isOpen('.editor')), 'окно открылось сразу после сброса');
  const item = await itemIn('.day .item', 'Поменять фильтр для воды');
  await sleep(300);
  await page.mouse.click(item.x + 40, item.y + 8);
  await sleep(350);
  assert(await isOpen('.editor'), 'через 0,6 с клик не открыл окно');
});

await check('лента → день: клик сразу после сброса не открывает окно', async () => {
  const from = await itemIn('.cat-b .item', 'Научиться печь хлеб на закваске');
  const fri = await boxOf(`document.querySelectorAll('.day')[4]`);
  const tx = fri.x + 40, ty = fri.y + 200;
  await page.mouse.move(from.x + 40, from.y + 8); await page.mouse.down();
  for (let i = 1; i <= 12; i++) { await page.mouse.move(from.x + 40 + (tx - from.x - 40) * i / 12, from.y + 8 + (ty - from.y - 8) * i / 12); await sleep(16); }
  await sleep(100); await page.mouse.up(); await sleep(50);
  const dropped = await itemIn('.day .item', 'Научиться печь хлеб на закваске');
  await page.mouse.click(dropped.x + 40, dropped.y + 8);
  await sleep(300);
  assert(!(await isOpen('.editor')), 'окно открылось');
});

await check('во время поиска правило то же: долгое нажатие — не переименование, короткое — да', async () => {
  await page.type('.search input', 'Сад'); await sleep(400);
  const b = await itemIn('.cat-b .item', 'Сад с травами на балконе');
  assert(!(await page.$('.cat-b .item[data-drag]')), 'во время поиска записи перетаскиваемые');
  await press(b, { holdMs: 400 });
  assert(!(await isOpen('.cat-b .inline-input')), 'долгое нажатие открыло переименование');
  await press(await itemIn('.cat-b .item', 'Сад с травами на балконе'));
  assert(await isOpen('.cat-b .inline-input'), 'короткий клик не открыл');
  await page.keyboard.press('Escape'); await sleep(200);
  await page.focus('.search input'); await page.keyboard.press('Escape'); await sleep(300);
});

await check('клавиатура после сброса работает сразу (Enter на кнопке)', async () => {
  const from = await itemIn('.day .item', 'Написать отчёт');
  const sat = await boxOf(`document.querySelectorAll('.day')[5]`);
  await page.mouse.move(from.x + 40, from.y + 8); await page.mouse.down();
  await page.mouse.move(sat.x + 40, sat.y + 250, { steps: 12 }); await sleep(100); await page.mouse.up();
  await page.focus('.day:nth-of-type(1) .add');
  await page.keyboard.press('Enter'); await sleep(300);
  assert(await page.$('.day:nth-of-type(1) .inline-input'), 'Enter на «+ дело» не сработал');
});

await check('автопрокрутка начинается не сразу, а после задержки у края', async () => {
  await load();
  const board = await api('GET', 'board');
  const books = board.categories.find(c => c.name === 'Книги и фильмы');
  for (let i = 1; i <= 25; i++) await api('POST', 'tasks', { title: `Книга ${i}`, target: { kind: 'someday', categoryId: books.id, index: 99 } });
  await load();
  const scroller = await boxOf(`document.querySelector('.someday .panel-scroll')`);
  const from = await itemIn('.cat-b .item', 'Сад с травами на балконе');
  await page.mouse.move(from.x + 40, from.y + 8); await page.mouse.down();
  await page.mouse.move(from.x + 40, scroller.y + scroller.height - 10, { steps: 15 });
  const before = await page.$eval('.someday .panel-scroll', e => e.scrollTop);
  await sleep(200);
  const early = await page.$eval('.someday .panel-scroll', e => e.scrollTop);
  await sleep(800);
  const late = await page.$eval('.someday .panel-scroll', e => e.scrollTop);
  await page.keyboard.press('Escape'); await page.mouse.up(); await sleep(300);
  assert(early === before, `прокрутка началась сразу: ${before} → ${early}`);
  assert(late > early + 50, `прокрутка не началась: ${early} → ${late}`);
});

await check('протягивание мышью по экрану не выделяет текст; в полях ввода выделение работает', async () => {
  const head = await boxOf(`document.querySelector('.week-h')`);
  await page.mouse.move(head.x + 20, head.y + 20); await page.mouse.down();
  await page.mouse.move(head.x + 900, head.y + 600, { steps: 10 }); await page.mouse.up();
  const selected = await page.evaluate(() => window.getSelection().toString());
  assert(selected === '', `выделено: «${selected.slice(0, 40)}»`);
  await page.click('.search input'); await page.keyboard.type('проверка');
  await page.keyboard.down('Control'); await page.keyboard.press('KeyA'); await page.keyboard.up('Control');
  const inInput = await page.$eval('.search input', i => i.selectionEnd - i.selectionStart);
  assert(inInput === 'проверка'.length, `в поле выделено ${inInput}`);
});

await browser.close();
console.log(results.join('\n'));
console.log(consoleErrors.length ? `\nОшибки консоли:\n${consoleErrors.join('\n')}` : '\nОшибок в консоли нет');

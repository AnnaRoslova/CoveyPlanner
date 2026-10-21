// @seed — нужны демо-данные (tools/seed-demo.ps1)
// Удаление прямо из списка дня: при наведении — серая 🗑 справа (поверх края записи, текст и пилюли не сдвигаются);
// на касании — свайп влево показывает 🗑. Клик — дело в корзину без подтверждения, плашка «Удалено «…» · Вернуть»,
// значок корзины «вздрагивает». Правило случайных кликов (dnd.js): долгое нажатие на 🗑 — не клик; перетаскивание не сломано.
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
const settle = () => sleep(450);
function assert(c, m) { if (!c) throw new Error(m); }
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-day-trash-${results.length}.png` }); }
  if (await page.$('.editor')) { await page.keyboard.press('Escape'); await settle(); }
}
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day'); await settle(); }
const api = path => page.evaluate(async p => (await fetch(`/api/${p}`)).json(), path);
const trashCount = async () => (await api('board')).trashCount;
const toast = async () => (await page.$('.toast')) ? (await page.$eval('.toast', e => [...e.querySelectorAll('span, button')]
  .map(x => x.textContent.trim()).filter(Boolean).join(' '))) : null;
const allDayTitles = () => page.$$eval('.day .item .title', ts => ts.map(t => t.textContent.trim()));
const dayOf = title => page.evaluate(t => [...document.querySelectorAll('.day')]
  .findIndex(d => [...d.querySelectorAll('.item .title')].some(e => e.textContent.trim() === t)), title);
const itemHandle = async title => (await page.evaluateHandle(t => [...document.querySelectorAll('.day .item')]
  .find(i => i.querySelector('.title').textContent.trim() === t), title)).asElement();
const trashBtn = title => page.evaluate(t => {
  const item = [...document.querySelectorAll('.day .item')].find(i => i.querySelector('.title').textContent.trim() === t);
  const b = item?.querySelector('.trash-btn');
  if (!b) return null;
  const s = getComputedStyle(b), r = b.getBoundingClientRect();
  return { opacity: Number(s.opacity), events: s.pointerEvents, x: r.x + r.width / 2, y: r.y + r.height / 2, swiped: item.classList.contains('swiped') };
}, title);
const textBox = title => page.evaluate(t => {
  const item = [...document.querySelectorAll('.day .item')].find(i => i.querySelector('.title').textContent.trim() === t);
  const r = item.querySelector('.tx').getBoundingClientRect();
  return `${r.x},${r.y},${r.width},${r.height}`;
}, title);
const hover = async title => { const b = await (await itemHandle(title)).boundingBox(); await page.mouse.move(b.x + b.width / 2, b.y + b.height / 2); await sleep(300); };

await load();

await check('🗑 у каждого дела в днях; без наведения не видна и не нажимается', async () => {
  const counts = await page.evaluate(() => ({
    items: document.querySelectorAll('.day .item').length,
    btns: document.querySelectorAll('.day .item .trash-btn').length,
    feed: document.querySelectorAll('.someday .item .trash-btn, .roles-panel .item .trash-btn').length,
  }));
  assert(counts.items > 0 && counts.btns === counts.items, `кнопок ${counts.btns} у ${counts.items} дел`);
  assert(counts.feed === 0, 'кнопка появилась в ленте или у целей');
  await page.mouse.move(800, 5);
  await sleep(300);
  const b = await trashBtn('Созвон с заказчиком');
  assert(b.opacity === 0 && b.events === 'none', JSON.stringify(b));
});

await check('наведение — 🗑 видна; текст и пилюли не сдвигаются', async () => {
  const before = await textBox('Демо для команды'); // с пилюлями «15:00» и «камень»
  await hover('Демо для команды');
  const b = await trashBtn('Демо для команды');
  assert(b.opacity === 1 && b.events === 'auto', JSON.stringify(b));
  assert(await textBox('Демо для команды') === before, 'текст сдвинулся');
  await (await page.$('.week')).screenshot({ path: `${SHOTS}/day-trash-hover.png` });
});

await check('клик по 🗑 — дело в корзину сразу: плашка «Удалено «…» · Вернуть», корзина вздрагивает, окно не открывается', async () => {
  const trash = await trashCount();
  const pulse = await page.$eval('.someday .trash .trash-ico', e => e.classList.contains('shake'));
  await hover('Созвон с заказчиком');
  const b = await trashBtn('Созвон с заказчиком');
  await page.mouse.click(b.x, b.y); await settle();
  assert(!(await allDayTitles()).includes('Созвон с заказчиком'), 'дело осталось в дне');
  assert(await toast() === '«Созвон с заказчиком» в корзине · Вернуть', `плашка: ${await toast()}`);
  assert(!(await page.$('.editor')), 'открылось окно дела');
  assert(!pulse && await page.$eval('.someday .trash .trash-ico', e => e.classList.contains('shake')), 'корзина не вздрогнула');
  assert(await trashCount() === trash, 'на сервер ушло сразу, без 6 секунд');
});

await check('«Вернуть» — дело на прежнем месте, сервер не трогали', async () => {
  const trash = await trashCount();
  await page.click('.toast .action'); await settle();
  assert(await dayOf('Созвон с заказчиком') === 1, `день: ${await dayOf('Созвон с заказчиком')}`);
  assert(await trashCount() === trash, 'на сервере что-то изменилось');
});

await check('без «Вернуть» — через 6 секунд дело на сервере в корзине; занятие цели перестаёт считаться', async () => {
  const trash = await trashCount();
  await hover('Демо для команды');
  const b = await trashBtn('Демо для команды');
  await page.mouse.click(b.x, b.y); await settle();
  assert(await toast() === '«Демо для команды» в корзине · Вернуть', `плашка: ${await toast()}`);
  await sleep(6500);
  assert(await trashCount() === trash + 1, `в корзине ${await trashCount()}, было ${trash}`);
  await load();
  assert(!(await allDayTitles()).includes('Демо для команды'), 'дело вернулось после перезагрузки');
});

await check('долгое нажатие на 🗑 — не клик (правило dnd.js), дело на месте', async () => {
  await hover('Ужин с семьёй');
  const b = await trashBtn('Ужин с семьёй');
  await page.mouse.move(b.x, b.y);
  await page.mouse.down(); await sleep(450); await page.mouse.up(); await settle();
  assert((await allDayTitles()).includes('Ужин с семьёй'), 'удалилось долгим нажатием');
  assert(!(await page.$('.toast')) || !(await toast()).startsWith('Удалено'), 'плашка удаления');
});

await check('мышь: перетаскивание дела между днями работает, ничего не удаляется', async () => {
  const trash = await trashCount();
  const from = await (await itemHandle('Ужин с семьёй')).boundingBox();
  const to = await (await page.$$('.day'))[2].boundingBox();
  const sx = from.x + 40, sy = from.y + from.height / 2, x = to.x + to.width / 2, y = to.y + to.height - 30;
  await page.mouse.move(sx, sy); await page.mouse.down();
  for (let i = 1; i <= 14; i++) { await page.mouse.move(sx + (x - sx) * i / 14, sy + (y - sy) * i / 14); await sleep(16); }
  await sleep(150); await page.mouse.up(); await sleep(700);
  assert(await dayOf('Ужин с семьёй') === 2, `день: ${await dayOf('Ужин с семьёй')}`);
  assert(await trashCount() === trash && !(await page.$('.editor')), 'удалилось или открылось окно');
});

// ---------- Палец: наведения нет, 🗑 — после свайпа влево ----------
await page.setViewport({ width: 1600, height: 900, hasTouch: true, isMobile: false });
const cdp = await page.createCDPSession();
await cdp.send('Emulation.setEmulatedMedia', { features: [{ name: 'hover', value: 'none' }, { name: 'pointer', value: 'coarse' }] });
await load();

async function swipe(title, dx, dy = 0) {
  const b = await (await itemHandle(title)).boundingBox();
  const x = b.x + b.width - 30, y = b.y + b.height / 2;
  await page.touchscreen.touchStart(x, y);
  for (let i = 1; i <= 8; i++) { await page.touchscreen.touchMove(x + dx * i / 8, y + dy * i / 8); await sleep(16); }
  await page.touchscreen.touchEnd(); await settle();
}

await check('палец: без свайпа 🗑 не видна', async () => {
  const b = await trashBtn('Ужин с семьёй');
  assert(b.opacity === 0 && b.events === 'none', JSON.stringify(b));
});

await check('палец: свайп вниз (прокрутка) и короткий свайп влево 🗑 не показывают', async () => {
  await swipe('Ужин с семьёй', 0, 60);
  assert(!(await trashBtn('Ужин с семьёй')).swiped, 'показалась от вертикального свайпа');
  await swipe('Ужин с семьёй', -20);
  assert(!(await trashBtn('Ужин с семьёй')).swiped, 'показалась от короткого свайпа');
});

await check('палец: свайп влево — 🗑 видна, окно не открылось, перетаскивания нет', async () => {
  await swipe('Ужин с семьёй', -80);
  const b = await trashBtn('Ужин с семьёй');
  assert(b.swiped && b.opacity === 1 && b.events === 'auto', JSON.stringify(b));
  assert(!(await page.$('.editor')) && !(await page.$('.drag-ghost')), 'окно или перетаскивание');
  await (await page.$('.week')).screenshot({ path: `${SHOTS}/day-trash-swipe.png` });
});

await check('палец: касание в другом месте прячет 🗑', async () => {
  await page.touchscreen.tap(800, 12); await settle();
  assert(!(await trashBtn('Ужин с семьёй')).swiped, 'не спряталась');
});

await check('палец: свайп и касание 🗑 — в корзину с плашкой', async () => {
  await swipe('Ужин с семьёй', -80);
  const b = await trashBtn('Ужин с семьёй');
  await page.touchscreen.tap(b.x, b.y); await settle();
  assert(!(await allDayTitles()).includes('Ужин с семьёй'), 'дело осталось');
  assert(await toast() === '«Ужин с семьёй» в корзине · Вернуть', `плашка: ${await toast()}`);
  await page.click('.toast .action'); await settle();
  assert((await allDayTitles()).includes('Ужин с семьёй'), '«Вернуть» не вернуло');
});

await check('палец: долгое нажатие по-прежнему начинает перетаскивание, короткое касание — окно', async () => {
  const b = await (await itemHandle('Ужин с семьёй')).boundingBox();
  const x = b.x + 40, y = b.y + b.height / 2;
  await page.touchscreen.touchStart(x, y); await sleep(450);
  assert(await page.$('.drag-ghost'), 'долгое нажатие не начало перетаскивание');
  await page.keyboard.press('Escape');
  await page.touchscreen.touchEnd(); await sleep(700);
  await page.touchscreen.tap(x, y); await settle();
  assert(await page.$('.editor'), 'касание не открыло окно');
});

// Снимки в двух темах: 🗑 при наведении
await page.setViewport({ width: 1600, height: 900 });
await cdp.send('Emulation.setEmulatedMedia', { features: [] });
for (const theme of ['lotus', 'obsidian']) {
  await check(`снимок 🗑 при наведении — тема ${theme}`, async () => {
    await page.evaluate(t => localStorage.setItem('planner.theme', t), theme);
    await load();
    await hover('Ужин с семьёй');
    const day = await (await page.$$('.day'))[2].boundingBox();
    await page.screenshot({ path: `${SHOTS}/day-trash-${theme}.png`, clip: { x: day.x - 10, y: day.y - 10, width: day.width + 20, height: Math.min(day.height + 20, 400) } });
  });
}
await page.evaluate(() => localStorage.removeItem('planner.theme'));

await browser.close();
if (consoleErrors.length) results.push(`FAIL ошибки в консоли: ${consoleErrors.join(' | ')}`);
console.log(results.join('\n'));

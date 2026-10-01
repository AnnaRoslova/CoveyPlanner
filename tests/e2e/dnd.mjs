// @seed — нужны демо-данные (tools/seed-demo.ps1)
// Шаг 9: перетаскивание в ленте — порядок, перенос между категориями, сброс на заголовок,
// отмена, клик без перетаскивания, автопрокрутка, палец (долгое нажатие / быстрый свайп).
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
page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text() + ' ' + (m.location()?.url ?? '')); });
page.on('pageerror', e => consoleErrors.push(e.message));
const sleep = ms => new Promise(r => setTimeout(r, ms));
const settle = () => sleep(500);
function assert(c, m) { if (!c) throw new Error(m); }
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-dnd-${results.length}.png` }); }
}
const api = (method, path, body) => page.evaluate(async (method, path, body) => {
  const r = await fetch('/api/' + path, { method, headers: { 'Content-Type': 'application/json' }, body: body ? JSON.stringify(body) : undefined });
  return r.status === 204 ? null : r.json();
}, method, path, body);
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.cat-b .item'); await settle(); }

const itemsIn = name => page.evaluate(n => {
  const cat = [...document.querySelectorAll('.cat')].find(c => c.querySelector('.cat-h .name')?.textContent.trim() === n);
  return cat ? [...cat.querySelectorAll('.cat-b .item .tx')].map(e => e.childNodes[0].textContent.trim()) : null;
}, name);
const countOf = name => page.evaluate(n => [...document.querySelectorAll('.cat')].find(c => c.querySelector('.cat-h .name')?.textContent.trim() === n)?.querySelector('.cnt')?.textContent, name);
const box = async selectorFn => {
  const h = await page.evaluateHandle(selectorFn);
  const b = await h.asElement()?.boundingBox();
  assert(b, 'элемент не найден');
  return b;
};
const itemBox = (cat, title) => box(new Function(`return [...document.querySelectorAll('.cat')].find(c => c.querySelector('.cat-h .name')?.textContent.trim() === ${JSON.stringify(cat)})
  ?.querySelectorAll('.cat-b .item').values().find(i => i.querySelector('.tx').childNodes[0].textContent.trim() === ${JSON.stringify(title)})`));
const headerBox = cat => box(new Function(`return [...document.querySelectorAll('.cat-h')].find(h => h.querySelector('.name')?.textContent.trim() === ${JSON.stringify(cat)})`));

// Перетащить мышью из центра записи в точку (x, y) плавно, с паузой перед отпусканием
async function mouseDrag(from, x, y, { hold = 150, beforeUp } = {}) {
  await page.mouse.move(from.x + from.width / 2, from.y + from.height / 2);
  await page.mouse.down();
  const steps = 12;
  const sx = from.x + from.width / 2, sy = from.y + from.height / 2;
  for (let i = 1; i <= steps; i++) {
    await page.mouse.move(sx + (x - sx) * i / steps, sy + (y - sy) * i / steps);
    await sleep(16);
  }
  await sleep(hold);
  if (beforeUp) await beforeUp();
  await page.mouse.up();
  await settle();
}

await load();

await check('мышь: порядок внутри категории, после перезагрузки сохраняется', async () => {
  const from = await itemBox('Идеи', 'Сделать фотокнигу за лето');
  const last = await itemBox('Идеи', 'Сад с травами на балконе');
  await mouseDrag(from, last.x + 40, last.y + last.height - 3, {
    beforeUp: async () => {
      const ui = await page.evaluate(() => ({
        ghost: !!document.querySelector('.drag-ghost'),
        line: !document.querySelector('.drop-line')?.hidden,
        zone: !document.querySelector('.drop-zone-box')?.hidden,
        source: !!document.querySelector('.drag-source'),
      }));
      assert(ui.ghost && ui.line && ui.zone && ui.source, `подсказки: ${JSON.stringify(ui)}`);
      await page.screenshot({ path: `${SHOTS}/dnd-during.png` });
    },
  });
  const order = await itemsIn('Идеи');
  assert(JSON.stringify(order) === '["Научиться печь хлеб на закваске","Сад с травами на балконе","Сделать фотокнигу за лето"]', `Идеи: ${order}`);
  assert(!(await page.$('.drag-ghost, .drop-line, .drop-zone-box, .drag-source')), 'подсказки остались после сброса');
  await load();
  assert(JSON.stringify(await itemsIn('Идеи')) === JSON.stringify(order), 'после перезагрузки порядок другой');
});

await check('после перетаскивания клик не открывает окно; обычный клик — открывает', async () => {
  assert(!(await page.$('.editor[aria-label="Мысль"]')), 'открылось окно после перетаскивания');
  const b = await itemBox('Идеи', 'Сад с травами на балконе');
  await page.mouse.click(b.x + 40, b.y + b.height / 2); await settle();
  assert(await page.$('.editor[aria-label="Мысль"]'), 'клик не открыл окно');
  await page.keyboard.press('Escape'); await settle();
});

await check('мышь: перенос в другую категорию на нужное место', async () => {
  const from = await itemBox('Книги и фильмы', 'Толстой, «Детство»');
  const second = await itemBox('Идеи', 'Сад с травами на балконе');
  await mouseDrag(from, second.x + 40, second.y + 4);
  const ideas = await itemsIn('Идеи');
  assert(JSON.stringify(ideas) === '["Научиться печь хлеб на закваске","Толстой, «Детство»","Сад с травами на балконе","Сделать фотокнигу за лето"]', `Идеи: ${ideas}`);
  assert((await itemsIn('Книги и фильмы')).length === 2, 'в Книгах не 2');
  assert(await countOf('Книги и фильмы') === '2' && await countOf('Идеи') === '4', 'счётчики');
  await load();
  assert((await itemsIn('Идеи'))[1] === 'Толстой, «Детство»', 'после перезагрузки');
});

await check('мышь: сброс на заголовок свёрнутой категории — наверх неё', async () => {
  const from = await itemBox('Идеи', 'Научиться печь хлеб на закваске');
  const header = await headerBox('Дом');
  await mouseDrag(from, header.x + 60, header.y + header.height / 2);
  assert(await countOf('Дом') === '5', `Дом: ${await countOf('Дом')}`);
  assert((await itemsIn('Дом')).length === 0, 'Дом раскрылся сам');
  await page.evaluate(() => [...document.querySelectorAll('.cat-h')].find(h => h.textContent.includes('Дом')).click());
  await settle();
  assert((await itemsIn('Дом'))[0] === 'Научиться печь хлеб на закваске', `Дом: ${await itemsIn('Дом')}`);
});

await check('Esc и сброс мимо — ничего не меняется', async () => {
  const before = await itemsIn('Идеи');
  const from = await itemBox('Идеи', 'Толстой, «Детство»');
  await page.mouse.move(from.x + 40, from.y + from.height / 2);
  await page.mouse.down();
  await page.mouse.move(from.x + 40, from.y + 80, { steps: 8 });
  await page.keyboard.press('Escape');
  await page.mouse.up(); await settle();
  assert(!(await page.$('.drag-ghost')), 'призрак остался после Esc');
  const outside = await box(() => document.querySelector('.week-h')); // не зона: заголовок недели
  const from2 = await itemBox('Идеи', 'Толстой, «Детство»');
  await mouseDrag(from2, outside.x + 400, outside.y + outside.height / 2);
  assert(JSON.stringify(await itemsIn('Идеи')) === JSON.stringify(before), `Идеи: ${await itemsIn('Идеи')}`);
});

await check('во время поиска перетаскивание выключено', async () => {
  await page.type('.search input', 'идея'); await settle();
  assert(!(await page.$('.cat-b .item[data-drag]')), 'записи перетаскиваемые во время поиска');
  await page.focus('.search input'); await page.keyboard.press('Escape'); await settle();
  assert(await page.$('.cat-b .item[data-drag]'), 'после поиска перетаскивание не вернулось');
});

await check('автопрокрутка ленты у нижнего края', async () => {
  const board = await api('GET', 'board');
  const books = board.categories.find(c => c.name === 'Книги и фильмы');
  for (let i = 1; i <= 25; i++) await api('POST', 'tasks', { title: `Книга ${i}`, target: { kind: 'someday', categoryId: books.id, index: 99 } });
  await load();
  const scroller = await box(() => document.querySelector('.someday .panel-scroll'));
  const from = await itemBox('Идеи', 'Толстой, «Детство»');
  const before = await page.$eval('.someday .panel-scroll', e => e.scrollTop);
  let after = 0;
  await mouseDrag(from, from.x + 40, scroller.y + scroller.height - 10, {
    hold: 700,
    beforeUp: async () => {
      after = await page.$eval('.someday .panel-scroll', e => e.scrollTop);
      await page.keyboard.press('Escape'); // проверяем только прокрутку, никуда не бросаем
    },
  });
  assert(after > before + 100, `прокрутка ${before} → ${after}`);
});

// ---------- Палец ----------
await page.setViewport({ width: 1600, height: 900, hasTouch: true, isMobile: false });
await load();

await check('палец: быстрый свайп — не перетаскивание', async () => {
  const before = await itemsIn('Идеи');
  const b = await itemBox('Идеи', 'Толстой, «Детство»');
  await page.touchscreen.touchStart(b.x + 40, b.y + b.height / 2);
  for (let i = 1; i <= 6; i++) { await page.touchscreen.touchMove(b.x + 40, b.y + b.height / 2 + i * 12); await sleep(16); }
  await sleep(400);
  assert(!(await page.$('.drag-ghost')), 'перетаскивание началось от свайпа');
  await page.touchscreen.touchEnd(); await settle();
  assert(JSON.stringify(await itemsIn('Идеи')) === JSON.stringify(before), 'порядок изменился');
});

await check('палец: долгое нажатие и перенос', async () => {
  const from = await itemBox('Идеи', 'Сделать фотокнигу за лето');
  const first = await itemBox('Идеи', 'Толстой, «Детство»');
  const x = from.x + 40, y = from.y + from.height / 2;
  await page.touchscreen.touchStart(x, y);
  await sleep(450);
  assert(await page.$('.drag-ghost'), 'долгое нажатие не начало перетаскивание');
  const ty = first.y + 4;
  for (let i = 1; i <= 10; i++) { await page.touchscreen.touchMove(x, y + (ty - y) * i / 10); await sleep(20); }
  await sleep(150);
  await page.touchscreen.touchEnd(); await settle();
  assert((await itemsIn('Идеи'))[0] === 'Сделать фотокнигу за лето', `Идеи: ${await itemsIn('Идеи')}`);
  assert(!(await page.$('.editor[aria-label="Мысль"]')), 'после перетаскивания открылось окно');
});

await check('палец: короткое касание — это клик (окно)', async () => {
  const b = await itemBox('Идеи', 'Сад с травами на балконе');
  await page.touchscreen.tap(b.x + 40, b.y + b.height / 2); await settle();
  assert(await page.$('.editor[aria-label="Мысль"]'), 'касание не открыло окно');
});

await browser.close();
console.log(results.join('\n'));
console.log(consoleErrors.length ? `\nОшибки консоли:\n${consoleErrors.join('\n')}` : '\nОшибок в консоли нет');

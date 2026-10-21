// @seed — нужны демо-данные (tools/seed-demo.ps1)
// Явные кнопки (docs/specs/fixes-2026-10.md, D1–D4): у кнопок системы зона нажатия не меньше 24px, при наведении фон меняется,
// с клавиатуры виден фокус; «Сохранить» — главная (залитая), «+ дело» — пунктирная; вход в «✓ Выполненное» — значком в шапке недели,
// строки «✓ выполненное» внизу ленты нет. Снимки для сравнения «до/после» — out/buttons-<тема>-<screen|task|toast>.png («Лотос», «Обсидиан»).
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
function assert(c, m) { if (!c) throw new Error(m); }
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-buttons-${results.length}.png` }); }
}
async function load(theme = 'lotus') {
  await page.goto(BASE, { waitUntil: 'networkidle0' });
  await page.evaluate(t => { localStorage.setItem('planner.theme', t); localStorage.setItem('planner.right-mode', 'feed'); }, theme);
  await page.goto(BASE, { waitUntil: 'networkidle0' });
  await page.waitForSelector('.day'); await page.evaluate(() => document.fonts.ready); await sleep(600);
}
const openFirstDayTask = () => page.evaluate(() => [...document.querySelectorAll('.day > .item .title')][0].click());

// Снимки: экран, окно дела, плашка — в двух темах
for (const theme of ['lotus', 'obsidian']) {
  await load(theme);
  await page.screenshot({ path: `${SHOTS}/buttons-${theme}-screen.png` });
  await openFirstDayTask(); await sleep(500);
  await (await page.$('.editor[aria-label="Дело"]'))?.screenshot({ path: `${SHOTS}/buttons-${theme}-task.png` });
  await page.keyboard.press('Escape'); await sleep(300);
  await page.evaluate(() => document.querySelector('.cat-b > .item .ck-hit')?.click()); await sleep(600);
  const toast = await page.$('.toast');
  if (toast) {
    const b = await toast.boundingBox();
    await page.screenshot({ path: `${SHOTS}/buttons-${theme}-toast.png`, clip: { x: Math.max(0, b.x - 40), y: Math.max(0, b.y - 40), width: Math.min(1600, b.width + 80), height: b.height + 80 } });
    await page.evaluate(() => [...document.querySelectorAll('.toast button.action')].find(a => a.textContent.trim() === 'вернуть')?.click());
    await sleep(500);
  }
}

await load();

/** Кнопки системы на экране (и в открытом окне): размеры. */
const sizes = () => page.evaluate(() => [...document.querySelectorAll('.btn, .add, .icon-b, .toast .action')]
  .filter(b => { const r = b.getBoundingClientRect(); return r.width > 0 && r.height > 0 && getComputedStyle(b).visibility !== 'hidden'; })
  .map(b => { const r = b.getBoundingClientRect(); return { t: (b.textContent.trim() || b.title || b.className).slice(0, 30), h: r.height, w: r.width }; }));

await check('D2.1: кнопки на экране — высота не меньше 24px (значки — и ширина)', async () => {
  const small = (await sizes()).filter(s => s.h < 23.5);
  assert(small.length === 0, JSON.stringify(small.slice(0, 8)));
});

await check('D2.1: в окне дела — то же; «Сохранить» — главная (залитая), «Отмена» — обычная, «⋯» — обычная, «в корзину» в «⋯» — опасный пункт', async () => {
  await openFirstDayTask(); await sleep(500);
  const small = (await sizes()).filter(s => s.h < 23.5);
  assert(small.length === 0, JSON.stringify(small.slice(0, 8)));
  const kinds = await page.evaluate(() => [...document.querySelectorAll('.editor .buttons .btn')].map(b => b.className.replace('btn', '').trim() + ':' + b.textContent.trim()));
  assert(kinds.some(k => k.startsWith('primary') && k.endsWith('Сохранить')), JSON.stringify(kinds));
  assert(kinds.some(k => k.startsWith('more-b')), JSON.stringify(kinds));
  await page.click('.editor .more-b'); await sleep(300);
  assert(await page.$eval('.more-menu .option.danger', o => o.textContent.includes('в корзину')), 'в «⋯» нет опасного «в корзину»');
  await page.keyboard.press('Escape'); await sleep(200);
  assert(kinds.some(k => k === ':Отмена' || k === 'quiet:Отмена'), JSON.stringify(kinds)); // .btn.quiet — прежнее имя обычной
  await page.keyboard.press('Escape'); await sleep(300);
});

await check('D2: «+ дело» — пилюля с пунктирной обводкой; «Отмена» — с тонкой сплошной обводкой', async () => {
  const add = await page.$eval('.day .add', b => getComputedStyle(b).borderStyle);
  assert(add.includes('dashed'), `«+ дело»: ${add}`);
  await openFirstDayTask(); await sleep(400);
  const cancel = await page.evaluate(() => { const b = [...document.querySelectorAll('.editor .buttons .btn')].find(x => x.textContent.trim() === 'Отмена'); const cs = getComputedStyle(b); return cs.borderStyle + ' ' + cs.borderWidth; });
  assert(cancel.startsWith('solid') && !cancel.endsWith(' 0px'), `«Отмена»: ${cancel}`);
  await page.keyboard.press('Escape'); await sleep(300);
});

await check('D2.2: при наведении фон кнопки меняется («+ дело», «свернуть всё», значок 🫙)', async () => {
  await page.hover('.day:nth-child(3)');
  for (const sel of ['.day .add', '.someday .feed-menu', '.week-h .jar-b']) {
    const el = await page.$(sel);
    assert(el, `нет ${sel}`);
    await page.mouse.move(0, 0); await sleep(250);
    const before = await el.evaluate(b => getComputedStyle(b).backgroundColor);
    await el.hover(); await sleep(300);
    const after = await el.evaluate(b => getComputedStyle(b).backgroundColor);
    assert(before !== after, `${sel}: ${before} → ${after}`);
  }
  await page.mouse.move(0, 0);
});

await check('D2.3: фокус с клавиатуры (Tab) — у кнопки видна обводка', async () => {
  await page.focus('.week-h .nav'); await page.keyboard.press('Tab'); await sleep(150);
  const ring = await page.evaluate(() => { const a = document.activeElement; const cs = getComputedStyle(a); return { tag: a.tagName, style: cs.outlineStyle, width: parseFloat(cs.outlineWidth) }; });
  assert(ring.tag === 'BUTTON' && ring.style !== 'none' && ring.width >= 2, JSON.stringify(ring));
});

await check('D3: «✓» в шапке недели рядом с 🛠 🫙 📊 открывает «Выполненное»; внизу ленты строки «✓ выполненное» нет', async () => {
  const order = await page.evaluate(() => [...document.querySelectorAll('.week-h .icon-b')].map(b => b.textContent.trim() || b.title));
  assert(JSON.stringify(order.slice(0, 4)) === '["🛠","✓","🫙","📊"]', JSON.stringify(order));
  assert(!(await page.$('.done-link')), 'строка внизу ленты осталась');
  await page.click('.week-h .done-b'); await sleep(500);
  assert(await page.$('.done-screen'), 'экран «Выполненное» не открылся');
  await page.keyboard.press('Escape'); await sleep(300);
});

await check('D1: плашка — действия маленькими пилюлями (не подчёркнутый текст)', async () => {
  await page.evaluate(() => document.querySelector('.cat-b > .item .ck-hit').click()); await sleep(600);
  const a = await page.evaluate(() => [...document.querySelectorAll('.toast button.action')].map(b => {
    const cs = getComputedStyle(b); return { t: b.textContent.trim(), deco: cs.textDecorationLine, radius: parseFloat(cs.borderTopLeftRadius), h: b.getBoundingClientRect().height };
  }));
  assert(a.length >= 2 && a.every(x => x.deco === 'none' && x.radius >= 8 && x.h >= 23.5), JSON.stringify(a));
  await page.evaluate(() => [...document.querySelectorAll('.toast button.action')].find(b => b.textContent.trim() === 'вернуть')?.click());
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);

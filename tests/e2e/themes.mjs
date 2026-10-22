// @seed — нужны демо-данные (tools/seed-demo.ps1)
// Меню тем: кнопка-палитра в правом верхнем углу шапки недели, сетка карточек (цвета + короткое имя),
// у выбранной — галочка и обводка; закрывается выбором, кликом мимо и Esc. Выбор запоминается;
// шрифты тем скачиваются только для выбранной темы.
import puppeteer from 'puppeteer-core';
import { openTotals, openThemes } from './lib/nav.mjs';

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
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-themes-${results.length}.png` }); }
}
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day'); await sleep(500); }

const theme = () => page.evaluate(() => document.documentElement.dataset.theme);
const isOpen = async () => !!(await page.$('.themes'));
const fontFiles = () => page.evaluate(() => performance.getEntriesByType('resource')
  .map(e => e.name.split('/').pop()).filter(n => n.endsWith('.woff2')));
const themeFonts = /^(cormorant|manrope|chakra|exo|kurale|pt-serif)/;
async function openMenu() {
  await openThemes(page); // BI5: темы — в «⚙ Настройках»
}
async function choose(key) {
  await openMenu();
  await page.click(`.themes .theme[data-key="${key}"]`);
  await sleep(500);
  // В «Настройках» выбор не закрывает окно (можно перебрать темы) — закрываем сами
  await page.keyboard.press('Escape'); await sleep(300);
}

await load();

await check('«⚙ Настройки» — в правом верхнем углу шапки, правее «камней»', async () => {
  const [palette, rocks, header] = await page.evaluate(() => ['.settings-b', '.rocks', '.week-h']
    .map(s => document.querySelector(s).getBoundingClientRect().toJSON()));
  assert(palette.left > rocks.right + 8, 'палитра не отделена от «камней»');
  assert(header.right - palette.right < 30, 'палитра не в правом углу');
});

await check('по умолчанию «Лотос»; шрифты тем не скачаны', async () => {
  assert(await theme() === 'lotus', `тема: ${await theme()}`);
  assert(!(await fontFiles()).some(f => themeFonts.test(f)), 'шрифты тем скачаны');
});

await check('меню: карточки Лотос, Пион, Платина, Обсидиан, Земля, Вода, Пустота — цвета и имя, без описаний; у выбранной галочка', async () => {
  await openMenu();
  const cards = await page.$$eval('.themes .theme', ts => ts.map(t => ({
    key: t.dataset.key, name: t.querySelector('.t-name').textContent.trim(), on: t.classList.contains('on'),
    check: !!t.querySelector('.check'), swatches: t.querySelectorAll('.swatches .sw').length,
    text: t.textContent.trim(),
  })));
  assert(JSON.stringify(cards.map(c => c.name)) === '["Лотос","Пион","Платина","Обсидиан","Земля","Вода","Пустота"]', `карточки: ${cards.map(c => c.name)}`);
  assert(cards.every(c => c.swatches >= 3 && c.swatches <= 4), 'не 3–4 цвета');
  assert(cards.every(c => c.text === c.name), 'есть описания');
  assert(cards.filter(c => c.on && c.check).map(c => c.key).join() === 'lotus', 'галочка не у выбранной');
  // Цвета превью — свои у каждой темы
  const papers = await page.$$eval('.themes .sw.paper', s => s.map(e => getComputedStyle(e).backgroundColor));
  assert(new Set(papers).size === 7, `цвета превью одинаковые: ${papers}`);
  await page.screenshot({ path: `${SHOTS}/theme-menu.png` });
});

const windowColor = () => page.$eval('meta[name="theme-color"]', m => m.content);

await check('рамка окна (theme-color) — в цвет фона темы: при смене и после перезагрузки', async () => {
  assert(await windowColor() === '#d3ece4', `Лотос: ${await windowColor()}`);
  await choose('obsidian');
  assert(await windowColor() === '#16171a', `Обсидиан: ${await windowColor()}`);
  await load();
  assert(await windowColor() === '#16171a', `после перезагрузки: ${await windowColor()}`);
  await choose('lotus');
  assert(await windowColor() === '#d3ece4', `снова Лотос: ${await windowColor()}`);
  const manifest = await (await fetch(new URL('manifest.webmanifest', BASE))).json();
  assert(manifest.theme_color === '#d3ece4', `manifest: ${manifest.theme_color}`);
});

await check('Esc закрывает меню сразу после открытия', async () => {
  await openMenu();
  await page.keyboard.press('Escape'); await sleep(200);
  assert(!(await isOpen()), 'не закрылось');
});

await check('клик мимо закрывает меню, тема не меняется', async () => {
  await openMenu();
  await page.mouse.click(12, 880); await sleep(300); // угол экрана — заведомо мимо окна «Настройки»
  assert(!(await isOpen()), 'не закрылось');
  assert(await theme() === 'lotus', 'тема поменялась');
});

await check('«Обсидиан»: применяется сразу и переживает перезагрузку', async () => {
  await choose('obsidian');
  assert(await theme() === 'obsidian', `тема: ${await theme()}`);
  assert(!(await isOpen()), 'меню не закрылось');
  const panel = await page.$eval('.roles-panel', p => getComputedStyle(p).backgroundColor);
  assert(!/255, 255, 255/.test(panel) && !/srgb 1 1 1/.test(panel), `левая панель светлая: ${panel}`);
  await load();
  assert(await theme() === 'obsidian', `после перезагрузки: ${await theme()}`);
  await openMenu();
  assert(await page.$eval('.themes .theme.on', t => t.dataset.key) === 'obsidian', 'в меню отмечена не «Обсидиан»');
  await page.keyboard.press('Escape'); await sleep(200);
});

await check('«Пион»: скачан только его шрифт (Cormorant Garamond)', async () => {
  await page.evaluate(() => localStorage.setItem('planner.theme', 'lotus'));
  await load(); // свежий список загрузок, страница в «Лотосе»
  await choose('peony');
  await page.evaluate(() => document.fonts.ready); await sleep(400);
  const files = (await fontFiles()).filter(f => themeFonts.test(f));
  assert(files.length > 0 && files.every(f => f.startsWith('cormorant')), `шрифты: ${files}`);
});

await check('«Земля»: скачаны только её шрифты (Kurale, PT Serif); декор — только в «Пяти кольцах»', async () => {
  await page.evaluate(() => localStorage.setItem('planner.theme', 'lotus'));
  await load();
  assert(!(await page.$('.gorin-layer, .gorin-foot, .gorin-defs')), 'декор в «Лотосе»');
  await choose('earth');
  await page.evaluate(() => document.fonts.ready); await sleep(400);
  const files = (await fontFiles()).filter(f => themeFonts.test(f));
  assert(files.length > 0 && files.every(f => /^(kurale|pt-serif)/.test(f)), `шрифты: ${files}`);
  assert(await page.$('.gorin-defs') && await page.$('.week .gorin-layer') && await page.$('.roles-panel .gorin-foot'), 'нет декора');
  const blocking = await page.$$eval('.gorin-layer, .gorin-foot, .gorin-layer svg, .gorin-foot svg',
    els => els.filter(e => getComputedStyle(e).pointerEvents !== 'none').length);
  assert(blocking === 0, `декор ловит мышь: ${blocking}`);
  await choose('lotus');
});

await check('кто выбрал «Ночь», тому включается «Обсидиан» (и запоминается)', async () => {
  await page.evaluate(() => localStorage.setItem('planner.theme', 'night'));
  await load();
  assert(await theme() === 'obsidian', `тема: ${await theme()}`);
  assert(await page.evaluate(() => localStorage.getItem('planner.theme')) === 'obsidian', 'выбор не сохранён');
});

await check('сохранённая тема, которой нет в списке, — возвращается «Лотос»', async () => {
  await page.evaluate(() => localStorage.setItem('planner.theme', 'neon'));
  await load();
  assert(await theme() === 'lotus', `тема: ${await theme()}`);
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));

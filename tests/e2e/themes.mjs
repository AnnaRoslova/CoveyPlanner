// @seed — нужны демо-данные (tools/seed-demo.ps1)
// Темы оформления: переключатель в шапке недели с мини-превью, выбор запоминается,
// шрифты тем скачиваются только для выбранной темы. Снимки каждой темы — в out/theme-*.png.
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
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-themes-${results.length}.png` }); }
}
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day'); await sleep(500); }

const theme = () => page.evaluate(() => document.documentElement.dataset.theme);
const fontFiles = () => page.evaluate(() => performance.getEntriesByType('resource')
  .map(e => e.name.split('/').pop()).filter(n => n.endsWith('.woff2')));
const themeFonts = /^(cormorant|manrope|chakra|exo)/;
async function openPicker() {
  if (!(await page.$('.themes'))) await page.click('.palette-b');
  await page.waitForSelector('.themes');
  await sleep(200);
}
async function choose(key) {
  await openPicker();
  await page.click(`.themes .theme[data-key="${key}"]`);
  await sleep(500);
}

await load();

await check('по умолчанию «Лотос»; шрифты тем не скачаны', async () => {
  assert(await theme() === 'lotus', `тема: ${await theme()}`);
  const files = await fontFiles();
  assert(!files.some(f => themeFonts.test(f)), `скачаны: ${files}`);
});

await check('переключатель: 10 тем с мини-превью, текущая отмечена; превью не грузят шрифты', async () => {
  await openPicker();
  const tiles = await page.$$eval('.themes .theme', ts => ts.map(t => ({
    key: t.dataset.key, on: t.classList.contains('on'), name: t.querySelector('.t-name').textContent,
    preview: t.querySelector('.mini').dataset.theme,
  })));
  assert(tiles.length === 10, `тем: ${tiles.length}`);
  assert(tiles.every(t => t.preview === t.key), 'у превью не своя тема');
  assert(tiles.find(t => t.on)?.key === 'lotus', 'не отмечена текущая');
  // Превью ночи — тёмное, превью лотоса — светлое
  const bg = await page.$$eval('.themes .mini .m-day', ds => ds.map(d => getComputedStyle(d).backgroundColor));
  assert(bg.length === 30, `дней в превью: ${bg.length}`);
  const files = await fontFiles();
  assert(!files.some(f => themeFonts.test(f)), `скачаны: ${files}`);
  await page.screenshot({ path: `${SHOTS}/theme-picker.png` });
  await page.keyboard.press('Escape'); await sleep(200);
});

await check('клик мимо закрывает список', async () => {
  await openPicker();
  await page.mouse.click(700, 600); await sleep(300);
  assert(!(await page.$('.themes')), 'список не закрылся');
});

await check('«Ночь»: тема применяется сразу и переживает перезагрузку', async () => {
  await choose('night');
  assert(await theme() === 'night', `тема: ${await theme()}`);
  assert(!(await page.$('.themes')), 'список не закрылся');
  const paper = await page.$eval('.day', d => getComputedStyle(d).backgroundColor);
  assert(paper === 'rgb(43, 53, 57)', `бумага дня: ${paper}`);
  await load();
  assert(await theme() === 'night', `после перезагрузки: ${await theme()}`);
  await openPicker();
  assert(await page.$eval('.themes .theme.on', t => t.dataset.key) === 'night', 'в списке отмечена не «Ночь»');
  await page.keyboard.press('Escape'); await sleep(200);
});

await check('«Неон»: скачан только его шрифт (Chakra Petch)', async () => {
  await load(); // свежий список загрузок
  await choose('neon');
  await page.evaluate(() => document.fonts.ready); await sleep(400);
  const files = (await fontFiles()).filter(f => themeFonts.test(f));
  assert(files.length > 0 && files.every(f => f.startsWith('chakra')), `шрифты: ${files}`);
});

for (const key of ['lotus', 'peony', 'dune', 'graphite', 'powder', 'sea', 'lavender', 'night', 'neon', 'android']) {
  await choose(key);
  await page.evaluate(() => document.fonts.ready); await sleep(300);
  await page.screenshot({ path: `${SHOTS}/theme-${key}.png` });
}

await check('вернуть «Лотос»', async () => {
  await choose('lotus');
  assert(await theme() === 'lotus', `тема: ${await theme()}`);
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));

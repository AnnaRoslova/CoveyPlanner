// Стрелки листания не «убегают» (docs/specs/fixes-2026-10.md, C1–C3): «‹» и «›» рядом, подпись периода — после них, фиксированной ширины.
// Координаты стрелок не меняются при листании 12 недель вперёд (до недели другого года) и 12 назад; «Сегодня» стрелки не двигает;
// подпись видна целиком (в том числе самая длинная); то же для стрелок экрана «📊 Отчёт».
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
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-week-nav-${results.length}.png` }); }
}
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day'); await page.evaluate(() => document.fonts.ready); await sleep(500); }

/** Прямоугольники кнопок (округлены до 0,5px) и помещается ли подпись целиком. */
const arrows = (selector, label) => page.evaluate((sel, lab) => ({
  boxes: [...document.querySelectorAll(sel)].map(b => { const r = b.getBoundingClientRect(); return [r.x, r.y, r.width].map(v => Math.round(v * 2) / 2); }),
  fits: (() => { const l = document.querySelector(lab); return l.scrollWidth <= l.clientWidth + 1; })(),
  text: document.querySelector(lab).textContent.trim(),
}), selector, label);
const same = (a, b) => JSON.stringify(a) === JSON.stringify(b);

await load();

await check('C1: в шапке «‹» и «›» рядом, подпись после них', async () => {
  const order = await page.evaluate(() => [...document.querySelector('.week-h').querySelectorAll('.nav, .wt')].map(e => e.className.split(' ')[0]));
  assert(same(order.slice(0, 3), ['nav', 'nav', 'wt']), JSON.stringify(order));
});

await check('C2: 12 недель вперёд и 12 назад — стрелки на месте, подпись видна целиком («Сегодня» их не двигает)', async () => {
  const start = await arrows('.week-h .nav', '.week-h .wt');
  assert(start.boxes.length === 2 && start.fits, JSON.stringify(start));
  const seen = [];
  for (const [title, n] of [['Следующая неделя', 12], ['Предыдущая неделя', 12]]) {
    for (let i = 0; i < n; i++) {
      await page.click(`.week-h .nav[title^="${title}"]`); await sleep(250);
      const now = await arrows('.week-h .nav', '.week-h .wt');
      seen.push(now.text);
      assert(same(now.boxes, start.boxes), `после «${title}» №${i + 1} (${now.text}): ${JSON.stringify(now.boxes)} ≠ ${JSON.stringify(start.boxes)}`);
      assert(now.fits, `подпись обрезана: ${now.text}`);
    }
  }
  assert(seen.some(t => /\d{4}$/.test(t)), `не дошли до недели другого года: ${seen.join(' | ')}`);
});

await check('C2: самая длинная подпись («28 сентября — 4 октября 2027») помещается', async () => {
  const fits = await page.evaluate(() => {
    const l = document.querySelector('.week-h .wt');
    const before = l.textContent;
    l.textContent = '28 сентября — 4 октября 2027';
    const ok = l.scrollWidth <= l.clientWidth + 1;
    l.textContent = before;
    return ok;
  });
  assert(fits, 'не помещается');
});

await check('C3: «📊 Отчёт» — 12 месяцев вперёд и назад, стрелки периода на месте', async () => {
  await load();
  await openTotals(page, 'Отчёт'); await page.waitForSelector('.report-screen .range .nav'); await sleep(400);
  const start = await arrows('.report-screen .range .nav', '.report-screen .range-label');
  assert(start.boxes.length === 2, JSON.stringify(start));
  for (const [title, n] of [['Позже', 12], ['Раньше', 12]]) {
    for (let i = 0; i < n; i++) {
      await page.click(`.report-screen .range .nav[title="${title}"]`); await sleep(250);
      const now = await arrows('.report-screen .range .nav', '.report-screen .range-label');
      assert(same(now.boxes, start.boxes), `после «${title}» №${i + 1} (${now.text}): ${JSON.stringify(now.boxes)}`);
      assert(now.fits, `подпись обрезана: ${now.text}`);
    }
  }
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);

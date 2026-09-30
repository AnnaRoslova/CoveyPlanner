// Дымовая проверка: страница загружается, клиент отрисовывается, в консоли нет ошибок.
// Первым делом полезна для опубликованной (Release) сборки — там клиент урезается (trimming).
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
page.on('console', m => { if (m.type() === 'error' || m.type() === 'warning') consoleErrors.push(`${m.type()}: ${m.text()}`); });
page.on('pageerror', e => consoleErrors.push(`pageerror: ${e.message}`));
const sleep = ms => new Promise(r => setTimeout(r, ms));
function assert(c, m) { if (!c) throw new Error(m); }
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-smoke-${results.length}.png` }); }
}

await check('страница загружается и клиент отрисовывается', async () => {
  await page.goto(BASE, { waitUntil: 'networkidle0', timeout: 60000 });
  await page.waitForSelector('.app', { timeout: 30000 });
  const days = await page.$$eval('.day', ds => ds.length);
  assert(days === 7, `дней ${days}`);
});

await check('PWA: манифест и иконки загружаются', async () => {
  const manifest = await page.evaluate(async () => {
    const href = document.querySelector('link[rel="manifest"]').href;
    const m = await (await fetch(href)).json();
    const icons = await Promise.all(m.icons.map(async i => (await fetch(new URL(i.src, href))).status));
    return { name: m.name, display: m.display, icons };
  });
  assert(manifest.name === 'CoveyPlanner' && manifest.display === 'standalone', JSON.stringify(manifest));
  assert(manifest.icons.length >= 2 && manifest.icons.every(s => s === 200), `иконки: ${manifest.icons}`);
});

await check('PWA: service worker активен и управляет страницей', async () => {
  const state = await page.evaluate(async () => {
    const reg = await Promise.race([navigator.serviceWorker.ready, new Promise(r => setTimeout(() => r(null), 15000))]);
    return reg?.active?.state ?? null;
  });
  assert(state === 'activated', `состояние: ${state}`);
  await page.reload({ waitUntil: 'networkidle0' });
  await page.waitForSelector('.app', { timeout: 30000 });
  const controlled = await page.evaluate(() => !!navigator.serviceWorker.controller);
  assert(controlled, 'страница не под управлением service worker');
});

await check('через service worker сохранение работает', async () => {
  const ok = await page.evaluate(async () => {
    const r = await fetch('/api/categories', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name: 'Дымовая', emoji: null }) });
    return r.status;
  });
  assert(ok === 201, `статус ${ok}`);
});

await check('в консоли нет ошибок', async () => {
  const errors = consoleErrors.filter(e => e.startsWith('error') || e.startsWith('pageerror'));
  assert(errors.length === 0, errors.join(' | ').slice(0, 800));
});

await browser.close();
console.log(results.join('\n'));
console.log(consoleErrors.length ? `\nКонсоль:\n${consoleErrors.join('\n')}` : '\nКонсоль чистая');

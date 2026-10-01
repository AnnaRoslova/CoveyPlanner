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

await check('иконки: 192, 512, maskable, apple-touch-icon 180, favicon — нужных размеров', async () => {
  const sizes = await page.evaluate(async () => {
    const size = src => new Promise((ok, fail) => {
      const img = new Image();
      img.onload = () => ok(`${img.naturalWidth}x${img.naturalHeight}`);
      img.onerror = () => fail(new Error(src));
      img.src = src;
    });
    const href = document.querySelector('link[rel="manifest"]').href;
    const m = await (await fetch(href)).json();
    const icons = await Promise.all(m.icons.map(async i => `${i.purpose} ${i.sizes} ${await size(new URL(i.src, href).href)}`));
    const apple = document.querySelector('link[rel="apple-touch-icon"]');
    const favicon = document.querySelector('link[rel="icon"][href$=".ico"]');
    const ico = favicon ? await (await fetch(favicon.href)).arrayBuffer() : null;
    return {
      icons,
      apple: apple ? `${apple.sizes} ${await size(apple.href)}` : null,
      // В ICO: число картинок — в байтах 4–5
      icoCount: ico ? new DataView(ico).getUint16(4, true) : 0,
    };
  });
  for (const purpose of ['any', 'maskable']) {
    for (const s of ['192x192', '512x512']) {
      assert(sizes.icons.includes(`${purpose} ${s} ${s}`), `нет ${purpose} ${s}: ${sizes.icons}`);
    }
  }
  assert(sizes.apple === '180x180 180x180', `apple-touch-icon: ${sizes.apple}`);
  assert(sizes.icoCount >= 3, `favicon.ico: ${sizes.icoCount} картинок`);
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

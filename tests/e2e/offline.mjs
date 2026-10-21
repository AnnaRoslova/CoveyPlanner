// Нет связи с сервером (docs/specs/batch-ux.md, BN): запросы к /api обрываются перехватом Puppeteer. Правки без связи остаются на экране,
// плашка «Нет связи — изменения не сохранены (2)» не исчезает; закрыть вкладку — браузер спрашивает; связь вернулась —
// «Повторить сейчас» отправляет очередь по порядку, «Связь восстановлена — сохранено 2», экран перечитан.
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
// Оборванные запросы браузер пишет в консоль сам — это ожидаемо
page.on('console', m => { if (m.type() === 'error' && !/ERR_INTERNET_DISCONNECTED|Failed to load resource|Failed to fetch/.test(m.text())) consoleErrors.push(m.text()); });
page.on('pageerror', e => consoleErrors.push(e.message));
const sleep = ms => new Promise(r => setTimeout(r, ms));
const settle = () => sleep(500);
function assert(c, m) { if (!c) throw new Error(m); }
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-offline-${results.length}.png` }); }
}

let offline = false;
await page.setRequestInterception(true);
page.on('request', r => (offline && new URL(r.url()).pathname.startsWith('/api/') ? r.abort('internetdisconnected') : r.continue()));
const dialogs = [];
page.on('dialog', async d => { dialogs.push(d.type()); await d.dismiss(); });

const board = async () => (await (await fetch(new URL('api/board', BASE))).json());
const feedTitles = () => page.$$eval('.someday .item .title', ts => ts.map(t => t.textContent.trim()));
const banner = async () => (await page.$('.offline')) ? page.$eval('.offline', b => b.textContent.replace(/\s+/g, ' ').trim()) : null;
async function capture(text) {
  await page.click('.someday .quick input');
  await page.keyboard.type(text);
  await page.keyboard.press('Enter');
  await sleep(300);
}

await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day'); await settle();

await check('BN1, BN2: без связи две мысли остаются на экране; плашка «Нет связи — изменения не сохранены (2)» не исчезает', async () => {
  offline = true;
  await capture('Первая без связи');
  await capture('Вторая без связи');
  await page.waitForSelector('.offline', { timeout: 5000 });
  const titles = await feedTitles();
  assert(titles.includes('Первая без связи') && titles.includes('Вторая без связи'), JSON.stringify(titles));
  assert((await banner()) === 'Нет связи — изменения не сохранены (2), повторю попытку Повторить сейчас', `плашка: ${await banner()}`);
  await sleep(2500); // первый повтор (через 2 с) тоже не прошёл — плашка на месте
  assert(await banner(), 'плашка исчезла');
  assert((await board()).somedayTasks.length === 0, 'на сервер что-то дошло');
  await page.screenshot({ path: `${SHOTS}/offline.png` });
});

await check('BN5: закрыть или перезагрузить вкладку с несохранённым — браузер спрашивает', async () => {
  await page.evaluate(() => { location.reload(); });
  await sleep(800);
  assert(dialogs.includes('beforeunload'), `вопросов: ${JSON.stringify(dialogs)}`);
  assert(await banner(), 'страница перезагрузилась');
});

await check('BN3: связь вернулась — «Повторить сейчас»: очередь ушла по порядку, «Связь восстановлена — сохранено 2»', async () => {
  offline = false;
  await page.click('.offline .action');
  await page.waitForFunction(() => !document.querySelector('.offline'), { timeout: 5000 });
  await settle();
  const toast = await page.$eval('.toast', t => t.textContent.trim());
  assert(toast === 'Связь восстановлена — сохранено 2', `плашка: ${toast}`);
  const saved = (await board()).somedayTasks.map(t => t.title);
  assert(JSON.stringify(saved) === JSON.stringify(['Вторая без связи', 'Первая без связи']), `на сервере: ${JSON.stringify(saved)}`);
  const titles = await feedTitles();
  assert(titles.indexOf('Вторая без связи') < titles.indexOf('Первая без связи'), JSON.stringify(titles));
});

await check('связь есть — правка уходит сразу, плашки нет', async () => {
  await capture('Третья со связью');
  await settle();
  assert(!(await banner()), 'плашка «Нет связи»');
  assert((await board()).somedayTasks.some(t => t.title === 'Третья со связью'), 'не дошло');
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);

// @seed — нужны демо-данные (tools/seed-demo.ps1)
// Лента: «+ категория» сверху (новая — в начало) и «свернуть / развернуть всё».
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
const settle = () => sleep(400);
function assert(c, m) { if (!c) throw new Error(m); }
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-feed-tools-${results.length}.png` }); }
}
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day'); await settle(); }
const categoryNames = () => page.$$eval('.someday .cat-h .name', ns => ns.map(n => n.textContent.trim()));
const expanded = () => page.$$eval('.someday .cat', cs => cs.map(c => c.classList.contains('open')));

await load();

await check('сверху ленты: «+ категория» и «свернуть всё» — над категориями', async () => {
  const m = await page.evaluate(() => {
    const tools = document.querySelector('.someday .feed-tools');
    const first = document.querySelector('.someday .cat');
    return tools && {
      add: tools.querySelector('.add')?.textContent.trim(), fold: tools.querySelector('.fold')?.textContent.trim(),
      above: tools.getBoundingClientRect().bottom <= first.getBoundingClientRect().top,
    };
  });
  assert(m && m.add === '+ категория' && m.above, JSON.stringify(m));
  assert(await page.$('.someday .panel-scroll > .add'), 'нижняя «+ категория» пропала');
});

await check('«+ категория» сверху: новая категория встаёт первой и сохраняется', async () => {
  await page.click('.someday .feed-tools .add'); await settle();
  const editorTop = await page.$eval('.someday .cat-editor', e => e.getBoundingClientRect().top);
  const firstTop = await page.$eval('.someday .cat', c => c.getBoundingClientRect().top);
  assert(editorTop < firstTop, 'редактор не сверху');
  await page.keyboard.type('Срочное'); await page.keyboard.press('Enter'); await settle();
  assert((await categoryNames())[0] === 'Срочное', `категории: ${await categoryNames()}`);
  await load();
  assert((await categoryNames())[0] === 'Срочное', `после перезагрузки: ${await categoryNames()}`);
});

await check('«свернуть всё» сворачивает все категории, «развернуть всё» — раскрывает; запоминается', async () => {
  const fold = () => page.$eval('.someday .feed-tools .fold', b => b.textContent.trim());
  if ((await fold()) === 'развернуть всё') { await page.click('.someday .feed-tools .fold'); await settle(); }
  assert(await fold() === 'свернуть всё', `кнопка: ${await fold()}`);
  await page.click('.someday .feed-tools .fold'); await settle();
  assert((await expanded()).every(e => !e), `раскрыты: ${await expanded()}`);
  assert(await fold() === 'развернуть всё', `кнопка: ${await fold()}`);
  await load();
  assert((await expanded()).every(e => !e), 'после перезагрузки раскрыты');
  await page.click('.someday .feed-tools .fold'); await settle();
  assert((await expanded()).every(e => e), `свёрнуты: ${await expanded()}`);
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));

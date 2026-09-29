// @seed — нужны демо-данные (tools/seed-demo.ps1)
// Раскладка: автосворачивание панелей, шторки, запоминание, ширина дней, пилюли, фокус.
import puppeteer from 'puppeteer-core';

const BASE = process.env.PLANNER_URL ?? 'http://localhost:5071/';
const SHOTS = process.argv[2] ?? '.';
const results = [];
const consoleErrors = [];

const browser = await puppeteer.launch({
  executablePath: process.env.PLANNER_BROWSER ?? 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  headless: true,
});
const page = await browser.newPage();
page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text() + ' ' + (m.location()?.url ?? '')); });
page.on('pageerror', e => consoleErrors.push(e.message));

const sleep = ms => new Promise(r => setTimeout(r, ms));
function assert(cond, msg) { if (!cond) throw new Error(msg); }
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-layout-${results.length}.png` }); }
}

async function at(width, reload = true) {
  await page.setViewport({ width, height: 900 });
  if (reload) {
    await page.goto(BASE, { waitUntil: 'networkidle0' });
    await page.waitForSelector('.app', { timeout: 20000 });
  }
  await sleep(500);
}

// Состояние панелей: open / strip / overlay
const modes = () => page.evaluate(() => {
  const mode = side => document.querySelector(`.side.${side}.mode-overlay`) ? 'overlay'
    : document.querySelector(`.side.${side}.mode-strip`) ? 'strip' : 'open';
  return `${mode('left')}|${mode('right')}`;
});

const days = () => page.evaluate(() => {
  const box = document.querySelector('.days');
  return {
    widths: [...document.querySelectorAll('.day')].map(d => Math.round(d.getBoundingClientRect().width)),
    overflow: box.scrollWidth - box.clientWidth,
  };
});

await check('1600: обе панели открыты', async () => {
  await page.goto(BASE, { waitUntil: 'networkidle0' });
  await page.evaluate(() => localStorage.clear());
  await at(1600);
  assert(await modes() === 'open|open', await modes());
});

await check('1300 (PWA): левая свернулась сама, лента открыта, все 7 дней ≥126px без прокрутки', async () => {
  await at(1300);
  assert(await modes() === 'strip|open', await modes());
  const d = await days();
  assert(d.widths.length === 7 && d.widths.every(w => w >= 126), `ширины ${d.widths}`);
  assert(d.overflow <= 0, `неделя прокручивается на ${d.overflow}px`);
  await page.screenshot({ path: `${SHOTS}/layout-1300.png` });
});

await check('1300: клик по полоске — роли поверх недели, клик мимо — закрыть', async () => {
  await page.click('.side.left .strip'); await sleep(300);
  assert(await modes() === 'overlay|open', await modes());
  await page.screenshot({ path: `${SHOTS}/layout-1300-overlay.png` });
  await page.mouse.click(800, 450); await sleep(300);
  assert(await modes() === 'strip|open', await modes());
});

await check('1300: свернули ленту — роли помещаются и открываются сами', async () => {
  await page.click('.someday .panel-toggle'); await sleep(300);
  assert(await modes() === 'open|strip', await modes());
  const d = await days();
  assert(d.overflow <= 0 && d.widths.every(w => w >= 126), `ширины ${d.widths}, прокрутка ${d.overflow}`);
});

await check('выбор запоминается после перезагрузки', async () => {
  await at(1300);
  assert(await modes() === 'open|strip', await modes());
});

await check('развернули ленту — снова сворачивается левая', async () => {
  await page.click('.side.right .strip'); await sleep(300);
  assert(await modes() === 'strip|open', await modes());
});

await check('1600: свернуть роли вручную, запоминается', async () => {
  await at(1600);
  await page.click('.roles-panel .panel-toggle'); await sleep(300);
  assert(await modes() === 'strip|open', await modes());
  await at(1600);
  assert(await modes() === 'strip|open', 'после перезагрузки');
  await page.click('.side.left .strip'); await sleep(300);
  assert(await modes() === 'open|open', 'не развернулась');
});

await check('ресайз окна без перезагрузки перестраивает раскладку', async () => {
  await at(1600);
  await at(1250, false);
  assert(await modes() === 'strip|strip', `1250: ${await modes()}`);
  await at(1500, false);
  assert(await modes() === 'open|open', `1500: ${await modes()}`);
});

await check('сворачивание при сужении окна не теряет открытый редактор и введённый текст', async () => {
  await at(1600);
  await page.evaluate(() => [...document.querySelectorAll('.panel-scroll > .add')].find(b => b.textContent.includes('+ роль')).click());
  await sleep(400);
  await page.keyboard.type('Черновик роли');
  await at(1300, false);
  assert(await modes() === 'strip|open', `1300: ${await modes()}`);
  await at(1600, false);
  const value = await page.$eval('.role-editor .inline-input', e => e.value).catch(() => null);
  assert(value === 'Черновик роли', `редактор: ${value}`);
});

await check('пилюли не переносятся, в тексте нет автопереносов по слогам', async () => {
  await at(1300);
  const pills = await page.$$eval('.pill', ps => ps.map(p => [getComputedStyle(p).whiteSpace, p.getBoundingClientRect().height]));
  assert(pills.length > 0, 'нет пилюль в демо-данных');
  assert(pills.every(([ws, h]) => ws === 'nowrap' && h < 20), `пилюли: ${JSON.stringify(pills)}`);
  const hyphens = await page.$eval('.day .tx', t => getComputedStyle(t).hyphens);
  assert(hyphens === 'manual', `hyphens: ${hyphens}`);
});

await check('фокус с клавиатуры — мятное кольцо, без чёрной рамки', async () => {
  await at(1600);
  const add = await page.$('.role .add');
  await add.focus();
  await page.keyboard.press('Tab'); await page.keyboard.down('Shift'); await page.keyboard.press('Tab'); await page.keyboard.up('Shift');
  const style = await page.evaluate(() => {
    const el = document.activeElement;
    const s = getComputedStyle(el);
    return { tag: el.className, outline: s.outlineStyle, shadow: s.boxShadow };
  });
  assert(style.outline === 'none', `outline ${style.outline}`);
  assert(style.shadow.includes('rgba') || style.shadow.includes('rgb'), `кольца нет: ${style.shadow} (${style.tag})`);
});

await browser.close();
console.log(results.join('\n'));
console.log(consoleErrors.length ? `\nОшибки консоли:\n${consoleErrors.join('\n')}` : '\nОшибок в консоли нет');

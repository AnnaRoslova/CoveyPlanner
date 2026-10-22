// @seed — нужны демо-данные (tools/seed-demo.ps1)
// Окна (дело, цель, роль, корзина) — на затемнённом фоне; клик по затемнению закрывает окно,
// как «Отмена»: изменения не сохраняются.
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
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-windows-${results.length}.png` }); }
}
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day'); await sleep(500); }

const titleOf = el => [...el.querySelector('.tx').childNodes].filter(n => !n.classList?.contains('pills') && !n.classList?.contains('note') && !n.classList?.contains('goal-days')).map(n => n.textContent).join('').trim();
async function clickItem(scope, title) {
  await page.evaluate((scope, title, src) => {
    const titleOf = eval(src);
    [...document.querySelectorAll(`${scope} .item`)].find(i => titleOf(i) === title).querySelector('.tx').click();
  }, scope, title, titleOf.toString());
  await sleep(400);
}
const titles = scope => page.evaluate((scope, src) => {
  const titleOf = eval(src);
  return [...document.querySelectorAll(`${scope} .item`)].map(titleOf);
}, scope, titleOf.toString());

// Затемнение есть, оно на весь экран и под окном; клик в угол экрана — по затемнению
async function clickDimmed(backdrop) {
  const dim = await page.$eval(backdrop, b => {
    const r = b.getBoundingClientRect();
    return { w: r.width, h: r.height, bg: getComputedStyle(b).backgroundColor };
  });
  assert(dim.w >= 1500 && dim.h >= 850, `затемнение не на весь экран: ${dim.w}×${dim.h}`);
  assert(!/rgba\(0, 0, 0, 0\)|transparent/.test(dim.bg), `фон не затемнён: ${dim.bg}`);
  await page.mouse.click(8, 890); await sleep(400);
}

await load();

await check('окно дела: клик по затемнению — закрыто, изменения не сохранены', async () => {
  await clickItem('.day', 'Созвон с заказчиком');
  await page.waitForSelector('.editor[aria-label="Дело"]');
  await page.$eval('.editor .title-input', e => e.select());
  await page.keyboard.type('Изменённое дело');
  await clickDimmed('.editor-backdrop');
  assert(!(await page.$('.editor')), 'окно не закрылось');
  assert((await titles('.day')).includes('Созвон с заказчиком'), 'изменение сохранилось');
});

await check('окно цели: клик по затемнению — закрыто, изменения не сохранены', async () => {
  await clickItem('.role', 'Бег');
  await page.waitForSelector('.editor[aria-label="Цель недели"]');
  await page.$eval('.editor .title-input', e => e.select());
  await page.keyboard.type('Изменённая цель');
  await clickDimmed('.editor-backdrop');
  assert(!(await page.$('.editor')), 'окно не закрылось');
  assert((await titles('.role')).includes('Бег'), 'изменение сохранилось');
});

await check('окно роли: на весь экран с затемнением; клик по нему — закрыто, имя прежнее', async () => {
  await page.evaluate(() => [...document.querySelectorAll('.role')].find(r => r.querySelector('.role-h .name')?.textContent.trim() === 'Семья').querySelector('.menu').click());
  await page.waitForSelector('.popover.more-menu .option.edit-role'); await page.click('.popover.more-menu .option.edit-role');
  await page.waitForSelector('.role-editor');
  await sleep(300);
  await page.$eval('.role-editor .inline-input', e => e.select());
  await page.keyboard.type('Родные');
  await clickDimmed('.editor-backdrop');
  assert(!(await page.$('.role-editor')), 'окно не закрылось');
  const names = await page.$$eval('.role-h .name', ns => ns.map(n => n.textContent.trim()));
  assert(names.includes('Семья') && !names.includes('Родные'), `роли: ${names}`);
});

await check('новая роль — тоже окно; клик по затемнению — роль не создана', async () => {
  const before = await page.$$eval('.role-h .name', ns => ns.length);
  await page.evaluate(() => [...document.querySelectorAll('.panel-scroll > .add')].find(b => b.textContent.includes('+ роль')).click());
  await page.waitForSelector('.role-editor');
  await page.keyboard.type('Друзья');
  await clickDimmed('.editor-backdrop');
  assert(await page.$$eval('.role-h .name', ns => ns.length) === before, 'роль создалась');
});

await check('корзина: затемнение вокруг; клик по нему — закрыта', async () => {
  await page.click('.trash'); await page.waitForSelector('.trash-popup'); await sleep(300);
  await clickDimmed('.someday .backdrop');
  assert(!(await page.$('.trash-popup')), 'корзина не закрылась');
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));

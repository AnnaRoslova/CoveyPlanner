// @seed — нужны демо-данные (tools/seed-demo.ps1)
// «дела роли на неделе» — пункт «⋯» роли (batch-3.md, M3; было — строка «дела в днях · N ▸» под целями, ui-declutter D4):
// дела недели с ролью роли, кроме занятий целей. Пункт раскрывает список «Чт · название» с «×», клик по делу — его окно;
// раскрытие запоминается; нет дел — пункт бледный «дел на неделе нет».
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
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-role-tasks-${results.length}.png` }); }
  if (await page.$('.editor')) { await page.keyboard.press('Escape'); await settle(); }
}
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day'); await settle(); }

// Карточка роли по названию: заголовок раскрытого списка и сам список
const roleInfo = name => page.evaluate(n => {
  const role = [...document.querySelectorAll('.role')].find(r => r.querySelector('.role-h .name')?.textContent.trim() === n);
  return {
    head: role.querySelector('.others-h')?.textContent.replace(/\s+/g, ' ').trim() ?? null,
    items: [...role.querySelectorAll('.others .other')].map(o => `${o.querySelector('.d').textContent} · ${o.querySelector('.t').textContent.trim()}`),
    done: [...role.querySelectorAll('.others .other.done')].map(o => o.querySelector('.t').textContent.trim()),
  };
}, name);
const roleCard = name => page.evaluateHandle(n => [...document.querySelectorAll('.role')].find(r => r.querySelector('.role-h .name')?.textContent.trim() === n), name);
/** Пункт «дела роли на неделе» в «⋯» роли: подпись, число справа, доступен ли; click — выбрать его. */
async function menuItem(name, click = false) {
  const card = await roleCard(name);
  await (await card.$('.role-h .name')).hover();
  await (await card.$('.role-h .menu')).click(); await settle();
  const item = await page.$eval('.popover.more-menu .option.role-tasks', b => ({
    label: b.querySelector('.option-name').textContent.trim(), hint: b.querySelector('.menu-hint')?.textContent.trim() ?? null, disabled: b.disabled }));
  if (click) { await page.click('.popover.more-menu .option.role-tasks'); } else { await page.keyboard.press('Escape'); }
  await settle();
  return item;
}
const clickHead = name => menuItem(name, true);

await load();

await check('«⋯ → дела роли на неделе · N» — без занятий целей; под целями строки нет; у роли без таких дел пункт бледный', async () => {
  assert(!(await page.$('.roles-panel .others-h')), 'список или строка видны сразу');
  const work = await menuItem('Работа');
  assert(work.label === 'дела роли на неделе' && work.hint === '1' && !work.disabled, JSON.stringify(work)); // «Созвон», а камни — нет
  assert((await menuItem('Семья')).hint === '2', 'Семья');
  const home = await menuItem('Дом');
  assert(home.disabled && home.hint === 'дел на неделе нет', JSON.stringify(home));
});

await check('клик раскрывает «Вт · Созвон с заказчиком»; выполненные — зачёркнуты', async () => {
  await clickHead('Работа');
  assert((await roleInfo('Работа')).head === 'дела роли на неделе · 1', 'заголовок списка');
  const work = await roleInfo('Работа');
  assert(JSON.stringify(work.items) === '["Вт · Созвон с заказчиком"]', JSON.stringify(work.items));
  await clickHead('Семья');
  const family = await roleInfo('Семья');
  assert(JSON.stringify(family.items) === '["Пн · Купить подарок маме","Пт · Ужин с семьёй"]', JSON.stringify(family.items));
  assert(JSON.stringify(family.done) === '["Купить подарок маме"]', `выполненные: ${family.done}`);
  await (await page.$('.roles-panel')).screenshot({ path: `${SHOTS}/role-tasks.png` });
});

await check('раскрытие запоминается после перезагрузки', async () => {
  await load();
  assert((await roleInfo('Работа')).items.length === 1, 'Работа свернулась');
  assert((await roleInfo('Семья')).items.length === 2, 'Семья свернулась');
  assert((await roleInfo('Ум')).items.length === 0, 'Ум раскрылся сам');
  await (await (await roleCard('Семья')).$('.others-x')).click(); await settle(); // «×» у списка
  await load();
  assert((await roleInfo('Семья')).items.length === 0, 'Семья не свернулась');
});

await check('клик по делу — окно этого дела', async () => {
  await page.evaluate(() => document.querySelector('.others .other').click()); await settle();
  assert(await page.$eval('.editor .title-input', i => i.value) === 'Созвон с заказчиком', 'другое окно');
  await page.keyboard.press('Escape'); await settle();
  assert(!(await page.$('.editor')), 'окно не закрылось');
});

await check('новое дело с ролью сразу попадает в список', async () => {
  const before = await roleInfo('Ум');
  await page.evaluate(() => [...document.querySelectorAll('.day')][2].querySelector('.add').click()); await settle();
  await page.keyboard.type('Лекция о памяти'); await page.keyboard.press('Enter'); await settle();
  await page.keyboard.press('Escape'); await settle();
  await page.evaluate(() => [...document.querySelectorAll('.day .item')].find(i => i.querySelector('.title').textContent.trim() === 'Лекция о памяти').querySelector('.title').click());
  await settle();
  await page.click('.editor .tag-add'); await settle();
  await page.evaluate(() => [...document.querySelectorAll('.editor .popover .option')].find(o => o.textContent.trim() === 'Ум').click());
  await settle(); await page.keyboard.press('Escape'); await settle();
  await page.click('.editor .buttons .btn.primary'); await settle();
  const after = await roleInfo('Ум');
  assert(before.items.length === 0 && after.head === null && (await menuItem('Ум')).hint === '2', `${before.head} → ${after.head}`);
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));

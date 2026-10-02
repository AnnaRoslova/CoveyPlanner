// @seed — нужны демо-данные (tools/seed-demo.ps1)
// «ещё дела: N ▸» в левой панели: дела недели с ролью роли, кроме занятий целей.
// Клик раскрывает список «Чт · название», клик по делу — его окно; раскрытие запоминается; нет дел — нет строки.
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

// Карточка роли по названию: строка «ещё дела» и раскрытый список
const roleInfo = name => page.evaluate(n => {
  const role = [...document.querySelectorAll('.role')].find(r => r.querySelector('.role-h .name')?.textContent.trim() === n);
  return {
    head: role.querySelector('.others-h')?.textContent.replace(/\s+/g, ' ').trim() ?? null,
    items: [...role.querySelectorAll('.others .other')].map(o => `${o.querySelector('.d').textContent} · ${o.querySelector('.t').textContent.trim()}`),
    done: [...role.querySelectorAll('.others .other.done')].map(o => o.querySelector('.t').textContent.trim()),
  };
}, name);
const clickHead = name => page.evaluate(n => [...document.querySelectorAll('.role')]
  .find(r => r.querySelector('.role-h .name')?.textContent.trim() === n).querySelector('.others-h').click(), name);

await load();

await check('строка «ещё дела: N» — без занятий целей; у роли без таких дел строки нет', async () => {
  const work = await roleInfo('Работа');
  assert(work.head === 'ещё дела: 1', `Работа: ${work.head}`); // «Созвон», а камни «Код-ревью» и «Демо» — нет
  assert(work.items.length === 0, 'список раскрыт сразу');
  assert((await roleInfo('Семья')).head === 'ещё дела: 2', 'Семья');
  assert((await roleInfo('Дом')).head === null, 'у «Дома» есть строка');
});

await check('клик раскрывает «Вт · Созвон с заказчиком»; выполненные — зачёркнуты', async () => {
  await clickHead('Работа'); await settle();
  const work = await roleInfo('Работа');
  assert(JSON.stringify(work.items) === '["Вт · Созвон с заказчиком"]', JSON.stringify(work.items));
  await clickHead('Семья'); await settle();
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
  await clickHead('Семья'); await settle();
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
  await page.evaluate(() => [...document.querySelectorAll('.editor .chip')].find(c => c.textContent.trim() === 'Ум').click());
  await page.click('.editor .btn:not(.quiet):not(.danger)'); await settle();
  const after = await roleInfo('Ум');
  assert(before.head === 'ещё дела: 1' && after.head === 'ещё дела: 2', `${before.head} → ${after.head}`);
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));

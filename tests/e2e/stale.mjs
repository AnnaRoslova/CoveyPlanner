// Давно лежащие мысли (docs/specs/batch-ux.md, BS): часы сервера сдвигаются (POST /api/dev/clock — только Development),
// чтобы записать мысли «в прошлом». Метка «4 мес» — только у старше срока, подсказка возраста — у всех; плашка для нескольких
// и для одной («Посмотреть · Оставить»), второй раз не напоминает; «Посмотреть» — лента «давно лежат»; срок — из «⚙ Настройки».
import puppeteer from 'puppeteer-core';
import { openThemes } from './lib/nav.mjs';

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
const settle = () => sleep(600);
function assert(c, m) { if (!c) throw new Error(m); }
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-stale-${results.length}.png` }); }
}
const api = async (method, path, body) => {
  const r = await fetch(new URL(`api/${path}`, BASE), {
    method, headers: { 'Content-Type': 'application/json' }, body: body ? JSON.stringify(body) : undefined,
  });
  if (!r.ok) throw new Error(`${method} ${path}: ${r.status} ${await r.text()}`);
  return r.status === 204 ? null : r.json();
};
/** Мысль в «неразобранном», записанная days дней назад. */
async function thought(title, days) {
  await api('POST', 'dev/clock', { days: -days });
  await api('POST', 'tasks', { title, target: { kind: 'someday', categoryId: null, index: 0 } });
  await api('POST', 'dev/clock', { days: 0 });
}
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day'); await settle(); }
const toast = async () => (await page.$('.toast')) ? (await page.$eval('.toast', e => [...e.querySelectorAll('span, button')]
  .map(x => x.textContent.trim()).join(' '))) : null;
const row = title => page.evaluate(t => {
  const item = [...document.querySelectorAll('.someday .item')].find(i => i.querySelector('.title')?.textContent.trim() === t);
  return item && { title: item.getAttribute('title'), age: item.querySelector('.age-mark')?.textContent.trim() ?? null };
}, title);

await thought('Сад на балконе', 122);
await thought('Дача', 95);
await api('POST', 'tasks', { title: 'Свежая', target: { kind: 'someday', categoryId: null, index: 0 } });
await load();

await check('BS4: несколько мыслей перешагнули срок — одна плашка «2 мысли лежат больше 3 мес · Посмотреть»', async () => {
  assert(await toast() === '2 мысли лежат больше 3 мес · Посмотреть', `плашка: ${await toast()}`);
});

await check('BS3: метка «4 мес» только у старше срока; подсказка возраста — у всех мыслей', async () => {
  const garden = await row('Сад на балконе');
  const dacha = await row('Дача');
  const fresh = await row('Свежая');
  assert(garden.age === '4 мес' && /^лежит 4 мес · с [0-9]+ [а-я]+/.test(garden.title), JSON.stringify(garden));
  assert(dacha.age === '3 мес' && dacha.title.startsWith('лежит 3 мес'), JSON.stringify(dacha));
  assert(fresh.age === null && fresh.title === 'записано сегодня', JSON.stringify(fresh));
  await (await page.$('.someday')).screenshot({ path: `${SHOTS}/stale-feed.png` });
});

await check('BS4: «Посмотреть» — лента «давно лежат» (самые давние сверху), крестик — обычная лента', async () => {
  await page.click('.toast .action'); await settle();
  const head = await page.$eval('.someday .stale-h', h => h.textContent.replace(/\s+/g, ' ').trim());
  const titles = await page.$$eval('.someday .panel-scroll .item .title', ts => ts.map(t => t.textContent.trim()));
  assert(head === 'Давно лежат · 2', head);
  assert(JSON.stringify(titles) === JSON.stringify(['Сад на балконе', 'Дача']), JSON.stringify(titles));
  await page.click('.someday .stale-h .close'); await settle();
  assert(!(await page.$('.someday .stale-h')) && await row('Свежая'), 'обычная лента не вернулась');
});

await check('BS4: о тех же мыслях второй раз не напоминает', async () => {
  await load();
  assert(await toast() === null, `плашка: ${await toast()}`);
});

await check('BS4: одна новая — «„Велосипед“ лежит уже 3 мес · Посмотреть · Оставить»; «Оставить» снимает метку и не напоминает', async () => {
  await thought('Велосипед', 100);
  await load();
  assert(await toast() === '«Велосипед» лежит уже 3 мес · Посмотреть · Оставить', `плашка: ${await toast()}`);
  await page.evaluate(() => [...document.querySelectorAll('.toast .action')].find(b => b.textContent.trim() === 'Оставить').click());
  await settle();
  assert((await row('Велосипед')).age === null, 'метка осталась');
  assert((await row('Велосипед')).title.startsWith('лежит 3 мес'), 'возраст в подсказке — по-прежнему от записи');
  await load();
  assert(await toast() === null, `плашка: ${await toast()}`);
});

await check('BS2: срок в «⚙ Настройки» — 12 мес: меток нет; «своё…» 1 мес; обратно 3', async () => {
  await openThemes(page);
  const pick = async label => {
    await page.click('.editor.settings .stale-term .select-field'); await sleep(300);
    await page.evaluate(l => [...document.querySelectorAll('.options .option')].find(o => o.textContent.trim() === l).click(), label);
    await settle();
  };
  await pick('12 мес');
  assert((await row('Сад на балконе')).age === null, 'метка при сроке 12 мес');
  await pick('своё…');
  const input = await page.$('.editor.settings .custom-term input');
  assert(input, 'нет поля «своё»');
  await input.click({ clickCount: 3 }); await input.type('2'); await page.keyboard.press('Tab'); await settle();
  assert((await api('GET', 'settings')).staleMonths === 2, 'срок 2 не сохранился');
  assert((await row('Сад на балконе')).age === '4 мес' && (await row('Дача')).age === '3 мес', 'метки при сроке 2 мес');
  await pick('3 мес');
  assert((await api('GET', 'settings')).staleMonths === 3, 'срок 3 не сохранился');
  await page.keyboard.press('Escape'); await settle();
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);

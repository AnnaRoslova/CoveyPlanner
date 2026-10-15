// @seed — нужны демо-данные (tools/seed-demo.ps1)
// Перенос дела на другую неделю: поле «Дата» в окне дела (календарь, «сегодня / завтра / через неделю / без даты»);
// плашка «Перенесено на … · Показать». Перетаскиванием — края недели «‹ прошлая неделя / следующая неделя ›» (проступают,
// пока тащат дело) или стрелки «‹ ›» в шапке: задержать ~0,5 с — неделя листается, Esc — возврат.
// Камень на другой неделе остаётся камнем — занятием той же цели там.
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
const settle = () => sleep(500);
function assert(c, m) { if (!c) throw new Error(m); }
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-week-move-${results.length}.png` }); }
  if (await page.$('.editor')) { await page.keyboard.press('Escape'); await settle(); }
}
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day'); await settle(); }
async function clickText(selector, text) {
  const ok = await page.evaluate((s, t) => {
    const el = [...document.querySelectorAll(s)].find(e => e.textContent.trim() === t);
    el?.click();
    return !!el;
  }, selector, text);
  assert(ok, `нет «${text}» (${selector})`);
  await settle();
}

// Даты — по-местному, как в приложении
const iso = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
const today = new Date(); today.setHours(12);
const monday = new Date(today); monday.setDate(today.getDate() - (today.getDay() + 6) % 7);
const day = (weekOffset, dayIndex) => { const d = new Date(monday); d.setDate(monday.getDate() + 7 * weekOffset + dayIndex); return d; };
const SHORT_DAYS = ['Вс', 'Пн', 'Вт', 'Ср', 'Чт', 'Пт', 'Сб'];
const MONTHS = ['янв', 'фев', 'мар', 'апр', 'мая', 'июн', 'июл', 'авг', 'сен', 'окт', 'ноя', 'дек'];
const shortDate = d => `${SHORT_DAYS[d.getDay()]} ${d.getDate()} ${MONTHS[d.getMonth()]}`;

const weekTitle = () => page.$eval('.wt', e => e.textContent);
const dayTitles = i => page.evaluate(i => [...document.querySelectorAll('.day')[i].querySelectorAll('.item .title')].map(t => t.textContent.trim()), i);
const dayItem = (i, title) => page.evaluate((i, t) => [...document.querySelectorAll('.day')[i].querySelectorAll('.item')]
  .find(e => e.querySelector('.title').textContent.trim() === t)?.querySelector('.pill.time')?.textContent ?? (
    [...document.querySelectorAll('.day')[i].querySelectorAll('.item')].some(e => e.querySelector('.title').textContent.trim() === t) ? '' : null), i, title);
const toast = async () => (await page.$('.toast')) ? page.$eval('.toast', e => [...e.querySelectorAll('span, button')].map(x => x.textContent.trim()).join(' ')) : null;
async function openTask(dayIndex, title) {
  await page.evaluate((i, t) => [...document.querySelectorAll('.day')[i].querySelectorAll('.item .title')].find(e => e.textContent.trim() === t).click(), dayIndex, title);
  await settle();
  assert(await page.$('.editor[aria-label="Дело"]'), 'окно дела не открылось');
}
async function setDate(d) {
  await page.$eval('.editor input[type="date"]', (input, v) => {
    input.value = v;
    input.dispatchEvent(new Event('change', { bubbles: true }));
  }, iso(d));
  await settle();
}
const save = async () => { await page.click('.editor .btn:not(.quiet):not(.danger)'); await sleep(900); };
async function goToday() {
  if (await page.$('.today-b')) { await page.click('.today-b'); await sleep(900); }
}
async function serverWeek(offset) {
  return page.evaluate(async w => (await fetch(`/api/week/${w}`)).json(), iso(day(offset, 0)));
}
async function boxOf(js) {
  const el = (await page.evaluateHandle(js)).asElement();
  assert(el, 'элемент не найден');
  return el.boundingBox();
}
const center = b => ({ x: b.x + b.width / 2, y: b.y + b.height / 2 });
async function moveSmooth(from, to, steps = 14) {
  for (let i = 1; i <= steps; i++) { await page.mouse.move(from.x + (to.x - from.x) * i / steps, from.y + (to.y - from.y) * i / steps); await sleep(16); }
}

await load();

await check('в окне дела — поле «Дата» с кнопками «сегодня», «завтра», «через неделю», «→ след. неделя, без дня» (N4), «без даты»', async () => {
  await openTask(0, 'Оплатить интернет');
  assert(await page.$eval('.editor input[type="date"]', i => i.value) === iso(day(0, 0)), 'в поле не дата дела');
  const chips = await page.$$eval('.editor .date-row .chip', cs => cs.map(c => c.textContent.trim()));
  assert(JSON.stringify(chips) === '["сегодня","завтра","через неделю","→ след. неделя, без дня","без даты"]', `кнопки: ${chips}`);
  await page.screenshot({ path: `${SHOTS}/week-move-editor.png` });
});

await check('«через неделю» — дело уходит на следующую неделю; плашка «Перенесено на … · Показать» листает туда', async () => {
  await openTask(0, 'Оплатить интернет');
  await clickText('.editor .date-row .chip', 'через неделю');
  await save();
  assert(!(await dayTitles(0)).includes('Оплатить интернет'), 'осталось на этой неделе');
  const target = new Date(today); target.setDate(today.getDate() + 7);
  assert(await toast() === `Перенесено на ${shortDate(target)} · Показать`, `плашка: ${await toast()}`);
  const before = await weekTitle();
  await page.click('.toast .action'); await sleep(900);
  assert(await weekTitle() !== before, 'неделя не перелистнулась');
  const idx = (target.getDay() + 6) % 7;
  assert((await dayTitles(idx)).includes('Оплатить интернет'), `нет в ${SHORT_DAYS[target.getDay()]}: ${await dayTitles(idx)}`);
  await goToday();
});

await check('дата в календаре: дело со временем — на другую неделю, время остаётся', async () => {
  await openTask(1, 'Созвон с заказчиком');
  await setDate(day(2, 2));
  await save();
  assert(!(await dayTitles(1)).includes('Созвон с заказчиком'), 'осталось во вторнике');
  const week = await serverWeek(2);
  const t = week.tasks.find(t => t.title === 'Созвон с заказчиком');
  assert(t && t.plannedDate === iso(day(2, 2)) && t.startTime?.startsWith('11:00'), `на сервере: ${JSON.stringify(t)}`);
});

await check('«без даты» — дело возвращается в ленту («неразобранное»)', async () => {
  await openTask(3, 'Написать отчёт');
  await clickText('.editor .date-row .chip', 'без даты');
  assert((await page.$eval('.editor .date-hint', e => e.textContent)).includes('неразобранное'), 'нет подсказки');
  await save();
  assert(!(await dayTitles(3)).includes('Написать отчёт'), 'осталось в четверге');
  const unsorted = await page.$$eval('.unsorted .item .title', ts => ts.map(t => t.textContent.trim()));
  assert(unsorted.includes('Написать отчёт'), `неразобранное: ${unsorted}`);
});

await check('камень через окно на другую неделю: там — занятие той же цели, здесь прогресс меньше', async () => {
  await openTask(3, 'Демо для команды');
  await setDate(day(1, 1));
  assert((await page.$eval('.editor .date-hint', e => e.textContent)).includes('станет занятием цели «Демо для команды»'), 'нет подсказки про цель');
  await save();
  const here = (await serverWeek(0)).goals.find(g => g.title === 'Демо для команды');
  assert(here && here.sessions.length === 0, `цель этой недели: ${JSON.stringify(here)}`);
  const there = (await serverWeek(1)).goals.find(g => g.title === 'Демо для команды');
  assert(there && there.sessions.length === 1 && there.timesPerWeek === 1, `цель следующей недели: ${JSON.stringify(there)}`);
  const days = await page.evaluate(() => [...document.querySelectorAll('.role .item')].find(i => i.querySelector('.title').textContent.trim() === 'Демо для команды').querySelectorAll('.goal-days .d').length);
  assert(days === 0, 'у цели на экране остался день занятия');
});

await check('перетаскивание: задержать над «›» — неделя листается, бросить в день новой недели', async () => {
  await goToday();
  const before = await weekTitle();
  const from = center(await boxOf(`[...document.querySelectorAll('.day')[5].querySelectorAll('.item')].find(i => i.querySelector('.title').textContent.trim() === 'Кофе с Олей')`));
  const next = center(await boxOf(`document.querySelector('.nav[title="Следующая неделя"]')`));
  await page.mouse.move(from.x, from.y); await page.mouse.down();
  await moveSmooth(from, next);
  await sleep(300);
  assert(await page.$('.nav.flip-armed'), 'стрелка не «заряжается»');
  await sleep(700);
  // Сразу уходим со стрелки (до снимка): держать дольше ~1,2 с — перелистнёт ещё раз
  await page.mouse.move(next.x + 30, next.y + 200); await sleep(200);
  await page.screenshot({ path: `${SHOTS}/week-move-flip.png` });
  assert(await weekTitle() !== before, 'неделя не перелистнулась');
  const after = await weekTitle();
  const mon = center(await boxOf(`document.querySelectorAll('.day')[0]`));
  await moveSmooth({ x: next.x + 30, y: next.y + 200 }, mon);
  await sleep(200);
  await page.mouse.up(); await sleep(900);
  assert(await weekTitle() === after, 'после броска неделя сменилась');
  assert((await dayTitles(0)).includes('Кофе с Олей'), `в понедельнике: ${await dayTitles(0)}`);
  const t = (await serverWeek(1)).tasks.find(t => t.title === 'Кофе с Олей');
  assert(t?.plannedDate === iso(day(1, 0)), `на сервере: ${JSON.stringify(t)}`);
});

await check('перетаскивание: долго держать над «›» — листает несколько недель; Esc — неделя и дело возвращаются', async () => {
  await goToday();
  const before = await weekTitle();
  const sat = await dayTitles(5);
  const from = center(await boxOf(`[...document.querySelectorAll('.day')[5].querySelectorAll('.item')].find(i => i.querySelector('.title').textContent.trim() === 'Поход в парк с детьми')`));
  const next = center(await boxOf(`document.querySelector('.nav[title="Следующая неделя"]')`));
  await page.mouse.move(from.x, from.y); await page.mouse.down();
  await moveSmooth(from, next);
  await sleep(1500); // ~две недели
  const far = await weekTitle();
  assert(far !== before, 'не листается');
  await page.keyboard.press('Escape'); await sleep(900);
  await page.mouse.up(); await settle();
  assert(await weekTitle() === before, `неделя не вернулась: ${await weekTitle()}`);
  assert(JSON.stringify(await dayTitles(5)) === JSON.stringify(sat), 'дело сдвинулось');
});

await check('камень перетаскиванием на следующую неделю — занятие той же цели там', async () => {
  await goToday();
  const from = center(await boxOf(`[...document.querySelectorAll('.day')[2].querySelectorAll('.item')].find(i => i.querySelector('.title').textContent.trim() === 'Записаться к стоматологу')`));
  const next = center(await boxOf(`document.querySelector('.nav[title="Следующая неделя"]')`));
  await page.mouse.move(from.x, from.y); await page.mouse.down();
  await moveSmooth(from, next);
  await sleep(1000);
  await page.mouse.move(next.x + 30, next.y + 200); await sleep(200);
  const wed = center(await boxOf(`document.querySelectorAll('.day')[2]`));
  await moveSmooth({ x: next.x + 30, y: next.y + 200 }, wed);
  await sleep(200);
  await page.mouse.up(); await sleep(1200);
  const rock = await page.evaluate(() => !![...document.querySelectorAll('.day')[2].querySelectorAll('.item')]
    .find(i => i.querySelector('.title').textContent.trim() === 'Записаться к стоматологу')?.querySelector('.pill.rock'));
  assert(rock, 'на новой неделе — не камень');
  const goals = await page.$$eval('.role .item .title', ts => ts.map(t => t.textContent.trim()));
  assert(goals.includes('Записаться к стоматологу'), `целей на новой неделе: ${goals}`);
});

await check('края недели: проступают, только пока тащат дело; держать над «следующая неделя ›» ~0,5 с — неделя листается, мысль из ленты — в день', async () => {
  await goToday();
  const visible = () => page.$$eval('.flip-zone', zs => zs.map(z => getComputedStyle(z).display !== 'none'));
  assert(JSON.stringify(await visible()) === '[false,false]', 'края видны без перетаскивания');
  const before = await weekTitle();
  const from = center(await boxOf(`[...document.querySelectorAll('.cat-b .item')].find(i => i.querySelector('.title').textContent.trim() === 'Сад с травами на балконе')`));
  await page.mouse.move(from.x, from.y); await page.mouse.down();
  await moveSmooth(from, { x: from.x - 80, y: from.y });
  assert(JSON.stringify(await visible()) === '[true,true]', 'края не проступили');
  const zone = center(await boxOf(`document.querySelector('.flip-zone.next')`));
  await moveSmooth({ x: from.x - 80, y: from.y }, zone);
  await sleep(250);
  assert(await page.$('.flip-zone.next.flip-armed'), 'край не «заряжается»');
  await page.screenshot({ path: `${SHOTS}/week-move-zone.png` });
  await sleep(450);
  assert(await weekTitle() !== before, 'неделя не перелистнулась');
  const wed = center(await boxOf(`document.querySelectorAll('.day')[2]`));
  await moveSmooth(zone, wed);
  await sleep(200);
  await page.mouse.up(); await sleep(900);
  assert((await dayTitles(2)).includes('Сад с травами на балконе'), `в среде: ${await dayTitles(2)}`);
  assert(JSON.stringify(await visible()) === '[false,false]', 'края остались после сброса');
});

await check('края не проступают, когда тащат цель', async () => {
  await goToday();
  const from = center(await boxOf(`[...document.querySelectorAll('.role .item')].find(i => i.querySelector('.title').textContent.trim() === 'Бег')`));
  await page.mouse.move(from.x, from.y); await page.mouse.down();
  await moveSmooth(from, { x: from.x + 120, y: from.y + 40 });
  const shown = await page.$$eval('.flip-zone', zs => zs.some(z => getComputedStyle(z).display !== 'none'));
  await page.keyboard.press('Escape'); await page.mouse.up(); await settle();
  assert(!shown, 'края видны при перетаскивании цели');
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));

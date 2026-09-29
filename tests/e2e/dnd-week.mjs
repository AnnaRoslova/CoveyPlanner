// @seed — нужны демо-данные (tools/seed-demo.ps1)
// Шаг 10: лента → день, день → лента, между днями, порядок в дне; дела со временем всегда сверху по времени.
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
page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text() + ' ' + (m.location()?.url ?? '')); });
page.on('pageerror', e => consoleErrors.push(e.message));
const sleep = ms => new Promise(r => setTimeout(r, ms));
const settle = () => sleep(550);
function assert(c, m) { if (!c) throw new Error(m); }
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-dndweek-${results.length}.png` }); }
}
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day .item'); await settle(); }

const titleOf = el => [...el.querySelector('.tx').childNodes].filter(n => !n.classList?.contains('pills') && !n.classList?.contains('note')).map(n => n.textContent).join('').trim();
const day = i => page.evaluate((i, titleOfSrc) => {
  const titleOf = new Function('return ' + titleOfSrc)();
  return [...document.querySelectorAll('.day')[i].querySelectorAll('.item')].map(it => ({
    title: titleOf(it), time: it.querySelector('.pill.time')?.textContent ?? null,
    rock: !!it.querySelector('.pill.rock'), done: it.classList.contains('done'),
  }));
}, i, titleOf.toString());
const dayTitles = async i => (await day(i)).map(t => t.title);
const feed = name => page.evaluate(n => {
  const cat = [...document.querySelectorAll('.cat')].find(c => c.querySelector('.cat-h .name')?.textContent.trim() === n);
  return cat ? [...cat.querySelectorAll('.cat-b .item .tx')].map(e => e.childNodes[0].textContent.trim()) : null;
}, name);
const countOf = name => page.evaluate(n => [...document.querySelectorAll('.cat')].find(c => c.querySelector('.cat-h .name')?.textContent.trim() === n)?.querySelector('.cnt')?.textContent, name);
const goalWhen = t => page.evaluate(t => [...document.querySelectorAll('.role .item')].find(r => r.querySelector('.tx').textContent.includes(t))?.querySelector('.pill.when')?.textContent ?? null, t);

async function boxOf(fnSrc) {
  const h = await page.evaluateHandle(fnSrc);
  const el = h.asElement();
  assert(el, 'элемент не найден');
  return await el.boundingBox();
}
const dayItemBox = (i, title) => boxOf(`(() => { const titleOf = ${titleOf.toString()}; return [...document.querySelectorAll('.day')[${i}].querySelectorAll('.item')].find(it => titleOf(it) === ${JSON.stringify(title)}); })()`);
const feedItemBox = (cat, title) => boxOf(`[...document.querySelectorAll('.cat')].find(c => c.querySelector('.cat-h .name')?.textContent.trim() === ${JSON.stringify(cat)})?.querySelectorAll('.cat-b .item').values().find(i => i.querySelector('.tx').childNodes[0].textContent.trim() === ${JSON.stringify(title)})`);
const dayBox = i => boxOf(`document.querySelectorAll('.day')[${i}]`);
const headerBox = cat => boxOf(`[...document.querySelectorAll('.cat-h')].find(h => h.querySelector('.name')?.textContent.trim() === ${JSON.stringify(cat)})`);

async function mouseDrag(from, x, y, { beforeUp } = {}) {
  const sx = from.x + Math.min(40, from.width / 2), sy = from.y + from.height / 2;
  await page.mouse.move(sx, sy);
  await page.mouse.down();
  for (let i = 1; i <= 14; i++) { await page.mouse.move(sx + (x - sx) * i / 14, sy + (y - sy) * i / 14); await sleep(16); }
  await sleep(150);
  if (beforeUp) await beforeUp();
  await page.mouse.up();
  await settle();
}

await load();

await check('лента → день под делом со временем; время не появляется; после перезагрузки на месте', async () => {
  const from = await feedItemBox('Идеи', 'Сделать фотокнигу за лето');
  const fri = await dayBox(4);
  await mouseDrag(from, fri.x + 40, fri.y + 300, {
    beforeUp: async () => { await page.screenshot({ path: `${SHOTS}/dnd-week-during.png` }); },
  });
  const f = await day(4);
  assert(JSON.stringify(f.map(t => t.title)) === '["Ужин с семьёй","Сделать фотокнигу за лето"]', `Пт: ${JSON.stringify(f)}`);
  assert(f[1].time === null, 'появилось время');
  assert(!(await feed('Идеи')).includes('Сделать фотокнигу за лето'), 'осталось в ленте');
  await load();
  assert((await dayTitles(4))[1] === 'Сделать фотокнигу за лето', 'после перезагрузки');
});

await check('лента → день между делами без времени', async () => {
  const from = await feedItemBox('Идеи', 'Сад с травами на балконе');
  const target = await dayItemBox(0, 'Оплатить интернет');
  await mouseDrag(from, target.x + 40, target.y + 4);
  const mon = await dayTitles(0);
  assert(JSON.stringify(mon) === '["Бег в парке","Купить подарок маме","Сад с травами на балконе","Оплатить интернет"]', `Пн: ${mon}`);
});

await check('порядок внутри дня', async () => {
  const from = await dayItemBox(0, 'Оплатить интернет');
  const target = await dayItemBox(0, 'Купить подарок маме');
  await mouseDrag(from, target.x + 40, target.y + 4);
  const mon = await dayTitles(0);
  assert(JSON.stringify(mon) === '["Бег в парке","Оплатить интернет","Купить подарок маме","Сад с травами на балконе"]', `Пн: ${mon}`);
  await load();
  assert(JSON.stringify(await dayTitles(0)) === JSON.stringify(mon), 'после перезагрузки');
});

await check('дело без времени нельзя поставить выше дел со временем', async () => {
  const from = await dayItemBox(0, 'Сад с травами на балконе');
  const top = await dayItemBox(0, 'Бег в парке');
  let lineY = null;
  await mouseDrag(from, top.x + 40, top.y + 3, {
    beforeUp: async () => { lineY = await page.$eval('.drop-line', l => l.hidden ? null : l.getBoundingClientRect().top); },
  });
  assert(lineY !== null && lineY > top.y + top.height - 4, `линия ${lineY} выше/на деле со временем (его низ ${top.y + top.height})`);
  const mon = await dayTitles(0);
  assert(mon[0] === 'Бег в парке' && mon[1] === 'Сад с травами на балконе', `Пн: ${mon}`);
});

await check('между днями: в конец субботы', async () => {
  const from = await dayItemBox(3, 'Написать отчёт');
  const sat = await dayBox(5);
  await mouseDrag(from, sat.x + 40, sat.y + 350);
  assert(JSON.stringify(await dayTitles(5)) === '["Поход в парк с детьми","Кофе с Олей","Написать отчёт"]', `Сб: ${await dayTitles(5)}`);
  assert(!(await dayTitles(3)).includes('Написать отчёт'), 'осталось в четверг');
});

await check('дело со временем в другой день: время сохраняется, встаёт по времени, линии нет', async () => {
  const from = await dayItemBox(1, 'Созвон с заказчиком');
  const wed = await dayBox(2);
  let line = 'нет данных';
  await mouseDrag(from, wed.x + 40, wed.y + 300, { beforeUp: async () => { line = await page.$eval('.drop-line', l => l.hidden); } });
  assert(line === true, 'для дела со временем показана линия');
  const w = await day(2);
  assert(w[0].title === 'Созвон с заказчиком' && w[0].time === '11:00', `Ср: ${JSON.stringify(w)}`);
  await load();
  assert((await day(2))[0].time === '11:00', 'после перезагрузки');
});

await check('«камень» в другой день — у цели слева новый день', async () => {
  assert(await goalWhen('Демо для команды') === 'Чт 15:00', 'исходно');
  const from = await dayItemBox(3, 'Демо для команды');
  const fri = await dayBox(4);
  await mouseDrag(from, fri.x + 40, fri.y + 300);
  assert(await goalWhen('Демо для команды') === 'Пт 15:00', `у цели: ${await goalWhen('Демо для команды')}`);
  const f = await day(4);
  assert(f.find(t => t.title === 'Демо для команды')?.rock, 'пропала метка «камень»');
});

await check('выполненное дело в другой день остаётся выполненным', async () => {
  const from = await dayItemBox(0, 'Купить подарок маме');
  const sun = await dayBox(6);
  await mouseDrag(from, sun.x + 40, sun.y + 200);
  const s = await day(6);
  assert(s.length === 1 && s[0].title === 'Купить подарок маме' && s[0].done, `Вс: ${JSON.stringify(s)}`);
});

await check('день → лента: в категорию на место, без даты; после перезагрузки там же', async () => {
  const from = await dayItemBox(0, 'Оплатить интернет');
  const target = await feedItemBox('Книги и фильмы', '«7 навыков» — перечитать главу 3');
  await mouseDrag(from, target.x + 40, target.y + 4);
  assert((await feed('Книги и фильмы'))[0] === 'Оплатить интернет', `Книги: ${await feed('Книги и фильмы')}`);
  assert(!(await dayTitles(0)).includes('Оплатить интернет'), 'осталось в понедельнике');
  await load();
  assert((await feed('Книги и фильмы'))[0] === 'Оплатить интернет', 'после перезагрузки');
});

await check('«камень» из дня в ленту: цель остаётся без дня, счётчик камней тот же', async () => {
  const rocksBefore = await page.$eval('.rocks b', b => b.textContent);
  const from = await dayItemBox(5, 'Поход в парк с детьми');
  const target = await feedItemBox('Идеи', 'Научиться печь хлеб на закваске');
  await mouseDrag(from, target.x + 40, target.y + target.height - 3);
  assert((await feed('Идеи')).includes('Поход в парк с детьми'), `Идеи: ${await feed('Идеи')}`);
  assert(await goalWhen('Поход в парк с детьми') === null, 'у цели осталась метка дня');
  assert(await page.$eval('.rocks b', b => b.textContent) === rocksBefore, 'изменился счётчик камней');
});

await check('день → заголовок свёрнутой категории', async () => {
  const before = Number(await countOf('Дом'));
  const from = await dayItemBox(4, 'Сделать фотокнигу за лето');
  const header = await headerBox('Дом');
  await mouseDrag(from, header.x + 60, header.y + header.height / 2);
  assert(Number(await countOf('Дом')) === before + 1, `Дом: ${await countOf('Дом')}`);
});

await check('клик по делу в дне открывает окно, а после перетаскивания — нет', async () => {
  assert(!(await page.$('.editor')), 'окно открылось после перетаскивания');
  const b = await dayItemBox(2, 'Записаться к стоматологу');
  await page.mouse.click(b.x + 50, b.y + b.height / 2); await settle();
  assert(await page.$('.editor'), 'клик не открыл окно');
  await page.keyboard.press('Escape'); await settle();
});

await browser.close();
console.log(results.join('\n'));
console.log(consoleErrors.length ? `\nОшибки консоли:\n${consoleErrors.join('\n')}` : '\nОшибок в консоли нет');

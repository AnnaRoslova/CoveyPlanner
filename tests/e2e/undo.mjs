// @seed — нужны демо-данные (tools/seed-demo.ps1)
// Защита от случайного удаления: после удаления — плашка «Удалено «…» · Вернуть» на 6 секунд; «Вернуть» отменяет полностью
// (на сервер удаление уходит только через 6 секунд или перед следующим изменением). Крестиков в строках ленты и дней нет (у дел в днях — 🗑 при наведении, см. day-trash.mjs):
// удаляют из окна или перетаскиванием на значок корзины (он подсвечивается).
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
const settle = () => sleep(450);
function assert(c, m) { if (!c) throw new Error(m); }
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-undo-${results.length}.png` }); }
  if (await page.$('.editor')) { await page.keyboard.press('Escape'); await settle(); }
}
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day'); await settle(); }
const api = path => page.evaluate(async p => (await fetch(`/api/${p}`)).json(), path);
const weekStart = () => page.evaluate(() => {
  const d = new Date(); d.setDate(d.getDate() - (d.getDay() + 6) % 7);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
});

const dayTitles = i => page.evaluate(i => [...document.querySelectorAll('.day')[i].querySelectorAll('.item .title')]
  .map(t => t.textContent.trim()), i);
const feedTitles = name => page.evaluate(n => {
  const cat = [...document.querySelectorAll('.cat')].find(c => c.querySelector('.cat-h .name')?.textContent.trim() === n);
  return cat ? [...cat.querySelectorAll('.cat-b .item .title')].map(t => t.textContent.trim()) : null;
}, name);
const goals = () => page.$$eval('.role .item .title', ts => ts.map(t => t.textContent.trim()));
const roleNames = () => page.$$eval('.role-h .name', ns => ns.map(n => n.textContent.trim()));
const categoryNames = () => page.$$eval('.cat-h .name', ns => ns.map(n => n.textContent.trim()));
// Текст плашки: «сообщение · кнопка» (отступы вокруг «·» — в CSS)
const toast = async () => (await page.$('.toast')) ? (await page.$eval('.toast', e => [...e.querySelectorAll('span, button')]
  .map(x => x.textContent.trim()).join(' '))) : null;
const undo = async () => { await page.click('.toast .action'); await settle(); };
async function clickText(selector, text) {
  const ok = await page.evaluate((s, t) => {
    const el = [...document.querySelectorAll(s)].find(e => e.textContent.trim() === t);
    el?.click();
    return !!el;
  }, selector, text);
  assert(ok, `нет кнопки «${text}» (${selector})`);
  await settle();
}
async function openDayTask(day, title) {
  await page.evaluate((d, t) => [...document.querySelectorAll('.day')[d].querySelectorAll('.item .title')]
    .find(e => e.textContent.trim() === t).click(), day, title);
  await settle();
  assert(await page.$('.editor'), 'окно не открылось');
}
async function boxOf(js) {
  const el = (await page.evaluateHandle(js)).asElement();
  assert(el, 'элемент не найден');
  return el.boundingBox();
}
async function dragTo(from, to, { beforeUp } = {}) {
  const sx = from.x + Math.min(40, from.width / 2), sy = from.y + from.height / 2;
  const x = to.x + to.width / 2, y = to.y + to.height / 2;
  await page.mouse.move(sx, sy);
  await page.mouse.down();
  for (let i = 1; i <= 14; i++) { await page.mouse.move(sx + (x - sx) * i / 14, sy + (y - sy) * i / 14); await sleep(16); }
  await sleep(150);
  if (beforeUp) await beforeUp();
  await page.mouse.up();
  await sleep(700); // после сброса dnd.js полсекунды гасит клики
}

await load();
const monday = await weekStart();

await check('в строках ленты и дней нет крестика удаления', async () => {
  const crosses = await page.$$eval('.someday .item .row-action, .day .item .row-action', bs => bs.length);
  assert(crosses === 0, `крестиков: ${crosses}`);
});

await check('«в корзину» из окна — плашка «Удалено «…» · Вернуть»; «Вернуть» ставит дело на прежнее место, сервер не трогали', async () => {
  const before = await dayTitles(0);
  await openDayTask(0, 'Купить подарок маме');
  await page.click('.editor .more-b'); await sleep(300); await clickText('.editor .popover .option .option-name', 'в корзину');
  assert(!(await dayTitles(0)).includes('Купить подарок маме'), 'не исчезло');
  assert(await toast() === '«Купить подарок маме» в корзине · Вернуть', `плашка: ${await toast()}`);
  await undo();
  assert(JSON.stringify(await dayTitles(0)) === JSON.stringify(before), `после «Вернуть»: ${await dayTitles(0)}`);
  assert(await toast() === null, 'плашка осталась');
  await sleep(6500);
  assert((await api('board')).trashCount === 0, 'на сервере удалилось');
  await load();
  assert(JSON.stringify(await dayTitles(0)) === JSON.stringify(before), 'после перезагрузки');
});

await check('без «Вернуть» плашка через 6 с исчезает, дело — в корзине на сервере', async () => {
  await openDayTask(0, 'Оплатить интернет');
  await page.click('.editor .more-b'); await sleep(300); await clickText('.editor .popover .option .option-name', 'в корзину');
  await sleep(3000);
  assert(await toast() !== null, 'плашка исчезла раньше времени');
  assert((await api('board')).trashCount === 0, 'на сервер ушло раньше времени');
  await sleep(3800);
  assert(await toast() === null, 'плашка не исчезла');
  assert((await api('board')).trashCount === 1, 'на сервере не удалилось');
});

await check('перетаскивание на значок корзины: он подсвечивается, мысль удаляется; «Вернуть» — на место', async () => {
  const before = await feedTitles('Идеи');
  const from = await boxOf(`[...document.querySelectorAll('.cat-b .item')].find(i => i.querySelector('.title').textContent.trim() === 'Сад с травами на балконе')`);
  let lit = false;
  await dragTo(from, await boxOf(`document.querySelector('.someday .trash')`), {
    beforeUp: async () => {
      lit = await page.$eval('.someday .trash', t => t.classList.contains('drop-hover'));
      await page.screenshot({ path: `${SHOTS}/undo-trash-hover.png` });
    },
  });
  assert(lit, 'корзина не подсветилась');
  assert(!(await page.$('.someday .trash.drop-hover')), 'подсветка осталась после сброса');
  assert(!(await feedTitles('Идеи')).includes('Сад с травами на балконе'), 'не удалилось');
  assert(await toast() === '«Сад с травами на балконе» в корзине · Вернуть', `плашка: ${await toast()}`);
  await undo();
  assert(JSON.stringify(await feedTitles('Идеи')) === JSON.stringify(before), `после «Вернуть»: ${await feedTitles('Идеи')}`);
});

await check('дело из дня → на корзину; следующее изменение сразу отправляет удаление (и плашка уходит)', async () => {
  const trash = (await api('board')).trashCount;
  const from = await boxOf(`[...document.querySelectorAll('.day')[4].querySelectorAll('.item')].find(i => i.querySelector('.title').textContent.trim() === 'Ужин с семьёй')`);
  await dragTo(from, await boxOf(`document.querySelector('.someday .trash')`));
  assert(!(await dayTitles(4)).includes('Ужин с семьёй'), 'не удалилось');
  assert(await toast() !== null, 'нет плашки');
  // Отметить другое дело — удаление уходит на сервер сразу
  await page.evaluate(() => [...document.querySelectorAll('.day')[1].querySelectorAll('.item')]
    .find(i => i.querySelector('.title').textContent.trim() === 'Созвон с заказчиком').querySelector('.ck-hit').click());
  await settle();
  assert(await toast() === null, 'плашка осталась');
  assert((await api('board')).trashCount === trash + 1, 'на сервер не ушло');
});

await check('удалить цель: цель и невыполненное занятие исчезают; «Вернуть» — всё как было', async () => {
  const goalsBefore = await goals();
  const thuBefore = await dayTitles(3);
  const row = await page.evaluateHandle(() => [...document.querySelectorAll('.role .item')].find(i => i.querySelector('.title').textContent.trim() === 'Демо для команды'));
  await row.hover();
  await (await row.$('.row-action[title="Удалить цель"]')).click(); await settle();
  assert(!(await goals()).includes('Демо для команды'), 'цель осталась');
  assert(!(await dayTitles(3)).includes('Демо для команды'), 'занятие осталось в дне');
  assert(await toast() === 'Удалено «Демо для команды» · Вернуть', `плашка: ${await toast()}`);
  await undo();
  assert(JSON.stringify(await goals()) === JSON.stringify(goalsBefore), 'цели не вернулись');
  assert(JSON.stringify(await dayTitles(3)) === JSON.stringify(thuBefore), 'занятие не вернулось');
  const rock = await page.evaluate(() => !![...document.querySelectorAll('.day')[3].querySelectorAll('.item')]
    .find(i => i.querySelector('.title').textContent.trim() === 'Демо для команды')?.querySelector('.title.rock'));
  assert(rock, 'нет метки «камень»');
  await sleep(6500);
  const week = await api(`week/${monday}`);
  assert(week.goals.some(g => g.title === 'Демо для команды'), 'на сервере удалилась');
});

await check('удалить цель без «Вернуть»: через 6 с на сервере удалена, занятие — в корзине', async () => {
  const trash = (await api('board')).trashCount;
  const row = await page.evaluateHandle(() => [...document.querySelectorAll('.role .item')].find(i => i.querySelector('.title').textContent.trim() === 'Демо для команды'));
  await row.hover();
  await (await row.$('.row-action[title="Удалить цель"]')).click();
  await sleep(6800);
  const week = await api(`week/${monday}`);
  assert(!week.goals.some(g => g.title === 'Демо для команды'), 'цель на сервере осталась');
  assert((await api('board')).trashCount === trash + 1, 'занятие не в корзине');
  assert(!(await dayTitles(3)).includes('Демо для команды'), 'на экране занятие вернулось');
});

await check('удалить категорию вместе с делами — «Вернуть» возвращает категорию и дела', async () => {
  const before = await feedTitles('Книги и фильмы');
  const head = await page.evaluateHandle(() => [...document.querySelectorAll('.cat-h')].find(h => h.querySelector('.name').textContent.trim() === 'Книги и фильмы'));
  await head.hover();
  await (await head.$('.menu')).click(); await settle();
  await clickText('.cat-editor .btn', 'удалить');
  await clickText('.cat-editor .btn', 'Удалить вместе с делами');
  assert(!(await categoryNames()).includes('Книги и фильмы'), 'категория осталась');
  assert(await toast() === 'Удалено «Книги и фильмы» · Вернуть', `плашка: ${await toast()}`);
  await undo();
  assert(JSON.stringify(await feedTitles('Книги и фильмы')) === JSON.stringify(before), `дела: ${await feedTitles('Книги и фильмы')}`);
  await load();
  assert(JSON.stringify(await feedTitles('Книги и фильмы')) === JSON.stringify(before), 'после перезагрузки');
});

await check('удалить роль — «Вернуть» возвращает роль, её цели и роль у дел', async () => {
  const roles = await roleNames();
  const goalsBefore = await goals();
  const head = await page.evaluateHandle(() => [...document.querySelectorAll('.role-h')].find(h => h.querySelector('.name').textContent.trim() === 'Семья'));
  await head.hover();
  await (await head.$('.menu')).click(); await settle();
  await page.waitForSelector('.popover.more-menu .option.edit-role'); await page.click('.popover.more-menu .option.edit-role'); await settle();
  await clickText('.role-editor .btn', 'удалить');
  await clickText('.role-editor .btn', 'Удалить');
  assert(!(await roleNames()).includes('Семья'), 'роль осталась');
  assert(!(await goals()).includes('Поход в парк с детьми'), 'цель роли осталась');
  await undo();
  assert(JSON.stringify(await roleNames()) === JSON.stringify(roles), `роли: ${await roleNames()}`);
  assert(JSON.stringify(await goals()) === JSON.stringify(goalsBefore), 'цели не вернулись');
  await load();
  assert((await roleNames()).includes('Семья'), 'после перезагрузки роли нет');
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));

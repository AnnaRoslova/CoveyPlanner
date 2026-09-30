// @seed — нужны демо-данные (tools/seed-demo.ps1)
// Шаг 7: неделя — «+ дело», отметка «выполнено», окно редактирования. На демо-данных (seed-demo.ps1).
import puppeteer from 'puppeteer-core';

const BASE = process.env.PLANNER_URL ?? 'http://localhost:5071/';
const SHOTS = process.argv[2] ?? '.';
const results = [];
const consoleErrors = [];

const browser = await puppeteer.launch({
  executablePath: process.env.PLANNER_BROWSER ?? 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  headless: true,
  defaultViewport: { width: 1600, height: 900 },
});
const page = await browser.newPage();
page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text() + ' ' + (m.location()?.url ?? '')); });
page.on('pageerror', e => consoleErrors.push(e.message));

const sleep = ms => new Promise(r => setTimeout(r, ms));
const settle = () => sleep(450);
function assert(cond, msg) { if (!cond) throw new Error(msg); }
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-week-${results.length}.png` }); }
}
async function load() {
  await page.goto(BASE, { waitUntil: 'networkidle0' });
  await page.waitForSelector('.day', { timeout: 20000 });
  await settle();
}

// Дела дня (0 = понедельник): название без пилюль, время, отметки
const day = i => page.evaluate(i => {
  const d = document.querySelectorAll('.day')[i];
  return [...d.querySelectorAll('.item')].map(item => {
    const tx = item.querySelector('.tx');
    const title = [...tx.childNodes].filter(n => !n.classList?.contains('pills')).map(n => n.textContent).join('').trim();
    return {
      title,
      time: item.querySelector('.pill.time')?.textContent ?? null,
      rock: !!item.querySelector('.pill.rock'),
      done: item.classList.contains('done'),
      color: item.style.getPropertyValue('--c').trim() || null,
    };
  });
}, i);
const titles = async i => (await day(i)).map(t => t.title);

async function itemHandle(dayIndex, title) {
  const items = await page.$$(`.day:nth-of-type(${dayIndex + 1}) .item`);
  for (const h of items) {
    const t = await h.evaluate(el => [...el.querySelector('.tx').childNodes]
      .filter(n => !n.classList?.contains('pills')).map(n => n.textContent).join('').trim());
    if (t === title) return h;
  }
  throw new Error(`нет дела «${title}» в дне ${dayIndex}`);
}
async function openEditor(dayIndex, title) {
  const h = await itemHandle(dayIndex, title);
  await h.$eval('.tx', el => el.click());
  await page.waitForSelector('.editor', { timeout: 3000 });
  await sleep(200);
}
const goal = title => page.evaluate(t => {
  const row = [...document.querySelectorAll('.role .item')].find(r => r.querySelector('.tx').textContent.includes(t));
  return row ? { text: row.querySelector('.tx').textContent.trim(), when: row.querySelector('.pill.when')?.textContent ?? null, done: row.classList.contains('done') } : null;
}, title);
const rocks = () => page.$eval('.rocks b', b => b.textContent);

await load();

await check('«+ дело»: в конец дня, поле остаётся; Esc закрывает; после перезагрузки на месте', async () => {
  await page.click('.day:nth-of-type(1) .add'); await settle();
  await page.type('.day:nth-of-type(1) .inline-input', 'Новое дело'); await page.keyboard.press('Enter'); await settle();
  assert(await page.$('.day:nth-of-type(1) .inline-input'), 'поле закрылось');
  await page.keyboard.press('Escape'); await settle();
  const t = await titles(0);
  assert(t[t.length - 1] === 'Новое дело', `Пн: ${t}`);
  await load();
  const t2 = await titles(0);
  assert(t2[t2.length - 1] === 'Новое дело', `после перезагрузки Пн: ${t2}`);
});

await check('чекбокс: отметить и снять, запоминается; клик по чекбоксу не открывает окно', async () => {
  const h = await itemHandle(0, 'Оплатить интернет');
  await (await h.$('.ck')).click(); await settle();
  assert(!(await page.$('.editor')), 'открылось окно редактирования');
  assert((await day(0)).find(t => t.title === 'Оплатить интернет').done, 'не отметилось');
  await load();
  assert((await day(0)).find(t => t.title === 'Оплатить интернет').done, 'после перезагрузки не отмечено');
  await (await (await itemHandle(0, 'Оплатить интернет')).$('.ck')).click(); await settle();
  assert(!(await day(0)).find(t => t.title === 'Оплатить интернет').done, 'не снялось');
});

await check('отметка «камня» отмечает и цель слева, счётчик камней растёт', async () => {
  const before = await rocks();
  await (await (await itemHandle(2, 'Записаться к стоматологу')).$('.ck')).click(); await settle();
  const g = await goal('Записаться к стоматологу');
  assert(g?.done, `цель: ${JSON.stringify(g)}`);
  const after = await rocks();
  assert(after !== before && after.startsWith(String(Number(before[0]) + 1)), `камней было ${before}, стало ${after}`);
  await load();
  assert((await goal('Записаться к стоматологу')).done, 'после перезагрузки цель не отмечена');
});

await check('окно: название, роль, время, квадрант, заметки → запись с временем встаёт первой', async () => {
  await openEditor(3, 'Написать отчёт');
  await page.$eval('.editor .title-input', e => e.select());
  await page.type('.editor .title-input', 'Написать отчёт за сентябрь');
  await (await page.evaluateHandle(() => [...document.querySelectorAll('.editor .chip')].find(c => c.textContent.trim() === 'Работа'))).click();
  await page.$eval('.editor input[type=time]', e => e.focus());
  await page.keyboard.type('0915');
  await (await page.evaluateHandle(() => [...document.querySelectorAll('.editor .quadrants button')].find(b => b.textContent.trim() === 'II'))).click();
  await page.type('.editor textarea', 'черновик у Маши');
  await (await page.evaluateHandle(() => [...document.querySelectorAll('.editor .btn')].find(b => b.textContent.trim() === 'Сохранить'))).click();
  await settle();
  assert(!(await page.$('.editor')), 'окно не закрылось');
  const thu = await day(3);
  assert(thu[0].title === 'Написать отчёт за сентябрь' && thu[0].time === '09:15', `Чт: ${JSON.stringify(thu)}`);
  assert(thu[0].color === '#8fb4e3', `цвет роли: ${thu[0].color}`);
});

await check('после перезагрузки окно показывает сохранённое', async () => {
  await load();
  await openEditor(3, 'Написать отчёт за сентябрь');
  const v = await page.evaluate(() => ({
    notes: document.querySelector('.editor textarea').value,
    time: document.querySelector('.editor input[type=time]').value,
    q: document.querySelector('.editor .quadrants .on')?.textContent.trim(),
    role: document.querySelector('.editor .chip.on')?.textContent.trim(),
  }));
  assert(v.notes === 'черновик у Маши' && v.time === '09:15' && v.q === 'II' && v.role === 'Работа', JSON.stringify(v));
  await page.keyboard.press('Escape'); await settle();
  assert(!(await page.$('.editor')), 'Esc не закрыл');
});

await check('Esc — отмена без сохранения', async () => {
  await openEditor(3, 'Написать отчёт за сентябрь');
  await page.type('.editor .title-input', ' ИЗМЕНЕНО');
  await page.keyboard.press('Escape'); await settle();
  assert((await titles(3)).includes('Написать отчёт за сентябрь'), `Чт: ${await titles(3)}`);
});

await check('убрать время у «камня»: дело уходит в конец, у цели слева по-прежнему «Чт»', async () => {
  assert((await goal('Демо для команды')).when === 'Чт', 'исходно не «Чт»');
  await openEditor(3, 'Демо для команды');
  await page.click('.editor .clear');
  await (await page.evaluateHandle(() => [...document.querySelectorAll('.editor .btn')].find(b => b.textContent.trim() === 'Сохранить'))).click();
  await settle();
  const t = await titles(3);
  assert(t[t.length - 1] === 'Демо для команды', `Чт: ${t}`);
  assert((await goal('Демо для команды')).when === 'Чт', `у цели: ${(await goal('Демо для команды')).when}`);
  await load();
  const t2 = await titles(3);
  assert(t2[t2.length - 1] === 'Демо для команды', `после перезагрузки Чт: ${t2}`);
});

await check('переименование занятия цель не меняет; Enter в названии сохраняет', async () => {
  await openEditor(1, 'Код-ревью модуля оплаты');
  await page.$eval('.editor .title-input', e => e.select());
  await page.type('.editor .title-input', 'Код-ревью оплаты');
  await page.keyboard.press('Enter'); await settle();
  assert((await titles(1)).includes('Код-ревью оплаты'), `Вт: ${await titles(1)}`);
  assert(await goal('Код-ревью модуля оплаты'), 'цель переименовалась вместе с занятием');
});

await check('«в корзину» из окна; в корзине видно, из какого дня', async () => {
  await openEditor(0, 'Новое дело');
  await (await page.evaluateHandle(() => [...document.querySelectorAll('.editor .btn')].find(b => b.textContent.trim() === 'в корзину'))).click();
  await settle();
  assert(!(await titles(0)).includes('Новое дело'), 'не удалилось');
  await page.click('.trash'); await sleep(600);
  const rows = await page.$$eval('.trash-popup .row', rs => rs.map(r => r.textContent));
  assert(rows.some(r => r.includes('Новое дело') && r.includes('из плана: Пн, 28 сентября')), `корзина: ${rows}`);
  await page.click('.trash-popup .row-action.positive'); await sleep(600);
  assert((await titles(0)).includes('Новое дело'), 'не вернулось в день');
  await page.click('.backdrop'); await settle();
});

await page.screenshot({ path: `${SHOTS}/week-final.png` });
await openEditor(3, 'Написать отчёт за сентябрь');
await page.screenshot({ path: `${SHOTS}/week-editor.png` });

await browser.close();
console.log(results.join('\n'));
console.log(consoleErrors.length ? `\nОшибки консоли:\n${consoleErrors.join('\n')}` : '\nОшибок в консоли нет');

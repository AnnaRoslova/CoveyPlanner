// @seed — нужны демо-данные (tools/seed-demo.ps1): коллекции «Книги» (категория «Хочу прочитать», 18 + 3 дочитанных, цель 24)
// и «Проекты» (два проекта с шагами); категория «Книги и фильмы».
// Коллекции: полка, цель года, записи; новая коллекция с категорией ленты; дело из категории в плане → отметили — на полке,
// подсказка «📚 +1»; сняли — ушло; шаги дела в окне; проект с шагами — «N шагов · M недель», шаги раскрываются,
// клик по названию — окно дела. Снимки — out/collections-<тема>.png.
import puppeteer from 'puppeteer-core';
import { DatabaseSync } from 'node:sqlite';

const BASE = process.env.PLANNER_URL ?? 'http://localhost:5071/';
const SHOTS = process.argv[2] ?? '.';
const results = [];
const consoleErrors = [];
const browser = await puppeteer.launch({
  executablePath: process.env.PLANNER_BROWSER ?? 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  headless: true, defaultViewport: { width: 1600, height: 1000 },
});
const page = await browser.newPage();
page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text()); });
page.on('pageerror', e => consoleErrors.push(e.message));
const sleep = ms => new Promise(r => setTimeout(r, ms));
const settle = () => sleep(600);
function assert(c, m) { if (!c) throw new Error(m); }
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-collections-${results.length}.png` }); }
}
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.role'); await settle(); }
const escape = async () => { await page.keyboard.press('Escape'); await sleep(350); };
const pad = n => String(n).padStart(2, '0');
const iso = d => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
const today = new Date();
const year = today.getFullYear();
const todayIndex = (today.getDay() + 6) % 7;
const api = (method, path, body) => page.evaluate(async (method, path, body) => {
  const r = await fetch(`/api/${path}`, { method, headers: { 'Content-Type': 'application/json' }, body: body ? JSON.stringify(body) : undefined });
  if (!r.ok) throw new Error(`${method} ${path}: ${r.status}`);
  return r.status === 204 ? null : r.json();
}, method, path, body);

const clickIn = (sel, text) => page.evaluate((sel, text) => {
  const el = [...document.querySelectorAll(sel)].find(b => b.textContent.trim() === text);
  if (!el) throw new Error(`нет «${text}» в ${sel}`);
  el.click();
}, sel, text);
const card = name => `.jar-card[aria-label="Копилка «${name}»"]`;
const text = (sel) => page.$eval(sel, e => e.textContent.replace(/\s+/g, ' ').trim()).catch(() => null);
const toastText = () => page.evaluate(() => document.querySelector('.toast')?.textContent.replace(/\s+/g, ' ').trim() ?? null);
const dayItem = (i, title) => page.evaluateHandle((i, t) => [...document.querySelectorAll('.day')[i].querySelectorAll('.item')]
  .find(it => it.querySelector('.title')?.childNodes[0]?.textContent.trim() === t) ?? null, i, title);
async function openScreen() { await page.click('.jar-b'); await page.waitForSelector('.habits'); await settle(); }

await load();

await check('демо: «Книги» — 21 за всё время, «3 из 24» в этом году, 3 корешка и 3 записи; разбивка по годам', async () => {
  await openScreen();
  assert(await text(`${card('Книги')} .total-n`) === '21', `всего: ${await text(`${card('Книги')} .total-n`)}`);
  const goal = await text(`${card('Книги')} .goal-text`);
  assert(goal === `3 из 24 в ${year}`, `цель: ${goal}`);
  assert(await page.$$eval(`${card('Книги')} .spine`, s => s.length) === 3, 'корешков не 3');
  assert(await page.$$eval(`${card('Книги')} .entry`, s => s.length) === 3, 'записей не 3');
  assert(await text(`${card('Книги')} .years`) === `${year}: 3`, `годы: ${await text(`${card('Книги')} .years`)}`);
  assert((await text(`${card('Книги')} .linked`))?.includes('Хочу прочитать'), 'нет категории ленты');
  assert(!(await page.$(`${card('Книги')} .plus-one`)), 'у коллекции есть «+1 сегодня»');
  assert(!(await page.$(`${card('Книги')} .weeks`)), 'у коллекции лента недель');
});

await check('демо: «Проекты» — «4 шага · за 1 день»; клик по строке раскрывает шаги (только просмотр)', async () => {
  const infos = await page.$$eval(`${card('Проекты')} .entry-steps-info`, es => es.map(e => e.textContent.trim()));
  assert(infos.includes('4 шага · за 1 день') && infos.includes('3 шага · за 1 день'), JSON.stringify(infos));
  await page.evaluate(sel => [...document.querySelectorAll(`${sel} .entry`)].find(e => e.textContent.includes('Полка в прихожей')).querySelector('.entry-row').click(), card('Проекты'));
  await settle();
  const steps = await page.$$eval(`${card('Проекты')} .entry-steps .step-title`, s => s.map(x => x.textContent.trim()));
  assert(steps.join('|') === 'Замерить стену|Купить доски|Покрасить|Повесить', JSON.stringify(steps));
  assert(!(await page.$(`${card('Проекты')} .entry-steps button.step-ck`)), 'шаги можно отмечать');
});

await check('новая коллекция «Фильмы» с категорией «Книги и фильмы»: в ленте у категории 🫙, в её настройках «→ Фильмы»', async () => {
  await page.click('.add-habit');
  await page.waitForSelector('.editor[aria-label="Копилка"]');
  await clickIn('.editor[aria-label="Копилка"] .segments.kind button', 'коллекция');
  await page.type('.editor[aria-label="Копилка"] .title-input', 'Фильмы');
  assert(!(await page.$('.editor[aria-label="Копилка"] input[aria-label="Норма в неделю"]')), 'у коллекции есть норма');
  await clickIn('.editor[aria-label="Копилка"] .chips .chip', '📚 Книги и фильмы');
  await clickIn('.editor[aria-label="Копилка"] .btn', 'Создать'); await settle();
  assert(await text(`${card('Фильмы')} .total-n`) === '0', 'нет карточки «Фильмы»');
  await escape();
  const header = await page.evaluate(() => [...document.querySelectorAll('.cat-h')].find(h => h.querySelector('.name')?.textContent.trim() === 'Книги и фильмы')?.querySelector('.cat-jar')?.getAttribute('title'));
  assert(header === 'выполненные дела — в коллекцию «Фильмы»', `значок: ${header}`);
  await page.evaluate(() => [...document.querySelectorAll('.cat-h')].find(h => h.querySelector('.name')?.textContent.trim() === 'Книги и фильмы').querySelector('.menu').click());
  await sleep(300);
  assert((await text('.cat-editor .linked'))?.endsWith('Фильмы'), `в настройках: ${await text('.cat-editor .linked')}`);
  await escape();
});

let movieId;
await check('дело из «Книги и фильмы» в плане: отметили — подсказка «+1 в «Фильмы» · 1 в году», на полке; сняли — ушло', async () => {
  const board = await api('GET', 'board');
  const movie = board.somedayTasks.find(t => t.title === '«Интерстеллар» ещё раз');
  movieId = movie.id;
  await api('POST', `tasks/${movie.id}/move`, { kind: 'day', date: iso(today), index: 99 });
  await load();
  const item = (await dayItem(todayIndex, '«Интерстеллар» ещё раз')).asElement();
  assert(item, 'дела нет в сегодняшнем дне');
  await item.$eval('.ck-hit', e => e.click());
  await sleep(400);
  const toast = await toastText();
  assert(toast?.includes(`+1 в «Фильмы» · 1 в ${year}`), `подсказка: ${toast}`);
  assert(await page.$('.toast.clip-toast .clip'), 'нет анимации');
  await page.screenshot({ path: `${SHOTS}/collections-toast.png` });
  await settle();
  await openScreen();
  assert(await text(`${card('Фильмы')} .total-n`) === '1', `всего: ${await text(`${card('Фильмы')} .total-n`)}`);
  assert((await text(`${card('Фильмы')} .entry-title`)) === '«Интерстеллар» ещё раз', 'нет записи');
  await escape();
  await item.$eval('.ck-hit', e => e.click()); await settle();
  await openScreen();
  assert(await text(`${card('Фильмы')} .total-n`) === '0', 'запись не ушла');
  await escape();
});

await check('ручная привязка важнее категории: дело из «Книги и фильмы» в «Проекты» — у «Фильмов» не считается', async () => {
  const task = await api('GET', `tasks/${movieId}`);
  const projects = (await api('GET', 'habits')).habits.find(h => h.name === 'Проекты');
  await api('PUT', `tasks/${movieId}`, { title: task.title, notes: null, roleIds: [], quadrant: null, startTime: null, durationMinutes: null, habitId: projects.id });
  await api('PUT', `tasks/${movieId}/done`, { isDone: true });
  const habits = (await api('GET', 'habits')).habits;
  assert(habits.find(h => h.name === 'Фильмы').collection.total === 0, 'засчиталось в «Фильмы»');
  assert(habits.find(h => h.name === 'Проекты').collection.total === 3, 'не засчиталось в «Проекты»');
  await api('PUT', `tasks/${movieId}/done`, { isDone: false });
});

await check('окно дела: шаги — добавить Enter, отметить, переименовать, удалить; в дне шагов не видно', async () => {
  await api('POST', 'tasks', { title: 'Сарай', target: { kind: 'day', date: iso(today), index: 99 } });
  await load();
  const item = (await dayItem(todayIndex, 'Сарай')).asElement();
  await item.$eval('.tx', e => e.click());
  await page.waitForSelector('.editor[aria-label="Дело"] .step-new');
  for (const step of ['Фундамент', 'Стены', 'Крыша']) {
    await page.type('.editor .step-new', step); await page.keyboard.press('Enter'); await sleep(350);
  }
  assert(await page.$('.editor[aria-label="Дело"]'), 'Enter в шаге закрыл окно');
  let titles = await page.$$eval('.editor .step-title', s => s.map(x => x.textContent.trim()));
  assert(titles.join('|') === 'Фундамент|Стены|Крыша', JSON.stringify(titles));
  await page.click('.editor .step .step-ck'); await sleep(350);
  assert((await text('.editor .label-row .hint')) === '1 из 3', `счётчик: ${await text('.editor .label-row .hint')}`);
  await clickIn('.editor .step-title', 'Стены'); await sleep(200);
  await page.keyboard.down('Control'); await page.keyboard.press('a'); await page.keyboard.up('Control');
  await page.keyboard.type('Стены из бруса'); await page.keyboard.press('Enter'); await sleep(350);
  await page.evaluate(() => [...document.querySelectorAll('.editor .step')].find(s => s.textContent.includes('Крыша')).querySelector('.row-action').click());
  await sleep(350);
  titles = await page.$$eval('.editor .step-title', s => s.map(x => x.textContent.trim()));
  assert(titles.join('|') === 'Фундамент|Стены из бруса', JSON.stringify(titles));
  await escape();
  const dayTitles = await page.evaluate(i => [...document.querySelectorAll('.day')[i].querySelectorAll('.item .title')].map(t => t.textContent.trim()), todayIndex);
  assert(!dayTitles.some(t => t.startsWith('Фундамент')), 'шаг попал в день');
});

await check('проект с шагами в «Проекты»: первый шаг 5 недель назад — запись «2 шага · 5 недель»; клик по названию — окно дела', async () => {
  const sarai = (await api('GET', `week/${iso(today)}`)).tasks.find(t => t.title === 'Сарай');
  const projects = (await api('GET', 'habits')).habits.find(h => h.name === 'Проекты');
  await api('PUT', `tasks/${sarai.id}`, { title: 'Сарай', notes: null, roleIds: [], quadrant: null, startTime: null, durationMinutes: null, habitId: projects.id });
  // Первый шаг «отмечен» 34 дня назад — прямо во временной базе сценария
  const steps = await api('GET', `tasks/${sarai.id}/steps`);
  const first = steps.find(s => s.isDone);
  const db = new DatabaseSync(process.env.PLANNER_DB);
  const before = new Date(today); before.setDate(before.getDate() - 34);
  db.prepare('UPDATE Tasks SET CompletedAt = ? WHERE Id = ?').run(`${iso(before)} 09:00:00`, first.id);
  db.close();
  await load();
  const item = (await dayItem(todayIndex, 'Сарай')).asElement();
  await item.$eval('.ck-hit', e => e.click()); await sleep(400);
  assert((await toastText())?.includes('+1 в «Проекты»'), `подсказка: ${await toastText()}`);
  await settle();
  await openScreen();
  const info = await page.evaluate(sel => [...document.querySelectorAll(`${sel} .entry`)].find(e => e.querySelector('.entry-title').textContent.trim() === 'Сарай')?.querySelector('.entry-steps-info')?.textContent.trim(), card('Проекты'));
  assert(info === '2 шага · 5 недель', `запись: ${info}`);
  await page.screenshot({ path: `${SHOTS}/collections-lotus.png` });
  await page.evaluate(sel => [...document.querySelectorAll(`${sel} .entry-title`)].find(e => e.textContent.trim() === 'Полка в прихожей').click(), card('Проекты'));
  await page.waitForSelector('.editor[aria-label="Дело"]'); await sleep(300);
  assert(await page.$eval('.editor .title-input', e => e.value) === 'Полка в прихожей', 'открылось не то дело');
  assert(await page.$$eval('.editor .step', s => s.length) === 4, 'в окне не 4 шага');
  await escape();
});

await check('каникулы — только для привычек: коллекций в выборе нет', async () => {
  await openScreen();
  await clickIn('.habits .btn', '🏖 Каникулы');
  await page.waitForSelector('.editor[aria-label="Каникулы"]');
  const chips = await page.$$eval('.editor[aria-label="Каникулы"] .chip', c => c.map(x => x.textContent.trim()));
  assert(!chips.some(c => c.includes('Книги') || c.includes('Проекты') || c.includes('Фильмы')), JSON.stringify(chips));
  await escape(); await escape();
});

await check('смена вида: «Книги» → привычка (серии вместо полки) → снова коллекция (записи на месте)', async () => {
  await openScreen();
  const edit = async () => {
    await page.evaluate(sel => [...document.querySelector(sel).querySelectorAll('.btn')].find(b => b.textContent.trim() === 'настроить').click(), card('Книги'));
    await page.waitForSelector('.editor[aria-label="Копилка"]'); await sleep(200);
  };
  await edit();
  await clickIn('.editor[aria-label="Копилка"] .segments.kind button', 'привычка');
  await clickIn('.editor[aria-label="Копилка"] .btn', 'Сохранить'); await settle();
  assert(await page.$(`${card('Книги')} .weeks`), 'не стала привычкой');
  await edit();
  await clickIn('.editor[aria-label="Копилка"] .segments.kind button', 'коллекция');
  await clickIn('.editor[aria-label="Копилка"] .btn', 'Сохранить'); await settle();
  assert(await text(`${card('Книги')} .total-n`) === '21', `после возврата: ${await text(`${card('Книги')} .total-n`)}`);
  await escape();
});

for (const theme of ['peony', 'obsidian']) {
  await check(`снимок экрана «Копилки»: ${theme}`, async () => {
    await page.evaluate(k => localStorage.setItem('planner.theme', k), theme);
    await load();
    await page.evaluate(() => document.fonts.ready);
    await openScreen();
    await page.screenshot({ path: `${SHOTS}/collections-${theme}.png` });
    await escape();
  });
}
await page.evaluate(() => localStorage.setItem('planner.theme', 'lotus'));

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);

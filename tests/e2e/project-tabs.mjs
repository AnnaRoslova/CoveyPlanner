// Проекты понятнее (docs/specs/batch-ux.md, BP): вкладки «Мысли · N | Проекты · N» над правой колонкой (🛠 в шапке нет);
// пустая вкладка объясняет, что такое проект; «🛠 Сделать проектом» у мысли → «Теперь в «Проектах» · Показать · Вернуть»;
// автоотметка проекта → «„…“ выполнен → в «Выполненном» · Показать · Вернуть». Данные — через API (чистая база).
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
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-project-tabs-${results.length}.png` }); }
  for (let i = 0; i < 3 && await page.$('.editor'); i++) { await page.keyboard.press('Escape'); await settle(); }
}
const api = (method, path, body) => page.evaluate(async (m, p, b) => {
  const r = await fetch(`/api/${p}`, { method: m, headers: { 'Content-Type': 'application/json', 'X-Planner': '1' }, body: b ? JSON.stringify(b) : undefined });
  if (!r.ok) throw new Error(`${m} ${p}: ${r.status} ${await r.text()}`);
  const text = await r.text();
  return text ? JSON.parse(text) : null;
}, method, path, body);
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day'); await settle(); }
const tabs = () => page.$$eval('.right-tabs .rt', bs => bs.map(b => ({ text: b.textContent.replace(/\s+/g, ' ').trim(), on: b.classList.contains('on') })));
const tab = name => page.evaluate(n => [...document.querySelectorAll('.right-tabs .rt')].find(b => b.textContent.startsWith(n)).click(), name);
const toast = () => page.evaluate(() => document.querySelector('.toast')?.textContent.replace(/\s+/g, ' ').trim() ?? null);
const toastAction = t => page.evaluate(t => [...document.querySelectorAll('.toast button.action')].find(b => b.textContent.trim() === t)?.click(), t);
const iso = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

await load();
const ideas = await api('POST', 'categories', { name: 'Идеи' });
await api('POST', 'tasks', { title: 'Ремонт на кухне', target: { kind: 'someday', categoryId: ideas.id, index: 0 } });
await api('POST', 'tasks', { title: 'Сад', target: { kind: 'someday', categoryId: ideas.id, index: 1 } });
await load();

await check('BP1: вкладки «Мысли · 2 | Проекты · 0», в шапке недели 🛠 нет', async () => {
  const t = await tabs();
  assert(JSON.stringify(t) === JSON.stringify([{ text: 'Мысли · 2', on: true }, { text: 'Проекты · 0', on: false }]), JSON.stringify(t));
  assert(!(await page.$('.week-h .projects-b')), '🛠 в шапке осталась');
});

await check('BP4: пустая вкладка «Проекты» объясняет, что такое проект, и даёт «+ проект»', async () => {
  await tab('Проекты'); await settle();
  const text = await page.$eval('.someday .nothing', e => e.textContent.replace(/\s+/g, ' ').trim());
  assert(text === 'Проект — дело из нескольких шагов, например «Ремонт» или книга. Шаги можно раскладывать по дням.', text);
  assert(await page.$('.someday .add-project'), 'нет «+ проект»');
  await (await page.$('.someday')).screenshot({ path: `${SHOTS}/project-tabs-empty.png` });
  await tab('Мысли'); await settle();
});

await check('BP2: «🛠 Сделать проектом» у мысли → «Теперь в «Проектах» · Показать · Вернуть»; «Показать» — вкладка «Проекты»; числа на вкладках', async () => {
  await page.evaluate(() => [...document.querySelectorAll('.cat-b > .item')].find(i => i.querySelector('.title').textContent.trim() === 'Ремонт на кухне').querySelector('.title').click());
  await settle();
  assert(!(await page.$('.editor .add-step')), 'у мысли есть «+ шаг»');
  await page.click('.editor .make-project-b'); await settle();
  const t = await toast();
  assert(t?.includes('Теперь в «Проектах»') && t.includes('Показать') && t.includes('Вернуть'), JSON.stringify(t));
  await toastAction('Показать'); await settle();
  const after = await tabs();
  assert(JSON.stringify(after) === JSON.stringify([{ text: 'Мысли · 1', on: false }, { text: 'Проекты · 1', on: true }]), JSON.stringify(after));
  const titles = await page.$$eval('.someday .pgroup > .item .title', ts => ts.map(x => x.textContent.trim()));
  assert(titles.includes('Ремонт на кухне'), JSON.stringify(titles));
});

await check('BP5: последний шаг проекта отметили — «„…“ выполнен → в «Выполненном» · Показать · Вернуть»; «Показать» — экран «Выполненное»', async () => {
  const project = (await api('GET', 'board')).somedayTasks.find(x => x.title === 'Ремонт на кухне');
  const step = await api('POST', `tasks/${project.id}/steps`, { title: 'Стены' });
  await api('POST', `tasks/${step.id}/move`, { kind: 'day', date: iso(new Date()), index: 0 });
  await load();
  await page.evaluate(() => [...document.querySelectorAll('.day .item')].find(i => i.querySelector('.title')?.textContent.trim() === 'Стены').querySelector('.ck-hit').click());
  await settle();
  const t = await toast();
  assert(t?.includes('«Ремонт на кухне» выполнен → в «Выполненном»') && t.includes('Показать') && t.includes('Вернуть'), JSON.stringify(t));
  await toastAction('Показать'); await settle();
  assert(await page.$('.done-screen'), 'экран «Выполненное» не открылся');
  await page.keyboard.press('Escape'); await settle();
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);

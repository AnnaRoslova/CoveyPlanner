// @seed — нужны демо-данные (tools/seed-demo.ps1)
// Словарь и «Как это устроено» (docs/specs/batch-ux.md, BG): экран открывается и закрывается Esc; на экранах нет «Копилки»-слов в окнах,
// «сделано», «Подробно»; занятие — ◆ с подсказкой «занятие цели «…»»; плашка удаления дела — «„…“ в корзине · Вернуть»;
// в интерфейсе нет упоминаний Кови и «7 навыков» (кроме данных демо).
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
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-glossary-${results.length}.png` }); }
  for (let i = 0; i < 3 && await page.$('.editor, .help-screen'); i++) { await page.keyboard.press('Escape'); await settle(); }
}
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day'); await settle(); }
const openDayTask = title => page.evaluate(t => [...document.querySelectorAll('.day .item')].find(i => i.querySelector('.title')?.textContent.trim() === t).querySelector('.title').click(), title);

await load();

await check('BG3: «Как это устроено» — словарь, схема, клавиши; Esc закрывает', async () => {
  await page.click('.week-h .settings-b'); await settle(); // BI5: «Как это устроено» — в «⚙ Настройках» (и клавишей «?», BK)
  await page.click('.editor.settings .help-link'); await settle();
  assert(await page.$('.help-screen'), 'экран не открылся');
  const words = await page.$$eval('.help-screen .glossary dt', ds => ds.map(d => d.textContent.trim()));
  for (const w of ['мысль', 'дело', 'проект', 'шаг', 'цель недели', 'камень', 'занятие', 'привычка', 'скрепка']) assert(words.includes(w), `нет «${w}»: ${words}`);
  assert(await page.$('.help-screen svg.scheme'), 'нет схемы');
  assert((await page.$$('.help-screen .keys tr')).length >= 10, 'нет клавиш');
  await (await page.$('.help-screen')).screenshot({ path: `${SHOTS}/help-screen.png` });
  await page.keyboard.press('Escape'); await settle();
  assert(!(await page.$('.help-screen')), 'Esc не закрыл');
});

await check('BG1–BG2: занятие — ◆ с подсказкой «занятие цели «…»»; в окне — «занятие цели «…»», «выполнено», без «камень»/«сделано»', async () => {
  const rock = await page.evaluate(() => {
    const i = [...document.querySelectorAll('.day .item')].find(x => x.querySelector('.title.rock'));
    return i ? i.querySelector('.title-box').getAttribute('title') : null;
  });
  assert(rock?.startsWith('занятие цели «'), `подсказка ◆: ${rock}`);
  await openDayTask('Демо для команды'); await settle();
  const note = await page.$eval('.editor .rock-note', e => e.textContent.replace(/\s+/g, ' ').trim());
  assert(note.includes('занятие цели «Демо для команды»') && !note.includes('камень') && !note.includes('сделано'), note);
});

await check('BG-b: удаление дела — «„…“ в корзине · Вернуть»', async () => {
  await page.hover('.day:nth-child(1) .item'); await sleep(300);
  const title = await page.$eval('.day:nth-child(1) .item .title', t => t.textContent.trim());
  await page.evaluate(() => document.querySelector('.day:nth-child(1) .item .trash-btn').click()); await settle();
  const t = await page.$eval('.toast', e => e.textContent.replace(/\s+/g, ' ').trim());
  assert(t.startsWith(`«${title}» в корзине`) && t.includes('Вернуть'), t);
  await page.evaluate(() => [...document.querySelectorAll('.toast button.action')].find(b => b.textContent.trim() === 'Вернуть').click()); await settle();
});

await check('BM5: в интерфейсе нет «Кови» и «7 навыков» (кроме мыслей и дел — это данные)', async () => {
  const found = await page.evaluate(() => {
    const out = [];
    const walk = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
    for (let n = walk.nextNode(); n; n = walk.nextNode()) {
      if (n.parentElement.closest('.item .title, .title-input, textarea')) continue;
      if (/Кови|7 навык/i.test(n.textContent)) out.push(n.textContent.trim().slice(0, 60));
    }
    for (const el of document.querySelectorAll('[title]')) if (/Кови|7 навык/i.test(el.title) && !el.closest('.item .title')) out.push(el.title.slice(0, 60));
    return out;
  });
  assert(found.length === 0, JSON.stringify(found));
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);

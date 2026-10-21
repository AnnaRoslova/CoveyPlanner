// @seed — нужны демо-данные (tools/seed-demo.ps1)
// «Моя миссия» (docs/specs/batch-ux.md, BM): пустая — «Начни с конца: кем ты хочешь быть?»; текст миссии (с переносами строк)
// и строка роли сохраняются по уходу из поля и переживают перезагрузку; строка роли — в подсказке названия роли и в окне роли;
// блок «✦ Моя миссия» в левой панели — по настройке; шаг 4 недельного обзора.
import puppeteer from 'puppeteer-core';
import { openThemes } from './lib/nav.mjs';

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
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-mission-${results.length}.png` }); }
  for (let i = 0; i < 3 && await page.$('.editor, .mission, .review'); i++) { await page.keyboard.press('Escape'); await settle(); }
}
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day'); await settle(); }
const MISSION = 'Жить спокойно и бережно.\nДоводить начатое до конца.';
async function openMission() {
  await openThemes(page);
  await page.click('.editor.settings .mission-link'); await page.waitForSelector('.mission'); await sleep(300);
}
const visionInput = role => page.evaluateHandle(r => [...document.querySelectorAll('.mission .vision')]
  .find(v => v.querySelector('.role').textContent.trim() === r).querySelector('input'), role);

await load();

await check('BM4: пустая миссия — «Начни с конца: кем ты хочешь быть?»', async () => {
  await openMission();
  const placeholder = await page.$eval('.mission .mission-text', t => t.placeholder);
  assert(placeholder === 'Начни с конца: кем ты хочешь быть?', placeholder);
  assert(await page.$$eval('.mission .vision', vs => vs.length) >= 8, 'не все роли');
});

await check('BM1: миссия с переносами и строка роли сохраняются по уходу из поля и переживают перезагрузку', async () => {
  await openMission();
  await page.click('.mission .mission-text');
  await page.keyboard.type('Жить спокойно и бережно.');
  await page.keyboard.down('Shift'); await page.keyboard.press('Enter'); await page.keyboard.up('Shift');
  await page.keyboard.type('Доводить начатое до конца.');
  const work = await visionInput('Работа');
  await work.click(); // уход из поля миссии — сохранение
  await page.keyboard.type('спокойным профессионалом');
  await page.keyboard.press('Tab'); await settle();
  await (await page.$('.mission')).screenshot({ path: `${SHOTS}/mission.png` });
  await page.keyboard.press('Escape'); await settle();
  await load();
  await openMission();
  assert(await page.$eval('.mission .mission-text', t => t.value) === MISSION, 'миссия не сохранилась');
  assert(await (await visionInput('Работа')).evaluate(i => i.value) === 'спокойным профессионалом', 'строка роли не сохранилась');
});

await check('BM2: строка роли — в подсказке названия роли и в окне роли', async () => {
  const title = await page.evaluate(() => [...document.querySelectorAll('.role-toggle')]
    .find(b => b.querySelector('.name').textContent.trim() === 'Работа').title);
  assert(title.startsWith('спокойным профессионалом · '), title);
  await page.evaluate(() => {
    const toggle = [...document.querySelectorAll('.role-toggle')].find(b => b.querySelector('.name').textContent.trim() === 'Работа');
    toggle.parentElement.querySelector('.menu[title="Изменить роль"]').click();
  });
  await settle();
  assert(await page.$eval('.role-editor .vision-input', i => i.value) === 'спокойным профессионалом', 'нет в окне роли');
});

await check('BM2: блок «✦ Моя миссия» в левой панели — только по настройке; раскрывается текстом', async () => {
  assert(!(await page.$('.roles-panel .mission-block')), 'блок без настройки');
  await openThemes(page);
  await page.click('.editor.settings .mission-settings input[type="checkbox"]'); await settle();
  await page.keyboard.press('Escape'); await settle();
  assert(await page.$('.roles-panel .mission-block'), 'блока нет');
  await page.click('.roles-panel .mission-toggle'); await settle();
  assert(await page.$eval('.roles-panel .mission-block .mission-text', p => p.textContent) === MISSION, 'не тот текст');
  await load();
  assert(await page.$('.roles-panel .mission-block .mission-text'), 'настройка или раскрытие не запомнились');
});

await check('BR6: шаг 4 обзора — миссия и строки ролей, «изменить» открывает «Мою миссию»', async () => {
  await page.keyboard.press('KeyW'); await settle();
  for (let i = 0; i < 3; i++) { await page.click('.review .next'); await settle(); }
  assert(await page.$eval('.review .mission-text', p => p.textContent) === MISSION, 'нет миссии');
  const work = await page.evaluate(() => [...document.querySelectorAll('.review .row')]
    .find(r => r.querySelector('.role-title')?.textContent.trim() === 'Работа')?.querySelector('.vision').textContent);
  assert(work === 'спокойным профессионалом', work);
  await page.click('.review .edit-mission'); await settle();
  assert(await page.$('.mission'), '«изменить» не открыл миссию');
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);

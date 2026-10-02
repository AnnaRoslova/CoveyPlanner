// @seed — нужны демо-данные (tools/seed-demo.ps1)
// Несколько ролей у дела: в окне дела роли — чипы с множественным выбором, «без роли» снимает все;
// на записи чекбокс цвета первой роли (по порядку ролей), при двух и больше — точки всех ролей.
// У занятия цели роль одна — от цели, выбрать нельзя.
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
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-task-roles-${results.length}.png` }); }
  if (await page.$('.editor')) { await page.keyboard.press('Escape'); await settle(); } // следующей проверке — без открытого окна
}
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day'); await settle(); }

const TITLE = 'Кофе с Олей';
const row = () => page.evaluateHandle(t => [...document.querySelectorAll('.day .item')].find(i => i.querySelector('.title').textContent.trim() === t), TITLE);
async function openEditor() { await (await (await row()).$('.title')).click(); await settle(); assert(await page.$('.editor'), 'окно не открылось'); }
const chip = name => page.evaluateHandle(n => [...document.querySelectorAll('.editor [aria-label="Роли"] .chip')].find(c => c.textContent.trim() === n), name);
const chipsOn = () => page.$$eval('.editor [aria-label="Роли"] .chip.on', cs => cs.map(c => c.textContent.trim()));
const save = async () => { await page.click('.editor .btn:not(.quiet):not(.danger)'); await settle(); };
// Цвет роли по имени — из её точки в левой панели
const roleColor = name => page.evaluate(n => getComputedStyle([...document.querySelectorAll('.role')]
  .find(r => r.querySelector('.role-h .name')?.textContent.trim() === n).querySelector('.role-h .dot')).backgroundColor, name);
const look = () => row().then(r => r.evaluate(i => ({
  ck: getComputedStyle(i.querySelector('.ck')).borderColor,
  dots: [...i.querySelectorAll('.role-dots i')].map(d => getComputedStyle(d).backgroundColor),
})));

await load();

await check('окно дела: роли — множественный выбор, «без роли» снимает все', async () => {
  await openEditor();
  await (await chip('без роли')).click(); // в демо у дела роль «Общение и эмоции»
  await (await chip('Семья')).click();
  await (await chip('Работа')).click();
  assert(JSON.stringify(await chipsOn()) === '["Семья","Работа"]', `выбраны: ${await chipsOn()}`);
  await (await chip('Работа')).click();
  assert(JSON.stringify(await chipsOn()) === '["Семья"]', `после снятия: ${await chipsOn()}`);
  await (await chip('без роли')).click();
  assert(JSON.stringify(await chipsOn()) === '["без роли"]', `после «без роли»: ${await chipsOn()}`);
  await page.keyboard.press('Escape'); await settle();
});

await check('две роли: чекбокс — цвет первой по порядку ролей, точки обеих; после перезагрузки так же', async () => {
  await openEditor();
  await (await chip('без роли')).click();
  await (await chip('Работа')).click();
  await (await chip('Я сама')).click(); // «Я сама» в списке ролей выше «Работы»
  await save();
  const [self, work] = [await roleColor('Я сама'), await roleColor('Работа')];
  let l = await look();
  assert(l.ck === self, `чекбокс ${l.ck}, а «Я сама» ${self}`);
  assert(JSON.stringify(l.dots) === JSON.stringify([self, work]), `точки: ${l.dots}`);
  await load();
  l = await look();
  assert(l.ck === self && l.dots.length === 2, `после перезагрузки: ${JSON.stringify(l)}`);
  await openEditor();
  assert(JSON.stringify(await chipsOn()) === '["Я сама","Работа"]', `в окне: ${await chipsOn()}`);
  await page.keyboard.press('Escape'); await settle();
});

await check('одна роль — без точек; без роли — бледный чекбокс', async () => {
  await openEditor();
  await (await chip('Я сама')).click(); await save();
  let l = await look();
  assert(l.ck === await roleColor('Работа') && l.dots.length === 0, `одна роль: ${JSON.stringify(l)}`);
  await openEditor();
  await (await chip('без роли')).click(); await save();
  l = await look();
  assert(l.dots.length === 0, `без роли — точки: ${l.dots}`);
  await openEditor();
  assert(JSON.stringify(await chipsOn()) === '["без роли"]', `в окне: ${await chipsOn()}`);
  await page.keyboard.press('Escape'); await settle();
});

await check('занятие цели: роль одна, от цели, выбрать нельзя', async () => {
  await (await page.evaluateHandle(() => [...document.querySelectorAll('.day .item')].find(i => i.querySelector('.pill.rock'))).then(h => h.$('.title'))).click();
  await settle();
  assert(!(await page.$('.editor .field .chips')) || !(await page.$$eval('.editor .field', fs => fs[0].querySelector('.chips'))), 'у занятия можно выбирать роли');
  assert(await page.$('.editor .from-goal'), 'нет «задаётся у цели»');
  await page.keyboard.press('Escape'); await settle();
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));

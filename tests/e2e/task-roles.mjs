// @seed — нужны демо-данные (tools/seed-demo.ps1)
// Несколько ролей у дела: в окне дела роли — маленькие чипы «● Тело ×» и «+» со списком ролей (множественный выбор);
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
// Роли в окне — чипы «● Тело ×»; «+» открывает список ролей (галочки у выбранных)
const chipsOn = () => page.$$eval('.editor .tags .tag .tag-name', ts => ts.map(t => t.textContent.trim()));
const optionsOn = () => page.$$eval('.editor .popover .option.on .option-name', ts => ts.map(t => t.textContent.trim()));
async function clickOption(name) {
  await page.evaluate(n => [...document.querySelectorAll('.editor .popover .option')].find(o => o.textContent.trim() === n).click(), name);
  await settle();
}
async function pickRoles(...names) {
  await page.click('.editor .tag-add'); await settle();
  for (const n of names) await clickOption(n);
  await page.keyboard.press('Escape'); await settle(); // закрывает только список
}
async function clearRoles() {
  while (await page.$('.editor .tag-x')) { await page.click('.editor .tag-x'); await settle(); }
}
const save = async () => { await page.click('.editor .buttons .btn:not(.quiet):not(.danger)'); await settle(); };
// Цвет роли по имени — из её точки в левой панели
const roleColor = name => page.evaluate(n => getComputedStyle([...document.querySelectorAll('.role')]
  .find(r => r.querySelector('.role-h .name')?.textContent.trim() === n).querySelector('.role-h .dot')).backgroundColor, name);
const look = () => row().then(r => r.evaluate(i => ({
  ck: getComputedStyle(i.querySelector('.ck')).borderColor,
  dots: [...i.querySelectorAll('.role-dots i')].map(d => getComputedStyle(d).backgroundColor),
})));

await load();

await check('окно дела: роли — «+» открывает список, можно несколько; «×» снимает; Esc закрывает только список', async () => {
  await openEditor();
  await clearRoles(); // в демо у дела роль «Общение и эмоции»
  assert(await page.$eval('.editor .tag-empty', e => e.textContent.trim()) === 'без роли', 'нет «без роли»');
  await page.click('.editor .tag-add'); await settle();
  assert(await page.$('.editor .popover'), 'список не открылся');
  await clickOption('Семья');
  await clickOption('Работа');
  assert(JSON.stringify(await optionsOn()) === '["Семья","Работа"]', `галочки: ${await optionsOn()}`);
  assert(JSON.stringify(await chipsOn()) === '["Семья","Работа"]', `выбраны: ${await chipsOn()}`);
  await page.screenshot({ path: `${SHOTS}/task-roles-popover.png` });
  await clickOption('Работа');
  assert(JSON.stringify(await chipsOn()) === '["Семья"]', `после снятия: ${await chipsOn()}`);
  await page.keyboard.press('Escape'); await settle();
  assert(!(await page.$('.editor .popover')) && await page.$('.editor'), 'Esc закрыл не только список');
  await page.click('.editor .tag-add'); await settle();
  await page.click('.editor .title-input'); await settle();
  assert(!(await page.$('.editor .popover')) && await page.$('.editor'), 'клик мимо не закрыл список');
  await page.click('.editor .tag-x'); await settle();
  assert(JSON.stringify(await chipsOn()) === '[]', `после «×»: ${await chipsOn()}`);
  await page.keyboard.press('Escape'); await settle();
});

await check('две роли: чекбокс — цвет первой по порядку ролей, точки обеих; после перезагрузки так же', async () => {
  await openEditor();
  await clearRoles();
  await pickRoles('Работа', 'Я сама'); // «Я сама» в списке ролей выше «Работы»
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
  await page.click('.editor .tag-x'); await settle(); // первая — «Я сама»
  await save();
  let l = await look();
  assert(l.ck === await roleColor('Работа') && l.dots.length === 0, `одна роль: ${JSON.stringify(l)}`);
  await openEditor();
  await clearRoles(); await save();
  l = await look();
  assert(l.dots.length === 0, `без роли — точки: ${l.dots}`);
  await openEditor();
  assert(JSON.stringify(await chipsOn()) === '[]' && await page.$('.editor .tag-empty'), `в окне: ${await chipsOn()}`);
  await page.keyboard.press('Escape'); await settle();
});

await check('занятие цели: роль одна, от цели, выбрать нельзя', async () => {
  await (await page.evaluateHandle(() => [...document.querySelectorAll('.day .item')].find(i => i.querySelector('.pill.rock'))).then(h => h.$('.title'))).click();
  await settle();
  assert(!(await page.$('.editor .tag-add')) && !(await page.$('.editor .tag-x')), 'у занятия можно выбирать роли');
  assert((await chipsOn()).length === 1, `роли: ${await chipsOn()}`);
  assert(await page.$eval('.editor .tags .hint', e => e.textContent.trim()) === '· от цели', 'нет «от цели»');
  await page.keyboard.press('Escape'); await settle();
});

await check('список ролей открыли и сразу закрыли — дальше Esc и клики без ошибок', async () => {
  await openEditor();
  for (let i = 0; i < 10; i++) await page.click('.editor .tag-add'); // открыть и сразу закрыть, раньше чем загрузится скрипт списка
  await settle();
  await page.click('.editor .title-input'); await page.keyboard.press('Escape'); await settle();
  assert(!(await page.$('.editor')), 'Esc не закрыл окно');
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));

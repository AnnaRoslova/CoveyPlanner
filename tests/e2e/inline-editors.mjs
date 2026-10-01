// @seed — нужны демо-данные (tools/seed-demo.ps1)
// Встроенные редакторы закрываются кликом мимо и по Esc. Esc — отмена. Клик мимо: в редакторе категории и выборе
// категории — отмена; в полях «+ дело», «+ добавить», «+ цель» — набранное сохраняется, пустое поле закрывается.
// Лента: правка категории, новая категория, «+ добавить», переименование дела (клик мимо = сохранить, как раньше).
// Неделя: «+ дело». Левая панель: «+ цель», выбор категории при удалении цели.
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
// 409 — ожидаемый ответ «нужна категория» при удалении цели
page.on('console', m => { if (m.type() === 'error' && !m.text().includes('409')) consoleErrors.push(m.text()); });
page.on('pageerror', e => consoleErrors.push(e.message));
const sleep = ms => new Promise(r => setTimeout(r, ms));
const settle = () => sleep(400);
function assert(c, m) { if (!c) throw new Error(m); }
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-inline-${results.length}.png` }); }
  await page.keyboard.press('Escape'); await settle(); // следующей проверке — чистый экран
}
const api = (method, path, body) => page.evaluate(async (method, path, body) => {
  const r = await fetch('/api/' + path, { method, headers: { 'Content-Type': 'application/json' }, body: body ? JSON.stringify(body) : undefined });
  return r.status === 204 ? null : r.json();
}, method, path, body);
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day'); await settle(); }

// «Мимо» — заголовок недели: там нет кнопок
async function clickAway() {
  const r = await page.$eval('.wt', e => e.getBoundingClientRect().toJSON());
  await page.mouse.click(r.left + r.width / 2, r.top + r.height / 2); await settle();
}
const categoryNames = () => page.$$eval('.someday .cat-h .name', ns => ns.map(n => n.textContent.trim()));
const category = name => page.evaluateHandle(n => [...document.querySelectorAll('.someday .cat')]
  .find(c => c.querySelector('.cat-h .name')?.textContent.trim() === n), name);
const feedTitles = name => page.evaluate(n => [...[...document.querySelectorAll('.someday .cat')]
  .find(c => c.querySelector('.cat-h .name')?.textContent.trim() === n).querySelectorAll('.item .tx')]
  .map(t => t.firstChild.textContent.trim()), name);
const role = name => page.evaluateHandle(n => [...document.querySelectorAll('.role')]
  .find(r => r.querySelector('.role-h .name')?.textContent.trim() === n), name);
const has = sel => page.$(sel).then(Boolean);

async function openCategoryEditor(name) {
  const cat = await category(name);
  await (await cat.$('.cat-h')).hover();
  await (await cat.$('.cat-h .menu')).click(); await settle();
  assert(await has('.someday .cat-editor'), 'редактор категории не открылся');
}

await load();

await check('правка категории: клик мимо закрывает, название не меняется', async () => {
  await openCategoryEditor('Идеи');
  await page.keyboard.type(' и планы');
  await clickAway();
  assert(!(await has('.someday .cat-editor')), 'редактор не закрылся');
  assert((await categoryNames()).includes('Идеи'), `категории: ${await categoryNames()}`);
});

await check('правка категории: Esc закрывает — и из поля, и после клика по выбору эмодзи', async () => {
  await openCategoryEditor('Идеи');
  await page.keyboard.press('Escape'); await settle();
  assert(!(await has('.someday .cat-editor')), 'Esc в поле не закрыл');
  await openCategoryEditor('Идеи');
  await page.click('.someday .cat-editor .emoji-btn'); await settle(); // фокус ушёл из поля на кнопку
  await page.keyboard.press('Escape'); await settle();
  assert(!(await has('.someday .cat-editor')), 'Esc после выбора эмодзи не закрыл');
});

await check('правка категории: клики внутри (эмодзи, поле) не закрывают', async () => {
  await openCategoryEditor('Идеи');
  await page.click('.someday .cat-editor .emoji-btn'); await settle();
  await page.click('.someday .cat-editor .inline-input'); await settle();
  assert(await has('.someday .cat-editor'), 'закрылся от клика внутри');
});

await check('открыть правку другой категории, пока открыта первая, — первая закрывается', async () => {
  await openCategoryEditor('Идеи');
  await openCategoryEditor('Дом');
  assert((await page.$$('.someday .cat-editor')).length === 1, 'открыты два редактора');
  assert((await categoryNames()).includes('Идеи'), 'первая категория не вернулась на место');
});

await check('новая категория: клик мимо и Esc закрывают, категория не создаётся', async () => {
  const before = (await categoryNames()).length;
  await page.click('.someday .panel-scroll > .add'); await settle();
  await page.keyboard.type('Черновик');
  await clickAway();
  assert(!(await has('.someday .cat-editor')), 'клик мимо не закрыл');
  await page.click('.someday .panel-scroll > .add'); await settle();
  await page.keyboard.type('Черновик');
  await page.keyboard.press('Escape'); await settle();
  assert(!(await has('.someday .cat-editor')), 'Esc не закрыл');
  assert((await categoryNames()).length === before, `категории: ${await categoryNames()}`);
});

// Поля «+ …»: Esc — отмена; клик мимо — набранное сохраняется, пустое поле просто закрывается
async function addField(name, open, field, count) {
  await check(`${name}: Esc — отмена, клик мимо — сохраняет набранное, пустое — закрывает`, async () => {
    const before = await count();
    await open(); await page.keyboard.type('Недописанное'); await page.keyboard.press('Escape'); await settle();
    assert(!(await field()), 'Esc не закрыл');
    assert(await count() === before, 'Esc сохранил');
    await open(); await clickAway();
    assert(!(await field()), 'пустое поле не закрылось');
    assert(await count() === before, 'пустое поле что-то создало');
    await open(); await page.keyboard.type('Дописанное до конца'); await clickAway(); await settle();
    assert(!(await field()), 'поле с текстом не закрылось');
    assert(await count() === before + 1, `не сохранилось: ${await count()} вместо ${before + 1}`);
  });
}

await addField('«+ добавить» в ленте',
  async () => { await (await (await category('Идеи')).$('.cat-b > .add')).click(); await settle(); },
  async () => (await category('Идеи')).$('.inline-input'),
  async () => (await feedTitles('Идеи')).length);

await check('переименование дела в ленте: клик мимо — сохраняет и закрывает (как раньше), Esc — отменяет', async () => {
  const first = (await feedTitles('Идеи'))[0];
  await (await (await category('Идеи')).$('.item .tx')).click(); await settle();
  await page.keyboard.press('End'); await page.keyboard.type('!');
  await clickAway();
  assert(!(await (await category('Идеи')).$('.inline-input')), 'поле не закрылось');
  assert((await feedTitles('Идеи'))[0] === first + '!', `первое дело: ${(await feedTitles('Идеи'))[0]}`);
  await (await (await category('Идеи')).$('.item .tx')).click(); await settle();
  await page.keyboard.type('???');
  await page.keyboard.press('Escape'); await settle();
  assert((await feedTitles('Идеи'))[0] === first + '!', 'Esc сохранил');
});

await addField('«+ дело» в дне',
  async () => { await page.$$eval('.day', ds => ds[6].querySelector('.add').click()); await settle(); },
  () => page.$$eval('.day', ds => ds[6].querySelector('.inline-input')),
  () => page.$$eval('.day', ds => ds[6].querySelectorAll('.item').length));

await addField('«+ цель»',
  async () => { await (await (await role('Дом')).$('.add')).click(); await settle(); },
  async () => (await role('Дом')).$('.inline-input'),
  async () => (await (await role('Дом')).$$('.item')).length);

await check('выбор категории при удалении цели: клик мимо и Esc — цель остаётся', async () => {
  const home = (await api('GET', 'board')).roles.find(r => r.name === 'Дом');
  const cat = await api('POST', 'categories', { name: 'Временная', emoji: null });
  const task = await api('POST', 'tasks', { title: 'Разобрать антресоль', target: { kind: 'someday', categoryId: cat.id, index: 0 } });
  const monday = (await api('GET', 'week/' + new Date().toISOString().slice(0, 10))).weekStart;
  await api('POST', 'goals/from-task', { taskId: task.id, roleId: home.id, weekStart: monday, index: 0 });
  await api('DELETE', `categories/${cat.id}`);
  await load();
  const ask = async () => {
    const row = await page.evaluateHandle(() => [...document.querySelectorAll('.role .item')].find(i => i.textContent.includes('Разобрать антресоль')));
    await row.hover(); await (await row.$('.row-action[title="Удалить цель"]')).click(); await settle();
    assert(await has('.role .choice select'), 'не спросили категорию');
  };
  await ask();
  await clickAway();
  assert(!(await has('.role .choice')), 'клик мимо не закрыл');
  await ask();
  await page.keyboard.press('Escape'); await settle();
  assert(!(await has('.role .choice')), 'Esc не закрыл');
  assert(await page.evaluate(() => [...document.querySelectorAll('.role .item')].some(i => i.textContent.includes('Разобрать антресоль'))), 'цель удалилась');
});

await check('окна по-прежнему закрываются по Esc (слои редакторов им не мешают)', async () => {
  await page.click('.palette-b'); await page.waitForSelector('.themes'); await sleep(200);
  await page.keyboard.press('Escape'); await settle();
  assert(!(await has('.themes')), 'меню тем не закрылось');
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));

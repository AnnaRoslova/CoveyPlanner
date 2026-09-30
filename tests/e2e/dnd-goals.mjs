// @seed — нужны демо-данные (tools/seed-demo.ps1)
// Шаг 11: перетаскивание целей и ролей — дело → роль (цель недели), цель → день, цель → лента,
// порядок целей и перенос между ролями, порядок ролей.
import puppeteer from 'puppeteer-core';

const BASE = process.env.PLANNER_URL ?? 'http://localhost:5071/';
const SHOTS = process.argv[2] ?? '.';
const results = [];
const consoleErrors = [];
const browser = await puppeteer.launch({
  executablePath: process.env.PLANNER_BROWSER ?? 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  headless: true, defaultViewport: { width: 1600, height: 1300 }, // левая панель помещается целиком
});
const page = await browser.newPage();
page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text() + ' ' + (m.location()?.url ?? '')); });
page.on('pageerror', e => consoleErrors.push(e.message));
const sleep = ms => new Promise(r => setTimeout(r, ms));
const settle = () => sleep(600);
function assert(c, m) { if (!c) throw new Error(m); }
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-goals-${results.length}.png` }); }
}
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.role'); await settle(); }

const titleOf = el => [...el.querySelector('.tx').childNodes].filter(n => !n.classList?.contains('pills') && !n.classList?.contains('note')).map(n => n.textContent).join('').trim();
const T = `const titleOf = ${titleOf};`;
const roleSel = name => `[...document.querySelectorAll('.role')].find(r => r.querySelector('.role-h .name')?.textContent.trim() === ${JSON.stringify(name)})`;
const goalsOf = name => page.evaluate(`(() => { ${T} const r = ${roleSel(name)}; return r ? [...r.querySelectorAll('.item')].map(i => ({ title: titleOf(i), when: i.querySelector('.pill.when')?.textContent ?? null, done: i.classList.contains('done') })) : null; })()`);
const goalTitles = async name => (await goalsOf(name)).map(g => g.title);
const day = i => page.evaluate(`(() => { ${T} return [...document.querySelectorAll('.day')[${i}].querySelectorAll('.item')].map(it => ({ title: titleOf(it), time: it.querySelector('.pill.time')?.textContent ?? null, rock: !!it.querySelector('.pill.rock') })); })()`);
const dayTitles = async i => (await day(i)).map(t => t.title);
const feed = name => page.evaluate(n => {
  const cat = [...document.querySelectorAll('.cat')].find(c => c.querySelector('.cat-h .name')?.textContent.trim() === n);
  return cat ? [...cat.querySelectorAll('.cat-b .item .tx')].map(e => e.childNodes[0].textContent.trim()) : null;
}, name);
const rocks = () => page.$eval('.rocks b', b => b.textContent);
const userRoles = () => page.$$eval('.user-roles > .role .role-h .name', ns => ns.map(n => n.textContent.trim()));

// Прокрутить элемент в видимую часть панели и вернуть его рамку.
// Цель сброса — в середину (у края панели сработала бы автопрокрутка), источник — ближайшим способом.
async function boxOf(src, block = 'nearest') {
  const el = (await page.evaluateHandle(src)).asElement();
  assert(el, `не найдено: ${src.slice(0, 80)}`);
  await el.evaluate((e, b) => e.scrollIntoView({ block: b }), block);
  await sleep(100);
  return el.boundingBox();
}
const goalBox = (role, title) => boxOf(`(() => { ${T} return [...${roleSel(role)}.querySelectorAll('.item')].find(i => titleOf(i) === ${JSON.stringify(title)}); })()`);
const roleBox = role => boxOf(roleSel(role));
const roleHeadBox = role => boxOf(`${roleSel(role)}.querySelector('.role-h')`);
const dayItemBox = (i, title) => boxOf(`(() => { ${T} return [...document.querySelectorAll('.day')[${i}].querySelectorAll('.item')].find(it => titleOf(it) === ${JSON.stringify(title)}); })()`);
const dayBox = i => boxOf(`document.querySelectorAll('.day')[${i}]`);
const feedItemBox = (cat, title) => boxOf(`[...document.querySelectorAll('.cat')].find(c => c.querySelector('.cat-h .name')?.textContent.trim() === ${JSON.stringify(cat)})?.querySelectorAll('.cat-b .item').values().find(i => i.querySelector('.tx').childNodes[0].textContent.trim() === ${JSON.stringify(title)})`);

async function mouseDrag(from, x, y, { grabX = 30, grabY = null } = {}) {
  const sx = from.x + grabX, sy = grabY ?? from.y + Math.min(from.height / 2, 12);
  await page.mouse.move(sx, sy); await page.mouse.down();
  for (let i = 1; i <= 14; i++) { await page.mouse.move(sx + (x - sx) * i / 14, sy + (y - sy) * i / 14); await sleep(16); }
  await sleep(150);
  const line = await page.$eval('.drop-line', l => !l.hidden).catch(() => false);
  await page.mouse.up(); await settle();
  return line;
}

await load();
const rocksTotal = async () => Number((await rocks()).split(' из ')[1]);

await check('лента → пустая роль: становится целью, уходит из ленты, камней +1', async () => {
  const before = await rocksTotal();
  const from = await feedItemBox('Идеи', 'Сделать фотокнигу за лето');
  const head = await roleHeadBox('Дух');
  const line = await mouseDrag(from, head.x + 40, head.y + head.height + 6);
  assert(line, 'нет линии под заголовком пустой роли');
  assert(JSON.stringify(await goalTitles('Дух')) === '["Сделать фотокнигу за лето"]', `Дух: ${JSON.stringify(await goalsOf('Дух'))}`);
  assert(!(await feed('Идеи')).includes('Сделать фотокнигу за лето'), 'осталось в ленте');
  assert(await rocksTotal() === before + 1, `камней ${await rocks()}`);
  await load();
  assert((await goalTitles('Дух'))[0] === 'Сделать фотокнигу за лето', 'после перезагрузки');
});

await check('лента → роль на нужное место (над первой целью)', async () => {
  const from = await feedItemBox('Идеи', 'Сад с травами на балконе');
  const first = await goalBox('Дом', 'Поменять фильтр для воды');
  await mouseDrag(from, first.x + 40, first.y + 3);
  assert(JSON.stringify(await goalTitles('Дом')) === '["Сад с травами на балконе","Поменять фильтр для воды"]', `Дом: ${await goalTitles('Дом')}`);
});

await check('дело из дня → роль: цель с меткой дня, в дне — «камень»', async () => {
  const from = await dayItemBox(1, 'Созвон с заказчиком');
  const r = await roleBox('Работа');
  await mouseDrag(from, r.x + 40, r.y + r.height - 20);
  const g = (await goalsOf('Работа')).find(x => x.title === 'Созвон с заказчиком');
  assert(g?.when === 'Вт 11:00', `цель: ${JSON.stringify(g)}`);
  assert((await day(1)).find(t => t.title === 'Созвон с заказчиком')?.rock, 'в дне нет «камня»');
});

await check('«камень» из дня → другая роль: цель переезжает, а не дублируется', async () => {
  const from = await dayItemBox(2, 'Записаться к стоматологу');
  const r = await roleBox('Семья');
  await mouseDrag(from, r.x + 40, r.y + r.height - 20);
  assert(!(await goalTitles('Я сама')).includes('Записаться к стоматологу'), 'осталась у «Я сама»');
  const fam = await goalTitles('Семья');
  assert(fam.filter(t => t === 'Записаться к стоматологу').length === 1, `Семья: ${fam}`);
});

await check('порядок целей внутри роли', async () => {
  const from = await goalBox('Работа', 'Созвон с заказчиком');
  const first = await goalBox('Работа', 'Код-ревью модуля оплаты');
  await mouseDrag(from, first.x + 40, first.y + 3);
  const order = await goalTitles('Работа');
  assert(order[0] === 'Созвон с заказчиком', `Работа: ${order}`);
  await load();
  assert((await goalTitles('Работа'))[0] === 'Созвон с заказчиком', 'после перезагрузки');
});

await check('цель → другая роль', async () => {
  const from = await goalBox('Ум', '100 страниц');
  const r = await roleBox('Дух');
  await mouseDrag(from, r.x + 40, r.y + r.height - 20);
  assert((await goalTitles('Ум')).length === 0, 'осталась у «Ум»');
  assert((await goalTitles('Дух')).at(-1) === '100 страниц', `Дух: ${await goalTitles('Дух')}`);
});

await check('цель без дела → день: появляется дело-«камень», у цели метка дня', async () => {
  const from = await goalBox('Дом', 'Поменять фильтр для воды');
  const sun = await dayBox(6);
  await mouseDrag(from, sun.x + 40, sun.y + 200);
  const s = await day(6);
  assert(s.some(t => t.title === 'Поменять фильтр для воды' && t.rock), `Вс: ${JSON.stringify(s)}`);
  assert((await goalsOf('Дом')).find(g => g.title === 'Поменять фильтр для воды')?.when === 'Вс', 'у цели нет «Вс»');
  await load();
  assert((await dayTitles(6)).includes('Поменять фильтр для воды'), 'после перезагрузки');
});

await check('цель с делом → другой день: дело переезжает с временем, метка цели меняется', async () => {
  const from = await goalBox('Работа', 'Демо для команды');
  const fri = await dayBox(4);
  await mouseDrag(from, fri.x + 40, fri.y + 300);
  assert(!(await dayTitles(3)).includes('Демо для команды'), 'осталось в четверге');
  const f = await day(4);
  assert(f[0].title === 'Демо для команды' && f[0].time === '15:00', `Пт: ${JSON.stringify(f)}`);
  assert((await goalsOf('Работа')).find(g => g.title === 'Демо для команды')?.when === 'Пт 15:00', 'метка цели');
});

await check('цель «на неделю без дня» → понедельник между делами', async () => {
  const from = await goalBox('Дух', 'Сделать фотокнигу за лето');
  const target = await dayItemBox(0, 'Оплатить интернет');
  await mouseDrag(from, target.x + 40, target.y + 3);
  const mon = await dayTitles(0);
  assert(JSON.stringify(mon) === '["Бег в парке","Купить подарок маме","Сделать фотокнигу за лето","Оплатить интернет"]', `Пн: ${mon}`);
  assert((await goalsOf('Дух')).find(g => g.title === 'Сделать фотокнигу за лето')?.when === 'Пн', 'метка цели');
});

await check('цель без дела → лента: мысль в категории, цель исчезает, камней −1', async () => {
  const before = await rocksTotal();
  const from = await goalBox('Общение и эмоции', 'Увидеться с Олей');
  const first = await feedItemBox('Книги и фильмы', '«7 навыков» — перечитать главу 3');
  await mouseDrag(from, first.x + 40, first.y + 3);
  assert((await feed('Книги и фильмы'))[0] === 'Увидеться с Олей', `Книги: ${await feed('Книги и фильмы')}`);
  assert((await goalTitles('Общение и эмоции')).length === 0, 'цель осталась');
  assert(await rocksTotal() === before - 1, `камней ${await rocks()}`);
});

await check('цель с делом в дне → лента: дело уходит из дня в категорию', async () => {
  const from = await goalBox('Работа', 'Код-ревью модуля оплаты');
  const target = await feedItemBox('Идеи', 'Научиться печь хлеб на закваске');
  await mouseDrag(from, target.x + 40, target.y + target.height - 3);
  assert((await feed('Идеи')).includes('Код-ревью модуля оплаты'), `Идеи: ${await feed('Идеи')}`);
  assert(!(await dayTitles(1)).includes('Код-ревью модуля оплаты'), 'осталось во вторнике');
  assert(!(await goalTitles('Работа')).includes('Код-ревью модуля оплаты'), 'цель осталась');
  await load();
  assert((await feed('Идеи')).includes('Код-ревью модуля оплаты'), 'после перезагрузки');
});

await check('порядок ролей перетаскиванием за заголовок; сохраняется', async () => {
  const from = await roleHeadBox('Дом');
  const target = await roleHeadBox('Я сама');
  await mouseDrag(from, target.x + 60, target.y + 2, { grabX: 60, grabY: from.y + from.height / 2 });
  const order = await userRoles();
  assert(order[0] === 'Дом', `роли: ${order}`);
  await load();
  assert((await userRoles())[0] === 'Дом', `после перезагрузки: ${await userRoles()}`);
});

await check('измерения пилы не перетаскиваются, роль туда не встаёт', async () => {
  assert(!(await page.$('.saw .role[data-drag]')), 'у измерений пилы есть data-drag');
  const before = await userRoles();
  const from = await roleHeadBox('Семья');
  const saw = await roleBox('Ум');
  await mouseDrag(from, saw.x + 60, saw.y + 10, { grabX: 60, grabY: from.y + from.height / 2 });
  assert(JSON.stringify(await userRoles()) === JSON.stringify(before), `роли: ${await userRoles()}`);
});

await check('клик по цели — переименование, а не перетаскивание', async () => {
  const b = await goalBox('Семья', 'Поход в парк с детьми');
  await page.mouse.click(b.x + 60, b.y + 8); await settle();
  assert(await page.$('.role .inline-input'), 'клик не открыл переименование');
  await page.keyboard.press('Escape'); await settle();
});

await browser.close();
console.log(results.join('\n'));
console.log(consoleErrors.length ? `\nОшибки консоли:\n${consoleErrors.join('\n')}` : '\nОшибок в консоли нет');

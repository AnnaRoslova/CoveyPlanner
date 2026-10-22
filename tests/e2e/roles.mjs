// @seed — нужны демо-данные (tools/seed-demo.ps1)
// Шаг 8: левая панель — цели (добавить, отметить, переименовать, удалить) и роли (создать, цвет, порядок, удалить).
import puppeteer from 'puppeteer-core';

const BASE = process.env.PLANNER_URL ?? 'http://localhost:5071/';
const SHOTS = process.argv[2] ?? '.';
const results = [];
const consoleErrors = [];
const browser = await puppeteer.launch({
  executablePath: process.env.PLANNER_BROWSER ?? 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  headless: true, defaultViewport: { width: 1600, height: 1000 },
});
const page = await browser.newPage();
page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text() + ' ' + (m.location()?.url ?? '')); });
page.on('pageerror', e => consoleErrors.push(e.message));
const sleep = ms => new Promise(r => setTimeout(r, ms));
const settle = () => sleep(450);
function assert(c, m) { if (!c) throw new Error(m); }
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-roles-${results.length}.png` }); }
}
const api = (method, path, body) => page.evaluate(async (method, path, body) => {
  const r = await fetch('/api/' + path, { method, headers: { 'Content-Type': 'application/json' }, body: body ? JSON.stringify(body) : undefined });
  return r.status === 204 ? null : r.json();
}, method, path, body);
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.role'); await settle(); }

// Роль по названию (элемент) и её цели
const roleHandle = async name => {
  const h = await page.evaluateHandle(n => [...document.querySelectorAll('.role')].find(r => r.querySelector('.role-h .name')?.textContent.trim() === n), name);
  assert(await h.evaluate(e => !!e), `нет роли «${name}»`);
  return h;
};
const goalsOf = name => page.evaluate(n => {
  const r = [...document.querySelectorAll('.role')].find(r => r.querySelector('.role-h .name')?.textContent.trim() === n);
  return r ? [...r.querySelectorAll('.item')].map(i => ({
    title: [...i.querySelector('.tx').childNodes].filter(c => !c.classList?.contains('pills') && !c.classList?.contains('goal-days')).map(c => c.textContent).join('').trim(),
    done: i.classList.contains('done'),
  })) : null;
}, name);
// «выполнено/всего» по строкам целей роли (счёта у роли больше нет, ui-declutter D5)
const countOf = name => page.evaluate(n => { const r = [...document.querySelectorAll('.role')].find(x => x.querySelector('.role-h .name')?.textContent.trim() === n); return r ? (() => { const items = [...r.querySelectorAll('.role-body > .item')]; return items.length ? `${items.filter(i => i.classList.contains('done')).length}/${items.length}` : null; })() : null; }, name);
const userRoles = () => page.$$eval('.user-roles > .role .role-h .name', ns => ns.map(n => n.textContent.trim()));
async function goalRow(roleName, title) {
  const role = await roleHandle(roleName);
  const rows = await role.$$('.item');
  for (const r of rows) {
    const t = await r.evaluate(i => [...i.querySelector('.tx').childNodes].filter(c => !c.classList?.contains('pills') && !c.classList?.contains('goal-days')).map(c => c.textContent).join('').trim());
    if (t === title) return r;
  }
  throw new Error(`нет цели «${title}» у «${roleName}»`);
}
const dayTitles = i => page.evaluate(i => [...document.querySelectorAll('.day')[i].querySelectorAll('.item .tx')]
  .map(tx => [...tx.childNodes].filter(c => !c.classList?.contains('pills') && !c.classList?.contains('goal-days')).map(c => c.textContent).join('').trim()), i);
const dayItem = (i, title) => page.evaluate((i, t) => {
  const it = [...document.querySelectorAll('.day')[i].querySelectorAll('.item')].find(x => x.querySelector('.tx').textContent.includes(t));
  return it ? { done: it.classList.contains('done'), rock: !!it.querySelector('.title.rock') } : null;
}, i, title);
async function openRoleMenu(name) {
  const role = await roleHandle(name);
  await role.hover();
  await (await role.$('.menu')).click(); await settle();
  await page.waitForSelector('.popover.more-menu .option.edit-role'); await page.click('.popover.more-menu .option.edit-role'); await settle();
}
const clickButton = async (scope, text) => {
  const h = await page.evaluateHandle((s, t) => [...document.querySelectorAll(`${s} button`)].find(b => b.textContent.trim() === t), scope, text);
  assert(await h.evaluate(e => !!e), `нет кнопки «${text}» в ${scope}`);
  await h.click(); await settle();
};

await load();

await check('«+ цель» у измерения пилы; после перезагрузки на месте', async () => {
  const role = await roleHandle('Дух');
  await (await role.$('.add-goal')).click(); await settle();
  await page.keyboard.type('Медитация 10 минут'); await page.keyboard.press('Enter'); await settle();
  await page.keyboard.press('Escape'); await settle();
  assert(JSON.stringify((await goalsOf('Дух')).map(g => g.title)) === '["Медитация 10 минут"]', JSON.stringify(await goalsOf('Дух')));
  assert(await countOf('Дух') === '0/1', `счётчик ${await countOf('Дух')}`);
  await load();
  assert((await goalsOf('Дух')).length === 1, 'после перезагрузки нет');
});

await check('отметить цель: счётчик роли и камней', async () => {
  const before = await page.$eval('.rocks b', b => b.textContent);
  await (await (await goalRow('Ум', '100 страниц')).$('.ck')).click(); await settle();
  assert(await countOf('Ум') === '1/1', `Ум ${await countOf('Ум')}`);
  const after = await page.$eval('.rocks b', b => b.textContent);
  assert(after !== before, `камней ${before} → ${after}`);
});

await check('клик по отметке выполненной цели снимает отметку с её занятия в дне', async () => {
  assert((await dayItem(1, 'Код-ревью')).done, 'исходно не выполнено');
  await (await (await goalRow('Работа', 'Код-ревью модуля оплаты')).$('.ck')).click(); await settle();
  assert(!(await dayItem(1, 'Код-ревью')).done, 'дело осталось отмеченным');
  await load();
  assert(!(await dayItem(1, 'Код-ревью')).done, 'после перезагрузки');
});

await check('переименовать цель в её окне — занятие с тем же названием тоже', async () => {
  await (await goalRow('Работа', 'Демо для команды')).$eval('.tx', e => e.click()); await settle();
  await page.$eval('.editor .title-input', e => e.select());
  await page.keyboard.type('Демо для всей команды'); await page.keyboard.press('Enter'); await settle();
  assert((await dayTitles(3)).includes('Демо для всей команды'), `Чт: ${await dayTitles(3)}`);
  await load();
  assert((await goalsOf('Работа')).some(g => g.title === 'Демо для всей команды'), 'после перезагрузки');
});

await check('удалить цель без занятий и цель с невыполненным занятием (оно уходит в корзину)', async () => {
  const f = await goalRow('Дом', 'Поменять фильтр для воды');
  await f.hover(); await (await f.$('.row-action[title="Удалить цель"]')).click(); await settle();
  assert((await goalsOf('Дом')).length === 0, 'цель не удалилась');
  const s = await goalRow('Я сама', 'Записаться к стоматологу');
  await s.hover(); await (await s.$('.row-action[title="Удалить цель"]')).click(); await settle();
  assert((await goalsOf('Я сама')).length === 0, 'цель не удалилась');
  assert(!(await dayItem(2, 'Записаться к стоматологу')), 'занятие осталось в среде');
});

await check('удалить цель, чьё дело без дня и без категории — дело в «неразобранное», без вопросов', async () => {
  const board = await api('GET', 'board');
  const home = board.roles.find(r => r.name === 'Дом');
  const cat = await api('POST', 'categories', { name: 'Временная', emoji: null });
  const task = await api('POST', 'tasks', { title: 'Разобрать антресоль', target: { kind: 'someday', categoryId: cat.id, index: 0 } });
  const monday = (await api('GET', 'week/' + new Date().toISOString().slice(0, 10))).weekStart;
  await api('POST', 'goals/from-task', { taskId: task.id, roleId: home.id, weekStart: monday, index: 0 });
  await api('DELETE', `categories/${cat.id}`);
  await load();
  const g = await goalRow('Дом', 'Разобрать антресоль');
  await g.hover(); await (await g.$('.row-action[title="Удалить цель"]')).click(); await settle();
  assert((await goalsOf('Дом')).length === 0, 'цель не удалилась');
  const inUnsorted = await page.evaluate(() => [...document.querySelectorAll('.unsorted .item')].some(i => i.textContent.includes('Разобрать антресоль')));
  assert(inUnsorted, 'дело не появилось в «неразобранном»');
});

await check('«+ роль» с цветом из палитры', async () => {
  await page.evaluate(() => [...document.querySelectorAll('.panel-scroll > .add')].find(b => b.textContent.includes('+ роль')).click()); await settle();
  await page.keyboard.type('Друзья');
  const swatches = await page.$$('.role-editor .swatch');
  assert(swatches.length === 18, `цветов ${swatches.length}`);
  const cols = await page.$eval('.role-editor .swatches', s => getComputedStyle(s).gridTemplateColumns.split(' ').length);
  assert(cols === 6, `колонок ${cols}`);
  await swatches[8].click(); await settle();
  await page.screenshot({ path: `${SHOTS}/role-editor.png`, clip: { x: 0, y: 380, width: 280, height: 620 } });
  await clickButton('.role-editor', 'Создать');
  assert((await userRoles()).at(-1) === 'Друзья', `роли ${await userRoles()}`);
  const color = await (await roleHandle('Друзья')).evaluate(r => r.style.getPropertyValue('--c').trim());
  assert(color === '#b7c4f0', `цвет ${color}`);
});

await check('правка роли: название и цвет; кнопок ↑↓ больше нет; сохраняется', async () => {
  await openRoleMenu('Друзья');
  const fits = await page.evaluate(() => {
    const ed = document.querySelector('.role-editor').getBoundingClientRect();
    return [...document.querySelectorAll('.role-editor button')].every(b => { const r = b.getBoundingClientRect(); return r.right <= ed.right + 0.5 && r.left >= ed.left - 0.5; });
  });
  assert(fits, 'кнопки редактора выходят за край');
  const labels = await page.$$eval('.role-editor button', bs => bs.map(b => b.textContent.trim()));
  assert(!labels.includes('↑') && !labels.includes('↓'), `кнопки ${labels}`);
  await page.$eval('.role-editor .inline-input', e => e.select());
  await page.keyboard.type('Друзья и соседи');
  await (await page.$$('.role-editor .swatch'))[0].click();
  await clickButton('.role-editor', 'Сохранить');
  await load();
  const roles = await userRoles();
  assert(JSON.stringify(roles) === '["Я сама","Семья","Работа","Дом","Друзья и соседи"]', `порядок ${roles}`);
  const color = await (await roleHandle('Друзья и соседи')).evaluate(r => r.style.getPropertyValue('--c').trim());
  assert(color === '#9fd3bd', `цвет ${color}`);
});

await check('измерение пилы: только цвет, без переименования и удаления', async () => {
  await openRoleMenu('Тело');
  assert(!(await page.$('.role-editor .inline-input')), 'есть поле названия');
  const buttons = await page.$$eval('.role-editor button', bs => bs.map(b => b.textContent.trim()));
  assert(!buttons.includes('удалить') && !buttons.includes('↑'), `кнопки ${buttons}`);
  await (await page.$$('.role-editor .swatch'))[1].click();
  await clickButton('.role-editor', 'Сохранить');
  const color = await (await roleHandle('Тело')).evaluate(r => r.style.getPropertyValue('--c').trim());
  assert(color === '#6fbfae', `цвет ${color}`);
});

await check('удалить роль с подтверждением', async () => {
  await openRoleMenu('Друзья и соседи');
  await clickButton('.role-editor', 'удалить');
  assert((await page.$eval('.role-editor .confirm span', e => e.textContent)).includes('Друзья и соседи'), 'нет подтверждения');
  await clickButton('.role-editor', 'Удалить');
  assert(!(await userRoles()).includes('Друзья и соседи'), 'роль осталась');
});

await check('удалить роль, у цели которой дело без дня и категории — дело в «неразобранное», без вопросов', async () => {
  const role = await api('POST', 'roles', { name: 'Временная роль', color: null });
  const cat = await api('POST', 'categories', { name: 'Ещё временная', emoji: null });
  const task = await api('POST', 'tasks', { title: 'Купить ёлку', target: { kind: 'someday', categoryId: cat.id, index: 0 } });
  const monday = (await api('GET', 'week/' + new Date().toISOString().slice(0, 10))).weekStart;
  await api('POST', 'goals/from-task', { taskId: task.id, roleId: role.id, weekStart: monday, index: 0 });
  await api('DELETE', `categories/${cat.id}`);
  await load();
  await openRoleMenu('Временная роль');
  await clickButton('.role-editor', 'удалить');
  await clickButton('.role-editor', 'Удалить');
  assert(!(await userRoles()).includes('Временная роль'), 'роль осталась');
  const inFeed = await page.evaluate(() => [...document.querySelectorAll('.unsorted .item')].some(i => i.textContent.includes('Купить ёлку')));
  assert(inFeed, 'дело не вернулось в ленту');
});

await check('больше трёх целей — мягкая подсказка', async () => {
  const role = await roleHandle('Работа');
  await (await role.$('.add-goal')).click(); await settle();
  for (const t of ['Третья', 'Четвёртая']) { await page.keyboard.type(t); await page.keyboard.press('Enter'); await settle(); }
  await page.keyboard.press('Escape'); await settle();
  const hint = await (await roleHandle('Работа')).evaluate(r => r.querySelector('.hint')?.textContent);
  assert(hint?.includes('1–3'), `подсказка: ${hint}`);
  assert((await goalsOf('Работа')).length === 4, 'не 4 цели');
});

await page.screenshot({ path: `${SHOTS}/roles-final.png` });
await browser.close();
console.log(results.join('\n'));
console.log(consoleErrors.length ? `\nОшибки консоли:\n${consoleErrors.join('\n')}` : '\nОшибок в консоли нет');

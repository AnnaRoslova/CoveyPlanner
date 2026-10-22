// @seed — нужны демо-данные (tools/seed-demo.ps1)
// Постоянные цели («каждую неделю»): переключатель в окне цели (у пилы включён по умолчанию), цель сама появляется
// на следующей неделе (без занятий; если выбраны дни — с занятиями в эти дни), удаление — вопрос «Только на этой неделе» / «С этой недели и дальше» / «Отмена», с «Вернуть».
// Выключение «каждую неделю» при уже созданных следующих неделях — вопрос «Закончить серию» (fixes-2026-10.md, E1.2); снова включили — недели вернулись (E1.1).
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
const settle = () => sleep(450);
function assert(c, m) { if (!c) throw new Error(m); }
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-goal-series-${results.length}.png` }); }
  for (const sel of ['.editor']) if (await page.$(sel)) { await page.keyboard.press('Escape'); await settle(); }
}
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day'); await settle(); }
async function clickText(selector, text) {
  const ok = await page.evaluate((s, t) => {
    const el = [...document.querySelectorAll(s)].find(e => e.textContent.trim() === t);
    el?.click();
    return !!el;
  }, selector, text);
  assert(ok, `нет «${text}» (${selector})`);
  await settle();
}

const goalsOf = role => page.evaluate(n => [...[...document.querySelectorAll('.role')]
  .find(r => r.querySelector('.role-h .name')?.textContent.trim() === n)
  .querySelectorAll('.item .title')].map(t => t.textContent.trim()), role);
const goalRow = title => page.evaluateHandle(t => [...document.querySelectorAll('.role .item')].find(i => i.querySelector('.title').textContent.trim() === t), title);
async function addGoal(role, title) {
  await page.evaluate(n => [...document.querySelectorAll('.role')].find(r => r.querySelector('.role-h .name')?.textContent.trim() === n)
    .querySelector('.role-h .add-goal').click(), role);
  await settle();
  await page.keyboard.type(title); await page.keyboard.press('Enter'); await settle();
  await page.keyboard.press('Escape'); await settle();
}
async function openGoal(title) {
  await (await (await goalRow(title)).$('.title')).click(); await settle();
  assert(await page.$('.editor[aria-label="Цель недели"]'), 'окно цели не открылось');
  // Раскрыть все редкие поля окна (сводки и «+ …»): здесь проверяются поля, правило «заполненное видно» — в declutter.mjs
  for (let i = 0; i < 14; i++) { const b = await page.$('.editor .sum-chip:not(.static), .editor .adder'); if (!b) break; await b.click(); await new Promise(r => setTimeout(r, 150)); } await new Promise(r => setTimeout(r, 250));
}
const switchOn = () => page.$eval('.editor .switch-row input', i => i.checked);
// Листать неделю и дождаться, что заголовок сменился и неделя загрузилась
async function flip(title) {
  const before = await page.$eval('.wt', e => e.textContent);
  await page.click(`.nav[title^="${title}"]`);
  await page.waitForFunction(b => document.querySelector('.wt').textContent !== b, { timeout: 3000 }, before);
  await sleep(700);
}
const nextWeek = () => flip('Следующая неделя');
const prevWeek = () => flip('Предыдущая неделя');
const toast = async () => (await page.$('.toast')) ? page.$eval('.toast', e => [...e.querySelectorAll('span, button')].map(x => x.textContent.trim()).join(' ')) : null;
async function deleteByCross(title) {
  const row = await goalRow(title);
  await row.hover();
  await (await row.$('.row-action[title="Удалить цель"]')).click(); await settle();
}

await load();

await check('«каждую неделю»: у цели пилы включено, у цели обычной роли — выключено', async () => {
  await openGoal('Бег');
  assert(await switchOn(), 'у «Бега» (Тело) выключено');
  await page.screenshot({ path: `${SHOTS}/goal-series-editor.png` });
  await page.keyboard.press('Escape'); await settle();
  await openGoal('Демо для команды');
  assert(!(await switchOn()), 'у цели «Работы» включено');
});

await check('новая цель пилы появляется на следующей неделе — без занятий, прогресс с нуля', async () => {
  await addGoal('Тело', 'Бассейн');
  await nextWeek();
  const body = await goalsOf('Тело');
  assert(body.includes('Бассейн') && body.includes('Бег'), `Тело на следующей неделе: ${body}`);
  const run = await (await goalRow('Бег')).evaluate(i => ({ days: i.querySelectorAll('.goal-days .d').length, progress: (t => t ? t.replace(/.*выполнено (\d+) из (\d+).*/, '$1/$2') : null)((i.querySelector('.pct-ring, .pct')?.getAttribute('title'))) ?? i.querySelector('.progress')?.textContent }));
  assert(run.days === 0 && run.progress === '0/3', `«Бег»: ${JSON.stringify(run)}`);
  assert(!(await goalsOf('Работа')).includes('Демо для команды'), 'разовая цель тоже появилась');
});

await check('удалить постоянную: вопрос с тремя кнопками; «Отмена» ничего не меняет', async () => {
  await deleteByCross('Бассейн');
  assert(await page.$eval('.confirm-delete .question', e => e.textContent.trim()) === 'Удалить «Бассейн»?', 'нет вопроса');
  const buttons = await page.$$eval('.confirm-delete .btn', bs => bs.map(b => b.textContent.trim()));
  assert(JSON.stringify(buttons) === '["Только на этой неделе","С этой недели и дальше","Отмена"]', `кнопки: ${buttons}`);
  await clickText('.confirm-delete .btn', 'Отмена');
  assert(!(await page.$('.confirm-delete')) && (await goalsOf('Тело')).includes('Бассейн'), 'отмена не сработала');
});

// Перейти на неделю: 0 — текущая, 1 — следующая, -1 — прошлая
async function goWeek(offset) {
  if (await page.$('.today-b')) {
    const before = await page.$eval('.wt', e => e.textContent);
    await page.click('.today-b');
    await page.waitForFunction(b => document.querySelector('.wt').textContent !== b, { timeout: 3000 }, before);
    await sleep(700);
  }
  for (let i = 0; i < Math.abs(offset); i++) await (offset > 0 ? nextWeek() : prevWeek());
}
// Цели роли на неделе — по данным сервера (неделя открывается — постоянные цели появляются)
const monday = await page.evaluate(() => {
  const d = new Date(); d.setDate(d.getDate() - (d.getDay() + 6) % 7);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
});
async function serverGoals(offset, role) {
  return page.evaluate(async (monday, offset, role) => {
    const d = new Date(monday); d.setDate(d.getDate() + 7 * offset);
    const week = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
    const board = await (await fetch('/api/board')).json();
    const roleId = board.roles.find(r => r.name === role).id;
    return (await (await fetch(`/api/week/${week}`)).json()).goals.filter(g => g.roles.some(r => r.roleId === roleId)).map(g => g.title);
  }, monday, offset, role);
}

await check('«Бассейн» — с этой недели и дальше; «Теннис» каждую неделю → через неделю «Теннис» есть, «Бассейна» нет; раньше — на месте', async () => {
  await goWeek(0);
  await deleteByCross('Бассейн');
  await clickText('.confirm-delete .btn', 'С этой недели и дальше');
  assert(await toast() === 'Удалено «Бассейн» — с этой недели и дальше · Вернуть', `плашка: ${await toast()}`);
  assert(!(await goalsOf('Тело')).includes('Бассейн'), 'не удалилась');
  await addGoal('Тело', 'Теннис');
  await goWeek(1);
  const body = await goalsOf('Тело');
  assert(body.includes('Теннис') && !body.includes('Бассейн'), `через неделю: ${body}`);
  assert(JSON.stringify(await serverGoals(2, 'Тело')) === '["Бег","Теннис"]', `через две недели: ${await serverGoals(2, 'Тело')}`);
  assert(JSON.stringify(await serverGoals(0, 'Тело')) === '["Бег","Теннис"]', `эта неделя: ${await serverGoals(0, 'Тело')}`);
});

await check('«Вернуть» после «с этой недели и дальше» — серия продолжается', async () => {
  await goWeek(1);
  await deleteByCross('Теннис');
  await clickText('.confirm-delete .btn', 'С этой недели и дальше');
  await page.click('.toast .action'); await settle();
  assert((await goalsOf('Тело')).includes('Теннис'), 'не вернулась');
  assert((await serverGoals(3, 'Тело')).includes('Теннис'), 'серия не продолжается');
});

await check('«Только на этой неделе» — на следующей неделе цель снова есть, а здесь не возвращается', async () => {
  await goWeek(0);
  await deleteByCross('Теннис');
  await clickText('.confirm-delete .btn', 'Только на этой неделе');
  assert(await toast() === 'Удалено «Теннис» — на этой неделе · Вернуть', `плашка: ${await toast()}`);
  await goWeek(1); // листание отправляет удаление на сервер сразу
  assert((await goalsOf('Тело')).includes('Теннис'), 'на следующей неделе нет');
  await goWeek(0);
  assert(!(await goalsOf('Тело')).includes('Теннис'), 'на этой неделе вернулась');
});

await check('выключить «каждую неделю» (E1.2): вопрос «Закончить … после этой недели?» с числом целей; «Отмена» — окно на месте', async () => {
  await goWeek(1); // цель следующей недели уже создана
  assert((await goalsOf('Ум')).includes('100 страниц'), 'на следующей неделе нет');
  await goWeek(0);
  await openGoal('100 страниц');
  await page.click('.editor .switch-row'); await settle();
  assert(!(await switchOn()), 'переключатель не выключился');
  await page.click('.editor .buttons .btn.primary'); await settle();
  const q = await page.$eval('.series-end', e => e.textContent.replace(/\s+/g, ' ').trim()).catch(() => null);
  assert(q?.includes('Закончить «100 страниц» после этой недели?') && /Уберутся цели: \d+, занятий: \d+/.test(q)
    && q.includes('Закончить серию') && q.includes('Отмена'), JSON.stringify(q));
  await page.screenshot({ path: `${SHOTS}/goal-series-end.png` });
  await page.click('.series-end .btn:not(.danger)'); await settle();
  assert(!(await page.$('.series-end')), 'вопрос не закрылся');
  assert(await page.$('.editor[aria-label="Цель недели"]') && !(await switchOn()), 'окно цели закрылось или переключатель сброшен');
  await page.keyboard.press('Escape'); await settle();
  await goWeek(1);
  assert((await goalsOf('Ум')).includes('100 страниц'), 'после «Отмена» цели следующей недели нет');
});

await check('«Закончить серию» — со следующей недели цели нет; в окне выключено', async () => {
  await goWeek(0);
  await openGoal('100 страниц');
  await page.click('.editor .switch-row'); await settle();
  await page.click('.editor .buttons .btn.primary'); await settle();
  await page.click('.series-end .btn.danger'); await settle();
  assert(!(await page.$('.editor')), 'окна не закрылись');
  await openGoal('100 страниц');
  assert(!(await switchOn()), 'после сохранения включено');
  await page.keyboard.press('Escape'); await settle();
  await goWeek(1);
  assert(!(await goalsOf('Ум')).includes('100 страниц'), 'на следующей неделе есть');
});

await check('E1.1: снова включить «каждую неделю» — цель следующей недели вернулась (без вопроса)', async () => {
  await goWeek(0);
  await openGoal('100 страниц');
  await page.click('.editor .switch-row'); await settle();
  assert(await switchOn(), 'переключатель не включился');
  await page.click('.editor .buttons .btn.primary'); await settle();
  assert(!(await page.$('.series-end')), 'вопрос при включении');
  await goWeek(1);
  assert((await goalsOf('Ум')).includes('100 страниц'), 'на следующей неделе не вернулась');
  await goWeek(2);
  assert((await goalsOf('Ум')).includes('100 страниц'), 'через неделю нет');
  // Дальше сценарий ждёт законченную серию — выключаем снова
  await goWeek(0);
  await openGoal('100 страниц');
  await page.click('.editor .switch-row'); await settle();
  await page.click('.editor .buttons .btn.primary'); await settle();
  await page.click('.series-end .btn.danger'); await settle();
});

await check('разовую цель удаляют без вопроса', async () => {
  await goWeek(0);
  await deleteByCross('Демо для команды');
  assert(!(await page.$('.confirm-delete')), 'спросили про разовую');
  assert(!(await goalsOf('Работа')).includes('Демо для команды'), 'не удалилась');
});

// Занятия цели по дням показанной недели: индексы дней (0 — Пн), где есть дело с этим названием
const sessionDays = title => page.evaluate(t => [...document.querySelectorAll('.day')]
  .map((d, i) => [...d.querySelectorAll('.item .title')].some(e => e.textContent.trim() === t) ? i : -1).filter(i => i >= 0), title);
// «Повтор» — выпадающий список, под ним 7 кружков дней (подписи П В С …, полные названия — в aria-label)
const repeat = () => page.$eval('.editor .repeat-row .select-label', e => e.textContent.trim());
const dayDots = () => page.$$eval('.editor .day-dot.on', ds => ds.map(d => d.getAttribute('aria-label')));
async function chooseRepeat(label) {
  await page.click('.editor .repeat-row .select-field'); await settle();
  await clickText('.editor .popover .option', label);
}
async function chooseDays(...names) {
  for (const n of names) { await page.click(`.editor .day-dot[aria-label="${n}"]`); await settle(); }
}

await check('«по каким дням»: только у постоянной цели; по умолчанию «сам раскладываю»', async () => {
  await goWeek(0);
  await openGoal('Увидеться с Олей');
  assert(await repeat() === 'сам раскладываю', `повтор: ${await repeat()}`);
  assert(!(await page.$('.editor .day-dots')), 'кружки дней видны при «сам раскладываю»');
  await page.click('.editor .switch-row'); await settle(); // выключили «каждую неделю»
  assert(!(await page.$('.editor .repeat-row')), 'повтор виден у разовой цели');
  await page.keyboard.press('Escape'); await settle();
});

await check('дни Пн, Ср, Пт: «раз в неделю» = 3 (заблокировано), занятия встали в эти дни; на следующей неделе — тоже', async () => {
  await goWeek(0);
  await openGoal('Увидеться с Олей');
  await chooseRepeat('свои дни');
  await chooseDays('Пн', 'Ср', 'Пт');
  assert(await repeat() === 'свои дни', `повтор: ${await repeat()}`);
  assert(await page.$eval('.editor .times-fixed', e => e.textContent.trim()) === '3', 'не 3');
  assert(!(await page.$('.editor .times-per-week .step')), 'степпер не заблокирован');
  await page.screenshot({ path: `${SHOTS}/goal-series-days.png` });
  await page.click('.editor .buttons .btn.primary'); await sleep(1200);
  assert(JSON.stringify(await sessionDays('Увидеться с Олей')) === '[0,2,4]', `эта неделя: ${await sessionDays('Увидеться с Олей')}`);
  await goWeek(1);
  assert(JSON.stringify(await sessionDays('Увидеться с Олей')) === '[0,2,4]', `следующая: ${await sessionDays('Увидеться с Олей')}`);
});

await check('сменить дни на Вт, Чт: лишние — в корзину с плашкой «Убрано занятий: N · Вернуть»; «Вернуть» возвращает', async () => {
  await goWeek(0);
  await openGoal('Увидеться с Олей');
  await chooseDays('Пн', 'Ср', 'Пт', 'Вт', 'Чт'); // сняли Пн, Ср, Пт и выбрали Вт, Чт
  assert(JSON.stringify(await dayDots()) === '["Вт","Чт"]', `выбрано: ${await dayDots()}`);
  assert(await repeat() === 'свои дни', `повтор: ${await repeat()}`);
  await page.click('.editor .buttons .btn.primary'); await sleep(1500);
  assert(JSON.stringify(await sessionDays('Увидеться с Олей')) === '[1,3]', `после смены: ${await sessionDays('Увидеться с Олей')}`);
  const t = await toast();
  // По 3 занятия (Пн, Ср, Пт) на этой неделе и на каждой уже открытой следующей
  const removed = Number(/^Убрано занятий: (\d+) · Вернуть$/.exec(t ?? '')?.[1]);
  assert(removed >= 6 && removed % 3 === 0, `плашка: ${t}`);
  await page.click('.toast .action'); await sleep(1500);
  assert(JSON.stringify(await sessionDays('Увидеться с Олей')) === '[0,1,2,3,4]', `после «Вернуть»: ${await sessionDays('Увидеться с Олей')}`);
});

await check('«каждый день» — 7 раз в неделю; «будни», «выходные»; кружок руками — «свои дни»', async () => {
  await goWeek(0);
  await openGoal('Бег');
  await chooseRepeat('каждый день');
  assert(await page.$eval('.editor .times-fixed', e => e.textContent.trim()) === '7', 'не 7');
  assert((await dayDots()).length === 7, `выбрано: ${await dayDots()}`);
  await chooseRepeat('будни');
  assert(await page.$eval('.editor .times-fixed', e => e.textContent.trim()) === '5', 'будни — не 5');
  assert(JSON.stringify(await dayDots()) === '["Пн","Вт","Ср","Чт","Пт"]', `будни: ${await dayDots()}`);
  await chooseRepeat('выходные');
  assert(JSON.stringify(await dayDots()) === '["Сб","Вс"]', `выходные: ${await dayDots()}`);
  await chooseDays('Пт'); // ручное переключение — «свои дни»
  assert(await repeat() === 'свои дни', `после кружка: ${await repeat()}`);
  assert(await page.$eval('.editor .times-fixed', e => e.textContent.trim()) === '3', 'Пт, Сб, Вс — не 3');
  await chooseRepeat('сам раскладываю');
  assert(!(await page.$('.editor .day-dots')), 'кружки остались');
  await page.keyboard.press('Escape'); await settle();
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));

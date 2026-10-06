// @seed — нужны демо-данные (tools/seed-demo.ps1): постоянные цели пилы «Бег» (Тело, 3 раза), «100 страниц» (Ум, стартовое 41),
// «Увидеться с Олей» (Общение) — это и есть привычки; «Записаться к стоматологу» (Я сама) — обычная цель.
// Привычка = постоянная цель с «🫙 копить» (у пилы — по умолчанию): экран 🫙 по ролям, без «+ копилка»; в окне цели — «копить», строка привычки и банка,
// «В копилку» нигде нет; отметка занятия → счёт, пилюля, подсказка «📎 +1»; «+1 сегодня» (одно в день); новая цель пилы — новая
// привычка; сняли «каждую неделю» — в архив с историей, включили — продолжается; удаление из архива с «Вернуть» (цель остаётся);
// каникулы. В конце — история «задним числом» (норма по неделям) и снимки экрана «Копилки» в темах (out/habits-<тема>.png).
import puppeteer from 'puppeteer-core';
import { makeHabitHistory, HISTORY } from './lib/habits-history.mjs';

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
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-habits-${results.length}.png` }); }
}
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.role'); await settle(); }
const escape = async () => { await page.keyboard.press('Escape'); await sleep(350); };
const todayIndex = (new Date().getDay() + 6) % 7;

const titleOf = el => [...el.querySelector('.tx').childNodes].filter(n => !n.classList?.contains('pills') && !n.classList?.contains('note') && !n.classList?.contains('goal-days')).map(n => n.textContent).join('').trim();
const T = `const titleOf = ${titleOf};`;
const goalSel = title => `(() => { ${T} return [...document.querySelectorAll('.role .item')].find(i => titleOf(i) === ${JSON.stringify(title)}); })()`;
const dayItemSel = (i, title) => `(() => { ${T} return [...document.querySelectorAll('.day')[${i}].querySelectorAll('.item')].find(it => titleOf(it) === ${JSON.stringify(title)}); })()`;
const pillOf = title => page.evaluate(`(() => { const g = ${goalSel(title)}; return g?.querySelector('.jar-pill')?.textContent.trim() ?? null; })()`);
const clickIn = (sel, text) => page.evaluate((sel, text) => {
  const el = [...document.querySelectorAll(sel)].find(b => b.textContent.trim() === text);
  if (!el) throw new Error(`нет «${text}» в ${sel}`);
  el.click();
}, sel, text);
const toastText = () => page.evaluate(() => document.querySelector('.toast')?.textContent.replace(/\s+/g, ' ').trim() ?? null);
const card = name => `.jar-card[aria-label="Привычка «${name}»"]`;
const cardTotal = name => page.evaluate(s => document.querySelector(`${s} .total-n`)?.textContent.trim() ?? null, card(name));
const cardButton = (name, text) => page.evaluate((s, t) => [...document.querySelector(s).querySelectorAll('.btn')].find(b => b.textContent.trim() === t).click(), card(name), text);
const serverHabit = name => page.evaluate(async n => (await (await fetch('/api/habits')).json()).habits.find(h => h.name === n) ?? null, name);
const api = (method, path, body) => page.evaluate(async (m, p, b) => {
  const r = await fetch(`/api/${p}`, { method: m, headers: { 'Content-Type': 'application/json' }, body: b ? JSON.stringify(b) : undefined });
  if (!r.ok) throw new Error(`${m} ${p}: ${r.status}`);
  return r.status === 204 ? null : r.json();
}, method, path, body);

async function boxOf(src) {
  const el = (await page.evaluateHandle(src)).asElement();
  assert(el, `не найдено: ${src.slice(0, 80)}`);
  await el.evaluate(e => e.scrollIntoView({ block: 'nearest' }));
  await sleep(100);
  return el.boundingBox();
}
async function mouseDrag(from, x, y) {
  const sx = from.x + 30, sy = from.y + Math.min(from.height / 2, 12);
  await page.mouse.move(sx, sy); await page.mouse.down();
  for (let i = 1; i <= 14; i++) { await page.mouse.move(sx + (x - sx) * i / 14, sy + (y - sy) * i / 14); await sleep(16); }
  await sleep(150);
  await page.mouse.up(); await settle();
}
async function openGoal(title) {
  const el = (await page.evaluateHandle(goalSel(title))).asElement();
  assert(el, `нет цели ${title}`);
  await el.$eval('.tx', e => e.click());
  await page.waitForSelector('.editor[aria-label="Цель недели"]', { timeout: 3000 });
  await sleep(200);
}
const habitLine = () => page.evaluate(() => document.querySelector('.editor .habit-line')?.textContent.replace(/\s+/g, ' ').trim() ?? null);
async function openScreen() {
  await page.click('.jar-b');
  await page.waitForSelector('.habits');
  await settle();
}
/** Группы экрана: [измерение, [привычки]] по порядку. */
const groups = () => page.evaluate(() => [...document.querySelectorAll('.habits .dimension')].map(h => [
  h.textContent.trim(),
  [...h.nextElementSibling.querySelectorAll('.jar-card .jar-name .name')].map(n => n.textContent.trim()),
]));
async function setRecurring(goal, on) {
  await openGoal(goal);
  const box = await page.$('.editor .switch-row input[role="switch"]');
  if (await box.evaluate(e => e.checked) !== on) {
    await page.evaluate(() => document.querySelector('.editor .switch-row input[role="switch"]').click());
    await sleep(150);
  }
  await clickIn('.editor .btn', 'Сохранить'); await settle();
}
const showArchived = async () => {
  if (!(await page.$('.archived-h[aria-expanded="true"]'))) { await page.click('.archived-h'); await sleep(300); }
};

await load();

await check('🫙 в шапке: экран по измерениям пилы — Бег (Тело), 100 страниц (Ум, 41), Увидеться с Олей (Общение); «+ копилка» нет', async () => {
  await openScreen();
  const g = JSON.stringify(await groups());
  assert(g === JSON.stringify([['Тело', ['Бег']], ['Ум', ['100 страниц']], ['Общение и эмоции', ['Увидеться с Олей']]]), g);
  assert(await cardTotal('100 страниц') === '41', `100 страниц: ${await cardTotal('100 страниц')}`);
  assert(await cardTotal('Бег') === '0', `Бег: ${await cardTotal('Бег')}`);
  assert((await page.$$eval(`${card('Бег')} .weeks .w`, ws => ws.length)) === 26, 'не 26 недель');
  assert(!(await page.$('.add-habit')), 'кнопка «+ копилка» осталась');
  await escape();
  assert(!(await page.$('.habits')), 'экран не закрылся');
});

await check('окно цели «Бег»: строка «🫙 привычка: 0 скрепок · 🔥0» и банка; «В копилку» нет; у обычной цели и дела — ничего', async () => {
  await openGoal('Бег');
  assert(await habitLine() === '🫙 привычка: 0 скрепок · 🔥0', `строка: ${await habitLine()}`);
  assert(await page.$('.editor input[aria-label="Стартовое число"]') && await page.$('.editor input[aria-label="Скрепок в банке"]'), 'нет настроек банки');
  assert(!(await page.$('.editor .select-field[aria-label="В копилку"]')), 'поле «В копилку» осталось');
  await escape();
  await openGoal('Записаться к стоматологу');
  assert(await habitLine() === null, 'строка привычки у обычной цели');
  await escape();
  const el = (await page.evaluateHandle(dayItemSel(1, 'Почитать 30 страниц'))).asElement();
  await el.$eval('.tx', e => e.click());
  await page.waitForSelector('.editor[aria-label="Дело"]'); await sleep(200);
  assert(!(await page.$('.editor .select-field[aria-label="В копилку"]')) && !(await page.$('.editor .habit-line')), 'в окне дела — копилка');
  await escape();
});

await check('цель → сегодня; отметили занятие — подсказка «📎 +1 в «Бег» · 1 · неделя 1 из 3», пилюля «🫙 1»', async () => {
  const from = await boxOf(goalSel('Бег'));
  const day = await boxOf(`document.querySelectorAll('.day')[${todayIndex}]`);
  await mouseDrag(from, day.x + 40, day.y + day.height - 40);
  const item = (await page.evaluateHandle(dayItemSel(todayIndex, 'Бег'))).asElement();
  assert(item, 'занятие не встало в день');
  await item.$eval('.ck-hit', e => e.click());
  await sleep(400);
  const toast = await toastText();
  assert(toast?.includes('📎') && toast.includes('+1 в «Бег» · 1 · неделя 1 из 3'), `подсказка: ${toast}`);
  assert(await page.$('.toast.clip-toast .clip'), 'нет анимированной скрепки');
  await page.screenshot({ path: `${SHOTS}/habits-toast.png` });
  await settle();
  assert(await pillOf('Бег') === '🫙 1', `пилюля: ${await pillOf('Бег')}`);
});

await check('сняли отметку — скрепка ушла: пилюля «🫙 0»', async () => {
  const item = (await page.evaluateHandle(dayItemSel(todayIndex, 'Бег'))).asElement();
  await item.$eval('.ck-hit', e => e.click());
  await settle();
  assert(await pillOf('Бег') === '🫙 0', `пилюля: ${await pillOf('Бег')}`);
  await item.$eval('.ck-hit', e => e.click()); // снова отмечено — для следующих проверок
  await settle();
});

await check('клик по пилюле — экран «Копилки» на «Бег» (карточка в фокусе)', async () => {
  await page.evaluate(`${goalSel('Бег')}.querySelector('.jar-pill').click()`);
  await page.waitForSelector('.habits'); await settle();
  const focused = await page.evaluate(() => document.querySelector('.jar-card.focused')?.getAttribute('aria-label'));
  assert(focused === 'Привычка «Бег»', `в фокусе: ${focused}`);
  assert(await cardTotal('Бег') === '1', `счёт: ${await cardTotal('Бег')}`);
});

await check('«+1 сегодня» у «Бег»: сегодня уже отмечено — второго занятия нет', async () => {
  await cardButton('Бег', '+1 сегодня');
  await sleep(400);
  const toast = await toastText();
  assert(toast?.toLowerCase().includes('сегодня уже отмечено'), `подсказка: ${toast}`);
  await settle();
  assert(await cardTotal('Бег') === '1', `счёт: ${await cardTotal('Бег')}`);
});

await check('«+1 сегодня» у «Увидеться с Олей» (1 раз в неделю): норма выполнена — сразу «🔥 1 нед. подряд», занятие в сегодня', async () => {
  await cardButton('Увидеться с Олей', '+1 сегодня');
  await sleep(400);
  assert((await toastText())?.includes('+1 в «Увидеться с Олей» · 1 · неделя 1 из 1 ✓'), `подсказка: ${await toastText()}`);
  await settle();
  const chip = await page.$eval(`${card('Увидеться с Олей')} .jar-info .chip`, e => e.textContent.replace(/\s+/g, ' ').trim());
  assert(chip === '🔥 1 нед. подряд', `серия: ${chip}`);
  await escape();
  const done = await page.evaluate(`(() => { const it = ${dayItemSel(todayIndex, 'Увидеться с Олей')}; return it?.classList.contains('done') ?? null; })()`);
  assert(done === true, `занятие в дне: ${done}`);
  assert(await pillOf('Увидеться с Олей') === '🫙 1 · 🔥1', `пилюля: ${await pillOf('Увидеться с Олей')}`);
});

await check('банка в окне цели: стартовое число +3 — пилюля «🫙 4»', async () => {
  await openGoal('Бег');
  for (let i = 0; i < 3; i++) {
    await page.evaluate(() => document.querySelector('.editor input[aria-label="Стартовое число"]').closest('.stepper').querySelector('.step[aria-label="Больше"]').click());
    await sleep(100);
  }
  await clickIn('.editor .btn', 'Сохранить'); await settle();
  assert(await pillOf('Бег') === '🫙 4', `пилюля: ${await pillOf('Бег')}`);
  assert((await serverHabit('Бег'))?.startCount === 3, 'на сервере не сохранилось');
});

await check('новая постоянная цель в Духе «Йога» — новая привычка в группе «Дух»', async () => {
  const role = (await page.evaluateHandle(() => [...document.querySelectorAll('.role')].find(r => r.querySelector('.role-h .name')?.textContent.trim() === 'Дух'))).asElement();
  await (await role.$('.add')).click(); await settle();
  await page.keyboard.type('Йога'); await page.keyboard.press('Enter'); await settle();
  await escape();
  assert(await pillOf('Йога') === '🫙 0', `пилюля: ${await pillOf('Йога')}`);
  await openScreen();
  const dukh = (await groups()).find(([d]) => d === 'Дух');
  assert(JSON.stringify(dukh?.[1]) === '["Йога"]', JSON.stringify(await groups()));
  await cardButton('Йога', '+1 сегодня'); await settle(); // скрепка — чтобы в архиве было что показать
  await escape();
});

await check('привычка в двух измерениях — один раз, в группе первого (Тело), «Дух» — чипом', async () => {
  const goal = (await api('GET', `week/${new Date().toISOString().slice(0, 10)}`)).goals.find(g => g.title === 'Йога');
  const roles = Object.fromEntries((await api('GET', 'board')).roles.map(r => [r.name, r.id]));
  await api('PUT', `goals/${goal.id}`, { title: 'Йога', roleIds: [roles['Дух'], roles['Тело']], timesPerWeek: 1, startTime: null, quadrant: null, notes: null, durationMinutes: null });
  await load();
  await openScreen();
  const g = await groups();
  assert(JSON.stringify(g.find(([d]) => d === 'Тело')?.[1]) === '["Бег","Йога"]', JSON.stringify(g));
  assert(!g.some(([d]) => d === 'Дух'), `группа «Дух» осталась: ${JSON.stringify(g)}`);
  const chips = await page.$$eval(`${card('Йога')} .dim-chip`, cs => cs.map(c => c.textContent.trim()));
  assert(JSON.stringify(chips) === '["Дух"]', JSON.stringify(chips));
  await escape();
});

await check('сняли «каждую неделю» — привычка в архиве (свёрнут) с историей; включили — снова среди идущих', async () => {
  await setRecurring('Йога', false);
  await openScreen();
  assert(!(await page.$(`.grid ${card('Йога')}`)), 'осталась среди идущих');
  assert((await page.$eval('.archived-h', e => e.textContent)).includes('в архиве: 1'), 'нет строки архива');
  assert(await page.$('.archived-h[aria-expanded="false"]'), 'архив не свёрнут');
  await showArchived();
  assert(await cardTotal('Йога') === '1', `в архиве счёт: ${await cardTotal('Йога')}`);
  assert(!(await page.$(`${card('Йога')} .plus-one`)), 'у архивной — «+1 сегодня»');
  await escape();
  await setRecurring('Йога', true);
  await openScreen();
  assert(await page.$(`.grid ${card('Йога')}`) && !(await page.$('.archived-h')), 'не вернулась');
  assert(await cardTotal('Йога') === '1', `счёт: ${await cardTotal('Йога')}`);
  await escape();
});

await check('удалить можно только из архива: у идущей в «настроить» нет «удалить»', async () => {
  await openScreen();
  await cardButton('Йога', 'настроить');
  await page.waitForSelector('.editor.habit-editor'); await sleep(200);
  const buttons = await page.$$eval('.editor.habit-editor .btn', bs => bs.map(b => b.textContent.trim()));
  assert(!buttons.includes('удалить'), JSON.stringify(buttons));
  await escape(); await escape();
});

await check('удаление из архива — плашка «Удалено «Йога» · Вернуть»; «Вернуть» — привычка целиком', async () => {
  await setRecurring('Йога', false);
  await openScreen(); await showArchived();
  await cardButton('Йога', 'настроить');
  await page.waitForSelector('.editor.habit-editor'); await sleep(200);
  await clickIn('.editor.habit-editor .btn', 'удалить'); await sleep(300);
  assert(!(await page.$(card('Йога'))), 'карточка не исчезла');
  const toast = await toastText();
  assert(toast?.includes('Удалено «Йога»') && toast.includes('Вернуть'), `плашка: ${toast}`);
  await page.click('.toast .action'); await settle();
  assert(await page.$(card('Йога')), 'после «Вернуть» карточки нет');
  await sleep(6500); // срок плашки вышел — на сервер ничего не ушло
  assert((await serverHabit('Йога'))?.isArchived, 'на сервере удалилась');
  await escape();
});

await check('без «Вернуть» — через 6 с удалена на сервере; цель «Йога» и её занятие остались', async () => {
  await openScreen(); await showArchived();
  await cardButton('Йога', 'настроить');
  await page.waitForSelector('.editor.habit-editor'); await sleep(200);
  await clickIn('.editor.habit-editor .btn', 'удалить'); await sleep(300);
  assert(await serverHabit('Йога'), 'удалилась на сервере сразу, а не через 6 с');
  await sleep(6800);
  assert(await serverHabit('Йога') === null, 'на сервере привычка осталась');
  await escape();
  await load();
  assert(await page.evaluate(`!!${goalSel('Йога')}`), 'цель «Йога» пропала');
  assert(await pillOf('Йога') === null, 'у цели пилюля удалённой привычки');
  assert(await page.evaluate(`!!${dayItemSel(todayIndex, 'Йога')}`), 'занятие «Йога» пропало из дня');
});

await check('каникулы: добавить (для всех) — в списке, на карточке подсказка; удалить — исчезли', async () => {
  await openScreen();
  await clickIn('.habits .btn', '🏖 Каникулы');
  await page.waitForSelector('.editor[aria-label="Каникулы"]');
  await clickIn('.editor[aria-label="Каникулы"] .btn', 'Сохранить'); await settle();
  const items = await page.$$eval('.vac-list li', ls => ls.map(l => l.textContent.replace(/\s+/g, ' ').trim()));
  assert(items.length === 1 && items[0].includes('все привычки'), JSON.stringify(items));
  await escape();
  assert(await page.$('.habits'), 'Esc в каникулах закрыл экран');
  const hint = await page.$eval(`${card('Бег')} .hint.vac`, e => e.textContent.trim()).catch(() => null);
  assert(hint?.startsWith('🏖 Каникулы до'), `подсказка: ${hint}`);
  await clickIn('.habits .btn', '🏖 Каникулы');
  await page.waitForSelector('.vac-list');
  await page.click('.vac-list .row-action'); await settle();
  assert(!(await page.$('.vac-list')), 'каникулы не удалились');
  await escape(); await escape();
});

// История «задним числом» и снимки экрана в темах
await check('история привычек: серия, пауза, каникулы, норма по неделям — как в макете', async () => {
  await makeHabitHistory(BASE);
  await load();
  await openScreen();
  const chips = name => page.$$eval(`${card(name)} .jar-info .chip`, cs => cs.map(c => c.textContent.replace(/\s+/g, ' ').trim()));
  const training = await chips(HISTORY.training);
  assert(training[0] === '🔥 14 нед. подряд', JSON.stringify(training)); // 13 прошлых + эта (норма уже выполнена)
  const breathing = await chips(HISTORY.breathing);
  assert(breathing[0].startsWith('⚠ пауза · 🔥 9'), JSON.stringify(breathing));
  assert(await page.$(`${card(HISTORY.breathing)} .hint.warn`), 'нет подсказки «не пропускай дважды»');
  const reading = await page.$$eval(`${card(HISTORY.reading)} .weeks .w`, ws => ws.map(w => [w.className.replace('w ', '').split(' ')[0], w.title]));
  assert(reading.filter(([c]) => c === 'vac').length === 2, JSON.stringify(reading));
  // Норму снизили с 4 до 3 на 12-й неделе узора — прошлые недели остались «из 4»
  assert(reading[1][1].includes('4 из 4') && reading[24][1].includes('из 3'), `${reading[1][1]} / ${reading[24][1]}`);
  assert((await page.$$eval(`${card(HISTORY.reading)} .pip.extra`, p => p.length)) === 2, 'не две розовые точки сверх нормы 3');
  await escape();
  assert((await pillOf(HISTORY.training))?.endsWith(' · 🔥14'), `пилюля: ${await pillOf(HISTORY.training)}`);
});

/** Переключатель в окне цели по подписи («каждую неделю», «🫙 копить»); null — его нет. */
const switchOf = label => page.evaluateHandle(l => [...document.querySelectorAll('.editor .switch-row')]
  .find(r => r.textContent.includes(l))?.querySelector('input') ?? null, label);
async function setSwitch(label, on) {
  const box = (await switchOf(label)).asElement();
  assert(box, `нет переключателя «${label}»`);
  if (await box.evaluate(e => e.checked) !== on) { await box.evaluate(e => e.click()); await sleep(150); }
}

await check('🫙 копить: у постоянной цели пилы включено по умолчанию', async () => {
  await openGoal('Бег');
  const box = (await switchOf('🫙 копить')).asElement();
  assert(box && await box.evaluate(e => e.checked), 'у «Бег» «копить» выключен или его нет');
  await escape();
});

await check('🫙 копить у обычной роли: включили «каждую неделю» — «копить» выключен; включили — привычка в группе «Я сама»', async () => {
  await openGoal('Записаться к стоматологу');
  assert(!(await switchOf('🫙 копить')).asElement(), '«копить» виден у разовой цели');
  await setSwitch('каждую неделю', true);
  const box = (await switchOf('🫙 копить')).asElement();
  assert(box && !(await box.evaluate(e => e.checked)), 'у обычной роли «копить» включён по умолчанию');
  await setSwitch('🫙 копить', true);
  await clickIn('.editor .btn', 'Сохранить'); await settle();
  assert(await pillOf('Записаться к стоматологу') === '🫙 0', `пилюля: ${await pillOf('Записаться к стоматологу')}`);
  await openScreen();
  const g = (await groups()).find(([d]) => d === 'Я сама');
  assert(JSON.stringify(g?.[1]) === '["Записаться к стоматологу"]', JSON.stringify(await groups()));
  await cardButton('Записаться к стоматологу', '+1 сегодня'); await settle();
  await escape();
});

await check('выключили «копить» — привычка пропала (не в архиве); включили — тот же счёт', async () => {
  const name = 'Записаться к стоматологу';
  await openGoal(name); await setSwitch('🫙 копить', false);
  await clickIn('.editor .btn', 'Сохранить'); await settle();
  assert(await pillOf(name) === null, `пилюля осталась: ${await pillOf(name)}`);
  await openScreen();
  assert(!(await page.$(card(name))), 'карточка осталась на экране');
  await escape();
  await openGoal(name); await setSwitch('🫙 копить', true);
  await clickIn('.editor .btn', 'Сохранить'); await settle();
  assert((await pillOf(name))?.startsWith('🫙 1'), `пилюля: ${await pillOf(name)}`);
});

for (const theme of ['lotus', 'peony', 'platinum', 'obsidian']) {
  await check(`снимок экрана «Копилки»: ${theme}`, async () => {
    await page.evaluate(k => localStorage.setItem('planner.theme', k), theme);
    await load();
    await page.evaluate(() => document.fonts.ready);
    await openScreen();
    await page.screenshot({ path: `${SHOTS}/habits-${theme}.png` });
    await escape();
    await page.screenshot({ path: `${SHOTS}/habits-pills-${theme}.png`, clip: { x: 0, y: 0, width: 420, height: 1000 } });
  });
}
await page.evaluate(() => localStorage.setItem('planner.theme', 'lotus'));

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);

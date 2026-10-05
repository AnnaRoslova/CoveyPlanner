// @seed — нужны демо-данные (tools/seed-demo.ps1): цели «Бег» (Тело, 3 раза), «100 страниц» (Ум); копилки «Тренировки», «Чтение»
// Копилки привычек: экран 🫙, новая копилка, привязка цели → отметка занятия → счёт, пилюля и подсказка «📎 +1»; норма недели — сразу в серии;
// снятая отметка; «+1 сегодня»; новая копилка из окна цели; копилка у дела; каникулы; архив и удаление с «Вернуть». В конце — история «задним числом»
// и снимки экрана «Копилки» в нескольких темах (out/habits-<тема>.png).
import puppeteer from 'puppeteer-core';
import { makeHabitHistory } from './lib/habits-history.mjs';

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
const cardTotal = name => page.evaluate(n => document.querySelector(`.jar-card[aria-label="Копилка «${n}»"] .total-n`)?.textContent.trim() ?? null, name);

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
async function chooseHabit(name) {
  await page.click('.editor .select-field[aria-label="В копилку"]');
  await page.waitForSelector('.editor .popover');
  await clickIn('.editor .popover .option .option-name', name);
  await sleep(250);
}
const habitFieldText = () => page.$eval('.editor .select-field[aria-label="В копилку"]', e => e.textContent.replace(/\s+/g, ' ').trim());
async function openScreen() {
  await page.click('.jar-b');
  await page.waitForSelector('.habits');
  await settle();
}

await load();

await check('🫙 в шапке открывает экран «Копилки»: карточки демо-копилок, «Чтение» — 41 скрепка; Esc закрывает', async () => {
  await openScreen();
  assert(await cardTotal('Чтение') === '41', `Чтение: ${await cardTotal('Чтение')}`);
  assert(await cardTotal('Тренировки') === '0', `Тренировки: ${await cardTotal('Тренировки')}`);
  const weeks = await page.$$eval('.jar-card[aria-label="Копилка «Чтение»"] .weeks .w', ws => ws.length);
  assert(weeks === 26, `недель ${weeks}`);
  await escape();
  assert(!(await page.$('.habits')), 'экран не закрылся');
});

await check('«+ копилка»: «Пробежки», норма 2 — новая карточка', async () => {
  await openScreen();
  await page.click('.add-habit');
  await page.waitForSelector('.editor[aria-label="Копилка"]');
  await page.type('.editor[aria-label="Копилка"] .title-input', 'Пробежки');
  await page.evaluate(() => document.querySelector('.editor[aria-label="Копилка"] input[aria-label="Норма в неделю"]').closest('.stepper').querySelector('.step[aria-label="Меньше"]').click());
  await sleep(150);
  await clickIn('.editor[aria-label="Копилка"] .btn', 'Создать');
  await settle();
  assert(await cardTotal('Пробежки') === '0', 'нет карточки «Пробежки»');
  const of = await page.$eval('.jar-card[aria-label="Копилка «Пробежки»"] .of', e => e.textContent.trim());
  assert(of === '0 из 2', `эта неделя: ${of}`);
  await escape();
});

await check('окно цели «Бег»: «🫙 В копилку» → «Тренировки» — у цели пилюля «🫙 0»', async () => {
  await openGoal('Бег');
  assert(await habitFieldText() === 'нет', `в поле: ${await habitFieldText()}`);
  await chooseHabit('Тренировки');
  assert((await habitFieldText()).includes('Тренировки'), `в поле: ${await habitFieldText()}`);
  await clickIn('.editor .btn', 'Сохранить'); await settle();
  assert(await pillOf('Бег') === '🫙 0', `пилюля: ${await pillOf('Бег')}`);
});

await check('цель → сегодня; отметили занятие — подсказка «📎 +1 в «Тренировки» · 1 · неделя 1 из 3», пилюля «🫙 1»', async () => {
  const from = await boxOf(goalSel('Бег'));
  const day = await boxOf(`document.querySelectorAll('.day')[${todayIndex}]`);
  await mouseDrag(from, day.x + 40, day.y + day.height - 40);
  const item = (await page.evaluateHandle(dayItemSel(todayIndex, 'Бег'))).asElement();
  assert(item, 'занятие не встало в день');
  await item.$eval('.ck-hit', e => e.click());
  await sleep(400);
  const toast = await toastText();
  assert(toast?.includes('📎') && toast.includes('+1 в «Тренировки» · 1 · неделя 1 из 3'), `подсказка: ${toast}`);
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

await check('клик по пилюле — экран «Копилки» на «Тренировках» (карточка в фокусе)', async () => {
  await page.evaluate(`${goalSel('Бег')}.querySelector('.jar-pill').click()`);
  await page.waitForSelector('.habits'); await settle();
  const focused = await page.evaluate(() => document.querySelector('.jar-card.focused')?.getAttribute('aria-label'));
  assert(focused === 'Копилка «Тренировки»', `в фокусе: ${focused}`);
  assert(await cardTotal('Тренировки') === '1', `счёт: ${await cardTotal('Тренировки')}`);
});

await check('«+1 сегодня»: счёт 2, подсказка, в сегодняшнем дне — выполненное «Тренировки»', async () => {
  await page.evaluate(() => document.querySelector('.jar-card[aria-label="Копилка «Тренировки»"] .plus-one').click());
  await sleep(400);
  const toast = await toastText();
  assert(toast?.includes('+1 в «Тренировки» · 2 · неделя 2 из 3'), `подсказка: ${toast}`);
  await settle();
  assert(await cardTotal('Тренировки') === '2', `счёт: ${await cardTotal('Тренировки')}`);
  await escape();
  const done = await page.evaluate(`(() => { const it = ${dayItemSel(todayIndex, 'Тренировки')}; return it?.classList.contains('done') ?? null; })()`);
  assert(done === true, `дело в дне: ${done}`);
});

await check('норма этой недели выполнена — неделя сразу в серии: «🔥 1 нед. подряд», пилюля «🫙 3 · 🔥1»', async () => {
  assert(await pillOf('Бег') === '🫙 2', `до нормы: ${await pillOf('Бег')}`); // в процессе — серии ещё нет
  await openScreen();
  await page.evaluate(() => document.querySelector('.jar-card[aria-label="Копилка «Тренировки»"] .plus-one').click());
  await sleep(400);
  assert((await toastText())?.includes('неделя 3 из 3 ✓'), `подсказка: ${await toastText()}`);
  await settle();
  const chip = await page.$eval('.jar-card[aria-label="Копилка «Тренировки»"] .jar-info .chip', e => e.textContent.replace(/\s+/g, ' ').trim());
  assert(chip === '🔥 1 нед. подряд', `серия: ${chip}`);
  await escape();
  assert(await pillOf('Бег') === '🫙 3 · 🔥1', `пилюля: ${await pillOf('Бег')}`);
});

await check('окно цели «100 страниц»: «+ новая копилка…» — окно поверх, название и норма из цели; после «Создать» она выбрана', async () => {
  await openGoal('100 страниц');
  await chooseHabit('+ новая копилка…');
  await page.waitForSelector('.editor.nested[aria-label="Копилка"]');
  const name = await page.$eval('.editor.nested[aria-label="Копилка"] .title-input', e => e.value);
  const norm = await page.$eval('.editor.nested input[aria-label="Норма в неделю"]', e => e.value);
  assert(name === '100 страниц' && norm === '1', `название «${name}», норма ${norm}`);
  await clickIn('.editor.nested .btn', 'Создать'); await settle();
  assert(!(await page.$('.editor.nested')), 'окно копилки не закрылось');
  assert(await page.$('.editor[aria-label="Цель недели"]'), 'окно цели закрылось вместе с ним');
  assert((await habitFieldText()).includes('100 страниц'), `в поле: ${await habitFieldText()}`);
  await clickIn('.editor .btn', 'Сохранить'); await settle();
  assert(await pillOf('100 страниц') === '🫙 0', `пилюля: ${await pillOf('100 страниц')}`);
});

await check('Esc в окне новой копилки закрывает только его', async () => {
  await openGoal('Бег');
  await chooseHabit('+ новая копилка…');
  await page.waitForSelector('.editor.nested');
  await escape();
  assert(!(await page.$('.editor.nested')) && await page.$('.editor[aria-label="Цель недели"]'), 'закрылось не то');
  await escape();
});

await check('окно дела: копилка у обычного дела сохраняется', async () => {
  const el = (await page.evaluateHandle(dayItemSel(1, 'Почитать 30 страниц'))).asElement();
  assert(el, 'нет дела');
  await el.$eval('.tx', e => e.click());
  await page.waitForSelector('.editor[aria-label="Дело"]'); await sleep(200);
  await chooseHabit('Чтение');
  await clickIn('.editor .btn', 'Сохранить'); await settle();
  await el.$eval('.tx', e => e.click());
  await page.waitForSelector('.editor[aria-label="Дело"]'); await sleep(200);
  assert((await habitFieldText()).includes('Чтение'), `в поле: ${await habitFieldText()}`);
  await escape();
});

await check('каникулы: добавить (для всех) — в списке; удалить — исчезли', async () => {
  await openScreen();
  await clickIn('.habits .btn', '🏖 Каникулы');
  await page.waitForSelector('.editor[aria-label="Каникулы"]');
  await clickIn('.editor[aria-label="Каникулы"] .btn', 'Сохранить'); await settle();
  const items = await page.$$eval('.vac-list li', ls => ls.map(l => l.textContent.replace(/\s+/g, ' ').trim()));
  assert(items.length === 1 && items[0].includes('все копилки'), JSON.stringify(items));
  // Сегодня каникулы — на карточке подсказка
  await escape();
  assert(await page.$('.habits'), 'Esc в каникулах закрыл экран');
  const hint = await page.$eval('.jar-card[aria-label="Копилка «Тренировки»"] .hint.vac', e => e.textContent.trim()).catch(() => null);
  assert(hint?.startsWith('🏖 Каникулы до'), `подсказка: ${hint}`);
  await clickIn('.habits .btn', '🏖 Каникулы');
  await page.waitForSelector('.vac-list');
  await page.click('.vac-list .row-action'); await settle();
  assert(!(await page.$('.vac-list')), 'каникулы не удалились');
  await escape(); await escape();
});

await check('копилка в архиве: нет в выборе «В копилку», на экране — в «в архиве»', async () => {
  await openScreen();
  await page.evaluate(() => [...document.querySelector('.jar-card[aria-label="Копилка «Пробежки»"]').querySelectorAll('.btn')].find(b => b.textContent.trim() === 'настроить').click());
  await page.waitForSelector('.editor[aria-label="Копилка"]');
  await clickIn('.editor[aria-label="Копилка"] .btn', 'в архив'); await settle();
  assert(!(await page.$('.grid .jar-card[aria-label="Копилка «Пробежки»"]')), 'копилка осталась среди активных');
  assert((await page.$eval('.archived-h', e => e.textContent)).includes('в архиве: 1'), 'нет строки архива');
  await escape();
  await openGoal('Бег');
  await page.click('.editor .select-field[aria-label="В копилку"]'); await page.waitForSelector('.editor .popover');
  const options = await page.$$eval('.editor .popover .option .option-name', os => os.map(o => o.textContent.trim()));
  assert(!options.includes('Пробежки'), JSON.stringify(options));
  await escape(); await escape();
});

const openHabitEditor = async name => {
  await page.evaluate(n => [...document.querySelector(`.jar-card[aria-label="Копилка «${n}»"]`).querySelectorAll('.btn')].find(b => b.textContent.trim() === 'настроить').click(), name);
  await page.waitForSelector('.editor[aria-label="Копилка"]'); await sleep(200);
};
const editorButtons = () => page.$$eval('.editor[aria-label="Копилка"] .btn', bs => bs.map(b => b.textContent.trim()));
const serverHabit = name => page.evaluate(async n => (await (await fetch('/api/habits')).json()).habits.find(h => h.name === n) ?? null, name);
const showArchived = async () => {
  if (!(await page.$('.archived-h[aria-expanded="true"]'))) { await page.click('.archived-h'); await sleep(300); }
};

await check('удалить можно только копилку из архива: у активной — только «в архив»', async () => {
  await openScreen();
  await openHabitEditor('100 страниц');
  const active = await editorButtons();
  assert(active.includes('в архив') && !active.includes('удалить'), JSON.stringify(active));
  await clickIn('.editor[aria-label="Копилка"] .btn', 'в архив'); await settle();
  await showArchived();
  await openHabitEditor('100 страниц');
  const archived = await editorButtons();
  assert(archived.includes('удалить') && archived.includes('из архива'), JSON.stringify(archived));
  await escape();
});

await check('удаление — плашка «Удалено «100 страниц» · Вернуть»; «Вернуть» — копилка целиком, с привязкой цели', async () => {
  await openHabitEditor('100 страниц');
  await clickIn('.editor[aria-label="Копилка"] .btn', 'удалить'); await sleep(300);
  assert(!(await page.$('.jar-card[aria-label="Копилка «100 страниц»"]')), 'карточка не исчезла');
  const toast = await toastText();
  assert(toast?.includes('Удалено «100 страниц»') && toast.includes('Вернуть'), `плашка: ${toast}`);
  await escape();
  assert(await pillOf('100 страниц') === null, 'у цели осталась пилюля удалённой копилки');
  await page.click('.toast .action'); await settle();
  assert(await pillOf('100 страниц') === '🫙 0', `после «Вернуть»: ${await pillOf('100 страниц')}`);
  await sleep(6500); // срок плашки вышел — на сервер ничего не ушло
  const habit = await serverHabit('100 страниц');
  assert(habit?.isArchived, `на сервере: ${JSON.stringify(habit)}`);
});

await check('без «Вернуть» — через 6 с удалена на сервере; цель осталась без копилки', async () => {
  await openScreen();
  await showArchived();
  await openHabitEditor('100 страниц');
  await clickIn('.editor[aria-label="Копилка"] .btn', 'удалить'); await sleep(300);
  assert(await serverHabit('100 страниц'), 'удалилась на сервере сразу, а не через 6 с');
  await sleep(6800);
  assert(await serverHabit('100 страниц') === null, 'на сервере копилка осталась');
  await escape();
  await load();
  assert(await pillOf('100 страниц') === null, 'у цели пилюля');
});

// История «задним числом» и снимки экрана в темах
await check('история копилок: серия, пауза, каникулы — как в макете', async () => {
  await makeHabitHistory(BASE, process.env.PLANNER_DB);
  await load();
  await openScreen();
  const chips = name => page.$$eval(`.jar-card[aria-label="Копилка «${name}»"] .jar-info .chip`, cs => cs.map(c => c.textContent.replace(/\s+/g, ' ').trim()));
  const training = await chips('Тренировки');
  assert(training[0] === '🔥 14 нед. подряд', JSON.stringify(training)); // 13 прошлых + эта (норма уже выполнена)
  const meditation = await chips('Медитация');
  assert(meditation[0].startsWith('⚠ пауза · 🔥 9'), JSON.stringify(meditation));
  assert(await page.$('.jar-card[aria-label="Копилка «Медитация»"] .hint.warn'), 'нет подсказки «не пропускай дважды»');
  const reading = await page.$$eval('.jar-card[aria-label="Копилка «Чтение»"] .weeks .w', ws => ws.map(w => w.className.replace('w ', '').split(' ')[0]));
  assert(reading.filter(c => c === 'vac').length === 2, JSON.stringify(reading));
  assert((await page.$$eval('.jar-card[aria-label="Копилка «Чтение»"] .pip.extra', p => p.length)) === 1, 'нет розовой точки сверх нормы');
  await escape();
  assert((await pillOf('Бег'))?.endsWith(' · 🔥14'), `пилюля: ${await pillOf('Бег')}`);
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

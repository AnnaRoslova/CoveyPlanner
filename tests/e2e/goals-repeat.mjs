// @seed — нужны демо-данные (tools/seed-demo.ps1): цель «Бег» (Тело) 3 раза в неделю, без занятий
// Цели с повторами: цель → день добавляет занятие, прогресс «1/3», метки дней «Вт · Чт»,
// выбор «раз в неделю», перенос занятия, удаление цели (выполненные занятия остаются);
// не больше одного занятия в день (подсветка и «уже есть в этот день»), «можно несколько раз в день».
import puppeteer from 'puppeteer-core';

const BASE = process.env.PLANNER_URL ?? 'http://localhost:5071/';
const SHOTS = process.argv[2] ?? '.';
const results = [];
const consoleErrors = [];
const browser = await puppeteer.launch({
  executablePath: process.env.PLANNER_BROWSER ?? 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  headless: true, defaultViewport: { width: 1600, height: 1300 },
});
const page = await browser.newPage();
page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text()); });
page.on('pageerror', e => consoleErrors.push(e.message));
const sleep = ms => new Promise(r => setTimeout(r, ms));
const settle = () => sleep(600);
function assert(c, m) { if (!c) throw new Error(m); }
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-repeat-${results.length}.png` }); }
}
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.role'); await settle(); }

const titleOf = el => [...el.querySelector('.tx').childNodes].filter(n => !n.classList?.contains('pills') && !n.classList?.contains('note') && !n.classList?.contains('goal-days')).map(n => n.textContent).join('').trim();
const T = `const titleOf = ${titleOf};`;
const roleSel = name => `[...document.querySelectorAll('.role')].find(r => r.querySelector('.role-h .name')?.textContent.trim() === ${JSON.stringify(name)})`;
const goalSel = (role, title) => `(() => { ${T} return [...${roleSel(role)}.querySelectorAll('.item')].find(i => titleOf(i) === ${JSON.stringify(title)}); })()`;
const goal = (role, title) => page.evaluate(`(() => { const g = ${goalSel(role, title)}; return g ? {
  progress: ((i) => (t => t ? t.replace(/.*выполнено ([0-9]+) из ([0-9]+).*/, '$1/$2') : null)((i.querySelector('.pct-ring, .pct')?.getAttribute('title'))))(g), // D5: «2/5» — в подсказке процента
  when: [...g.querySelectorAll('.goal-days .d')].map(d => d.textContent).join(' · ') || null,
  doneDays: [...g.querySelectorAll('.goal-days .d.done')].map(d => d.textContent),
  done: g.classList.contains('done') } : null; })()`);
const day = i => page.evaluate(`(() => { ${T} return [...document.querySelectorAll('.day')[${i}].querySelectorAll('.item')].map(it => ({ title: titleOf(it), rock: !!it.querySelector('.title.rock'), done: it.classList.contains('done') })); })()`);

async function boxOf(src, block = 'nearest') {
  const el = (await page.evaluateHandle(src)).asElement();
  assert(el, `не найдено: ${src.slice(0, 80)}`);
  await el.evaluate((e, b) => e.scrollIntoView({ block: b }), block);
  await sleep(100);
  return el.boundingBox();
}
const goalBox = (role, title) => boxOf(goalSel(role, title));
const dayBox = i => boxOf(`document.querySelectorAll('.day')[${i}]`);
const dayItem = (i, title) => `(() => { ${T} return [...document.querySelectorAll('.day')[${i}].querySelectorAll('.item')].find(it => titleOf(it) === ${JSON.stringify(title)}); })()`;

async function mouseDrag(from, x, y) {
  const sx = from.x + 30, sy = from.y + Math.min(from.height / 2, 12);
  await page.mouse.move(sx, sy); await page.mouse.down();
  for (let i = 1; i <= 14; i++) { await page.mouse.move(sx + (x - sx) * i / 14, sy + (y - sy) * i / 14); await sleep(16); }
  await sleep(150);
  await page.mouse.up(); await settle();
}
async function goalToDay(i) {
  const from = await goalBox('Тело', 'Бег');
  const target = await dayBox(i);
  await mouseDrag(from, target.x + 40, target.y + target.height - 40);
}
async function openGoal(title, role = 'Тело') {
  const el = (await page.evaluateHandle(goalSel(role, title))).asElement();
  await el.$eval('.tx', e => e.click());
  await page.waitForSelector('.editor[aria-label="Цель недели"]', { timeout: 3000 });
  // Раскрыть все редкие поля окна (сводки и «+ …»): здесь проверяются поля, правило «заполненное видно» — в declutter.mjs
  for (let i = 0; i < 14; i++) { const b = await page.$('.editor .sum-chip:not(.static), .editor .adder'); if (!b) break; await b.click(); await new Promise(r => setTimeout(r, 150)); } await new Promise(r => setTimeout(r, 250));
  await sleep(200);
}
const save = async () => {
  await page.evaluate(() => [...document.querySelectorAll('.editor .btn')].find(b => b.textContent.trim() === 'Сохранить').click());
  await settle();
};
const runsOn = async i => (await day(i)).filter(t => t.title === 'Бег');

await load();

await check('цель на 3 раза: прогресс «0/3», без меток дней', async () => {
  const g = await goal('Тело', 'Бег');
  assert(g?.progress === '0/3' && g.when === null && !g.done, JSON.stringify(g));
});

await check('цель → вторник и четверг: два занятия-«камня», у цели «Вт · Чт»', async () => {
  await goalToDay(1);
  await goalToDay(3);
  const tue = await runsOn(1), thu = await runsOn(3);
  assert(tue.length === 1 && tue[0].rock, `Вт: ${JSON.stringify(tue)}`);
  assert(thu.length === 1 && thu[0].rock, `Чт: ${JSON.stringify(thu)}`);
  const g = await goal('Тело', 'Бег');
  assert(g.when === 'Вт · Чт' && g.progress === '0/3', JSON.stringify(g));
  await load();
  assert((await goal('Тело', 'Бег')).when === 'Вт · Чт', 'после перезагрузки');
});

const toastText = async () => (await page.$('.toast')) ? page.$eval('.toast', e => e.textContent.trim()) : null;

await check('ещё раз на вторник — ничего не создаётся: занятие подсвечено, «уже есть в этот день»', async () => {
  await goalToDay(1);
  assert((await runsOn(1)).length === 1, `Вт: ${JSON.stringify(await runsOn(1))}`);
  assert(await toastText() === 'уже есть в этот день', `плашка: ${await toastText()}`);
  const flashing = await page.evaluate(`(() => { const el = ${dayItem(1, 'Бег')}; return el?.classList.contains('flash'); })()`);
  assert(flashing, 'занятие не подсветилось');
  await page.screenshot({ path: `${SHOTS}/repeat-day-taken.png` });
});

await check('занятие из четверга во вторник (там уже есть) — остаётся в четверге', async () => {
  const from = await boxOf(dayItem(3, 'Бег'));
  const tue = await dayBox(1);
  await mouseDrag(from, tue.x + 40, tue.y + tue.height - 40);
  assert((await runsOn(3)).length === 1 && (await runsOn(1)).length === 1, 'занятие переехало');
  assert(await toastText() === 'уже есть в этот день', `плашка: ${await toastText()}`);
  await load();
  assert((await runsOn(3)).length === 1, 'после перезагрузки не в четверге');
});

await check('«можно несколько раз в день» — второе занятие во вторник', async () => {
  await openGoal('Бег');
  const label = await page.$$eval('.editor .switch-row', rs => rs.map(r => r.textContent.replace(/\s+/g, ' ').trim()));
  assert(label.some(t => t.startsWith('можно несколько раз в день')), `переключатели: ${label}`);
  await page.evaluate(() => [...document.querySelectorAll('.editor .switch-row')].find(r => r.textContent.includes('несколько раз в день')).click());
  await save();
  await goalToDay(1);
  assert((await runsOn(1)).length === 2, `Вт: ${JSON.stringify(await runsOn(1))}`);
  assert((await goal('Тело', 'Бег')).when === 'Вт · Вт · Чт', JSON.stringify(await goal('Тело', 'Бег')));
});

await check('отметка занятия в дне → прогресс «1/3», вторник темнее', async () => {
  const el = (await page.evaluateHandle(dayItem(1, 'Бег'))).asElement();
  await (await el.$('.ck')).click(); await settle();
  const g = await goal('Тело', 'Бег');
  assert(g.progress === '1/3' && JSON.stringify(g.doneDays) === '["Вт"]' && !g.done, JSON.stringify(g));
});

await check('«раз в неделю» → 2 в окне цели; отметка второго занятия — цель выполнена «2/2»', async () => {
  await openGoal('Бег');
  // «− 3 +»: один шаг вниз
  assert(await page.$eval('.times-per-week input', e => e.value) === '3', 'в поле не 3');
  await page.click('.times-per-week .step[title="Меньше"]'); await sleep(150);
  assert(await page.$eval('.times-per-week input', e => e.value) === '2', 'не стало 2');
  await save();
  assert(!(await page.$('.editor')), 'окно не закрылось');
  assert((await goal('Тело', 'Бег')).progress === '1/2', JSON.stringify(await goal('Тело', 'Бег')));
  const thu = (await page.evaluateHandle(dayItem(3, 'Бег'))).asElement();
  await (await thu.$('.ck')).click(); await settle();
  const g = await goal('Тело', 'Бег');
  assert(g.progress === '2/2' && g.done, JSON.stringify(g));
  await load();
  assert((await goal('Тело', 'Бег')).done, 'после перезагрузки');
});

await check('клик по отметке выполненной цели снимает последнюю отметку', async () => {
  const row = (await page.evaluateHandle(goalSel('Тело', 'Бег'))).asElement();
  await (await row.$('.ck')).click(); await settle();
  const g = await goal('Тело', 'Бег');
  assert(g.progress === '1/2' && !g.done, JSON.stringify(g));
  assert(!(await runsOn(3))[0].done, 'четверг остался отмеченным');
});

await check('занятие перетаскиванием из четверга в пятницу — у цели «Вт · Вт · Пт»', async () => {
  const from = await boxOf(dayItem(3, 'Бег'));
  const fri = await dayBox(4);
  await mouseDrag(from, fri.x + 40, fri.y + fri.height - 40);
  assert((await runsOn(3)).length === 0 && (await runsOn(4)).length === 1, 'занятие не переехало');
  assert((await goal('Тело', 'Бег')).when === 'Вт · Вт · Пт', JSON.stringify(await goal('Тело', 'Бег')));
});

await check('удалить цель: выполненное занятие остаётся без «камня», невыполненные — в корзину', async () => {
  const row = (await page.evaluateHandle(goalSel('Тело', 'Бег'))).asElement();
  await row.hover();
  await (await row.$('.row-action[title="Удалить цель"]')).click(); await settle();
  // «Бег» в «Теле» — постоянная цель: спрашивают, удалить только на этой неделе или дальше
  await page.evaluate(() => [...document.querySelectorAll('.confirm-delete .btn')].find(b => b.textContent.trim() === 'Только на этой неделе').click());
  await settle();
  assert(!(await goal('Тело', 'Бег')), 'цель осталась');
  const tue = await runsOn(1);
  assert(tue.length === 1 && tue[0].done && !tue[0].rock, `Вт: ${JSON.stringify(tue)}`);
  assert((await runsOn(4)).length === 0, 'пятничное занятие осталось');
});

// polish-3.md, R: выбранные дни повтора — одной строкой «Повтор: Вт, Чт, Сб ▸», больше нигде в окне
await check('R: дни повтора — только строкой «Повтор: Вт, Чт, Сб ▸» (в шапке окна их нет); клик — кружки; у цели без дней повтора дни занятий в шапке', async () => {
  const api = (method, path, body) => page.evaluate(async (m, p, b) => {
    const r = await fetch(`/api/${p}`, { method: m, headers: { 'Content-Type': 'application/json', 'X-Planner': '1' }, body: b ? JSON.stringify(b) : undefined });
    if (!r.ok) throw new Error(`${m} ${p}: ${r.status}`);
    const text = await r.text();
    return text ? JSON.parse(text) : null;
  }, method, path, body);
  const now = new Date(); now.setHours(0, 0, 0, 0);
  const monday = new Date(now); monday.setDate(now.getDate() - ((now.getDay() + 6) % 7));
  const iso = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  const body = (await api('GET', 'board')).roles.find(r => r.name === 'Тело').id;
  const yoga = await api('POST', 'goals', { roleId: body, weekStart: iso(monday), title: 'Йога', isRecurring: true });
  await api('PUT', `goals/${yoga.id}`, { title: 'Йога', roleIds: [body], timesPerWeek: 3, isRecurring: true, days: 2 | 8 | 32 });
  const calm = await api('POST', 'goals', { roleId: body, weekStart: iso(monday), title: 'Тишина', isRecurring: false });
  await api('PUT', `goals/${calm.id}`, { title: 'Тишина', roleIds: [body], timesPerWeek: 2 });
  await api('POST', `goals/${calm.id}/schedule`, { kind: 'day', date: iso(new Date(monday.getFullYear(), monday.getMonth(), monday.getDate() + 3)), index: 0 });
  await load();

  // Видимые «Пн…Вс» в окне цели — где они
  const dayTexts = () => page.evaluate(() => {
    const out = [];
    const walker = document.createTreeWalker(document.querySelector('.editor'), NodeFilter.SHOW_TEXT);
    while (walker.nextNode()) {
      const n = walker.currentNode;
      if (!/(^|[^А-Яа-яЁё])(Пн|Вт|Ср|Чт|Пт|Сб|Вс)([^А-Яа-яЁё]|$)/.test(n.textContent)) continue;
      const r = document.createRange(); r.selectNodeContents(n);
      const b = r.getBoundingClientRect();
      if (b.width > 2 && b.height > 2) out.push({ where: n.parentElement.closest('[data-summary], .rock-note, .day-dots')?.className ?? n.parentElement.className, text: n.textContent.trim() });
    }
    return out;
  });
  const open = async title => {
    const el = (await page.evaluateHandle(goalSel('Тело', title))).asElement();
    await el.$eval('.tx', e => e.click());
    await page.waitForSelector('.editor[aria-label="Цель недели"]', { timeout: 3000 }); await sleep(300);
  };

  await open('Йога');
  const chip = await page.$eval('.editor .sum-chip[data-summary="days"] .sum-text', c => c.textContent.trim()); // за ней — «▸»
  assert(chip === 'Повтор: Вт, Чт, Сб', `строка повтора: «${chip}»`);
  assert(!(await page.$('.editor .session-days')), 'дни занятий в шапке окна');
  const texts = await dayTexts();
  assert(texts.length === 1 && texts[0].where.includes('sum-chip'), `дни в окне не только в «Повтор»: ${JSON.stringify(texts)}`);
  await page.click('.editor .sum-chip[data-summary="days"]'); await sleep(300);
  const dots = await page.$$eval('.editor .day-dots .day-dot.on', ds => ds.map(d => d.getAttribute('aria-label')));
  assert(dots.length === 3, `кружков: ${dots.join(', ')}`);
  assert(!(await page.$('.editor .sum-chip[data-summary="days"]')), 'строка «Повтор: …» осталась рядом с кружками');
  await page.keyboard.press('Escape'); await settle();

  await open('Тишина');
  const header = await page.$eval('.editor .session-days', e => e.textContent.trim());
  assert(header === '· дни: Чт', `шапка цели без дней повтора: ${header}`);
  await page.keyboard.press('Escape'); await settle();
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));

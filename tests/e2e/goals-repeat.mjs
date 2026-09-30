// @seed — нужны демо-данные (tools/seed-demo.ps1): цель «Бег» (Тело) 3 раза в неделю, без занятий
// Цели с повторами: цель → день добавляет занятие, прогресс «1/3», метки дней «Вт · Чт»,
// выбор «раз в неделю», перенос занятия, удаление цели (выполненные занятия остаются).
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

const titleOf = el => [...el.querySelector('.tx').childNodes].filter(n => !n.classList?.contains('pills') && !n.classList?.contains('note')).map(n => n.textContent).join('').trim();
const T = `const titleOf = ${titleOf};`;
const roleSel = name => `[...document.querySelectorAll('.role')].find(r => r.querySelector('.role-h .name')?.textContent.trim() === ${JSON.stringify(name)})`;
const goalSel = (role, title) => `(() => { ${T} return [...${roleSel(role)}.querySelectorAll('.item')].find(i => titleOf(i) === ${JSON.stringify(title)}); })()`;
const goal = (role, title) => page.evaluate(`(() => { const g = ${goalSel(role, title)}; return g ? {
  progress: g.querySelector('.pill.progress')?.textContent ?? null,
  when: g.querySelector('.pill.when')?.textContent.trim() ?? null,
  doneDays: [...g.querySelectorAll('.pill.when .d.done')].map(d => d.textContent),
  done: g.classList.contains('done') } : null; })()`);
const day = i => page.evaluate(`(() => { ${T} return [...document.querySelectorAll('.day')[${i}].querySelectorAll('.item')].map(it => ({ title: titleOf(it), rock: !!it.querySelector('.pill.rock'), done: it.classList.contains('done') })); })()`);

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

await check('ещё раз на вторник — второе занятие в тот же день', async () => {
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

await check('«раз в неделю» → 2; отметка второго занятия — цель выполнена «2/2»', async () => {
  const row = (await page.evaluateHandle(goalSel('Тело', 'Бег'))).asElement();
  await row.hover();
  await (await row.$('.row-action[title="Сколько раз в неделю"]')).click(); await settle();
  await page.evaluate(() => [...document.querySelectorAll('.times .t')].find(b => b.textContent.trim() === '2').click());
  await settle();
  assert(!(await page.$('.times')), 'выбор не закрылся');
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
  assert(!(await goal('Тело', 'Бег')), 'цель осталась');
  const tue = await runsOn(1);
  assert(tue.length === 1 && tue[0].done && !tue[0].rock, `Вт: ${JSON.stringify(tue)}`);
  assert((await runsOn(4)).length === 0, 'пятничное занятие осталось');
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));

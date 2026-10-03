// @seed — нужны демо-данные (tools/seed-demo.ps1)
// Одно дело — несколько целей: «Прогулка в парке» в Пн засчитывается и в «10к шагов» (Тело), и в «Побыть на природе» (Дух).
// Связать: перетаскиванием дела на строку цели (подсветка «засчитать и в эту цель») и в окне дела — «засчитывается в цели».
// «Одно занятие цели в день»: другое невыполненное занятие заменяется (в корзину, «Вернуть»), выполненное — отказ.
import puppeteer from 'puppeteer-core';

const BASE = process.env.PLANNER_URL ?? 'http://localhost:5071/';
const SHOTS = process.argv[2] ?? '.';
const results = [];
const consoleErrors = [];
const browser = await puppeteer.launch({
  executablePath: process.env.PLANNER_BROWSER ?? 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  headless: true, defaultViewport: { width: 1600, height: 1200 },
});
const page = await browser.newPage();
page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text()); });
page.on('pageerror', e => consoleErrors.push(e.message));
const sleep = ms => new Promise(r => setTimeout(r, ms));
const settle = () => sleep(600);
function assert(c, m) { if (!c) throw new Error(m); }
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-goal-links-${results.length}.png` }); }
  if (await page.$('.editor')) { await page.keyboard.press('Escape'); await settle(); }
}
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day'); await settle(); }
const api = (method, path, body) => page.evaluate(async (m, p, b) => {
  const r = await fetch(`/api/${p}`, { method: m, headers: { 'Content-Type': 'application/json' }, body: b ? JSON.stringify(b) : undefined });
  return r.status === 204 ? null : r.json();
}, method, path, body);
const iso = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
const monday = (() => { const d = new Date(); d.setHours(12); d.setDate(d.getDate() - (d.getDay() + 6) % 7); return d; })();
const dayIso = i => { const d = new Date(monday); d.setDate(d.getDate() + i); return iso(d); };
async function clickText(selector, text) {
  const ok = await page.evaluate((s, t) => {
    const el = [...document.querySelectorAll(s)].find(e => e.textContent.trim() === t);
    el?.click();
    return !!el;
  }, selector, text);
  assert(ok, `нет «${text}» (${selector})`);
  await settle();
}

const dayRows = (i, title) => page.evaluate((i, t) => [...document.querySelectorAll('.day')[i].querySelectorAll('.item')]
  .filter(it => it.querySelector('.title').textContent.trim() === t)
  .map(it => ({ rock: !!it.querySelector('.pill.rock'), dots: it.querySelectorAll('.role-dots i').length, flash: it.classList.contains('flash') })), i, title);
const goalDays = title => page.evaluate(t => {
  const row = [...document.querySelectorAll('.role .item')].find(i => i.querySelector('.title').textContent.trim() === t);
  return row ? [...row.querySelectorAll('.goal-days .d')].map(d => d.textContent).join(' · ') : null;
}, title);
const toast = async () => (await page.$('.toast')) ? page.$eval('.toast', e => [...e.querySelectorAll('span, button')].map(x => x.textContent.trim()).join(' ')) : null;
async function boxOf(js) {
  const el = (await page.evaluateHandle(js)).asElement();
  assert(el, 'элемент не найден');
  await el.evaluate(e => e.scrollIntoView({ block: 'nearest' }));
  await sleep(100);
  return el.boundingBox();
}
const dayItemJs = (i, t) => `[...document.querySelectorAll('.day')[${i}].querySelectorAll('.item')].find(it => it.querySelector('.title').textContent.trim() === ${JSON.stringify(t)})`;
const goalRowJs = t => `[...document.querySelectorAll('.role .item')].find(i => i.querySelector('.title').textContent.trim() === ${JSON.stringify(t)})`;
async function dragOnGoal(dayIndex, task, goal, { beforeUp } = {}) {
  const from = await boxOf(dayItemJs(dayIndex, task));
  const to = await boxOf(goalRowJs(goal));
  const sx = from.x + 40, sy = from.y + from.height / 2, x = to.x + to.width / 2, y = to.y + to.height / 2;
  await page.mouse.move(sx, sy); await page.mouse.down();
  for (let i = 1; i <= 16; i++) { await page.mouse.move(sx + (x - sx) * i / 16, sy + (y - sy) * i / 16); await sleep(16); }
  await sleep(200);
  if (beforeUp) await beforeUp();
  await page.mouse.up(); await sleep(1200);
}
async function addToDay(i, title) {
  await page.evaluate(i => document.querySelectorAll('.day')[i].querySelector('.add').click(), i); await settle();
  await page.keyboard.type(title); await page.keyboard.press('Enter'); await settle();
  await page.keyboard.press('Escape'); await settle();
}

// Цели сценария: «10к шагов» (Тело) и «Побыть на природе» (Дух)
await load();
const board = await api('GET', 'board');
const roleId = name => board.roles.find(r => r.name === name).id;
await api('POST', 'goals', { roleId: roleId('Тело'), weekStart: iso(monday), title: '10к шагов' });
await api('POST', 'goals', { roleId: roleId('Дух'), weekStart: iso(monday), title: 'Побыть на природе' });
await load();

await check('«Прогулка в парке» в Пн → на строку «10к шагов»: подсветка «засчитать и в эту цель», у цели +1', async () => {
  await addToDay(0, 'Прогулка в парке');
  let lit = null;
  await dragOnGoal(0, 'Прогулка в парке', '10к шагов', {
    beforeUp: async () => {
      lit = await page.evaluate(js => { const r = eval(js); return r?.classList.contains('drop-hover') ? r.dataset.dropHint : null; }, goalRowJs('10к шагов'));
      await page.screenshot({ path: `${SHOTS}/goal-links-hover.png` });
    },
  });
  assert(lit === 'засчитать и в эту цель', `подсветка: ${lit}`);
  assert(await goalDays('10к шагов') === 'Пн', `у цели: ${await goalDays('10к шагов')}`);
  const rows = await dayRows(0, 'Прогулка в парке');
  assert(rows.length === 1 && rows[0].rock, `в Пн: ${JSON.stringify(rows)}`);
});

await check('в окне дела «засчитывается в цели» — добавить «Природу»: у обеих +1, одна запись, две точки ролей, роли «от целей»', async () => {
  await page.evaluate(js => eval(js).querySelector('.title').click(), dayItemJs(0, 'Прогулка в парке')); await settle();
  const on = await page.$$eval('.editor [aria-label="Цели"] .chip.on', cs => cs.map(c => c.textContent.trim()));
  assert(JSON.stringify(on) === '["10к шагов"]', `выбрано: ${on}`);
  await clickText('.editor [aria-label="Цели"] .chip', 'Побыть на природе');
  assert((await page.$eval('.editor .field .from-goal', e => e.textContent)).includes('от целей'), 'роли не «от целей»');
  await page.screenshot({ path: `${SHOTS}/goal-links-editor.png` });
  await page.click('.editor .btn:not(.quiet):not(.danger)'); await sleep(1200);
  assert(await goalDays('10к шагов') === 'Пн' && await goalDays('Побыть на природе') === 'Пн', 'не у обеих целей');
  const rows = await dayRows(0, 'Прогулка в парке');
  assert(rows.length === 1 && rows[0].rock && rows[0].dots === 2, `в Пн: ${JSON.stringify(rows)}`);
  await load();
  assert((await dayRows(0, 'Прогулка в парке'))[0].dots === 2, 'после перезагрузки');
});

await check('отметка «выполнено» общая: засчитывается в обе цели', async () => {
  await page.evaluate(js => eval(js).querySelector('.ck-hit').click(), dayItemJs(0, 'Прогулка в парке')); await sleep(900);
  const done = await page.evaluate(() => [...document.querySelectorAll('.role .item')]
    .filter(i => ['10к шагов', 'Побыть на природе'].includes(i.querySelector('.title').textContent.trim()))
    .map(i => i.classList.contains('done')));
  assert(JSON.stringify(done) === '[true,true]', `выполнены: ${done}`);
});

await check('у цели уже есть невыполненное занятие в этот день — оно заменяется (в корзину), «Вернуть» возвращает', async () => {
  const run = (await api('GET', `week/${iso(monday)}`)).goals.find(g => g.title === 'Бег');
  await api('POST', `goals/${run.id}/schedule`, { kind: 'day', date: dayIso(1), index: 0 });
  await load();
  await addToDay(1, 'Пробежка у реки');
  await dragOnGoal(1, 'Пробежка у реки', 'Бег');
  assert((await dayRows(1, 'Бег')).length === 0, 'старое занятие осталось');
  assert((await dayRows(1, 'Пробежка у реки'))[0]?.rock, 'пробежка не камень');
  assert(await toast() === 'Заменено «Бег» · Вернуть', `плашка: ${await toast()}`);
  await page.click('.toast .action'); await sleep(1500);
  assert((await dayRows(1, 'Бег')).length === 1, 'не вернулось');
  assert(!(await dayRows(1, 'Пробежка у реки'))[0].rock, 'пробежка осталась в цели');
});

await check('выполненное занятие в этот день — связь не создаётся: подсветка и «уже есть в этот день»', async () => {
  await page.evaluate(js => eval(js).querySelector('.ck-hit').click(), dayItemJs(1, 'Бег')); await sleep(900);
  await dragOnGoal(1, 'Пробежка у реки', 'Бег');
  assert(!(await dayRows(1, 'Пробежка у реки'))[0].rock, 'связалось');
  assert((await toast()) === 'уже есть в этот день', `плашка: ${await toast()}`);
});

await check('удалить одну из целей — общее дело остаётся в другой (не в корзине)', async () => {
  const row = (await page.evaluateHandle(goalRowJs('10к шагов'))).asElement();
  await row.hover();
  await (await row.$('.row-action[title="Удалить цель"]')).click(); await settle();
  if (await page.$('.confirm-delete')) await clickText('.confirm-delete .btn', 'Только на этой неделе');
  await page.click('.nav[title="Следующая неделя"]'); await sleep(900); // листание отправляет удаление на сервер
  await page.click('.today-b'); await sleep(900);
  const rows = await dayRows(0, 'Прогулка в парке');
  assert(rows.length === 1 && rows[0].rock && rows[0].dots === 0, `в Пн: ${JSON.stringify(rows)}`); // одна роль — без точек
  assert(await goalDays('Побыть на природе') === 'Пн', 'у «Природы» пропало');
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));

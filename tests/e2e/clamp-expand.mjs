// «Развернуть по длине» (docs/specs/tasks-steps-done.md, L1–L4, e2e п. 32–34): у обрезанной записи — «⌄», текст целиком в строке,
// «⌃» — обратно; у короткой значка нет; клик по тексту — окно; перетаскивание работает; «⌄» не задевает чекбокс и «▸»;
// не запоминается; у развёрнутой нет подсказки при наведении. В дне, в ленте и у шага в раскрытом списке. Данные — через API.
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
page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text()); });
page.on('pageerror', e => consoleErrors.push(e.message));
const sleep = ms => new Promise(r => setTimeout(r, ms));
const settle = () => sleep(600);
function assert(c, m) { if (!c) throw new Error(m); }
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-clamp-expand-${results.length}.png` }); }
}
const api = (method, path, body) => page.evaluate(async (m, p, b) => {
  const r = await fetch(`/api/${p}`, { method: m, headers: { 'Content-Type': 'application/json' }, body: b ? JSON.stringify(b) : undefined });
  if (!r.ok) throw new Error(`${m} ${p}: ${r.status} ${await r.text()}`);
  const text = await r.text();
  return text ? JSON.parse(text) : null;
}, method, path, body);
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day'); await settle(); }

const iso = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
const now = new Date();
const monday = new Date(now); monday.setDate(now.getDate() - ((now.getDay() + 6) % 7));
const dayIso = i => { const d = new Date(monday); d.setDate(monday.getDate() + i); return iso(d); };

const LONG = 'Очень длинная запись, которая точно не помещается в две строки узкой колонки дня и ленты, поэтому её нужно обрезать';
const LONG_STEP = 'Длинный шаг проекта, который тоже не помещается в две строки узкой колонки дня и поэтому обрезан';

/** Строка по названию: в селекторе области (scope) — среди .item. */
const row = (scope, title) => page.evaluateHandle((s, t) =>
  [...document.querySelectorAll(`${s} .item`)].find(i => i.querySelector('.title')?.textContent.trim() === t), scope, title);
const state = (scope, title) => page.evaluate((s, t) => {
  const i = [...document.querySelectorAll(`${s} .item`)].find(x => x.querySelector('.title')?.textContent.trim() === t);
  if (!i) return null;
  const title = i.querySelector('.title');
  const btn = i.querySelector('.unclamp');
  const lh = parseFloat(getComputedStyle(title).lineHeight);
  return {
    lines: Math.round(title.clientHeight / lh),
    arrow: !!btn && getComputedStyle(btn).display !== 'none',
    on: !!btn?.classList.contains('on'),
  };
}, scope, title);
async function clickArrow(scope, title) {
  const btn = await (await row(scope, title)).asElement().$('.unclamp');
  const b = await btn.boundingBox();
  await page.mouse.click(b.x + b.width / 2, b.y + b.height / 2); await settle();
}
const tipText = () => page.evaluate(() => document.querySelector('.clamp-tip')?.isConnected ? document.querySelector('.clamp-tip').textContent : null);

await load();
const ideas = await api('POST', 'categories', { name: 'Идеи' });
await api('POST', 'tasks', { title: LONG, target: { kind: 'someday', categoryId: ideas.id, index: 0 } });
await api('POST', 'tasks', { title: 'Коротко', target: { kind: 'someday', categoryId: ideas.id, index: 1 } });
const inDay = await api('POST', 'tasks', { title: LONG, target: { kind: 'day', date: dayIso(1), index: 0 } });
const project = await api('POST', 'tasks', { title: 'Ремонт', target: { kind: 'day', date: dayIso(2), index: 0 } });
await api('POST', `tasks/${project.id}/steps`, { title: LONG_STEP });
await load();
// Раскрыть шаги проекта (Ср)
await (await (await row('.day', 'Ремонт')).asElement().$('.fold')).click(); await settle();

for (const [where, scope, title] of [['в дне', '.day', LONG], ['в ленте', '.someday', LONG], ['у шага в раскрытом списке', '.day .steps', LONG_STEP]]) {
  await check(`${where}: «⌄» у обрезанной; клик — текст целиком, «⌃»; ещё клик — снова две строки`, async () => {
    let s = await state(scope, title);
    assert(s && s.lines === 2 && s.arrow && !s.on, `до: ${JSON.stringify(s)}`);
    await clickArrow(scope, title);
    s = await state(scope, title);
    assert(s.lines > 2 && s.arrow && s.on, `развернуть: ${JSON.stringify(s)}`);
    assert(!(await page.$('.editor')), 'открылось окно');
    await clickArrow(scope, title);
    s = await state(scope, title);
    assert(s.lines === 2 && !s.on, `свернуть: ${JSON.stringify(s)}`);
  });
}

await check('снимок: обрезанная и развёрнутая', async () => {
  await clickArrow('.day', LONG);
  await page.screenshot({ path: `${SHOTS}/clamp-expand.png`, clip: { x: 260, y: 60, width: 760, height: 380 } });
  await clickArrow('.day', LONG);
});

await check('у короткой записи «⌄» нет', async () => {
  const s = await state('.someday', 'Коротко');
  assert(s && !s.arrow, JSON.stringify(s));
});

await check('«⌄» не пересекается с чекбоксом и «▸»; клик по тексту развёрнутой записи — окно', async () => {
  const boxes = await page.evaluate(long => [...document.querySelectorAll('.item')].filter(i => i.querySelector('.unclamp') && getComputedStyle(i.querySelector('.unclamp')).display !== 'none')
    .map(i => {
      const u = i.querySelector('.unclamp').getBoundingClientRect();
      const others = [...i.querySelectorAll('.ck-hit, .fold')].map(e => e.getBoundingClientRect());
      return others.some(o => !(u.right <= o.left || u.left >= o.right || u.bottom <= o.top || u.top >= o.bottom));
    }), LONG);
  assert(boxes.length >= 3 && boxes.every(x => !x), JSON.stringify(boxes));
  await clickArrow('.day', LONG);
  const t = await (await row('.day', LONG)).asElement().$eval('.title', e => e.getBoundingClientRect().toJSON());
  await page.mouse.click(t.left + 20, t.top + 6); await settle();
  assert(await page.$eval('.editor .title-input', e => e.value) === LONG, 'клик по тексту не открыл окно');
  await page.keyboard.press('Escape'); await settle();
});

await check('развёрнутая — без подсказки при наведении; обрезанная — с подсказкой', async () => {
  await page.mouse.move(5, 5); await settle();
  let t = await (await row('.day', LONG)).asElement().$eval('.title', e => e.getBoundingClientRect().toJSON());
  await page.mouse.move(t.left + 30, t.top + 6); await sleep(900);
  assert(await tipText() === null, 'у развёрнутой подсказка');
  await page.mouse.move(5, 5); await settle();
  t = await (await row('.someday', LONG)).asElement().$eval('.title', e => e.getBoundingClientRect().toJSON());
  await page.mouse.move(t.left + 30, t.top + 6); await sleep(900);
  assert(await tipText() === LONG, `у обрезанной подсказка: ${await tipText()}`);
});

await check('развёрнутую запись можно перетащить в другой день', async () => {
  const from = await (await row('.day', LONG)).asElement().boundingBox();
  const fri = await (await page.evaluateHandle(() => document.querySelectorAll('.day')[4])).asElement().boundingBox();
  const sx = from.x + from.width / 2, sy = from.y + 10;
  await page.mouse.move(sx, sy); await page.mouse.down();
  for (let i = 1; i <= 14; i++) { await page.mouse.move(sx + (fri.x + 40 - sx) * i / 14, sy + (fri.y + fri.height - 40 - sy) * i / 14); await sleep(16); }
  await sleep(150); await page.mouse.up(); await settle();
  assert((await api('GET', `tasks/${inDay.id}`)).plannedDate === dayIso(4), 'не перенеслась');
});

await check('после перезагрузки всё снова в две строки (не запоминается)', async () => {
  await clickArrow('.someday', LONG);
  assert((await state('.someday', LONG)).on, 'не развернулась');
  await load();
  const s = await state('.someday', LONG);
  assert(s.lines === 2 && !s.on, JSON.stringify(s));
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);

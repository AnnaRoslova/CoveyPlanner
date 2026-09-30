// Порядок при перетаскивании: 1, 2, 3 дела в дне (с делом со временем и без) и в категории ленты.
// Каждый случай — на своих свежих данных (день недели / категория), созданных через API.
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
function assert(c, m) { if (!c) throw new Error(m); }
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-order-${results.length}.png` }); }
}
const api = (method, path, body) => page.evaluate(async (method, path, body) => {
  const r = await fetch('/api/' + path, { method, headers: { 'Content-Type': 'application/json' }, body: body ? JSON.stringify(body) : undefined });
  if (!r.ok) throw new Error(`${method} ${path}: ${r.status}`);
  return r.status === 204 ? null : r.json();
}, method, path, body);
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day'); await sleep(500); }

await page.goto(BASE, { waitUntil: 'networkidle0' });
const monday = (await api('GET', 'week/' + new Date().toISOString().slice(0, 10))).weekStart;
const dateOf = i => { const d = new Date(monday + 'T00:00:00Z'); d.setUTCDate(d.getUTCDate() + i); return d.toISOString().slice(0, 10); };

// Подготовить день i: очистить (в корзину) и создать дела; timed — [название, 'HH:MM']
async function setupDay(i, untimed, timed = []) {
  const week = await api('GET', 'week/' + monday);
  for (const t of week.tasks.filter(t => t.plannedDate === dateOf(i))) await api('DELETE', `tasks/${t.id}`);
  for (const [title, time] of timed) {
    const t = await api('POST', 'tasks', { title, target: { kind: 'day', date: dateOf(i), index: 99 } });
    await api('PUT', `tasks/${t.id}`, { title, notes: null, roleId: null, quadrant: null, startTime: time + ':00' });
  }
  for (const title of untimed) await api('POST', 'tasks', { title, target: { kind: 'day', date: dateOf(i), index: 99 } });
}
async function setupCategory(name, titles) {
  const c = await api('POST', 'categories', { name, emoji: null });
  for (const title of titles) await api('POST', 'tasks', { title, target: { kind: 'someday', categoryId: c.id, index: 99 } });
}

const titleOf = el => [...el.querySelector('.tx').childNodes].filter(n => !n.classList?.contains('pills') && !n.classList?.contains('note')).map(n => n.textContent).join('').trim();
const zoneEl = z => z.day !== undefined
  ? `document.querySelectorAll('.day')[${z.day}]`
  : z.role !== undefined
    ? `[...document.querySelectorAll('.role')].find(r => r.querySelector('.role-h .name')?.textContent.trim() === ${JSON.stringify(z.role)})`
    : `[...document.querySelectorAll('.cat')].find(c => c.querySelector('.cat-h .name')?.textContent.trim() === ${JSON.stringify(z.cat)}).querySelector('.cat-b')`;
// Зону — в середину видимой части панели (иначе у края сработает автопрокрутка)
const reveal = z => page.evaluate(`${zoneEl(z)}.scrollIntoView({ block: 'center' })`).then(() => sleep(150));
const titlesIn = z => page.evaluate(`(() => { const titleOf = ${titleOf}; return [...${zoneEl(z)}.querySelectorAll('.item')].map(titleOf); })()`);
const rectOf = (z, title) => page.evaluate(`(() => { const titleOf = ${titleOf};
  const it = [...${zoneEl(z)}.querySelectorAll('.item')].find(i => titleOf(i) === ${JSON.stringify(title)});
  if (!it) return null; const r = it.getBoundingClientRect(); return { x: r.x, y: r.y, w: r.width, h: r.height, bottom: r.bottom }; })()`);
const zoneRect = z => page.evaluate(`(() => { const r = ${zoneEl(z)}.getBoundingClientRect(); return { x: r.x, y: r.y, w: r.width, h: r.height, bottom: r.bottom }; })()`);
const headRect = z => page.evaluate(`(() => { const h = ${zoneEl(z)}.querySelector('.day-h'); const r = h.getBoundingClientRect(); return { x: r.x, y: r.y, h: r.height, bottom: r.bottom }; })()`);

// Перетащить дело title зоны z в точку (x, y); вернуть, была ли видна линия, и итоговый порядок
async function dragTo(z, title, x, y) {
  const from = await rectOf(z, title);
  assert(from, `нет «${title}»`);
  const sx = from.x + 30, sy = from.y + from.h / 2;
  await page.mouse.move(sx, sy); await page.mouse.down();
  for (let i = 1; i <= 12; i++) { await page.mouse.move(sx + (x - sx) * i / 12, sy + (y - sy) * i / 12); await sleep(16); }
  await sleep(120);
  const line = await page.$eval('.drop-line', l => !l.hidden).catch(() => false);
  await page.mouse.up(); await sleep(600);
  return { line, order: await titlesIn(z) };
}
// Точки: над первым делом (верхняя четверть), между делами, ниже последнего (пустое место)
const above = r => ({ x: r.x + 30, y: r.y + r.h * 0.25 });
const below = r => ({ x: r.x + 30, y: r.bottom + 30 });
const lowerHalf = r => ({ x: r.x + 30, y: r.y + r.h * 0.75 });

async function expectMove(name, z, title, point, expected, { line = true } = {}) {
  await check(name, async () => {
    const res = await dragTo(z, title, point.x, point.y);
    assert(res.line === line, `линия ${res.line ? 'видна' : 'не видна'}`);
    assert(JSON.stringify(res.order) === JSON.stringify(expected), `порядок ${JSON.stringify(res.order)}`);
    if (!process.env.NO_RELOAD) await load();
    assert(JSON.stringify(await titlesIn(z)) === JSON.stringify(expected), `после перезагрузки ${JSON.stringify(await titlesIn(z))}`);
  });
}

// ---------- Дни без дел со временем ----------
await setupDay(0, ['A']);
await setupDay(1, ['A', 'B']);
await setupDay(2, ['A', 'B', 'C']);
// ---------- Дни с делом со временем ----------
await setupDay(3, ['A'], [['T', '09:00']]);
await setupDay(4, ['A', 'B'], [['T', '09:00']]);
await setupDay(5, ['A', 'B', 'C'], [['T', '09:00']]);
// ---------- Лента ----------
await setupCategory('Один', ['A']);
await setupCategory('Два', ['A', 'B']);
await setupCategory('Три', ['A', 'B', 'C']);
await load();

for (const [label, z, timed] of [['день', { day: 0 }, false], ['день+время', { day: 3 }, true]]) {
  const head = timed ? ['T'] : [];
  await expectMove(`${label}, 1 дело: вниз в пустое место — остаётся`, z, 'A', below(await rectOf(z, 'A')), [...head, 'A']);
}

for (const [label, z, timed] of [['день', { day: 1 }, false], ['день+время', { day: 4 }, true], ['лента', { cat: 'Два' }, false]]) {
  const head = timed ? ['T'] : [];
  await expectMove(`${label}, 2 дела: A ниже B (пустое место) → B, A`, z, 'A', below(await rectOf(z, 'B')), [...head, 'B', 'A']);
  await expectMove(`${label}, 2 дела: A над первым B → A, B`, z, 'A', above(await rectOf(z, 'B')), [...head, 'A', 'B']);
  await expectMove(`${label}, 2 дела: A на нижнюю половину B → B, A`, z, 'A', lowerHalf(await rectOf(z, 'B')), [...head, 'B', 'A']);
  await expectMove(`${label}, 2 дела: B над первым A → B, A... обратно A над B`, z, 'A', above(await rectOf(z, 'B')), [...head, 'A', 'B']);
}

for (const [label, z, timed] of [['день', { day: 2 }, false], ['день+время', { day: 5 }, true], ['лента', { cat: 'Три' }, false]]) {
  const head = timed ? ['T'] : [];
  await expectMove(`${label}, 3 дела: A в конец (пустое место) → B, C, A`, z, 'A', below(await rectOf(z, 'C')), [...head, 'B', 'C', 'A']);
  await expectMove(`${label}, 3 дела: A над первым → A, B, C`, z, 'A', above(await rectOf(z, 'B')), [...head, 'A', 'B', 'C']);
  await expectMove(`${label}, 3 дела: C между A и B → A, C, B`, z, 'C', above(await rectOf(z, 'B')), [...head, 'A', 'C', 'B']);
  await expectMove(`${label}, 3 дела: A в середину (под C) → C, A, B`, z, 'A', lowerHalf(await rectOf(z, 'C')), [...head, 'C', 'A', 'B']);
}

// Над заголовком дня (выше всех дел) — наверх ручного списка; с делом со временем — под ним
await check('день без времени: бросить на заголовок дня → наверх', async () => {
  const z = { day: 2 };
  const h = await headRect(z);
  const res = await dragTo(z, 'B', h.x + 30, h.y + h.h / 2);
  assert(res.line, 'линия не видна');
  assert(JSON.stringify(res.order) === '["B","C","A"]', `порядок ${res.order}`);
});
await check('день+время: бросить на заголовок дня → сразу под делом со временем', async () => {
  const z = { day: 5 };
  const h = await headRect(z);
  const res = await dragTo(z, 'B', h.x + 30, h.y + h.h / 2);
  assert(JSON.stringify(res.order) === '["T","B","C","A"]', `порядок ${res.order}`);
});
await check('день+время: дело без времени выше дела со временем не встаёт', async () => {
  const z = { day: 4 };
  const res = await dragTo(z, 'B', ...Object.values(above(await rectOf(z, 'T'))));
  assert(res.order[0] === 'T', `порядок ${res.order}`);
});

// ---------- Цели в роли: 1, 2, 3 цели ----------
async function setupRole(name, titles) {
  const role = await api('POST', 'roles', { name, color: null });
  for (const title of titles) await api('POST', 'goals', { roleId: role.id, weekStart: monday, title });
}
await setupRole('Одна', ['A']);
await setupRole('Две', ['A', 'B']);
await setupRole('Три цели', ['A', 'B', 'C']);
await load();

// Под последней целью — «+ цель» внутри карточки роли (ниже уже соседняя роль)
const belowGoal = r => ({ x: r.x + 30, y: r.bottom + 10 });
async function roleMove(name, z, title, pointFn, expected) {
  await check(name, async () => {
    await reveal(z);
    const res = await dragTo(z, title, ...Object.values(await pointFn()));
    assert(res.line, 'линия не видна');
    assert(JSON.stringify(res.order) === JSON.stringify(expected), `порядок ${JSON.stringify(res.order)}`);
    await load();
    await reveal(z);
    assert(JSON.stringify(await titlesIn(z)) === JSON.stringify(expected), `после перезагрузки ${JSON.stringify(await titlesIn(z))}`);
  });
}
{
  const one = { role: 'Одна' }, two = { role: 'Две' }, three = { role: 'Три цели' };
  await roleMove('роль, 1 цель: вниз под неё — остаётся', one, 'A', async () => belowGoal(await rectOf(one, 'A')), ['A']);
  await roleMove('роль, 2 цели: A под B → B, A', two, 'A', async () => belowGoal(await rectOf(two, 'B')), ['B', 'A']);
  await roleMove('роль, 2 цели: A над первой → A, B', two, 'A', async () => above(await rectOf(two, 'B')), ['A', 'B']);
  await roleMove('роль, 3 цели: A в конец → B, C, A', three, 'A', async () => belowGoal(await rectOf(three, 'C')), ['B', 'C', 'A']);
  await roleMove('роль, 3 цели: A над первой → A, B, C', three, 'A', async () => above(await rectOf(three, 'B')), ['A', 'B', 'C']);
  await roleMove('роль, 3 цели: C между A и B → A, C, B', three, 'C', async () => above(await rectOf(three, 'B')), ['A', 'C', 'B']);
}

// ---------- Небольшой сдвиг: край призрака заходит за середину соседа ----------
// Сдвинуть запись на долю её высоты (вверх — отрицательная доля)
async function shiftBy(z, title, fraction) {
  const r = await rectOf(z, title);
  return dragTo(z, title, r.x + 30, r.y + r.h / 2 + r.h * fraction);
}

const long1 = 'Позвонить в налоговую насчёт вычета за лечение зубов';
const long2 = 'Разобрать документы для страховой и отсканировать чеки';
await setupDay(6, [long1, long2]);
await setupCategory('Длинные', [long1, long2]);
await load();

for (const [label, z] of [['день, длинные названия', { day: 6 }], ['лента, длинные названия', { cat: 'Длинные' }]]) {
  await check(`${label}: вторую вверх на 0,6 высоты → меняются местами`, async () => {
    const h = (await rectOf(z, long2)).h;
    assert(h > 30, `запись всего ${h}px — названия не переносятся, случай не проверяется`);
    const res = await shiftBy(z, long2, -0.6);
    assert(JSON.stringify(res.order) === JSON.stringify([long2, long1]), `порядок ${JSON.stringify(res.order)}`);
  });
  await check(`${label}: первую вниз на 0,6 высоты → меняются обратно`, async () => {
    const res = await shiftBy(z, long2, 0.6);
    assert(JSON.stringify(res.order) === JSON.stringify([long1, long2]), `порядок ${JSON.stringify(res.order)}`);
  });
  await check(`${label}: сдвиг на 0,3 высоты — остаётся на месте`, async () => {
    const res = await shiftBy(z, long1, 0.3);
    assert(JSON.stringify(res.order) === JSON.stringify([long1, long2]), `порядок ${JSON.stringify(res.order)}`);
  });
}

await check('линия вставки рисуется поверх призрака', async () => {
  const z = { day: 2 };
  const r = await rectOf(z, (await titlesIn(z)).at(-1));
  await page.mouse.move(r.x + 30, r.y + r.h / 2); await page.mouse.down();
  await page.mouse.move(r.x + 30, r.bottom + 12, { steps: 8 }); await sleep(120);
  const z1 = await page.evaluate(() => ({
    line: Number(getComputedStyle(document.querySelector('.drop-line')).zIndex),
    ghost: Number(getComputedStyle(document.querySelector('.drag-ghost')).zIndex),
    visible: !document.querySelector('.drop-line').hidden,
  }));
  await page.keyboard.press('Escape'); await page.mouse.up(); await sleep(300);
  assert(z1.visible && z1.line > z1.ghost, JSON.stringify(z1));
});

await browser.close();
console.log(results.join('\n'));
console.log(consoleErrors.length ? `\nОшибки консоли:\n${consoleErrors.join('\n')}` : '\nОшибок в консоли нет');

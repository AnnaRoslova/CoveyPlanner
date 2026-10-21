// Проекты и шаги в днях (docs/specs/tasks-steps-done.md, e2e п. 1–8): «▸ Ремонт ☑ 0/3», раскрытие в дне (переживает перезагрузку),
// шаг — в любой день (подпись «↳ Ремонт»), галочка общая, автоотметка проекта, шаг на строку проекта — снять с дня,
// шаг в ленту — стал мыслью, на другую неделю — задержкой над краем; проект в корзину вместе с шагами и «Вернуть».
// Данные создаются здесь же через API (чистая база).
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
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-steps-${results.length}.png` }); }
}
const api = (method, path, body) => page.evaluate(async (m, p, b) => {
  const r = await fetch(`/api/${p}`, { method: m, headers: { 'Content-Type': 'application/json' }, body: b ? JSON.stringify(b) : undefined });
  if (!r.ok) throw new Error(`${m} ${p}: ${r.status} ${await r.text()}`);
  return r.status === 204 || r.status === 201 && r.headers.get('content-length') === '0' ? null : r.json();
}, method, path, body);
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day'); await settle(); }

const iso = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
const now = new Date();
const monday = new Date(now); monday.setDate(now.getDate() - ((now.getDay() + 6) % 7));
const dayIso = i => { const d = new Date(monday); d.setDate(monday.getDate() + i); return iso(d); };

/** Строки дня i: название, подпись «↳ …», «☑ …», отметка; шаги раскрытых проектов — отдельно (steps). */
const day = i => page.evaluate(i => {
  const d = document.querySelectorAll('.day')[i];
  const row = it => ({
    title: it.querySelector('.title')?.textContent.trim(),
    parent: it.querySelector('.parent-of span')?.textContent.trim() ?? null,
    project: it.querySelector('.project-label')?.textContent.replace('▸', '').trim() ?? null,
    mark: it.querySelector('.day-mark')?.textContent.trim() ?? null,
    done: it.classList.contains('done'),
  });
  return {
    rows: [...d.querySelectorAll(':scope > .item')].map(row),
    steps: [...d.querySelectorAll('.steps .item')].map(row),
  };
}, i);
const rowEl = (i, title, inSteps = false) => page.evaluateHandle((i, t, s) =>
  [...document.querySelectorAll('.day')[i].querySelectorAll(s ? '.steps .item' : ':scope > .item')]
    .find(it => it.querySelector('.title')?.textContent.trim() === t), i, title, inSteps);
async function boxOf(handle) {
  const el = (await handle).asElement();
  assert(el, 'элемент не найден');
  await el.evaluate(e => e.scrollIntoView({ block: 'nearest' }));
  await sleep(100);
  return el.boundingBox();
}
async function mouseDrag(from, x, y, holdMs = 150) {
  const sx = from.x + Math.min(60, from.width / 2), sy = from.y + Math.min(from.height / 2, 10);
  await page.mouse.move(sx, sy); await page.mouse.down();
  for (let i = 1; i <= 14; i++) { await page.mouse.move(sx + (x - sx) * i / 14, sy + (y - sy) * i / 14); await sleep(16); }
  await sleep(holdMs);
  await page.mouse.up(); await settle();
}
const toastText = () => page.evaluate(() => document.querySelector('.toast')?.textContent.replace(/\s+/g, ' ').trim() ?? null);
const clickToastAction = text => page.evaluate(t => [...document.querySelectorAll('.toast button.action')].find(b => b.textContent.trim() === t)?.click(), text);

await load();
const repair = await api('POST', 'tasks', { title: 'Ремонт', target: { kind: 'day', date: dayIso(0), index: 0 } });
for (const t of ['Обои', 'Плинтус', 'Люстра']) await api('POST', `tasks/${repair.id}/steps`, { title: t });
const steps = await api('GET', `tasks/${repair.id}/steps`);
const id = t => steps.find(s => s.title === t).id;
await load();

await check('1. проект в дне: «▸ Ремонт ☑ 0/3», раскрыть — 3 шага; раскрытие переживает перезагрузку', async () => {
  const mon = await day(0);
  assert(mon.rows[0]?.project === '☑ 0/3', JSON.stringify(mon.rows));
  const fold = (await rowEl(0, 'Ремонт')).asElement();
  await (await fold.$('.expand')).click(); await settle();
  assert(JSON.stringify((await day(0)).steps.map(s => s.title)) === '["Обои","Плинтус","Люстра"]', JSON.stringify((await day(0)).steps));
  await load();
  assert((await day(0)).steps.length === 3, 'после перезагрузки свёрнут');
});

await check('2. шаг из раскрытого списка → четверг: там «↳ Ремонт», в списке метка «Чт»', async () => {
  const from = await boxOf(rowEl(0, 'Обои', true));
  const thu = await boxOf(page.evaluateHandle(() => document.querySelectorAll('.day')[3]));
  await mouseDrag(from, thu.x + thu.width / 2, thu.y + thu.height - 30);
  const t = await day(3);
  assert(t.rows.some(r => r.title === 'Обои' && r.parent === 'Ремонт'), JSON.stringify(t.rows));
  await page.screenshot({ path: `${SHOTS}/steps-week.png`, clip: { x: 260, y: 0, width: 1080, height: 520 } });
  assert((await day(0)).steps.find(s => s.title === 'Обои')?.mark === 'Чт', JSON.stringify((await day(0)).steps));
});

await check('3. отметили шаг в четверге — отмечен и в списке проекта, «☑ 1/3»', async () => {
  await (await (await rowEl(3, 'Обои')).asElement().$('.ck-hit')).click(); await settle();
  const mon = await day(0);
  assert(mon.steps.find(s => s.title === 'Обои')?.done, 'в списке не отмечен');
  assert(mon.rows[0].project === '☑ 1/3', mon.rows[0].project);
});

await check('4. клик по «↳ Ремонт» — окно проекта; клик по шагу — окно шага', async () => {
  await (await (await rowEl(3, 'Обои')).asElement().$('.parent-of')).click(); await settle();
  assert(await page.$eval('.editor .title-input', e => e.value) === 'Ремонт', 'не окно проекта');
  await page.keyboard.press('Escape'); await sleep(300);
  await (await (await rowEl(0, 'Плинтус', true)).asElement().$('.tx')).click(); await settle();
  assert(await page.$eval('.editor .title-input', e => e.value) === 'Плинтус', 'не окно шага');
  assert(!(await page.$('.editor .steps')), 'у шага список шагов');
  await page.keyboard.press('Escape'); await sleep(300);
});

await check('7a. шаг из четверга на строку проекта — снят с дня, остался шагом (метки дня нет)', async () => {
  const from = await boxOf(rowEl(3, 'Обои'));
  const target = await boxOf(rowEl(0, 'Ремонт'));
  await mouseDrag(from, target.x + target.width / 2, target.y + target.height / 2);
  assert(!(await day(3)).rows.some(r => r.title === 'Обои'), 'остался в четверге');
  const s = (await day(0)).steps.find(x => x.title === 'Обои');
  assert(s && s.mark === null, JSON.stringify((await day(0)).steps));
  const server = await api('GET', `tasks/${id('Обои')}`);
  assert(server.plannedDate === null && server.parentId === repair.id, JSON.stringify(server));
});

await check('6. отметили все шаги — проект отмечен сам, плашка; сняли галочку с шага — проект остался отмеченным', async () => {
  for (const t of ['Плинтус', 'Люстра']) {
    await (await (await rowEl(0, t, true)).asElement().$('.ck-hit')).click(); await settle();
  }
  assert((await day(0)).rows[0].done, 'проект не отмечен');
  assert((await toastText())?.includes('Все шаги сделаны'), `плашка: ${await toastText()}`);
  await (await (await rowEl(0, 'Люстра', true)).asElement().$('.ck-hit')).click(); await settle();
  const mon = await day(0);
  assert(mon.rows[0].done && mon.rows[0].project === '☑ 2/3', JSON.stringify(mon.rows[0]));
  await (await (await rowEl(0, 'Ремонт')).asElement().$('.ck-hit')).click(); await settle();
  assert(!(await day(0)).rows[0].done, 'проект не открылся по своей галочке');
});

await check('5. шаг на следующую неделю (задержка над краем) — у проекта метка с датой', async () => {
  const from = await boxOf(rowEl(0, 'Люстра', true));
  const edge = await boxOf(page.evaluateHandle(() => document.querySelector('.flip-zone.next') ?? document.querySelector('[data-flip="1"]')));
  // Держим над стрелкой «›» — неделя листается; бросаем в среду новой недели
  const sx = from.x + 40, sy = from.y + 8;
  await page.mouse.move(sx, sy); await page.mouse.down();
  for (let i = 1; i <= 10; i++) { await page.mouse.move(sx + 12 * i, sy); await sleep(16); }
  const arrow = await page.$('[data-flip="1"]');
  const ab = await arrow.boundingBox();
  await page.mouse.move(ab.x + ab.width / 2, ab.y + ab.height / 2, { steps: 6 });
  await sleep(900);
  const wed = await boxOf(page.evaluateHandle(() => document.querySelectorAll('.day')[2]));
  await page.mouse.move(wed.x + wed.width / 2, wed.y + wed.height - 30, { steps: 8 });
  await sleep(200);
  await page.mouse.up(); await settle();
  const server = await api('GET', `tasks/${id('Люстра')}`);
  const next = new Date(monday); next.setDate(monday.getDate() + 9);
  assert(server.plannedDate === iso(next), `дата ${server.plannedDate}, ждали ${iso(next)}`);
  await page.click('.today-b').catch(() => {}); await load();
  const mark = (await day(0)).steps.find(s => s.title === 'Люстра')?.mark ?? '';
  assert(/\d/.test(mark), `метка: «${mark}»`);
  void edge;
});

await check('7b. шаг из списка на категорию ленты — стал мыслью в ней', async () => {
  const shop = await api('POST', 'categories', { name: 'Покупки' });
  await load();
  const from = await boxOf(rowEl(0, 'Плинтус', true));
  const header = await boxOf(page.evaluateHandle(() =>
    [...document.querySelectorAll('.cat-h')].find(h => h.querySelector('.name')?.textContent.trim() === 'Покупки')));
  await mouseDrag(from, header.x + header.width / 2, header.y + header.height / 2);
  const server = await api('GET', `tasks/${id('Плинтус')}`);
  assert(server.state === 'Someday' && server.parentId === null && server.categoryId === shop.id, JSON.stringify(server));
  assert(!(await day(0)).steps.some(s => s.title === 'Плинтус'), 'остался в списке проекта');
});

await check('8. проект в корзину (шаг в другом дне) — шаги пропали, плашка «и N шагов» → «Вернуть»; без «Вернуть» — из корзины с шагами', async () => {
  await api('POST', `tasks/${id('Обои')}/move`, { kind: 'day', date: dayIso(4), index: 0 });
  await load();
  assert((await day(4)).rows.some(r => r.title === 'Обои'), 'шаг не в пятнице');
  await (await (await rowEl(0, 'Ремонт')).asElement().$('.tx')).click(); await settle();
  await page.click('.editor .more-b'); await sleep(300); await page.evaluate(() => [...document.querySelectorAll('.editor .popover .option')].find(b => b.querySelector('.option-name')?.textContent.trim() === 'в корзину').click());
  await settle();
  assert(!(await day(4)).rows.some(r => r.title === 'Обои'), 'шаг остался в пятнице');
  assert((await toastText())?.includes('шаг'), `плашка: ${await toastText()}`);
  await clickToastAction('Вернуть'); await settle();
  assert((await day(4)).rows.some(r => r.title === 'Обои'), 'после «Вернуть» шага нет');

  await (await (await rowEl(0, 'Ремонт')).asElement().$('.tx')).click(); await settle();
  await page.click('.editor .more-b'); await sleep(300); await page.evaluate(() => [...document.querySelectorAll('.editor .popover .option')].find(b => b.querySelector('.option-name')?.textContent.trim() === 'в корзину').click());
  await sleep(6800);
  const trash = await api('GET', 'trash');
  assert(trash.some(t => t.id === repair.id) && !trash.some(t => t.id === id('Обои')), JSON.stringify(trash.map(t => t.title)));
  await api('POST', `trash/${repair.id}/restore`);
  await load();
  assert((await day(4)).rows.some(r => r.title === 'Обои'), 'после возврата из корзины шага в пятнице нет');
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);

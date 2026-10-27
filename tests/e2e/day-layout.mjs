// @seed — нужны демо-данные (tools/seed-demo.ps1)
// День растёт вместе со списком: дела не вылезают за низ колонки, все дни одной высоты,
// неделя прокручивается вертикально, пустое место для нового дела (polish-2, A1) — сразу под последним делом.
// Чекбокс: зона нажатия — вся левая полоса записи (в днях и у целей), клик по ней не открывает окно.
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
const settle = () => sleep(400);
function assert(c, m) { if (!c) throw new Error(m); }
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-day-layout-${results.length}.png` }); }
}
const api = (method, path, body) => page.evaluate(async (method, path, body) => {
  const r = await fetch('/api/' + path, { method, headers: { 'Content-Type': 'application/json', 'X-Planner': '1' }, body: body ? JSON.stringify(body) : undefined });
  return r.status === 204 ? null : r.json();
}, method, path, body);
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day'); await settle(); }

await load();
const monday = (await api('GET', 'week/' + new Date().toISOString().slice(0, 10))).weekStart;
// В понедельник — длинный список, длиннее экрана
for (let i = 1; i <= 24; i++) {
  await api('POST', 'tasks', { title: `Длинный список, дело ${i}`, target: { kind: 'day', date: monday, index: 999 } });
}
await load();

await check('длинный день: дела внутри колонки, пустое место сразу под последним', async () => {
  const m = await page.$eval('.day', d => {
    const items = [...d.querySelectorAll('.item')];
    const last = items.at(-1).getBoundingClientRect();
    return { dayBottom: d.getBoundingClientRect().bottom, lastBottom: last.bottom, addTop: d.querySelector('.day-blank').getBoundingClientRect().top };
  });
  assert(m.lastBottom <= m.dayBottom, `дело вылезло за день: ${m.lastBottom} > ${m.dayBottom}`);
  assert(m.addTop >= m.lastBottom && m.addTop - m.lastBottom < 12, `пустое место не под последним делом: ${m.addTop - m.lastBottom}px`);
});

await check('все дни одной высоты — по самому длинному', async () => {
  const heights = await page.$$eval('.day', ds => ds.map(d => Math.round(d.getBoundingClientRect().height)));
  assert(new Set(heights).size === 1, `высоты: ${heights}`);
});

await check('в коротком дне пустое место тоже сразу под последним делом', async () => {
  const gap = await page.$$eval('.day', ds => {
    const d = ds.find(x => x.querySelectorAll('.item').length > 0 && x.querySelectorAll('.item').length < 4);
    return d.querySelector('.day-blank').getBoundingClientRect().top - [...d.querySelectorAll('.item')].at(-1).getBoundingClientRect().bottom;
  });
  assert(gap >= 0 && gap < 12, `зазор ${gap}px`);
});

await check('неделя прокручивается вертикально — до пустого места длинного дня', async () => {
  const s = await page.$eval('.days', el => ({ scroll: el.scrollHeight, client: el.clientHeight }));
  assert(s.scroll > s.client, `нет прокрутки: ${JSON.stringify(s)}`);
  await page.$eval('.days', el => el.scrollTop = el.scrollHeight);
  await settle();
  const visible = await page.$eval('.day .day-blank', a => { const r = a.getBoundingClientRect(); return r.bottom <= innerHeight && r.top >= 0; });
  assert(visible, 'пустое место не видно после прокрутки');
  await page.$eval('.days', el => el.scrollTop = 0);
});

// Точка у нижнего левого угла записи — внутри полосы чекбокса, но далеко от самого квадратика
async function stripPoint(handle) {
  const r = await handle.evaluate(i => i.getBoundingClientRect().toJSON());
  return { x: r.left + 3, y: r.bottom - 3, height: r.height };
}

await check('дело в дне: клик по левой полосе (не по квадратику) — «выполнено», окно не открывается', async () => {
  const item = await page.evaluateHandle(() => [...document.querySelectorAll('.day .item')]
    .find(i => !i.classList.contains('done') && i.getBoundingClientRect().height > 40));
  assert(item.asElement(), 'нет записи в две строки');
  const p = await stripPoint(item);
  await page.mouse.click(p.x, p.y); await settle();
  assert(!(await page.$('.editor')), 'открылось окно дела');
  assert(await item.evaluate(i => i.classList.contains('done')), 'не отмечено');
  // D3: выполненное уехало вниз дня — снимаем отметку там, где оно теперь
  const p2 = await stripPoint(item);
  await page.mouse.click(p2.x, p2.y); await settle();
  assert(!(await item.evaluate(i => i.classList.contains('done'))), 'отметка не снялась');
});

await check('ширина полосы ~30px: правее неё клик открывает окно', async () => {
  const item = await page.$('.day .item:not(.done)');
  const r = await item.evaluate(i => i.getBoundingClientRect().toJSON());
  const strip = await item.$eval('.ck-hit', b => b.getBoundingClientRect().toJSON());
  assert(Math.abs(strip.width - 30) <= 2 && Math.abs(strip.height - r.height) <= 1, `полоса ${strip.width}×${strip.height}, запись ${r.height}`);
  const square = await item.$eval('.ck', s => s.getBoundingClientRect().toJSON());
  assert(Math.abs(square.width - 14) <= 1 && square.top - r.top < 10, `квадратик ${square.width}px, отступ ${square.top - r.top}`);
  await page.mouse.click(r.left + 40, r.top + 8); await settle();
  assert(await page.$('.editor'), 'окно не открылось');
  await page.keyboard.press('Escape'); await settle();
});

await check('цель в левой панели: клик по левой полосе — отметка, окно цели не открывается', async () => {
  const goal = await page.evaluateHandle(() => [...document.querySelectorAll('.role .item')].find(i => i.textContent.includes('100 страниц')));
  const p = await stripPoint(goal);
  await page.mouse.click(p.x, p.y); await settle();
  assert(!(await page.$('.editor')), 'открылось окно цели');
  const count = await page.evaluate(() => { const r = [...document.querySelectorAll('.role')].find(x => x.textContent.includes('100 страниц')); return (() => { const items = [...r.querySelectorAll('.role-body > .item')]; return items.length ? `${items.filter(i => i.classList.contains('done')).length}/${items.length}` : null; })(); });
  assert(count === '1/1', `счётчик ${count}`);
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));

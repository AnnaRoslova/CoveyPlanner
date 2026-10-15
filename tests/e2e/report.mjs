// N5 (docs/specs/next-batch.md, N5.1–N5.7): «📊» в шапке недели — экран отчёта. Периоды переключаются, числа совпадают с API,
// «Скачать CSV» — файл с BOM, «;» и целой кириллицей; пустой период — «За этот период ничего нет». Данные — через API (чистая база).
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
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-report-${results.length}.png` }); }
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
const today = iso(now);
const monday = new Date(now); monday.setDate(now.getDate() - ((now.getDay() + 6) % 7));
const monthFrom = iso(new Date(now.getFullYear(), now.getMonth(), 1));
const monthTo = iso(new Date(now.getFullYear(), now.getMonth() + 1, 0));
const MONTHS = ['Январь', 'Февраль', 'Март', 'Апрель', 'Май', 'Июнь', 'Июль', 'Август', 'Сентябрь', 'Октябрь', 'Ноябрь', 'Декабрь'];

const screen = () => page.evaluate(() => {
  const s = document.querySelector('.report-screen');
  if (!s) return null;
  return {
    mode: s.querySelector('.period.on')?.textContent.trim(),
    label: s.querySelector('.range-label')?.textContent.trim() ?? null,
    rows: [...s.querySelectorAll('.srow:not(.oneoff)')].map(r => ({
      title: r.querySelector('.title').textContent.trim(), fact: r.querySelector('.fact').textContent.replace(/\s+/g, ' ').trim(),
      pct: r.querySelector('.pct')?.textContent.trim(), streak: r.querySelector('.streak')?.textContent.trim() })),
    oneoff: s.querySelector('.srow.oneoff .fact')?.textContent.replace(/\s+/g, ' ').trim() ?? null,
    done: [...s.querySelectorAll('.dgroup')].map(p => p.textContent.replace(/\s+/g, ' ').trim()),
    empty: s.querySelector('.empty')?.textContent.trim() ?? null,
    csv: s.querySelector('.csv')?.getAttribute('href'),
  };
});
const setMode = async label => { await page.evaluate(l => [...document.querySelectorAll('.report-screen .period')].find(b => b.textContent.trim() === l).click(), label); await settle(); };

await load();
const body = (await api('GET', 'board')).roles.find(r => r.name === 'Тело');
const run = await api('POST', 'goals', { roleId: body.id, weekStart: iso(monday), title: 'Бег', isRecurring: true });
const session = (await api('POST', `goals/${run.id}/schedule`, { kind: 'day', date: today, index: 0 })).task;
await api('PUT', `tasks/${session.id}/done`, { isDone: true });
const books = await api('POST', 'categories', { name: 'Книги', emoji: '📚' });
const dune = await api('POST', 'tasks', { title: 'Дюна', target: { kind: 'someday', categoryId: books.id, index: 0 } });
await api('PUT', `tasks/${dune.id}/done`, { isDone: true });
await load();

await check('N5.1, N5.2: «📊» в шапке — экран; по умолчанию текущий месяц; числа как в API', async () => {
  await page.click('.report-b'); await page.waitForSelector('.report-screen'); await settle();
  const s = await screen();
  assert(s.mode === 'месяц' && s.label === `${MONTHS[now.getMonth()]} ${now.getFullYear()}`, JSON.stringify(s));
  const r = await api('GET', `report?from=${monthFrom}&to=${monthTo}`);
  const row = r.series.find(x => x.title === 'Бег');
  const shown = s.rows.find(x => x.title === 'Бег');
  assert(shown && shown.fact === `${row.fact} / ${row.plan}` && row.fact === 1, JSON.stringify({ shown, row }));
  assert(s.done.some(d => d.startsWith('📚 Книги — 1:') && d.includes('Дюна')), JSON.stringify(s.done));
  await (await page.$('.report-screen')).screenshot({ path: `${SHOTS}/report.png` });
});

await check('N5.2: «неделя» — показанная неделя; стрелка ‹ — прошлая, «Бега» там нет (цели не было)', async () => {
  await setMode('неделя');
  let s = await screen();
  assert(s.mode === 'неделя' && s.rows.some(x => x.title === 'Бег'), JSON.stringify(s));
  await page.click('.report-screen .nav[title="Раньше"]'); await settle();
  s = await screen();
  assert(!s.rows.some(x => x.title === 'Бег'), JSON.stringify(s));
});

await check('N5.2: «год» и «N месяцев» переключаются, подпись года', async () => {
  await setMode('год');
  assert((await screen()).label === String(now.getFullYear()), JSON.stringify(await screen()));
  await setMode('N месяцев');
  const s = await screen();
  assert(s.mode === 'N месяцев' && s.label.includes('—'), JSON.stringify(s));
});

await check('N5.6: «Скачать CSV» — BOM, «;», CRLF, кириллица цела', async () => {
  await setMode('месяц');
  const href = (await screen()).csv;
  assert(href === `api/report.csv?from=${monthFrom}&to=${monthTo}`, href);
  const r = await page.evaluate(async h => {
    const res = await fetch(h);
    const bytes = new Uint8Array(await res.arrayBuffer());
    return { bom: [...bytes.slice(0, 3)], text: new TextDecoder('utf-8').decode(bytes.slice(3)), type: res.headers.get('content-type'), disp: res.headers.get('content-disposition') };
  }, href);
  assert(JSON.stringify(r.bom) === '[239,187,191]', JSON.stringify(r.bom));
  assert(r.text.includes('Привычка;Бег;Тело;') && r.text.includes('\r\n') && r.text.includes('Выполненное;Книги;Дюна;'), r.text.slice(0, 400));
  assert(r.type.startsWith('text/csv') && /attachment/.test(r.disp), JSON.stringify(r));
});

await check('N5: «свой» период в будущем — «За этот период ничего нет»; Esc закрывает', async () => {
  await setMode('свой');
  const set = async (i, value) => page.evaluate((i, v) => {
    const input = document.querySelectorAll('.report-screen .range input[type=date]')[i];
    input.value = v; input.dispatchEvent(new Event('change', { bubbles: true }));
  }, i, value);
  await set(0, '2031-01-01'); await settle();
  await set(1, '2031-02-01'); await settle();
  assert((await screen()).empty === 'За этот период ничего нет.', JSON.stringify(await screen()));
  await page.keyboard.press('Escape'); await settle();
  assert(!(await page.$('.report-screen')), 'Esc не закрыл');
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);

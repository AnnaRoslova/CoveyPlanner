// @seed — нужны демо-данные (tools/seed-demo.ps1)
// Горячие клавиши (docs/specs/batch-ux.md, BK): N, D, ← →, T, H, ?, P, Ctrl+K и «/», Ctrl+Z; при наборе текста и при открытом окне
// клавиши без Ctrl не срабатывают, Alt — не перехватывается. Быстрый поиск находит дело другой недели и открывает его окно.
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
const settle = () => sleep(500);
function assert(c, m) { if (!c) throw new Error(m); }
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-hotkeys-${results.length}.png` }); }
  for (let i = 0; i < 3 && await page.$('.editor, .habits, .done-screen, .report-screen, .help-screen, .search-box'); i++) {
    await page.keyboard.press('Escape'); await settle();
  }
  await page.evaluate(() => document.activeElement?.blur());
}
const api = (method, path, body) => page.evaluate(async (m, p, b) => {
  const r = await fetch(`/api/${p}`, { method: m, headers: { 'Content-Type': 'application/json' }, body: b ? JSON.stringify(b) : undefined });
  if (!r.ok) throw new Error(`${m} ${p}: ${r.status} ${await r.text()}`);
  const text = await r.text();
  return text ? JSON.parse(text) : null;
}, method, path, body);
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day'); await settle(); }
const weekLabel = () => page.$eval('.week-h .wt', e => e.textContent.trim());
const press = async (key, mods = []) => {
  for (const m of mods) await page.keyboard.down(m);
  await page.keyboard.press(key);
  for (const m of mods.reverse()) await page.keyboard.up(m);
  await settle();
};
const iso = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

await load();
const current = await weekLabel();
const far = new Date(); far.setDate(far.getDate() + 7 * 6);
await api('POST', 'tasks', { title: 'Купить шины на зиму', target: { kind: 'day', date: iso(far), index: 0 } });

await check('N — фокус в «Записать мысль…» (и вкладка «Мысли», даже если были «Проекты»)', async () => {
  await page.click('.right-tabs .rt:last-child'); await settle();
  await press('KeyN');
  assert(await page.evaluate(() => document.activeElement?.matches('.someday .quick input')), 'фокус не в поле');
  assert(await page.$('.right-tabs .rt.on') && await page.$eval('.right-tabs .rt.on', b => b.textContent.trim().startsWith('Мысли')), 'не «Мысли»');
});

await check('набор «n» в поле ничего не открывает и печатается', async () => {
  await page.click('.someday .quick input');
  await page.keyboard.type('nht');
  await settle();
  assert(await page.$eval('.someday .quick input', i => i.value) === 'nht', 'буквы не напечатались');
  assert(await weekLabel() === current && !(await page.$('.habits')), 'клавиши сработали в поле');
  await page.keyboard.press('Escape');
});

await check('← → — прошлая / следующая неделя; T — к сегодня', async () => {
  await press('ArrowRight');
  const next = await weekLabel();
  assert(next !== current, 'не перелистнулось вперёд');
  await press('ArrowRight');
  await press('ArrowLeft');
  assert(await weekLabel() === next, 'не вернулось на одну неделю');
  await press('KeyT');
  assert(await weekLabel() === current, `T: ${await weekLabel()}`);
});

await check('D — текущая неделя и «+ дело» в сегодняшнем дне', async () => {
  await press('ArrowRight');
  await press('KeyD');
  await sleep(300);
  assert(await weekLabel() === current, 'не текущая неделя');
  assert(await page.evaluate(() => document.activeElement?.closest('.day.today') && document.activeElement.tagName === 'INPUT'), 'нет поля в сегодняшнем дне');
  await page.keyboard.press('Escape'); await settle();
});

await check('H — «Итоги»; ? — «Как это устроено»; P — «Мысли ⇄ Проекты»', async () => {
  await press('KeyH');
  assert(await page.$('.totals-tabs'), 'Итоги не открылись');
  await press('Escape');
  await press('Slash', ['Shift']);
  assert(await page.$('.help-screen'), '«Как это устроено» не открылось');
  await press('Escape');
  await press('KeyP');
  assert(await page.$eval('.right-tabs .rt.on', b => b.textContent.trim().startsWith('Проекты')), 'P: не «Проекты»');
  await press('KeyP');
  assert(await page.$eval('.right-tabs .rt.on', b => b.textContent.trim().startsWith('Мысли')), 'P: не «Мысли»');
});

await check('при открытом окне клавиши без Ctrl не срабатывают; Alt+N не перехватывается', async () => {
  await press('KeyH');
  await press('ArrowRight');
  assert(await weekLabel() === current, 'неделя перелистнулась под окном');
  await press('Escape');
  await press('KeyN', ['Alt']);
  assert(!(await page.evaluate(() => document.activeElement?.matches('.someday .quick input'))), 'Alt+N перехвачен');
});

await check('Ctrl+K — быстрый поиск: дело другой недели, Enter — его неделя и окно', async () => {
  await press('KeyK', ['Control']);
  assert(await page.$('.search-box'), 'поиск не открылся');
  await page.keyboard.type('шины');
  await page.waitForSelector('.search-box .hit', { timeout: 3000 });
  const group = await page.$eval('.search-box .group', g => g.textContent.trim());
  const hit = await page.$eval('.search-box .hit.on', h => h.textContent.replace(/\s+/g, ' ').trim());
  assert(group === 'Дела' && hit.startsWith('Купить шины на зиму'), `${group} / ${hit}`);
  await (await page.$('.search-box')).screenshot({ path: `${SHOTS}/search.png` });
  await press('Enter');
  assert(await weekLabel() !== current, 'неделя не сменилась');
  assert(await page.$eval('.editor input, .editor textarea', i => i.value) === 'Купить шины на зиму', 'не то окно');
});

await check('«/» — тоже поиск; ↑↓ выбирают; Esc закрывает', async () => {
  await press('KeyT');
  await press('Slash');
  assert(await page.$('.search-box'), '«/» не открыл поиск');
  await page.keyboard.type('а');
  await page.waitForSelector('.search-box .hit', { timeout: 3000 });
  const hits = await page.$$('.search-box .hit');
  if (hits.length > 1) {
    await press('ArrowDown');
    assert(await page.$$eval('.search-box .hit', hs => hs.findIndex(h => h.classList.contains('on'))) === 1, '↓ не выбрал вторую');
  }
  await press('Escape');
  assert(!(await page.$('.search-box')), 'Esc не закрыл');
});

await check('Ctrl+Z — «Вернуть» на плашке (вне поля ввода)', async () => {
  const title = await page.$eval('.day .item .title', t => t.textContent.trim());
  await page.evaluate(t => {
    const item = [...document.querySelectorAll('.day .item')].find(i => i.querySelector('.title').textContent.trim() === t);
    item.querySelector('.trash-btn').click();
  }, title);
  await settle();
  assert((await page.$eval('.toast', t => t.textContent)).includes('в корзине'), 'нет плашки');
  await press('KeyZ', ['Control']);
  assert(await page.evaluate(t => [...document.querySelectorAll('.day .item .title')].some(x => x.textContent.trim() === t), title), 'дело не вернулось');
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);

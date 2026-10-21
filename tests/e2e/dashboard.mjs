// @seed — нужны демо-данные (tools/seed-demo.ps1)
// Дашборд «Отчёта» (docs/specs/batch-ux.md, BD): «Список ⇄ Дашборд» запоминается; тепловая карта — недели × дни, числа как в API,
// выбор одной привычки, подсказка ячейки «Чт 22 окт · Бег»; колесо пилы, баланс ролей, II квадрант по неделям, «давно лежат»;
// у графиков есть текстовые версии; Esc закрывает.
import puppeteer from 'puppeteer-core';
import { openTotals } from './lib/nav.mjs';
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
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-dashboard-${results.length}.png` }); }
}
const api = path => page.evaluate(async p => {
  const r = await fetch(`/api/${p}`);
  if (!r.ok) throw new Error(`${p}: ${r.status}`);
  return r.json();
}, path);
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day'); await settle(); }

const iso = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
const now = new Date();
const monthFrom = iso(new Date(now.getFullYear(), now.getMonth(), 1));
const monthTo = iso(new Date(now.getFullYear(), now.getMonth() + 1, 0));
const visible = d => d.habits.filter(h => !h.isArchived || h.clips.length > 0);
const stat = label => page.evaluate(l => [...document.querySelectorAll('.heat-totals div')]
  .find(x => x.querySelector('dt').textContent.trim() === l)?.querySelector('dd').textContent.trim(), label);

await makeHabitHistory(BASE); // скрепки за полгода — у демо-данных их нет
await load();
await openTotals(page, 'Отчёт');
const data = await api(`dashboard?from=${monthFrom}&to=${monthTo}`);

await check('BD1: «Список ⇄ Дашборд» — по умолчанию список; «Дашборд» показывает графики', async () => {
  assert(await page.$('.report-screen .view-switch .vs.on') && await page.$eval('.view-switch .vs.on', b => b.textContent.trim()) === 'Список', 'не список');
  await page.evaluate(() => [...document.querySelectorAll('.view-switch .vs')].find(b => b.textContent.trim() === 'Дашборд').click());
  await page.waitForSelector('.dash'); await settle();
  assert(await page.$eval('.view-switch .vs.on', b => b.getAttribute('aria-pressed')) === 'true', 'aria-pressed');
  await (await page.$('.report-screen')).screenshot({ path: `${SHOTS}/dashboard.png` });
});

await check('BD2: тепловая карта — недели × 7 дней; «всего скрепок» и серии как в API', async () => {
  const cells = await page.$$eval('.heat .cell', cs => cs.length);
  assert(cells === data.weeks.length * 7, `ячеек ${cells}, недель ${data.weeks.length}`);
  const habits = visible(data);
  assert(habits.length > 0 && habits.some(h => h.clips.length > 0), 'нет скрепок');
  const total = habits.reduce((s, h) => s + h.clips.length, 0);
  assert(await stat('всего скрепок') === String(total), `всего: ${await stat('всего скрепок')} ≠ ${total}`);
  assert(await stat('серия сейчас') === `🔥 ${Math.max(...habits.map(h => h.weekStreak))}`, `сейчас: ${await stat('серия сейчас')}`);
  assert(await stat('лучшая серия') === String(Math.max(...habits.map(h => h.bestWeekStreak))), `лучшая: ${await stat('лучшая серия')}`);
  const filled = await page.$$eval('.heat .cell:not(.l0)', cs => cs.length);
  assert(filled === new Set(habits.flatMap(h => h.clips)).size, `закрашено ${filled}`);
});

await check('BD2: подсказка ячейки — «Чт 22 окт · Бег»', async () => {
  const titles = await page.$$eval('.heat .cell:not(.l0)', cs => cs.map(c => c.title));
  assert(titles.length > 0 && titles.every(t => /^(Пн|Вт|Ср|Чт|Пт|Сб|Вс) [0-9]+ [а-я]+\.? · /.test(t)), JSON.stringify(titles.slice(0, 3)));
});

await check('BD2: одна привычка — числа только её', async () => {
  const habits = visible(data);
  if (habits.length < 2) return;
  const one = habits.find(h => h.clips.length > 0) ?? habits[0];
  await page.click('.heat-block .select-field'); await sleep(300);
  await page.evaluate(t => [...document.querySelectorAll('.options .option')].find(o => o.textContent.trim() === t).click(), one.title);
  await settle();
  assert(await stat('всего скрепок') === String(one.clips.length), `всего: ${await stat('всего скрепок')} ≠ ${one.clips.length}`);
  assert(await stat('лучшая серия') === String(one.bestWeekStreak), 'лучшая');
  await page.click('.heat-block .select-field'); await sleep(300);
  await page.evaluate(() => [...document.querySelectorAll('.options .option')].find(o => o.textContent.trim() === 'все привычки').click());
  await settle();
});

await check('BD3: колесо пилы — 4 лепестка с подписями «Тело · N», нулевой — серый', async () => {
  const petals = await page.$$eval('.wheel .petal', ps => ps.map(p => ({ title: p.querySelector('title').textContent, zero: p.classList.contains('zero') })));
  assert(petals.length === data.saw.length && data.saw.length === 4, `лепестков ${petals.length}`);
  data.saw.forEach((s, i) => assert(petals[i].title === `${s.name}: ${s.count}` && petals[i].zero === (s.count === 0), JSON.stringify(petals[i])));
  const labels = await page.$$eval('.wheel .petal-t', ts => ts.map(t => t.textContent));
  assert(JSON.stringify(labels) === JSON.stringify(data.saw.map(s => `${s.name} · ${s.count}`)), JSON.stringify(labels));
});

await check('BD4: баланс ролей — полосы с числами как в API, пила отдельной группой сверху', async () => {
  const rows = await page.$$eval('.roles-block .bar-row', rs => rs.map(r => `${r.querySelector('.bar-name').textContent}:${r.querySelector('.bar-n').textContent}`));
  assert(JSON.stringify(rows) === JSON.stringify(data.roles.map(r => `${r.name}:${r.count}`)), JSON.stringify(rows));
  if (data.roles.some(r => r.isSaw)) {
    assert(await page.$eval('.roles-block .group-h', g => g.textContent.trim()) === 'Пила', 'первая группа не «Пила»');
  }
});

await check('BD5: II квадрант — столбик на неделю, подсказка «II — N из M»', async () => {
  if (data.quadrants.every(q => q.marked === 0)) {
    assert(await page.$('.q2-block .empty'), 'нет пустого текста');
    return;
  }
  const cols = await page.$$eval('.q2-col', cs => cs.map(c => c.title));
  assert(cols.length === data.weeks.length, `столбиков ${cols.length}`);
  data.quadrants.forEach((q, i) => assert(cols[i].includes(`II — ${q.second} из ${q.marked}`), cols[i]));
});

await check('BD6: «Давно лежат» — счёт и самые старые; клик — окно мысли', async () => {
  const text = await page.$eval('.stale-block', b => b.textContent);
  assert(text.includes(`старше ${data.stale.months} мес`), text);
  if (data.stale.count === 0) return;
  const items = await page.$$eval('.stale-t .stale-title', ts => ts.map(t => t.textContent));
  assert(JSON.stringify(items) === JSON.stringify(data.stale.oldest.map(t => t.title)), JSON.stringify(items));
  await page.click('.stale-t'); await settle();
  assert(await page.$('.editor'), 'окно мысли не открылось');
  await page.keyboard.press('Escape'); await settle();
  await openTotals(page, 'Отчёт');
});

await check('BD7: у графиков есть текстовая версия (подписи для диктора, таблица)', async () => {
  assert(await page.$eval('.heat', h => h.getAttribute('role') === 'img' && h.getAttribute('aria-label').startsWith('Скрепок за период')), 'карта');
  assert(await page.$('.heat-block table.sr-only'), 'нет таблицы');
  assert(await page.$eval('.wheel', w => w.getAttribute('aria-label').includes(':')), 'колесо');
});

await check('период «год» — 52+ недели помещаются без горизонтальной прокрутки экрана', async () => {
  await page.click('.period-field .select-field'); await sleep(300);
  await page.evaluate(() => [...document.querySelectorAll('.options .option')].find(o => o.textContent.trim() === 'год').click());
  await page.waitForFunction(() => document.querySelectorAll('.heat .cell').length >= 52 * 7, { timeout: 5000 }); await settle();
  const r = await page.$eval('.report-screen', e => ({ sw: e.scrollWidth, cw: e.clientWidth }));
  assert(r.sw <= r.cw + 1, JSON.stringify(r));
  await (await page.$('.report-screen')).screenshot({ path: `${SHOTS}/dashboard-year.png` });
});

await check('BD1: вид запоминается — после перезагрузки «Отчёт» открывается дашбордом; Esc закрывает', async () => {
  await load();
  await openTotals(page, 'Отчёт');
  await page.waitForSelector('.dash');
  await page.keyboard.press('Escape'); await settle();
  assert(!(await page.$('.report-screen')), 'экран не закрылся');
  await openTotals(page, 'Отчёт');
  await page.evaluate(() => [...document.querySelectorAll('.view-switch .vs')].find(b => b.textContent.trim() === 'Список').click());
  await settle();
  assert(!(await page.$('.dash')) && await page.$('.report-screen .srow, .report-screen .empty'), 'список не вернулся');
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);

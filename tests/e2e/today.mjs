// «Сегодня» обновляется само: в полночь и когда окно снова активно.
// Часы страницы подменяются (клиент .NET берёт время из Date.now()): дата — своя, не от настоящей
// (воскресенье 11 октября 2026), и пока страница загружается, часы стоят — пускаем их, когда экран готов.
// Поэтому «полночь» наступает через несколько секунд после загрузки, сколько бы она ни длилась.
import puppeteer from 'puppeteer-core';

const BASE = process.env.PLANNER_URL ?? 'http://localhost:5071/';
const SHOTS = process.argv[2] ?? '.';
const results = [];
const consoleErrors = [];
const browser = await puppeteer.launch({
  executablePath: process.env.PLANNER_BROWSER ?? 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  headless: true, defaultViewport: { width: 1600, height: 900 },
});
const sleep = ms => new Promise(r => setTimeout(r, ms));
function assert(c, m) { if (!c) throw new Error(m); }
let page;
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page?.screenshot({ path: `${SHOTS}/fail-today-${results.length}.png` }); }
}

const iso = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
const addDays = (d, n) => { const r = new Date(d); r.setDate(r.getDate() + n); return r; };

// Открыть приложение так, будто сейчас fakeNow (локальное время). До готовности экрана часы стоят на fakeNow,
// потом идут. window.__clock.shift — перевести часы (например, на сутки вперёд).
async function openAt(fakeNow) {
  await page?.close();
  page = await browser.newPage();
  page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text()); });
  page.on('pageerror', e => consoleErrors.push(e.message));
  await page.evaluateOnNewDocument(at => {
    const realNow = Date.now.bind(Date);
    const clock = window.__clock = { at, since: null, shift: 0 };
    window.__runClock = () => { clock.since ??= realNow(); };
    Date.now = () => clock.at + clock.shift + (clock.since === null ? 0 : realNow() - clock.since);
  }, fakeNow.getTime());
  await page.goto(BASE, { waitUntil: 'networkidle0' });
  await page.waitForSelector('.day');
  await sleep(300);
  await page.evaluate(() => window.__runClock());
}

const todayKey = () => page.$eval('.day.today', d => d.dataset.key).catch(() => null);
const weekTitle = () => page.$eval('.week-h .wt', e => e.textContent.trim());
const firstDayKey = () => page.$eval('.day', d => d.dataset.key);
const hasTodayButton = async () => !!(await page.$('.week-h .today-b'));

const monday = new Date(2026, 9, 5, 12); // понедельник 5 октября 2026 — неделя сценария, от настоящей даты не зависит
const sunday = addDays(monday, 6);
const sundayNight = new Date(sunday.getFullYear(), sunday.getMonth(), sunday.getDate(), 23, 59, 56);
const nextMonday = addDays(monday, 7);

await check('в полночь на воскресенье → понедельник неделя листается на новую', async () => {
  await openAt(sundayNight);
  assert(await todayKey() === iso(sunday), `сегодня: ${await todayKey()}`);
  assert(await firstDayKey() === iso(monday), `неделя: ${await firstDayKey()}`);
  await sleep(8000);
  assert(await firstDayKey() === iso(nextMonday), `неделя не сменилась: ${await firstDayKey()}`);
  assert(await todayKey() === iso(nextMonday), `сегодня: ${await todayKey()}`);
  assert(!(await hasTodayButton()), 'кнопка «Сегодня» на текущей неделе');
});

await check('смотрю прошлую неделю — в полночь она остаётся на экране', async () => {
  await openAt(sundayNight);
  await page.click('.week-h .nav[title^="Предыдущая неделя"]'); await sleep(600);
  const shown = await firstDayKey();
  assert(shown === iso(addDays(monday, -7)), `неделя: ${shown}`);
  await sleep(8000);
  assert(await firstDayKey() === shown, `неделя сменилась: ${await firstDayKey()}`);
  assert(await hasTodayButton(), 'нет кнопки «Сегодня»');
});

await check('окно снова активно на следующий день — «сегодня» переезжает', async () => {
  const wednesdayNoon = new Date(monday.getFullYear(), monday.getMonth(), monday.getDate() + 2, 12, 0, 0);
  await openAt(wednesdayNoon);
  assert(await todayKey() === iso(addDays(monday, 2)), `сегодня: ${await todayKey()}`);
  await page.evaluate(() => { window.__clock.shift += 24 * 3600 * 1000; window.dispatchEvent(new Event('focus')); });
  await sleep(600);
  assert(await todayKey() === iso(addDays(monday, 3)), `сегодня после фокуса: ${await todayKey()}`);
  assert(await firstDayKey() === iso(monday), 'неделя сменилась');
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));

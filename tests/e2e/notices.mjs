// Место уведомлений (docs/specs/polish-1.md, P1–P6): плашка и «Нет связи» — в свободной части шапки недели, между подписью периода
// и «📈 ⚙»; не пересекают дни, записи и кнопки шапки (1600 и 1280 px, левая панель свёрнута и раскрыта, «Сегодня» в шапке);
// пока висит плашка, ✕ и 🗑 у первой записи каждого дня нажимаются всей площадью; при открытом недельном обзоре — внизу по центру.
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
page.on('pageerror', e => consoleErrors.push(e.message));
const sleep = ms => new Promise(r => setTimeout(r, ms));
const settle = () => sleep(450);
function assert(c, m) { if (!c) throw new Error(m); }
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-notices-${results.length}.png` }); }
  if (await page.$('.editor')) { await page.keyboard.press('Escape'); await settle(); }
}
const api = (method, path, body) => page.evaluate(async (m, p, b) => {
  const r = await fetch('/api/' + p, { method: m, headers: { 'Content-Type': 'application/json', 'X-Planner': '1' }, body: b ? JSON.stringify(b) : undefined });
  if (!r.ok) throw new Error(`${m} ${p}: ${r.status} ${await r.text()}`);
  return r.status === 204 ? null : r.json();
}, method, path, body);
let offline = false;
await page.setRequestInterception(true);
page.on('request', r => (offline && new URL(r.url()).pathname.startsWith('/api/') ? r.abort('internetdisconnected') : r.continue()));
page.on('dialog', d => d.accept());
async function load() {
  await page.goto(BASE, { waitUntil: 'networkidle0' });
  await page.evaluate(() => localStorage.setItem('planner.right-mode', 'feed'));
  await page.goto(BASE, { waitUntil: 'networkidle0' });
  await page.waitForSelector('.day'); await settle();
}
const iso = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
const now = new Date();
const monday = new Date(now.getFullYear(), now.getMonth(), now.getDate() - ((now.getDay() + 6) % 7));
const day = n => iso(new Date(monday.getFullYear(), monday.getMonth(), monday.getDate() + n));

/** Плашка по чекбоксу мысли: «„…“ выполнено · в корзину · вернуть» — самая длинная из частых. */
let fired = 0;
async function fire() {
  const ck = await page.evaluateHandle(() => document.querySelector('.someday .cat-b > .item .ck-hit'));
  await ck.asElement().click();
  await page.waitForSelector('.toast', { timeout: 3000 });
  await sleep(300);
  fired++;
}

/** Геометрия: где плашки, что они задевают. */
const geometry = sel => page.evaluate(sel => {
  const n = document.querySelector(sel);
  if (!n) return null;
  const r = n.getBoundingClientRect();
  const hit = b => r.left < b.right && b.left < r.right && r.top < b.bottom && b.top < r.bottom;
  const head = document.querySelector('.week-h').getBoundingClientRect();
  const names = els => [...els].filter(e => hit(e.getBoundingClientRect())).map(e => e.className || e.tagName);
  return {
    rect: [r.left | 0, r.top | 0, r.width | 0, r.height | 0],
    inHead: r.top >= head.top - 0.5 && r.bottom <= head.bottom + 0.5,
    days: names(document.querySelectorAll('.day')), items: names(document.querySelectorAll('.item')),
    buttons: names(document.querySelectorAll('.week-h button, .flip-zone')),
    free: n.closest('.notices').classList.contains('free'),
    actionsVisible: [...n.querySelectorAll('button')].every(b => { const a = b.getBoundingClientRect(); return a.width > 10 && a.right <= r.right + 0.5; }),
  };
}, sel);
const inPlace = (g, what) => {
  assert(g, `${what}: нет`);
  assert(!g.free && g.inHead, `${what}: не в шапке недели ${JSON.stringify(g)}`);
  assert(!g.days.length && !g.items.length && !g.buttons.length, `${what}: задевает ${JSON.stringify(g)}`);
  assert(g.actionsVisible, `${what}: действия не видны ${JSON.stringify(g)}`);
};

/** У первой записи дня: навести, плашка ✕/🗑 — каждая кнопка сверху во всех точках (середина и четыре точки к краям; кнопки круглые — углы не в счёт). */
async function toolsClear(i) {
  const row = await page.evaluateHandle(i => document.querySelectorAll('.day')[i].querySelector(':scope > .item'), i);
  const b = await row.asElement().boundingBox();
  await page.mouse.move(b.x + b.width / 3, b.y + b.height / 2); await sleep(250);
  return page.evaluate(i => {
    const item = document.querySelectorAll('.day')[i].querySelector(':scope > .item');
    const misses = [];
    for (const btn of item.querySelectorAll('.row-tools button')) {
      const r = btn.getBoundingClientRect();
      const cx = r.left + r.width / 2, cy = r.top + r.height / 2, dx = r.width * 0.32, dy = r.height * 0.32;
      for (const [x, y] of [[cx, cy], [cx - dx, cy - dy], [cx + dx, cy - dy], [cx - dx, cy + dy], [cx + dx, cy + dy], [cx, r.top + 1.5], [cx, r.bottom - 1.5]]) {
        const el = document.elementFromPoint(x, y);
        if (el?.closest('button') !== btn) misses.push(`${btn.className}@${x | 0},${y | 0}:${el?.className}`);
      }
    }
    return { count: item.querySelectorAll('.row-tools button').length, misses };
  }, i);
}

async function allDaysClear(what) {
  for (let i = 0; i < 7; i++) {
    if (!(await page.$('.toast'))) await fire(); // плашка живёт 6 с — держать её на экране
    const t = await toolsClear(i);
    assert(t.count >= 2 && !t.misses.length, `${what}, день ${i}: ${JSON.stringify(t)}`);
  }
  assert(await page.$('.toast'), `${what}: плашка ушла раньше проверки`);
}

// Данные: в каждом дне — длинная первая запись (две строки) и ещё одна; мысли для плашек
await load();
for (let i = 0; i < 7; i++) {
  await api('POST', 'tasks', { title: 'Позвонить в управляющую компанию насчёт протечки', target: { kind: 'day', date: day(i), index: 0 } });
  await api('POST', 'tasks', { title: `Дело ${i}`, target: { kind: 'day', date: day(i), index: 99 } });
}
const cat = await api('POST', 'categories', { name: 'Идеи' });
for (let i = 0; i < 40; i++) await api('POST', 'tasks', { title: `Мысль номер ${i} про очень важное`, target: { kind: 'someday', categoryId: cat.id, index: 0 } });
await load();

await check('P1: 1600 px — плашка в шапке недели между подписью и «📈 ⚙»; не задевает дни, записи и кнопки; действия видны', async () => {
  await fire();
  const g = await geometry('.toast');
  inPlace(g, 'плашка');
  const head = await page.evaluate(() => ({ wt: document.querySelector('.wt').getBoundingClientRect().right, icon: document.querySelector('.week-h .icon-b').getBoundingClientRect().left }));
  assert(g.rect[0] >= head.wt && g.rect[0] + g.rect[2] <= head.icon, `не между подписью и значками: ${JSON.stringify({ g, head })}`);
  await page.screenshot({ path: `${SHOTS}/notices-1600.png`, clip: { x: 0, y: 0, width: 1600, height: 200 } });
});

await check('P1: 1600 px — пока висит плашка, ✕ и 🗑 у первой записи каждого дня нажимаются всей площадью', async () => {
  await allDaysClear('1600');
});

await check('P1, P4: не текущая неделя («Сегодня» в шапке) — плашка правее «Сегодня», записи не задевает', async () => {
  await page.click('.nav[title^="Следующая неделя"]'); await settle();
  await fire();
  const g = await geometry('.toast');
  inPlace(g, 'плашка');
  const today = await page.evaluate(() => document.querySelector('.today-b').getBoundingClientRect().right);
  assert(g.rect[0] >= today, `заходит на «Сегодня»: ${JSON.stringify(g)} ${today}`);
  await page.click('.nav[title^="Предыдущая неделя"]'); await settle();
});

await check('P1, P4: 1280 px, левая панель свёрнута и раскрыта — плашка в шапке, ✕ и 🗑 нажимаются', async () => {
  await page.setViewport({ width: 1280, height: 900 }); await settle();
  await fire();
  inPlace(await geometry('.toast'), '1280, свёрнута');
  await allDaysClear('1280, свёрнута');
  await page.screenshot({ path: `${SHOTS}/notices-1280.png`, clip: { x: 0, y: 0, width: 1280, height: 200 } });
  const strip = await page.$('.panel.strip[title="Показать: Роли и цели"]');
  if (strip) { await strip.click(); await settle(); }
  await page.mouse.move(640, 600);
  await fire();
  const g = await geometry('.toast');
  assert(g && !g.days.length && !g.items.length && !g.buttons.length, `1280, раскрыта: ${JSON.stringify(g)}`);
  await page.screenshot({ path: `${SHOTS}/notices-1280-left.png`, clip: { x: 0, y: 0, width: 1280, height: 200 } });
  await page.setViewport({ width: 1600, height: 900 }); await load();
});

await check('P1, P6: «Нет связи» — там же, в шапке недели; ✕ и 🗑 у первых записей нажимаются', async () => {
  await page.waitForFunction(() => !document.querySelector('.toast'), { timeout: 10000 });
  offline = true;
  await page.click('.someday .quick input'); await page.keyboard.type('Без связи'); await page.keyboard.press('Enter');
  await page.waitForSelector('.offline', { timeout: 5000 });
  await page.waitForFunction(() => !document.querySelector('.toast'), { timeout: 10000 }).catch(() => {});
  inPlace(await geometry('.offline'), '«Нет связи»');
  for (let i = 0; i < 7; i++) {
    const t = await toolsClear(i);
    assert(!t.misses.length, `день ${i}: ${JSON.stringify(t)}`);
  }
  await page.screenshot({ path: `${SHOTS}/notices-offline.png`, clip: { x: 0, y: 0, width: 1600, height: 200 } });
  offline = false;
  await page.click('.offline .action');
  await page.waitForFunction(() => !document.querySelector('.offline'), { timeout: 8000 });
  await settle();
});

await check('P5: недельный обзор открыт — плашка («в корзине · Вернуть») внизу по центру, не в шапке под обзором', async () => {
  await load();
  await page.keyboard.press('KeyW');
  await page.waitForSelector('.review', { timeout: 5000 }); await settle();
  await page.evaluate(() => [...document.querySelectorAll('.review button')].find(b => b.textContent.trim() === 'в корзину').click());
  await page.waitForSelector('.toast', { timeout: 3000 }); await sleep(200);
  const g = await geometry('.toast');
  assert(g?.free, `не внизу: ${JSON.stringify(g)}`);
  assert(g.rect[1] + g.rect[3] > 800, `не у нижнего края: ${JSON.stringify(g)}`);
  await page.keyboard.press('Escape'); await settle();
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
results.forEach(r => console.log(r));
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);

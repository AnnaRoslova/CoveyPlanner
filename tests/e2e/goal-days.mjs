// Дни занятий цели — не под названием в левой панели (docs/specs/polish-3.md, D; batch-3.md, M2): во всех темах, в «Затачивании пилы»
// и в обычных ролях, у разовой цели, постоянной, привычки и цели с днями повтора, в развёрнутых и свёрнутых ролях, при наведении.
// Дни — только в подсказке строки и в окне цели; невидимый текст для диктора (.sr-only) не в счёт.
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
function assert(c, m) { if (!c) throw new Error(m); }
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-goal-days-${results.length}.png` }); }
}
const api = (method, path, body) => page.evaluate(async (m, p, b) => {
  const r = await fetch(`/api/${p}`, { method: m, headers: { 'Content-Type': 'application/json', 'X-Planner': '1' }, body: b ? JSON.stringify(b) : undefined });
  if (!r.ok) throw new Error(`${m} ${p}: ${r.status} ${await r.text()}`);
  const text = await r.text();
  return text ? JSON.parse(text) : null;
}, method, path, body);
const iso = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day'); await sleep(500); }

const THEMES = ['lotus', 'peony', 'platinum', 'obsidian', 'earth', 'water', 'void']; // темы, которые есть в выборе (ThemeState)
const today = new Date(); today.setHours(0, 0, 0, 0);
const monday = new Date(today); monday.setDate(today.getDate() - ((today.getDay() + 6) % 7));
const day = n => { const d = new Date(monday); d.setDate(monday.getDate() + n); return iso(d); };

await load();
const roles = (await api('GET', 'board')).roles;
const body = roles.find(r => r.sawDimension === 'Body');
const work = roles.find(r => r.name === 'Работа');
// Привычка с днями повтора (Пн, Ср, Пт) — занятия встают сами; одно отмечено
const run = await api('POST', 'goals', { roleId: body.id, weekStart: iso(monday), title: 'Бег', isRecurring: true });
await api('PUT', `goals/${run.id}`, { title: 'Бег', roleIds: [body.id], timesPerWeek: 3, isRecurring: true, days: 1 | 4 | 16, isHabit: true });
// Постоянная цель пилы без дней — занятия разложены руками
const stretch = await api('POST', 'goals', { roleId: body.id, weekStart: iso(monday), title: 'Растяжка', isRecurring: true });
await api('PUT', `goals/${stretch.id}`, { title: 'Растяжка', roleIds: [body.id], timesPerWeek: 2 });
await api('POST', `goals/${stretch.id}/schedule`, { kind: 'day', date: day(1), index: 0 });
await api('POST', `goals/${stretch.id}/schedule`, { kind: 'day', date: day(3), index: 0 });
// Разовая цель обычной роли
const review = await api('POST', 'goals', { roleId: work.id, weekStart: iso(monday), title: 'Код-ревью', isRecurring: false });
await api('PUT', `goals/${review.id}`, { title: 'Код-ревью', roleIds: [work.id], timesPerWeek: 2 });
const s = await api('POST', `goals/${review.id}/schedule`, { kind: 'day', date: day(4), index: 0 });
await api('PUT', `tasks/${s.task.id}/done`, { isDone: true });

/** Видимые «Пн…Вс» в панели «Роли и цели» (кроме текста для диктора). */
const visibleDays = () => page.evaluate(() => {
  const out = [];
  const walker = document.createTreeWalker(document.querySelector('.roles-panel'), NodeFilter.SHOW_TEXT);
  while (walker.nextNode()) {
    const node = walker.currentNode;
    if (!/(^|[^А-Яа-яЁё])(Пн|Вт|Ср|Чт|Пт|Сб|Вс)([^А-Яа-яЁё]|$)/.test(node.textContent)) continue;
    if (node.parentElement.closest('.sr-only')) continue;
    const range = document.createRange(); range.selectNodeContents(node);
    const box = range.getBoundingClientRect();
    if (box.width > 2 && box.height > 2) out.push(`${node.parentElement.className}: ${node.textContent.trim()}`);
  }
  return out;
});
const goalRow = title => page.evaluateHandle(t => [...document.querySelectorAll('.roles-panel .role .item')]
  .find(i => i.querySelector('.title')?.textContent.trim().startsWith(t)), title).then(h => h.asElement());

await load();
await check('цели на месте, дни занятий — в подсказке строки', async () => {
  for (const title of ['Бег', 'Растяжка', 'Код-ревью']) {
    const hint = await (await goalRow(title)).evaluate(i => i.title);
    assert(/ · (Пн|Вт|Ср|Чт|Пт|Сб|Вс)/.test(hint), `${title}: в подсказке нет дней — «${hint}»`);
  }
});

for (const theme of THEMES) {
  await page.evaluate(k => localStorage.setItem('planner.theme', k), theme);
  await load();
  await check(`${theme}: под названиями целей дней не видно (развёрнуто и при наведении)`, async () => {
    assert(await page.evaluate(() => document.documentElement.dataset.theme) === theme, 'тема не включилась');
    let found = await visibleDays();
    assert(found.length === 0, `видны дни: ${found.join(' | ')}`);
    for (const title of ['Бег', 'Растяжка', 'Код-ревью']) {
      const box = await (await goalRow(title)).boundingBox();
      await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2); await sleep(250);
      found = await visibleDays();
      assert(found.length === 0, `${title} при наведении: ${found.join(' | ')}`);
    }
    await page.mouse.move(800, 880);
  });
}

await check('свёрнутые роли и разделы — дней тоже нет', async () => {
  await page.evaluate(() => localStorage.setItem('planner.theme', 'lotus'));
  await load();
  await page.evaluate(() => [...document.querySelectorAll('.roles-panel .role .role-h .name')].find(n => n.textContent.trim() === 'Тело').closest('.role-toggle').click());
  await sleep(400);
  assert(await page.evaluate(() => [...document.querySelectorAll('.roles-panel .role')].some(r => r.classList.contains('folded'))), 'роль «Тело» не свернулась');
  let found = await visibleDays();
  assert(found.length === 0, `свёрнутая роль: ${found.join(' | ')}`);
  await page.evaluate(() => [...document.querySelectorAll('.roles-panel .role .role-h .name')].find(n => n.textContent.trim() === 'Тело').closest('.role-toggle').click());
  await sleep(400);
  found = await visibleDays();
  assert(found.length === 0, `снова развёрнута: ${found.join(' | ')}`);
});

await check('нет ошибок в консоли', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | '));
});

console.log(results.join('\n'));
await browser.close();
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);

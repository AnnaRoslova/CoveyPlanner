// Пропуски (docs/specs/skips.md, A1–A3, V1–V2, H2, S2, F1, T3): ✕ и 🗑 при наведении — плашкой над углом записи, щелчок по любой
// точке названия — окно дела (F1); ✕ → окошко → «не нужно»: «—», бледно, не зачёркнуто,
// внизу дня; чип «болезнь» — сразу «не сделано» с причиной; та же ✕ — отменить; клик по чекбоксу пропущенного — сделано;
// шаг «не сделано» уходит из дня в список проекта; цель: «не нужно» уменьшает норму, все «не нужно» — «—» вместо процента.
import puppeteer from 'puppeteer-core';
import { openTotals } from './lib/nav.mjs';

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
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-skips-${results.length}.png` }); }
  if (await page.$('.editor')) { await page.keyboard.press('Escape'); await settle(); }
}
const api = (method, path, body) => page.evaluate(async (m, p, b) => {
  const r = await fetch('/api/' + p, { method: m, headers: { 'Content-Type': 'application/json', 'X-Planner': '1' }, body: b ? JSON.stringify(b) : undefined });
  if (!r.ok) throw new Error(`${m} ${p}: ${r.status} ${await r.text()}`);
  return r.status === 204 ? null : r.json();
}, method, path, body);
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day'); await settle(); }
const iso = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
const now = new Date();
const monday = new Date(now.getFullYear(), now.getMonth(), now.getDate() - ((now.getDay() + 6) % 7));
const day = n => iso(new Date(monday.getFullYear(), monday.getMonth(), monday.getDate() + n));
const item = title => page.evaluateHandle(t => [...document.querySelectorAll('.day .item')]
  .find(i => i.querySelector('.title')?.textContent.trim() === t), title).then(h => h.asElement());
const hover = async title => { const b = await (await item(title)).boundingBox(); await page.mouse.move(b.x + b.width / 2, b.y + b.height / 2); await sleep(300); };
const info = title => page.evaluate(t => {
  const i = [...document.querySelectorAll('.day .item')].find(x => x.querySelector('.title')?.textContent.trim() === t);
  if (!i) return null;
  const day = i.closest('.day');
  const items = [...day.querySelectorAll('.item')];
  const tx = getComputedStyle(i.querySelector('.tx'));
  return {
    cls: i.className, mark: i.querySelector('.skip-mark')?.textContent.trim() ?? null, title: i.getAttribute('title'),
    strike: tx.textDecorationLine, index: items.indexOf(i), count: items.length,
  };
}, title);

// Данные: три дела в понедельник, проект с шагом в среду, цель «Бег» 2 раза с занятиями во вт и чт
await load();
for (const t of ['Стоматолог', 'Купить хлеб', 'Позвонить маме']) await api('POST', 'tasks', { title: t, target: { kind: 'day', date: day(0), index: 99 } });
const project = await api('POST', 'tasks', { title: 'Ремонт', target: { kind: 'someday', categoryId: null, index: 0 }, isProject: true });
const step = await api('POST', `tasks/${project.id}/steps`, { title: 'Купить плитку' });
await api('POST', `tasks/${step.id}/move`, { kind: 'day', date: day(2), index: 0 });
const board = await api('GET', 'board');
const body = board.roles.find(r => r.name === 'Тело').id;
const goal = await api('POST', 'goals', { roleId: body, weekStart: day(0), title: 'Бег', isRecurring: false });
await api('PUT', `goals/${goal.id}`, { title: 'Бег', roleIds: [body], timesPerWeek: 2 });
const s1 = (await api('POST', `goals/${goal.id}/schedule`, { kind: 'day', date: day(1), index: 0 })).task;
const s2 = (await api('POST', `goals/${goal.id}/schedule`, { kind: 'day', date: day(3), index: 0 })).task;
// Для F1: длинное дело (две строки) и проект с шагом в пятницу — запись с «⌄»
const LONG = 'Позвонить в управляющую компанию насчёт протечки на кухне и договориться о мастере';
await api('POST', 'tasks', { title: LONG, target: { kind: 'day', date: day(4), index: 0 } });
const kitchen = await api('POST', 'tasks', { title: 'Кухня', target: { kind: 'someday', categoryId: null, index: 0 }, isProject: true });
await api('POST', `tasks/${kitchen.id}/steps`, { title: 'Выбрать плитку' });
await api('POST', `tasks/${kitchen.id}/move`, { kind: 'day', date: day(4), index: 99 });
await load();

await check('плашка ✕/🗑 — над правым верхним углом, не на тексте; щелчок по любой точке названия при наведении — окно дела (F1)', async () => {
  for (const title of ['Стоматолог', LONG, 'Кухня', 'Купить плитку']) {
    await hover(title);
    const g = await page.evaluate(t => {
      const i = [...document.querySelectorAll('.day .item')].find(x => x.querySelector('.title')?.textContent.trim() === t);
      const tools = i.querySelector('.row-tools');
      const p = tools.getBoundingClientRect(), tx = i.querySelector('.tx').getBoundingClientRect();
      const btns = [...tools.querySelectorAll('button')].map(b => b.getBoundingClientRect()).map(r => [r.width, r.height]);
      // Сетка точек по названию: каждая должна попадать в название этой записи, а не в плашку
      const r = i.querySelector('.title').getBoundingClientRect();
      const misses = [];
      const textRight = r.right - parseFloat(getComputedStyle(i.querySelector('.title')).paddingRight); // справа от текста — зона «⌄» (R5)
      for (let x = r.left + 2; x < textRight - 1; x += 6) for (let y = r.top + 2; y < r.bottom - 1; y += 4) {
        const el = document.elementFromPoint(x, y);
        if (!el?.closest('.title-box') || el.closest('.item') !== i) misses.push(`${x - r.left | 0},${y - r.top | 0}:${el?.className}`);
      }
      return {
        opacity: Number(getComputedStyle(tools).opacity), above: p.bottom <= tx.top + 0.5, btns, misses: misses.join(' '),
        right: [textRight - 3, r.top + r.height / 2], mid: [r.left + r.width / 2, r.top + r.height / 2],
      };
    }, title);
    assert(g.opacity === 1 && g.above, `${title}: ${JSON.stringify(g)}`);
    assert(g.btns.length >= 1 && g.btns.every(([w, h]) => w >= 24 && h >= 24), `${title}: кнопки ${JSON.stringify(g.btns)}`);
    assert(!g.misses, `${title}: мимо названия ${g.misses}`);
    for (const [x, y] of [g.right, g.mid]) {
      await page.mouse.move(x, y); await sleep(200);
      await page.mouse.click(x, y);
      await page.waitForSelector('.editor .title-input', { timeout: 3000 });
      assert(await page.$eval('.editor .title-input', e => e.value) === title, `${title}: открылось не то окно`);
      assert(!(await page.$('.skip-dialog')), `${title}: открылось окошко пропуска`);
      await page.keyboard.press('Escape'); await settle();
    }
  }
});

await check('✕ при наведении в плашке с 🗑; клик — окошко с двумя крупными кнопками, причиной и быстрыми причинами', async () => {
  await hover('Стоматолог');
  const btn = await page.evaluate(() => {
    const i = [...document.querySelectorAll('.day .item')].find(x => x.querySelector('.title')?.textContent.trim() === 'Стоматолог');
    const b = i.querySelector('.skip-btn');
    return b ? { opacity: Number(getComputedStyle(b).opacity), title: b.title } : null;
  });
  assert(btn && btn.opacity === 1 && btn.title === 'Пропуск…', JSON.stringify(btn));
  await (await (await item('Стоматолог')).$('.skip-btn')).click();
  await page.waitForSelector('.skip-dialog');
  const dialog = await page.$eval('.skip-dialog', d => ({
    title: d.querySelector('.skip-title').textContent, choices: d.querySelectorAll('.skip-choice').length,
    chips: [...d.querySelectorAll('.skip-reason-chip')].map(c => c.textContent.trim()).join(','),
  }));
  assert(dialog.title.includes('«Стоматолог»') && dialog.choices === 2, JSON.stringify(dialog));
  assert(dialog.chips === 'болезнь,нет времени,форс-мажор,не захотелось', dialog.chips);
});

await check('«— Не нужно» с причиной: «—», бледно, не зачёркнуто, внизу дня, причина в подсказке; плашка «Вернуть»', async () => {
  // Между проверками окна закрываются — открыть окошко снова
  await hover('Стоматолог');
  await (await (await item('Стоматолог')).$('.skip-btn')).click();
  await page.waitForSelector('.skip-dialog .skip-reason');
  await page.type('.skip-reason', 'перенесли');
  await page.click('.skip-choice.not-needed');
  await page.waitForFunction(() => !document.querySelector('.skip-dialog'));
  await settle();
  const i = await info('Стоматолог');
  assert(i.cls.includes('not-needed') && i.mark === '—', JSON.stringify(i));
  assert(!i.strike.includes('line-through'), 'зачёркнуто');
  assert(i.index === i.count - 1, `не внизу дня: ${i.index} из ${i.count}`);
  assert(i.title === 'не нужно · перенесли', i.title);
  const toast = await page.$eval('.toast', e => e.textContent);
  assert(toast.includes('«Стоматолог» — не нужно') && toast.includes('Вернуть'), toast);
  const task = (await api('GET', `week/${day(0)}`)).tasks.find(t => t.title === 'Стоматолог');
  assert(task.state === 'Skipped' && task.skipKind === 'NotNeeded' && task.skipReason === 'перенесли', JSON.stringify(task));
});

await check('чип «болезнь» — одним нажатием «не сделано»: приглушённый красный ✕', async () => {
  await hover('Купить хлеб');
  await (await (await item('Купить хлеб')).$('.skip-btn')).click();
  await page.waitForSelector('.skip-dialog');
  await page.evaluate(() => [...document.querySelectorAll('.skip-reason-chip')].find(c => c.textContent.trim() === 'болезнь').click());
  await page.waitForFunction(() => !document.querySelector('.skip-dialog'));
  await settle();
  const i = await info('Купить хлеб');
  assert(i.cls.includes('missed') && i.mark === '✕' && i.title === 'не сделано · болезнь', JSON.stringify(i));
});

await check('та же ✕ у пропущенного — «Отменить пропуск», сразу, без окошка', async () => {
  await hover('Купить хлеб');
  const b = await (await item('Купить хлеб')).$('.skip-btn');
  assert((await b.evaluate(x => x.title)) === 'Отменить пропуск', 'подпись');
  await b.click();
  await settle();
  assert(!(await page.$('.skip-dialog')), 'открылось окошко');
  const i = await info('Купить хлеб');
  assert(!i.cls.includes('skipped') && i.mark === null, JSON.stringify(i));
});

await check('клик по чекбоксу пропущенного — сделано (пропуск снят)', async () => {
  await (await (await item('Стоматолог')).$('.ck-hit')).click();
  await settle();
  const i = await info('Стоматолог');
  assert(i.cls.includes('done') && !i.cls.includes('skipped'), i.cls);
  const task = (await api('GET', `week/${day(0)}`)).tasks.find(t => t.title === 'Стоматолог');
  assert(task.state === 'Done' && task.skipKind === null, JSON.stringify(task));
});

await check('шаг «не сделано» в дне — уходит из дня в список проекта, плашка «вернулся в «Ремонт»»', async () => {
  // Прошлое уведомление уходит (с polish-1, P1 оно уже не над записями — ожидание только для чистоты окошка)
  await page.waitForFunction(() => !document.querySelector('.toast'), { timeout: 10000 });
  await hover('Купить плитку');
  await (await (await item('Купить плитку')).$('.skip-btn')).click();
  await page.waitForSelector('.skip-dialog');
  await page.type('.skip-reason', 'нет в магазине');
  await page.keyboard.press('Enter'); // Enter в причине — «не сделано»
  await page.waitForFunction(() => !document.querySelector('.skip-dialog'));
  await settle();
  assert(!(await item('Купить плитку')), 'шаг остался в дне');
  const toast = await page.$eval('.toast', e => e.textContent);
  assert(toast.includes('вернулся в «Ремонт»'), toast);
  const history = await api('GET', `tasks/${step.id}/skips`);
  assert(history.length === 1 && history[0].reason === 'нет в магазине', JSON.stringify(history));
});

await check('цель: «не нужно» у занятия — норма меньше; оба «не нужно» — «—» вместо процента', async () => {
  await api('PUT', `tasks/${s1.id}/done`, { isDone: true });
  await api('PUT', `tasks/${s2.id}/skip`, { kind: 'NotNeeded' });
  await load();
  const pct = () => page.evaluate(() => {
    const row = [...document.querySelectorAll('.roles-panel .item')].find(i => i.querySelector('.title')?.textContent.trim() === 'Бег');
    return row ? [...row.querySelectorAll('.pct')].map(p => p.textContent.trim()).join(' ') : null;
  });
  assert((await pct()) === '100% ✓', await pct());
  await api('PUT', `tasks/${s1.id}/done`, { isDone: false });
  await api('PUT', `tasks/${s1.id}/skip`, { kind: 'NotNeeded' });
  await load();
  assert((await pct()) === '—', await pct());
});

await check('окно пропущенного дела — строка «✕ Не сделано · …» и «Отменить пропуск»; «⋯ → ✕ пропуск…» у открытого', async () => {
  await api('PUT', `tasks/${(await api('GET', `week/${day(0)}`)).tasks.find(t => t.title === 'Позвонить маме').id}/skip`, { kind: 'Missed', reason: 'не успела' });
  await load();
  await (await item('Позвонить маме')).click();
  await page.waitForSelector('.editor .skip-status');
  const status = await page.$eval('.editor .skip-status', e => e.textContent);
  assert(status.includes('Не сделано · не успела') && status.includes('Отменить пропуск'), status);
  await page.click('.editor .unskip');
  await settle();
  assert(!(await page.$('.editor .skip-status')), 'строка осталась');
});

await check('недельный обзор: пропущенных нет в «Не сделано», сверху сводка «не нужно: N · не сделано: M» (R1)', async () => {
  await load();
  await page.keyboard.press('KeyW');
  await page.waitForSelector('.review');
  const summary = await page.$eval('.review .skip-summary', e => e.textContent.trim());
  // К этому моменту пропущены оба занятия «Бег» («не нужно»); остальные пропуски отменены или стали «сделано»
  assert(summary === 'не нужно: 2 · не сделано: 0', summary);
  const rocks = await page.$eval('.review', e => e.textContent);
  assert(!rocks.includes('Бег'), 'пропущенные занятия «Бег» спрашиваются в обзоре');
  await page.keyboard.press('Escape'); await settle();
});

await check('отчёт: «Причины пропусков» — дело, вид, причина (O2)', async () => {
  await load();
  await openTotals(page, 'Отчёт');
  await page.waitForSelector('.report-skips li', { timeout: 10000 });
  const rows = await page.$$eval('.report-skips li', ls => ls.map(l => l.textContent.replace(/\s+/g, ' ').trim()));
  assert(rows.some(r => r.includes('Купить плитку') && r.includes('не сделано') && r.includes('нет в магазине')), rows.join(' | '));
  await page.keyboard.press('Escape'); await settle();
});

assert(consoleErrors.length === 0 || results.push(`FAIL ошибки на странице: ${consoleErrors.join(' | ')}`) || true, '');
await browser.close();
results.forEach(r => console.log(r));
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);

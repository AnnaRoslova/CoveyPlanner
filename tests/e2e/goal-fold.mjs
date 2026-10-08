// @seed — нужны демо-данные (tools/seed-demo.ps1)
// Левая панель: процент у каждой цели («Тренировка · 2 раза», одно выполнено → 50%; норма выполнена и сверх — «100% ✓» мятным;
// разовая цель — 0% / 100% ✓) и «свернуть / развернуть все цели» — одна кнопка в шапке, все цели всех ролей сразу,
// роли не сворачиваются; состояние запоминается в браузере.
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
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-goal-fold-${results.length}.png` }); }
  if (await page.$('.editor')) { await page.keyboard.press('Escape'); await settle(); }
}
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day'); await settle(); }
const api = (method, path, body) => page.evaluate(async (m, p, b) => {
  const r = await fetch(`/api/${p}`, { method: m, headers: { 'Content-Type': 'application/json' }, body: b ? JSON.stringify(b) : undefined });
  if (!r.ok) throw new Error(`${m} ${p}: ${r.status}`);
  return r.status === 204 ? null : r.json();
}, method, path, body);
const pad = n => String(n).padStart(2, '0');
const iso = d => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
const monday = (() => { const d = new Date(); d.setDate(d.getDate() - (d.getDay() + 6) % 7); return d; })();
const dayOfWeek = i => { const d = new Date(monday); d.setDate(d.getDate() + i); return iso(d); };

// Строка цели по названию (первая — цель может стоять под несколькими ролями)
const goalRow = title => page.evaluate(t => {
  const item = [...document.querySelectorAll('.roles-panel .role .item')].find(i => i.querySelector('.title')?.textContent.trim() === t);
  if (!item) return null;
  return {
    pct: item.querySelector('.pct')?.textContent.trim() ?? null,
    full: item.querySelector('.pct')?.classList.contains('full') ?? false,
    compact: item.classList.contains('compact'),
    days: !!item.querySelector('.goal-days'),
    jar: !!item.querySelector('.jar-pill'),
    progress: item.querySelector('.progress')?.textContent.trim() ?? null,
    height: item.getBoundingClientRect().height,
  };
}, title);
const foldButton = () => page.$eval('.roles-panel .head .fold', b => ({ title: b.title, pressed: b.getAttribute('aria-pressed') }));
const clickFold = async () => { await page.click('.roles-panel .head .fold'); await settle(); };
const toggleGoal = async title => {
  await page.evaluate(t => [...document.querySelectorAll('.roles-panel .role .item')]
    .find(i => i.querySelector('.title')?.textContent.trim() === t).querySelector('.ck-hit').click(), title);
  await settle();
};

// «Тренировка · 2 раза» в «Я сама»: одно занятие выполнено, второе стоит невыполненным
const roles = Object.fromEntries((await (await fetch(new URL('api/board', BASE))).json()).roles.map(r => [r.name, r.id]));
await load();
const training = await api('POST', 'goals', { roleId: roles['Я сама'], weekStart: iso(monday), title: 'Тренировка' });
await api('PUT', `goals/${training.id}`, {
  title: 'Тренировка', roleIds: [roles['Я сама']], timesPerWeek: 2, startTime: null, quadrant: null, notes: null, durationMinutes: null,
});
const first = (await api('POST', `goals/${training.id}/schedule`, { kind: 'day', date: dayOfWeek(0), index: 99 })).task;
await api('PUT', `tasks/${first.id}/done`, { isDone: true });
const second = (await api('POST', `goals/${training.id}/schedule`, { kind: 'day', date: dayOfWeek(1), index: 99 })).task;
await load();

await check('процент у каждой цели — и в «Затачивании пилы», и в ролях', async () => {
  const counts = await page.evaluate(() => ({
    goals: document.querySelectorAll('.roles-panel .role .item').length,
    pct: document.querySelectorAll('.roles-panel .role .item .pct').length,
    saw: document.querySelectorAll('.roles-panel .saw .role .item .pct').length,
  }));
  assert(counts.goals > 0 && counts.pct === counts.goals, `процентов ${counts.pct} у ${counts.goals} целей`);
  assert(counts.saw > 0, 'у целей пилы нет процента');
  const run = await goalRow('Бег');
  assert(run.pct === '0%' && !run.full, `Бег (0 из 3): ${run.pct}`);
});

await check('«Тренировка · 2 раза», одно выполнено → 50%, тихо (не мятная)', async () => {
  const row = await goalRow('Тренировка');
  assert(row.pct === '50%', `процент: ${row.pct}`);
  assert(!row.full, '50% — мятным');
  assert(row.progress === '1/2', `прогресс «1/2» пропал: ${row.progress}`);
});

await check('норма выполнена → «100% ✓» мятным; сверх нормы — тоже 100%', async () => {
  await api('PUT', `tasks/${second.id}/done`, { isDone: true });
  const third = (await api('POST', `goals/${training.id}/schedule`, { kind: 'day', date: dayOfWeek(2), index: 99 })).task;
  await api('PUT', `tasks/${third.id}/done`, { isDone: true });
  await load();
  const row = await goalRow('Тренировка');
  assert(row.pct === '100% ✓' && row.full, `3 из 2: ${row.pct} full=${row.full}`);
  const colors = await page.evaluate(() => {
    const items = [...document.querySelectorAll('.roles-panel .role .item')];
    const color = t => getComputedStyle(items.find(i => i.querySelector('.title').textContent.trim() === t).querySelector('.pct')).backgroundColor;
    return { full: color('Тренировка'), quiet: color('Бег') };
  });
  assert(colors.full !== colors.quiet, `у 100% нет мятного фона: ${colors.full}`);
});

await check('разовая цель: 0% → галочка цели → «100% ✓»', async () => {
  assert((await goalRow('Записаться к стоматологу')).pct === '0%', 'не 0%');
  await toggleGoal('Записаться к стоматологу');
  const row = await goalRow('Записаться к стоматологу');
  assert(row.pct === '100% ✓' && row.full, `после галочки: ${row.pct}`);
  await toggleGoal('Записаться к стоматологу');
  assert((await goalRow('Записаться к стоматологу')).pct === '0%', 'снятие галочки не вернуло 0%');
});

await check('по умолчанию все цели развёрнуты: дни занятий, «2/5»; пилюли банки у целей нет (R6)', async () => {
  const dentist = await goalRow('Записаться к стоматологу');
  assert(!dentist.compact && dentist.days, 'у цели нет строки дней');
  assert(!(await goalRow('Бег')).jar, 'у привычки в левой панели пилюля банки (R6: её больше нет)');
  assert((await foldButton()).title === 'Свернуть все цели', `подсказка: ${(await foldButton()).title}`);
  await page.evaluate(() => document.querySelector('.roles-panel .panel-scroll').scrollTop = 0);
  await (await page.$('.roles-panel')).screenshot({ path: `${SHOTS}/goal-fold-open.png` });
});

await check('«свернуть все цели» — все цели всех ролей в одну строку «название · процент»; роли остаются', async () => {
  const rolesBefore = await page.$$eval('.roles-panel .role-h', hs => hs.length);
  await clickFold();
  const state = await page.evaluate(() => {
    const items = [...document.querySelectorAll('.roles-panel .role .item')];
    return {
      all: items.length, compact: items.filter(i => i.classList.contains('compact')).length,
      extras: document.querySelectorAll('.roles-panel .role .item .goal-days, .roles-panel .role .item .jar-pill, .roles-panel .role .item .progress').length,
      pct: document.querySelectorAll('.roles-panel .role .item .pct').length,
      tall: items.filter(i => i.getBoundingClientRect().height > 30).map(i => i.querySelector('.title').textContent.trim()),
      roles: document.querySelectorAll('.roles-panel .role-h').length,
      adds: document.querySelectorAll('.roles-panel .role .add').length,
    };
  });
  assert(state.compact === state.all, `свёрнуто ${state.compact} из ${state.all}`);
  assert(state.extras === 0, `видно лишнее: ${state.extras}`);
  assert(state.pct === state.all, 'процент пропал');
  assert(state.tall.length === 0, `в несколько строк: ${state.tall}`);
  assert(state.roles === rolesBefore && state.adds === rolesBefore, 'роли свернулись');
  const button = await foldButton();
  assert(button.title === 'Развернуть все цели' && button.pressed === 'true', JSON.stringify(button));
  await (await page.$('.roles-panel')).screenshot({ path: `${SHOTS}/goal-fold-folded.png` });
});

await check('свёрнутые цели — запоминается после перезагрузки; работают клик, галочка', async () => {
  await load();
  assert((await goalRow('Бег')).compact && (await goalRow('Тренировка')).compact, 'развернулись после перезагрузки');
  await toggleGoal('Записаться к стоматологу');
  assert((await goalRow('Записаться к стоматологу')).pct === '100% ✓', 'галочка не сработала');
  await toggleGoal('Записаться к стоматологу');
  await page.evaluate(() => [...document.querySelectorAll('.roles-panel .role .item .title')].find(t => t.textContent.trim() === 'Тренировка').click());
  await settle();
  assert(await page.$('.editor'), 'клик по свёрнутой цели не открыл окно');
});

await check('новая цель в свёрнутом виде — тоже свёрнута', async () => {
  await page.evaluate(() => [...document.querySelectorAll('.roles-panel .role')]
    .find(r => r.querySelector('.role-h .name').textContent.trim() === 'Дом').querySelector('.add').click());
  await settle();
  await page.keyboard.type('Разобрать балкон'); await page.keyboard.press('Enter'); await sleep(800);
  await page.keyboard.press('Escape'); await settle();
  const row = await goalRow('Разобрать балкон');
  assert(row?.compact && row.pct === '0%', JSON.stringify(row));
});

await check('«развернуть все цели» — обратно, и это тоже запоминается', async () => {
  await clickFold();
  const state = await page.evaluate(() => document.querySelectorAll('.roles-panel .role .item.compact').length);
  assert(state === 0, `свёрнутых осталось: ${state}`);
  await load();
  assert(!(await goalRow('Бег')).compact, 'после перезагрузки свёрнуто');
  assert((await goalRow('Записаться к стоматологу')).days, 'нет строки дней');
});

// Снимки в двух темах: развёрнуто и свёрнуто
for (const theme of ['lotus', 'obsidian']) {
  await check(`снимки левой панели — тема ${theme}`, async () => {
    await page.evaluate(t => localStorage.setItem('planner.theme', t), theme);
    await load();
    await (await page.$('.roles-panel')).screenshot({ path: `${SHOTS}/goal-fold-${theme}-open.png` });
    await clickFold();
    await (await page.$('.roles-panel')).screenshot({ path: `${SHOTS}/goal-fold-${theme}-folded.png` });
    await clickFold();
  });
}
await page.evaluate(() => localStorage.removeItem('planner.theme'));

await browser.close();
if (consoleErrors.length) results.push(`FAIL ошибки в консоли: ${consoleErrors.join(' | ')}`);
console.log(results.join('\n'));

// @seed — нужны демо-данные (tools/seed-demo.ps1)
// Левая панель: процент у каждой цели («Тренировка · 2 раза», одно выполнено → 50%; норма выполнена и сверх — «100% ✓» мятным;
// разовая цель — 0% / 100% ✓) и сворачивание ролей (docs/specs/ui-declutter.md, U1): название роли — кнопка, «Затачивание пилы» —
// все измерения, кнопка в шапке — все роли; «+ цель» при наведении; состояние запоминается в браузере.
// batch-3.md, M2–M4: дни занятий — в подсказке строки; «+» у названия роли; заголовок раздела сворачивает цели его ролей, названия видны.
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
    pct: item.querySelector('.pct-ring') ? '○' : item.querySelector('.pct')?.textContent.trim() ?? null, // D1: не начато — кружок
    full: item.querySelector('.pct')?.classList.contains('full') ?? false,
    compact: item.classList.contains('compact'),
    daysVisible: (item.querySelector('.goal-days')?.getBoundingClientRect().height ?? 0) > 2,
    jar: !!item.querySelector('.jar-pill'),
    progress: ((i) => (t => t ? t.replace(/.*выполнено (\d+) из (\d+).*/, '$1/$2') : null)((i.querySelector('.pct-ring, .pct')?.getAttribute('title'))))(item), // D5: «2/5» — в подсказке процента
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
    // D1: не начато — пустой кружок .pct-ring вместо «0%»
    pct: [...document.querySelectorAll('.roles-panel .role .item')].filter(i => i.querySelector('.pct, .pct-ring')).length,
    saw: document.querySelectorAll('.roles-panel .saw .role .item :is(.pct, .pct-ring)').length,
  }));
  assert(counts.goals > 0 && counts.pct === counts.goals, `процентов ${counts.pct} у ${counts.goals} целей`);
  assert(counts.saw > 0, 'у целей пилы нет процента');
  const run = await goalRow('Бег');
  assert(run.pct === '○' && !run.full, `Бег (0 из 3): ${run.pct}`);
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
    const quiet = document.createElement('span'); quiet.className = 'pct'; items[0].appendChild(quiet);
    const q = getComputedStyle(quiet).backgroundColor; quiet.remove();
    return { full: color('Тренировка'), quiet: q };
  });
  assert(colors.full !== colors.quiet, `у 100% нет мятного фона: ${colors.full}`);
});

await check('разовая цель: 0% → галочка цели → «100% ✓»', async () => {
  assert((await goalRow('Записаться к стоматологу')).pct === '○', 'не пустой кружок');
  await toggleGoal('Записаться к стоматологу');
  const row = await goalRow('Записаться к стоматологу');
  assert(row.pct === '100% ✓' && row.full, `после галочки: ${row.pct}`);
  await toggleGoal('Записаться к стоматологу');
  assert((await goalRow('Записаться к стоматологу')).pct === '○', 'снятие галочки не вернуло кружок');
});

/** Роль по названию: свёрнута ли, видны ли её цели и «+ цель», подпись «N цели», розовая точка. */
const roleState = name => page.evaluate(n => {
  const r = [...document.querySelectorAll('.roles-panel .role')].find(x => x.querySelector('.role-h .name')?.textContent.trim() === n);
  if (!r) return null;
  const visible = el => !!el && el.getBoundingClientRect().height > 0;
  return {
    folded: r.classList.contains('folded'), expanded: r.querySelector('.role-toggle').getAttribute('aria-expanded'),
    goals: [...r.querySelectorAll('.item')].filter(visible).length, add: visible(r.querySelector('.role-h .add-goal')) && getComputedStyle(r.querySelector('.role-h .add-goal')).opacity === '1',
    peek: r.querySelector('.role-h .peek')?.textContent.trim() ?? null, dot: !!r.querySelector('.role-h .unplaced-dot'),
  };
}, name);
const clickRole = async name => {
  await page.evaluate(n => [...document.querySelectorAll('.roles-panel .role')].find(x => x.querySelector('.role-h .name')?.textContent.trim() === n).querySelector('.role-toggle').click(), name);
  await settle();
};

await check('по умолчанию роли развёрнуты: дни занятий — не строкой, а в подсказке (M2); пилюли банки у целей нет (R6); у роли нет счёта «0/1» (D5)', async () => {
  const dentist = await goalRow('Записаться к стоматологу');
  assert(!dentist.daysVisible, 'дни занятий видны строкой под целью');
  assert(!(await goalRow('Бег')).jar, 'у привычки в левой панели пилюля банки (R6: её больше нет)');
  assert((await foldButton()).title === 'Свернуть все роли', `подсказка: ${(await foldButton()).title}`);
  assert(!(await page.$('.roles-panel .role-h .n')), 'счёт у роли остался');
  await page.evaluate(() => document.querySelector('.roles-panel .panel-scroll').scrollTop = 0);
  await (await page.$('.roles-panel')).screenshot({ path: `${SHOTS}/goal-fold-open.png` });
});

await check('U1.1: клик по названию роли — её цели свёрнуты, строка «▸ Работа · 2 цели»; ещё клик — развёрнуты', async () => {
  await clickRole('Работа');
  let s = await roleState('Работа');
  assert(s.folded && s.expanded === 'false' && s.goals === 0 && !s.add && /^\d+ цел(ь|и|ей)$/.test(s.peek ?? ''), JSON.stringify(s));
  assert((await roleState('Дом')).goals > 0, 'свернулась и другая роль');
  await clickRole('Работа');
  s = await roleState('Работа');
  assert(!s.folded && s.goals > 0 && s.peek === null, JSON.stringify(s));
});

await check('U1.2: свёрнутая роль — запоминается после перезагрузки; галочка цели в другой роли работает', async () => {
  await clickRole('Дом');
  await load();
  assert((await roleState('Дом')).folded, 'развернулась после перезагрузки');
  await toggleGoal('Записаться к стоматологу');
  assert((await goalRow('Записаться к стоматологу')).pct === '100% ✓', 'галочка не сработала');
  await toggleGoal('Записаться к стоматологу');
  await clickRole('Дом');
});

await check('U1.4: у свёрнутой роли с неразложенной целью — розовая точка', async () => {
  await clickRole('Ум'); // «100 страниц» без занятий в днях — не разложено
  assert((await roleState('Ум')).dot, JSON.stringify(await roleState('Ум')));
  await clickRole('Ум');
});

await check('M4 (было U1.3): «Затачивание пилы» — папка: цели всех 4 измерений свёрнуты, названия видны', async () => {
  await page.click('.roles-panel .sect-toggle'); await settle();
  const saw = await page.$$eval('.roles-panel .saw .role', rs => rs.map(r => ({ folded: r.classList.contains('folded'), name: r.querySelector('.name').getBoundingClientRect().height > 0 })));
  assert(saw.length === 4 && saw.every(r => r.folded && r.name), JSON.stringify(saw));
  assert(await page.$eval('.roles-panel .sect-toggle', b => b.getAttribute('aria-expanded')) === 'false', 'aria-expanded');
  await page.click('.roles-panel .sect-toggle'); await settle();
  assert(await page.$$eval('.roles-panel .saw .role', rs => rs.every(r => !r.classList.contains('folded'))), 'пила не развернулась');
});

await check('U1.8: кнопка шапки — свернуть все роли и пилу; ещё раз — развернуть; запоминается', async () => {
  await clickFold();
  assert((await foldButton()).title === 'Развернуть все роли' && (await foldButton()).pressed === 'true', JSON.stringify(await foldButton()));
  const all = await page.$$eval('.roles-panel .user-roles .role', rs => rs.every(r => r.classList.contains('folded')));
  assert(all, 'не все роли свёрнуты');
  await (await page.$('.roles-panel')).screenshot({ path: `${SHOTS}/goal-fold-folded.png` });
  await load();
  assert((await roleState('Работа')).folded, 'после перезагрузки развёрнуто');
  await clickFold();
  assert(!(await roleState('Работа')).folded && !(await roleState('Тело')).folded, 'не развернулось');
});

await check('U1.7: «+ цель» — при наведении на роль; у роли без целей — всегда', async () => {
  const empty = await roleState('Дух');
  assert(empty && empty.goals === 0 && empty.add, `у пустой роли «+ цель» скрыта: ${JSON.stringify(empty)}`);
  await page.mouse.move(800, 450); await settle();
  assert(!(await roleState('Работа')).add, '«+ цель» видна без наведения');
  const box = await page.evaluate(() => { const r = [...document.querySelectorAll('.roles-panel .role')].find(x => x.querySelector('.role-h .name')?.textContent.trim() === 'Работа').getBoundingClientRect(); return { x: r.x + 40, y: r.y + 30 }; });
  await page.mouse.move(box.x, box.y); await settle();
  assert((await roleState('Работа')).add, '«+ цель» не появилась при наведении');
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

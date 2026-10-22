// Мелочи после проверки (docs/specs/batch-3.md, M): «×» у раскрытого редкого поля — очистить и свернуть в «+ …» (M1);
// дни занятий цели — не под названием, а в подсказке строки и в окне цели (M2); «+» у названия роли, «дела роли на неделе» в «⋯» (M3);
// заголовок раздела сворачивает цели всех его ролей, названия ролей видны; кнопка шапки — оба раздела (M4).
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
const settle = () => sleep(450);
function assert(c, m) { if (!c) throw new Error(m); }
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-small-fixes-${results.length}.png` }); }
  for (let i = 0; i < 3 && await page.$('.editor, .popover'); i++) {
    await page.keyboard.press('Escape'); await settle();
  }
  if (await page.$('.editor-backdrop')) { await page.mouse.click(4, 450); await settle(); } // Esc не дошёл (фокус вне окна) — клик по затемнению
  await page.mouse.move(800, 880);
  await page.evaluate(() => document.activeElement?.blur()); // фокус внутри роли тоже показывает «+»
  await sleep(300); // «+» роли гаснет плавно
}
const api = (method, path, body) => page.evaluate(async (m, p, b) => {
  const r = await fetch(`/api/${p}`, { method: m, headers: { 'Content-Type': 'application/json' }, body: b ? JSON.stringify(b) : undefined });
  if (!r.ok) throw new Error(`${m} ${p}: ${r.status} ${await r.text()}`);
  const text = await r.text();
  return text ? JSON.parse(text) : null;
}, method, path, body);
const iso = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day'); await settle(); }

const today = new Date(); today.setHours(0, 0, 0, 0);
const monday = new Date(today); monday.setDate(today.getDate() - ((today.getDay() + 6) % 7));
const DAYS = ['Вс', 'Пн', 'Вт', 'Ср', 'Чт', 'Пт', 'Сб'];

await load();
const roles = (await api('GET', 'board')).roles;
const roleId = name => roles.find(r => r.name === name).id;
const task = await api('POST', 'tasks', { title: 'Купить лампочки', target: { kind: 'day', date: iso(today), index: 0 } });
const goal = await api('POST', 'goals', { roleId: roleId('Работа'), weekStart: iso(monday), title: 'Код-ревью', isRecurring: false });
await api('PUT', `goals/${goal.id}`, { title: 'Код-ревью', roleIds: [roleId('Работа')], timesPerWeek: 3 });
await api('POST', `goals/${goal.id}/schedule`, { kind: 'day', date: iso(today), index: 0 });
await load();

const roleCard = name => page.evaluateHandle(n => [...document.querySelectorAll('.roles-panel .role')]
  .find(r => r.querySelector('.role-h .name')?.textContent.trim() === n), name);
const goalRow = title => page.evaluateHandle(t => [...document.querySelectorAll('.roles-panel .role .item')]
  .find(i => i.querySelector('.title')?.textContent.trim().startsWith(t)), title);
const editorState = () => page.$eval('.editor', ed => ({
  adders: [...ed.querySelectorAll('.adders .adder')].map(a => a.dataset.adder),
  fields: [...ed.querySelectorAll('[data-field]')].map(f => f.dataset.field),
  clears: [...ed.querySelectorAll('.rare-x')].map(b => b.dataset.clear),
  chips: [...ed.querySelectorAll('.sum-chip')].map(c => c.dataset.summary),
}));
const openTask = async () => {
  await page.evaluate(() => [...document.querySelectorAll('.day.today .item')].find(i => i.querySelector('.title')?.textContent.trim() === 'Купить лампочки').querySelector('.title').click());
  await page.waitForSelector('.editor'); await settle();
};
const save = async () => { await page.click('.editor .buttons .btn.primary'); await sleep(900); };

await check('M1: «+ длительность» → поле с «×»; «×» — поле свернулось, снова «+ длительность»', async () => {
  await openTask();
  await page.click('.editor .adder[data-adder="duration"]'); await settle();
  let s = await editorState();
  assert(s.fields.includes('duration') && s.clears.includes('duration') && !s.adders.includes('duration'), JSON.stringify(s));
  await page.click('.editor .rare-x[data-clear="duration"]'); await settle();
  s = await editorState();
  assert(!s.fields.includes('duration') && s.adders.includes('duration') && !s.chips.includes('duration'), JSON.stringify(s));
});

await check('M1: заполненное — при следующем открытии сводкой; клик — поле с «×»; «×» и «Сохранить» — значения нет', async () => {
  await api('PUT', `tasks/${task.id}`, { title: 'Купить лампочки', notes: null, roleIds: [], quadrant: null, startTime: null, durationMinutes: 30 });
  await load();
  await openTask();
  let s = await editorState();
  assert(s.chips.includes('duration') && !s.fields.includes('duration'), `нет сводки: ${JSON.stringify(s)}`);
  assert((await page.$eval('.editor .sum-chip[data-summary="duration"]', c => c.textContent)).includes('⏱'), 'сводка без ⏱');
  await page.click('.editor .sum-chip[data-summary="duration"]'); await settle();
  await page.click('.editor .rare-x[data-clear="duration"]'); await settle();
  await save();
  const saved = (await api('GET', `tasks/${task.id}`));
  assert(saved.durationMinutes === null, `длительность: ${saved.durationMinutes}`);
  await openTask();
  s = await editorState();
  assert(s.adders.includes('duration') && !s.chips.includes('duration'), JSON.stringify(s));
});

await check('M1: пустое раскрытое поле при закрытии окна — снова «+ …» (заметки)', async () => {
  await openTask();
  await page.click('.editor .adder[data-adder="notes"]'); await settle();
  await save();
  await openTask();
  const s = await editorState();
  assert(s.adders.includes('notes') && !s.fields.includes('notes') && !s.chips.includes('notes'), JSON.stringify(s));
});

await check('M1: окно цели — «+ заметки», текст, «×» — пусто и свёрнуто', async () => {
  await (await (await goalRow('Код-ревью')).$('.title')).click(); await page.waitForSelector('.editor'); await settle();
  await page.click('.editor .adder[data-adder="notes"]'); await settle();
  await page.type('.editor [data-field="notes"] textarea', 'ссылка на PR');
  await page.click('.editor .rare-x[data-clear="notes"]'); await settle();
  const s = await editorState();
  assert(s.adders.includes('notes') && !s.fields.includes('notes'), JSON.stringify(s));
});

await check('M2: дней под названием цели не видно; они в подсказке строки и в окне цели', async () => {
  const row = await goalRow('Код-ревью');
  const info = await row.evaluate(i => {
    const days = i.querySelector('.goal-days');
    return { title: i.title, visible: days ? days.getBoundingClientRect().height > 2 : false };
  });
  const day = DAYS[today.getDay()];
  assert(!info.visible, 'дни видны под названием');
  assert(info.title === `Код-ревью · ${day} · 0 из 3 · не разложено: 2 из 3`, `подсказка: ${info.title}`);
  await row.evaluate(i => i.querySelector('.title').click()); await page.waitForSelector('.editor'); await settle();
  const line = await page.$eval('.editor .session-days', e => e.textContent.trim());
  assert(line === `· дни: ${day}`, `в окне: ${line}`);
});

await check('M3: «+ цель» под целями нет; «+» у названия — при наведении, у роли без целей — всегда; создаёт цель', async () => {
  const work = await roleCard('Работа');
  assert(!(await work.$('.role-body > .add')), 'строка «+ цель» осталась');
  const opacity = async card => (await card.$('.role-h .add-goal')).evaluate(b => getComputedStyle(b).opacity);
  assert(await opacity(await roleCard('Дом')) === '1', '«+» у пустой роли скрыт');
  assert(await opacity(work) === '0', '«+» виден без наведения');
  const box = await (await work.$('.role-h')).boundingBox();
  await page.mouse.move(box.x + 40, box.y + box.height / 2); await sleep(400);
  const shown = await opacity(work);
  assert(shown === '1', `«+» не появился при наведении: ${shown}`);
  await (await work.$('.role-h .add-goal')).click(); await settle();
  await page.keyboard.type('Отчёт за месяц'); await page.keyboard.press('Enter'); await sleep(800);
  await page.keyboard.press('Escape'); await settle();
  assert(await goalRow('Отчёт за месяц').then(h => h.evaluate(x => !!x)), 'цель не создалась');
});

await check('M3: «дела в днях» под целями нет; «⋯ → дела роли на неделе» — список «Чт · …», «×» скрывает', async () => {
  await api('PUT', `tasks/${task.id}`, { title: 'Купить лампочки', notes: null, roleIds: [roleId('Дом')], quadrant: null, startTime: null, durationMinutes: null });
  await load();
  const home = await roleCard('Дом');
  assert(!(await page.$('.roles-panel .btn.others-h')), 'строка «дела в днях» осталась');
  await (await home.$('.role-h .name')).hover();
  await (await home.$('.role-h .menu')).click(); await settle();
  const item = await page.$eval('.popover.more-menu .option.role-tasks', b => ({ text: b.textContent.trim(), disabled: b.disabled }));
  assert(item.text.startsWith('дела роли на неделе') && !item.disabled, JSON.stringify(item));
  await page.click('.popover.more-menu .option.role-tasks'); await settle();
  const list = await (await roleCard('Дом')).$$eval('.others .other', os => os.map(o => `${o.querySelector('.d').textContent} · ${o.querySelector('.t').textContent.trim()}`));
  assert(JSON.stringify(list) === JSON.stringify([`${DAYS[today.getDay()]} · Купить лампочки`]), JSON.stringify(list));
  await (await (await roleCard('Дом')).$('.others-x')).click(); await settle();
  assert(!(await (await roleCard('Дом')).$('.others')), 'список не скрылся');
  // У роли без дел — пункт бледный
  const family = await roleCard('Семья');
  await (await family.$('.role-h .name')).hover();
  await (await family.$('.role-h .menu')).click(); await settle();
  assert(await page.$eval('.popover.more-menu .option.role-tasks', b => b.disabled), 'у роли без дел пункт доступен');
  await page.keyboard.press('Escape'); await settle();
  // «изменить роль…» — окно роли
  await (await family.$('.role-h .name')).hover();
  await (await family.$('.role-h .menu')).click(); await settle();
  await page.click('.popover.more-menu .option.edit-role'); await settle();
  assert(await page.$('.editor'), 'окно роли не открылось');
});

const foldInfo = () => page.$eval('.roles-panel', p => {
  const roles = [...p.querySelectorAll('.role')].map(r => ({
    saw: r.classList.contains('saw'), folded: r.classList.contains('folded'),
    nameVisible: r.querySelector('.role-h .name').getBoundingClientRect().height > 0,
  }));
  return { saw: roles.filter(r => r.saw), user: roles.filter(r => !r.saw) };
});
const section = name => page.evaluateHandle(n => [...document.querySelectorAll('.roles-panel .sect-toggle')].find(b => b.textContent.trim() === n), name);

await check('M4: клик по «Затачивание пилы» — цели всех измерений свёрнуты, названия видны; ещё клик — раскрыты', async () => {
  await (await section('Затачивание пилы')).click(); await settle();
  let f = await foldInfo();
  assert(f.saw.length === 4 && f.saw.every(r => r.folded && r.nameVisible), JSON.stringify(f.saw));
  assert(f.user.every(r => !r.folded), 'свернулись и роли');
  await (await section('Затачивание пилы')).click(); await settle();
  f = await foldInfo();
  assert(f.saw.every(r => !r.folded), 'пила не раскрылась');
});

await check('M4: «Роли» — так же; одна раскрытая роль в свёрнутом разделе — клик сворачивает все; запоминается', async () => {
  await (await section('Роли')).click(); await settle();
  let f = await foldInfo();
  assert(f.user.length >= 4 && f.user.every(r => r.folded && r.nameVisible), JSON.stringify(f.user));
  await (await (await roleCard('Работа')).$('.role-toggle')).click(); await settle(); // раскрыли одну
  await (await section('Роли')).click(); await settle();
  f = await foldInfo();
  assert(f.user.every(r => r.folded), 'не все свернулись');
  await load();
  f = await foldInfo();
  assert(f.user.every(r => r.folded) && f.saw.every(r => !r.folded), 'после перезагрузки другое');
  await (await section('Роли')).click(); await settle();
});

await check('M4: кнопка шапки — оба раздела; раздел одной строкой не сворачивается', async () => {
  await page.click('.roles-panel .head .fold'); await settle();
  let f = await foldInfo();
  assert([...f.saw, ...f.user].every(r => r.folded && r.nameVisible), JSON.stringify(f));
  await page.click('.roles-panel .head .fold'); await settle();
  f = await foldInfo();
  assert([...f.saw, ...f.user].every(r => !r.folded), 'не раскрылось');
});

await browser.close();
results.forEach(r => console.log(r));
if (consoleErrors.length) console.log(`FAIL ошибки в консоли: ${consoleErrors.slice(0, 3).join(' | ')}`);
process.exit(results.some(r => r.startsWith('FAIL')) || consoleErrors.length ? 1 : 0);

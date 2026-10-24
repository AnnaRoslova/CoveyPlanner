// Чище окна (docs/specs/ui-declutter.md, U2): сразу видно название, роль, дата и время; редкие поля — заполненные одной строкой-сводкой
// (клик раскрывает именно это поле), пустые — «+ …» (фокус в появившемся поле); редкие действия — в «⋯» (Esc закрывает меню, а не окно);
// у мысли — «🛠 Сделать проектом», у дела без шагов «+ шаг» нет; окно цели — основное одной-двумя строками. Данные — через API (чистая база).
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
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-declutter-${results.length}.png` }); }
  for (let i = 0; i < 3 && await page.$('.editor'); i++) { await page.keyboard.press('Escape'); await settle(); }
}
const api = (method, path, body) => page.evaluate(async (m, p, b) => {
  const r = await fetch(`/api/${p}`, { method: m, headers: { 'Content-Type': 'application/json', 'X-Planner': '1' }, body: b ? JSON.stringify(b) : undefined });
  if (!r.ok) throw new Error(`${m} ${p}: ${r.status} ${await r.text()}`);
  const text = await r.text();
  return text ? JSON.parse(text) : null;
}, method, path, body);
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day'); await settle(); }

const iso = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
const now = new Date();
const monday = new Date(now); monday.setDate(now.getDate() - ((now.getDay() + 6) % 7));
const dayIso = i => { const d = new Date(monday); d.setDate(monday.getDate() + i); return iso(d); };

async function openDayTask(i, title) {
  await page.evaluate((i, t) => [...document.querySelectorAll('.day')[i].querySelectorAll(':scope > .item')]
    .find(x => x.querySelector('.title').textContent.trim() === t).querySelector('.title').click(), i, title);
  await settle();
  assert(await page.$('.editor[aria-label="Дело"]'), 'окно дела не открылось');
}
/** Что видно в окне: строки «метка», раскрыт ли «Подробнее», его подпись, кнопки внизу. */
const editorState = () => page.evaluate(() => {
  const ed = document.querySelector('.editor');
  const visible = el => !!el && el.getBoundingClientRect().height > 0;
  return {
    labels: [...ed.querySelectorAll('.label')].filter(visible).map(l => l.textContent.trim()),
    chips: [...ed.querySelectorAll('.sum-chip')].map(c => c.querySelector('.sum-text').textContent.trim()),
    adders: [...ed.querySelectorAll('.adders .adder')].map(a => a.textContent.trim()),
    buttons: [...ed.querySelectorAll('.buttons > .btn, .buttons > button')].map(b => b.getAttribute('aria-label') ?? b.textContent.trim()),
    textarea: visible(ed.querySelector('textarea')),
    addStep: visible(ed.querySelector('.add-step')),
    makeProject: visible(ed.querySelector('.make-project-b')),
  };
});

await load();
const work = (await api('GET', 'board')).roles.find(r => r.name === 'Работа');
const body = (await api('GET', 'board')).roles.find(r => r.name === 'Тело');
const plain = await api('POST', 'tasks', { title: 'Позвонить', target: { kind: 'day', date: dayIso(1), index: 0 } });
await api('PUT', `tasks/${plain.id}`, { title: 'Позвонить', notes: null, roleIds: [work.id], quadrant: null, startTime: null, durationMinutes: null });
const noted = await api('POST', 'tasks', { title: 'С заметкой', target: { kind: 'day', date: dayIso(1), index: 1 } });
await api('PUT', `tasks/${noted.id}`, { title: 'С заметкой', notes: 'важная мысль', roleIds: [], quadrant: 2, startTime: null, durationMinutes: null });
const ideas = await api('POST', 'categories', { name: 'Идеи' });
await api('POST', 'tasks', { title: 'Сад', target: { kind: 'someday', categoryId: ideas.id, index: 0 } });
await api('POST', 'goals', { roleId: body.id, weekStart: dayIso(0), title: 'Йога' });
await load();

await check('U2.1, U2.2, U2.3: окно дела — сразу роль и дата; пустые редкие — «+ квадрант · + длительность · + проект · + заметки»; внизу «⋯», «Отмена», «Сохранить»', async () => {
  await openDayTask(1, 'Позвонить');
  const s = await editorState();
  assert(JSON.stringify(s.labels) === '["Роль","Дата"]', JSON.stringify(s.labels));
  assert(s.chips.length === 0 && JSON.stringify(s.adders) === '["+ квадрант","+ длительность","+ проект","+ заметки"]', JSON.stringify(s));
  assert(JSON.stringify(s.buttons) === '["Ещё действия","Отмена","Сохранить"]', JSON.stringify(s.buttons));
  assert(!s.textarea, 'заметки видны');
  assert(!s.addStep, '«+ шаг» у дела без шагов (BP3)');
  await (await page.$('.editor')).screenshot({ path: `${SHOTS}/declutter-task.png` });
});

await check('U2.2: заполненное — одной строкой-сводкой («Квадрант II · …», «📝 …»), клик раскрывает именно это поле; «+ …» — только пустые', async () => {
  await openDayTask(1, 'С заметкой');
  let s = await editorState();
  assert(JSON.stringify(s.chips) === '["Квадрант II · важно, не срочно","📝 важная мысль"]', JSON.stringify(s.chips));
  assert(JSON.stringify(s.adders) === '["+ длительность","+ проект"]' && !s.textarea, JSON.stringify(s));
  await page.click('.editor .sum-chip[data-summary="quadrant"]'); await settle();
  s = await editorState();
  assert(s.labels.includes('Квадрант') && !s.textarea && JSON.stringify(s.chips) === '["📝 важная мысль"]', JSON.stringify(s));
});

await check('U2.2: «+ заметки» — поле появляется, фокус в нём; «+ …» пропадает', async () => {
  await openDayTask(1, 'Позвонить');
  await page.click('.editor .adder[data-adder="notes"]'); await settle();
  assert(await page.evaluate(() => document.activeElement?.tagName === 'TEXTAREA'), 'фокус не в заметках');
  assert(!(await editorState()).adders.includes('+ заметки'), '«+ заметки» осталась');
});

await check('U2.7: «⋯» с клавиатуры — Enter открывает меню, ↓ по пунктам; Esc закрывает меню, окно остаётся; второй Esc — окно', async () => {
  await openDayTask(1, 'Позвонить');
  await page.focus('.editor .more-b'); await page.keyboard.press('Enter'); await settle();
  const items = await page.$$eval('.more-menu .option-name', os => os.map(o => o.textContent.trim()));
  assert(JSON.stringify(items) === '["🛠 сделать проектом","✕ пропуск…","в корзину"]', JSON.stringify(items));
  assert(await page.$eval('.more-menu .option.danger', o => o.textContent.includes('в корзину')), 'опасный пункт не розовый');
  await page.keyboard.press('ArrowDown'); await sleep(150);
  assert(await page.evaluate(() => document.activeElement?.classList.contains('option')), 'стрелка не ведёт по пунктам');
  await page.keyboard.press('Escape'); await settle();
  assert(!(await page.$('.more-menu')) && await page.$('.editor'), 'Esc закрыл окно вместе с меню');
  await page.keyboard.press('Escape'); await settle();
  assert(!(await page.$('.editor')), 'второй Esc не закрыл окно');
});

await check('U2.5: меню поля «Дата» — Esc закрывает только меню', async () => {
  await openDayTask(1, 'Позвонить');
  await page.click('.editor .date-field'); await settle();
  assert(await page.$('.date-menu'), 'меню не открылось');
  await page.keyboard.press('Escape'); await settle();
  assert(!(await page.$('.date-menu')) && await page.$('.editor'), 'Esc закрыл окно');
});

await check('U2.B, BP2: окно мысли — категория и «Дата: в ленте»; «🛠 Сделать проектом» с подписью; «+ шаг» нет', async () => {
  await page.evaluate(() => [...document.querySelectorAll('.cat-b > .item')].find(i => i.querySelector('.title').textContent.trim() === 'Сад').querySelector('.title').click());
  await settle();
  const s = await editorState();
  assert(JSON.stringify(s.labels) === '["Категория","Дата"]', JSON.stringify(s.labels));
  assert(s.makeProject && !s.addStep && JSON.stringify(s.adders) === '["+ заметки","+ проект"]', JSON.stringify(s));
  assert((await page.$eval('.editor .date-field', e => e.textContent.trim())) === 'в ленте', 'поле даты мысли');
  assert((await page.$eval('.editor .make-project .hint', e => e.textContent.trim())) === 'добавить шаги и раскладывать их по дням', 'нет подписи');
});

await check('U2.B: мысль в день через поле «Дата» — «завтра» → ушла из ленты, стоит в дне', async () => {
  await page.evaluate(() => [...document.querySelectorAll('.cat-b > .item')].find(i => i.querySelector('.title').textContent.trim() === 'Сад').querySelector('.title').click());
  await settle();
  await page.click('.editor .date-field'); await settle();
  await page.evaluate(() => [...document.querySelectorAll('.date-menu .option')].find(o => o.querySelector('.option-name')?.textContent.trim() === 'завтра').click()); await settle();
  await page.click('.editor .buttons .btn.primary'); await sleep(900);
  const tomorrow = new Date(); tomorrow.setDate(tomorrow.getDate() + 1);
  const t = (await api('GET', 'board')).somedayTasks.find(x => x.title === 'Сад');
  assert(!t, 'осталась в ленте');
  const week = await api('GET', `week/${iso(tomorrow)}`);
  assert(week.tasks.some(x => x.title === 'Сад' && x.plannedDate === iso(tomorrow)), JSON.stringify(week.tasks.map(x => [x.title, x.plannedDate])));
});

await check('U2.D: окно цели — основное одной-двумя строками (роль · раз в неделю · каждую неделю · копить); сводка банки; «+ дни · + время…»; «удалить цель» — в «⋯»', async () => {
  await page.evaluate(() => [...document.querySelectorAll('.role .item')].find(i => i.querySelector('.title')?.textContent.trim() === 'Йога').querySelector('.title').click());
  await settle();
  const s = await editorState();
  assert(s.labels.length === 0, `метки в основном: ${JSON.stringify(s.labels)}`);
  const main = await page.$eval('.editor .goal-main', m => m.getBoundingClientRect().height);
  assert(main < 80, `основное выше двух строк: ${main}`);
  assert(await page.$('.editor .goal-main .switch-row') && await page.$('.editor .goal-main .stepper'), 'нет «каждую неделю» или «раз в неделю»');
  assert(JSON.stringify(s.chips) === '["🫙 0 скрепок · 🔥0"]', JSON.stringify(s.chips));
  assert(JSON.stringify(s.adders) === '["+ дни","+ время","+ длительность","+ несколько в день","+ квадрант","+ проект","+ заметки"]', JSON.stringify(s.adders));
  await page.click('.editor .more-b'); await settle();
  const items = await page.$$eval('.more-menu .option-name', os => os.map(o => o.textContent.trim()));
  assert(items.at(-1) === 'удалить цель', JSON.stringify(items));
  await page.keyboard.press('Escape'); await settle();
  await (await page.$('.editor')).screenshot({ path: `${SHOTS}/declutter-goal.png` });
});

await check('U2.6: подсказка «каждую неделю» — только после переключения в этом окне', async () => {
  await page.evaluate(() => [...document.querySelectorAll('.role .item')].find(i => i.querySelector('.title')?.textContent.trim() === 'Йога').querySelector('.title').click());
  await settle();
  assert(!(await page.$('.editor .goal-main + .date-hint')), 'подсказка видна сразу');
  await page.click('.editor .goal-main .switch-row'); await settle();
  assert((await page.$eval('.editor .goal-main + .date-hint', e => e.textContent.trim())) === 'только эта неделя', 'нет подсказки после переключения');
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);

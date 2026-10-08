// Один список шагов на цель (docs/specs/tasks-steps-done.md, G1–G10, e2e п. 27–31): первый «+ шаг» в окне цели создаёт проект
// в «неразобранном»; у всех занятий цели — шаги проекта («▸ … ☑ N/M»), «+ шаг» в окне занятия — туда же; шаг из окна цели — в день;
// отметка шагов не трогает занятие и цель; постоянная цель — тот же список на следующей неделе, удаление «только на этой неделе» —
// проект остаётся. Данные — через API (чистая база).
import puppeteer from 'puppeteer-core';

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
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-goal-steps-${results.length}.png` }); }
}
const api = (method, path, body) => page.evaluate(async (m, p, b) => {
  const r = await fetch(`/api/${p}`, { method: m, headers: { 'Content-Type': 'application/json' }, body: b ? JSON.stringify(b) : undefined });
  if (!r.ok) throw new Error(`${m} ${p}: ${r.status} ${await r.text()}`);
  const text = await r.text();
  return text ? JSON.parse(text) : null;
}, method, path, body);
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day'); await settle(); }

const iso = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
const now = new Date();
const monday = new Date(now); monday.setDate(now.getDate() - ((now.getDay() + 6) % 7));
const dayIso = (i, base = monday) => { const d = new Date(base); d.setDate(base.getDate() + i); return iso(d); };
const GOAL = 'Подготовка к собесу';

const dayRow = (i, title) => page.evaluateHandle((i, t) =>
  [...document.querySelectorAll('.day')[i].querySelectorAll(':scope > .item')].find(it => it.querySelector('.title')?.textContent.trim() === t), i, title);
const dayInfo = i => page.evaluate(i => [...document.querySelectorAll('.day')[i].querySelectorAll(':scope > .item')].map(it => ({
  title: it.querySelector('.title').textContent.trim(), done: it.classList.contains('done'),
  project: it.querySelector('.project-label')?.textContent.replace('▸', '').trim() ?? null, parent: it.querySelector('.parent-of span')?.textContent.trim() ?? null,
})), i);
async function openGoal(title) {
  const el = (await page.evaluateHandle(t => [...document.querySelectorAll('.role .item')].find(i => i.querySelector('.title')?.textContent.trim() === t), title)).asElement();
  assert(el, `нет цели ${title}`);
  await (await el.$('.title')).click(); await settle();
  await page.waitForSelector('.editor[aria-label="Цель недели"]', { timeout: 3000 });
}
async function addStepInEditor(title) {
  // Поле «+ шаг» после Enter остаётся открытым для следующего шага
  if (await page.$('.editor .goal-steps .add-step')) { await page.click('.editor .goal-steps .add-step'); await sleep(200); }
  else await page.focus('.editor .goal-steps .inline-input');
  await page.keyboard.type(title); await page.keyboard.press('Enter'); await settle();
}
/** Закрыть окно: Esc закрывает верхний слой — сначала открытое поле «+ шаг», потом само окно. */
async function closeEditor() {
  for (let i = 0; i < 3 && await page.$('.editor'); i++) { await page.keyboard.press('Escape'); await settle(); }
}
const editorSteps = () => page.$$eval('.editor .goal-steps .steps .item .title', ts => ts.map(t => t.textContent.trim()));
const projectField = () => page.evaluate(() => [...document.querySelectorAll('.editor .project-row')]
  .find(r => r.querySelector('.label')?.textContent.trim() === 'Проект')?.querySelector('.select-label')?.textContent.trim() ?? null);

await load();
const work = (await api('GET', 'board')).roles.find(r => r.name === 'Работа');
const goal = await api('POST', 'goals', { roleId: work.id, weekStart: dayIso(0), title: GOAL, isRecurring: false });
await load();

let projectId;
await check('27. цель без проекта: «+ шаг» в окне цели — проект «Подготовка к собесу» в «неразобранном», он в поле «Проект»', async () => {
  await openGoal(GOAL);
  assert(await page.$eval('.editor .goal-steps', e => e.textContent.includes('первый шаг создаст проект')), 'нет подсказки G3');
  await addStepInEditor('Задачи');
  assert(JSON.stringify(await editorSteps()) === '["Задачи"]', JSON.stringify(await editorSteps()));
  assert(await projectField() === GOAL, `поле «Проект»: ${await projectField()}`);
  await closeEditor();
  const feed = await page.$$eval('.someday .item .title', ts => ts.map(t => t.textContent.trim()));
  assert(feed.includes(GOAL), JSON.stringify(feed));
  projectId = (await api('GET', `week/${dayIso(0)}`)).goals.find(g => g.id === goal.id).projectId;
  assert(projectId, 'у цели нет проекта на сервере');
});

await check('28. два занятия: у обоих «▸ … ☑ 0/1»; «+ шаг» в окне занятия среды — «☑ 0/2» у обоих и в окне цели', async () => {
  await api('POST', `goals/${goal.id}/schedule`, { kind: 'day', date: dayIso(1), index: 0 });
  await api('POST', `goals/${goal.id}/schedule`, { kind: 'day', date: dayIso(2), index: 0 });
  await load();
  for (const i of [1, 2]) {
    const r = (await dayInfo(i)).find(x => x.title === GOAL);
    assert(r?.project === '☑ 0/1' && r.parent === null, `день ${i}: ${JSON.stringify(r)}`);
  }
  await (await (await dayRow(2, GOAL)).asElement().$('.title')).click(); await settle();
  assert(await page.$eval('.editor .goal-steps .label', e => e.textContent.includes('Шаги цели')), 'нет блока «Шаги цели»');
  await addStepInEditor('Резюме');
  await closeEditor();
  for (const i of [1, 2]) assert((await dayInfo(i)).find(x => x.title === GOAL)?.project === '☑ 0/2', `день ${i}`);
  await openGoal(GOAL);
  assert(JSON.stringify(await editorSteps()) === '["Задачи","Резюме"]', JSON.stringify(await editorSteps()));
});

await check('29. шаг из окна цели перетащили в пятницу — там «↳ …», в списке метка дня', async () => {
  const step = (await page.$$('.editor .goal-steps .steps .item'))[0];
  const from = await step.boundingBox();
  const fri = await (await page.evaluateHandle(() => document.querySelectorAll('.day')[4])).asElement().boundingBox();
  const tx = fri.x + fri.width / 2, ty = fri.y + fri.height - 60;
  const sx = from.x + from.width / 2, sy = from.y + 10;
  await page.mouse.move(sx, sy); await page.mouse.down();
  for (let i = 1; i <= 16; i++) { await page.mouse.move(sx + (tx - sx) * i / 16, sy + (ty - sy) * i / 16); await sleep(16); }
  await sleep(200); await page.mouse.up(); await settle();
  const f = await dayInfo(4);
  assert(f.some(x => x.title === 'Задачи' && x.parent === GOAL), JSON.stringify(f));
  assert(await page.$('.editor'), 'окно цели закрылось');
  const mark = await page.$eval('.editor .goal-steps .steps .item .day-mark', e => e.textContent.trim());
  assert(mark.length > 0, 'нет метки дня');
  await page.keyboard.press('Escape'); await settle();
  assert(!(await page.$('.editor')), 'после перетаскивания из окна Esc его не закрыл');
});

await check('30. отметили шаг у занятия — занятие и цель не отмечены; последний — проект и это занятие отмечены (R4)', async () => {
  await (await (await dayRow(1, GOAL)).asElement().$('.expand')).click(); await settle();
  const clickStep = async t => {
    const s = await page.evaluateHandle(t => [...document.querySelectorAll('.day')[1].querySelectorAll('.steps .item')].find(i => i.querySelector('.title').textContent.trim() === t), t);
    await (await s.asElement().$('.ck-hit')).click(); await settle();
  };
  await clickStep('Задачи');
  let tue = (await dayInfo(1)).find(x => x.title === GOAL);
  assert(!tue.done && tue.project === '☑ 1/2', JSON.stringify(tue));
  let g = (await api('GET', `week/${dayIso(0)}`)).goals.find(x => x.id === goal.id);
  assert(g.sessions.every(s => !s.isDone), 'отмечено занятие цели');
  await clickStep('Резюме');
  tue = (await dayInfo(1)).find(x => x.title === GOAL);
  assert(tue.done, 'занятие не отмечено (R4: последний шаг из строки занятия отмечает и его)');
  assert((await api('GET', `tasks/${projectId}`)).state === 'Done', 'проект не отмечен');
  g = (await api('GET', `week/${dayIso(0)}`)).goals.find(x => x.id === goal.id);
  assert(g.sessions.filter(s => s.isDone).length === 1, 'отмечено не ровно одно занятие (то, из которого отметили)');
});

await check('31. постоянная цель: следующая неделя — тот же список; удалили «только на этой неделе» — проект с шагами в ленте', async () => {
  const mind = (await api('GET', 'board')).roles.find(r => r.name === 'Ум');
  const reading = await api('POST', 'goals', { roleId: mind.id, weekStart: dayIso(0), title: 'Чтение', isRecurring: true });
  const added = await api('POST', `goals/${reading.id}/steps`, { title: 'Глава 1' });
  const nextMonday = dayIso(7);
  const next = (await api('GET', `week/${nextMonday}`)).goals.find(g => g.seriesId === reading.seriesId);
  assert(next.projectId === added.createdProject.id, JSON.stringify(next));
  await api('DELETE', `goals/${reading.id}`);
  await load();
  const feed = await page.$$eval('.someday .item .title', ts => ts.map(t => t.textContent.trim()));
  assert(feed.includes('Чтение'), JSON.stringify(feed));
  assert((await api('GET', `tasks/${added.createdProject.id}/steps`)).length === 1, 'шаги проекта пропали');
});

// ---------- Этап 6в: R1, R3, R4 ----------
const R = 'Проект R';
const rGoal = await api('POST', 'goals', { roleId: work.id, weekStart: dayIso(0), title: R, isRecurring: false });
await api('POST', `goals/${rGoal.id}/schedule`, { kind: 'day', date: dayIso(5), index: 0 });
await api('POST', `goals/${rGoal.id}/schedule`, { kind: 'day', date: dayIso(6), index: 0 });
for (const t of ['Первый', 'Второй']) await api('POST', `goals/${rGoal.id}/steps`, { title: t });
const stepsIn = i => page.evaluate(i => [...document.querySelectorAll('.day')[i].querySelectorAll('.steps .item .title')].map(t => t.textContent.trim()), i);
const toastText = () => page.evaluate(() => document.querySelector('.toast')?.textContent.replace(/\s+/g, ' ').trim() ?? null);
const toastAction = t => page.evaluate(t => [...document.querySelectorAll('.toast button.action')].find(b => b.textContent.trim() === t)?.click(), t);

await check('R1: «⌄» у занятия в субботу раскрывает шаги только в этой строке — в воскресенье свёрнуто', async () => {
  await load();
  await (await (await dayRow(5, R)).asElement().$('.expand')).click(); await settle();
  assert(JSON.stringify(await stepsIn(5)) === '["Первый","Второй"]', JSON.stringify(await stepsIn(5)));
  assert((await stepsIn(6)).length === 0, `в воскресенье раскрыто: ${JSON.stringify(await stepsIn(6))}`);
  await load();
  assert((await stepsIn(5)).length === 2 && (await stepsIn(6)).length === 0, 'после перезагрузки не по строке');
});

await check('R3: 🗑 у шага в развёрнутой строке — шаг в корзину, плашка «Удалено «…» · Вернуть»; «Вернуть» — на месте', async () => {
  const step = (await page.evaluateHandle(() => [...document.querySelectorAll('.day')[5].querySelectorAll('.steps .item')].find(i => i.querySelector('.title').textContent.trim() === 'Второй'))).asElement();
  const b = await step.boundingBox();
  await page.mouse.move(b.x + b.width / 2, b.y + b.height / 2); await sleep(300);
  await (await step.$('.trash-btn')).click(); await settle();
  assert(JSON.stringify(await stepsIn(5)) === '["Первый"]', JSON.stringify(await stepsIn(5)));
  assert((await toastText())?.includes('Удалено «Второй»'), `плашка: ${await toastText()}`);
  await toastAction('Вернуть'); await settle();
  assert(JSON.stringify(await stepsIn(5)) === '["Первый","Второй"]', JSON.stringify(await stepsIn(5)));
});

// ---------- Найдено на проверке 6в: R7, защита 🗑 у шага ----------
const goalPct = t => page.evaluate(t => [...document.querySelectorAll('.role .item')].find(i => i.querySelector('.title')?.textContent.trim() === t)?.querySelector('.pct')?.textContent.trim() ?? null, t);

await check('R7: у цели слева «☑ N/M · %» — сразу меняется при отметке, добавлении и удалении шага', async () => {
  await load();
  assert(/^☑ 0\/2 · \d+%/.test(await goalPct(R)), `слева: ${await goalPct(R)}`);
  const step = (await page.evaluateHandle(() => [...document.querySelectorAll('.day')[5].querySelectorAll('.steps .item')].find(i => i.querySelector('.title').textContent.trim() === 'Первый'))).asElement();
  await (await step.$('.ck-hit')).click(); await settle();
  assert((await goalPct(R))?.startsWith('☑ 1/2 · '), `после отметки: ${await goalPct(R)}`);
  await (await (await dayRow(5, R)).asElement().$('.title')).click(); await settle();
  await addStepInEditor('Третий'); await closeEditor();
  assert((await goalPct(R))?.startsWith('☑ 1/3 · '), `после добавления: ${await goalPct(R)}`);
});

await check('🗑 у шага защищена, как у записей: долгое и «дёрнувшееся» нажатие не удаляют; короткий клик — в корзину', async () => {
  const third = async () => (await page.evaluateHandle(() => [...document.querySelectorAll('.day')[5].querySelectorAll('.steps .item')].find(i => i.querySelector('.title').textContent.trim() === 'Третий'))).asElement();
  const press = async (holdMs, moveX) => {
    const s = await third();
    const b = await s.boundingBox();
    await page.mouse.move(b.x + b.width / 2, b.y + b.height / 2); await sleep(250);
    const t = await (await third()).$('.trash-btn');
    const tb = await t.boundingBox();
    await page.mouse.move(tb.x + tb.width / 2, tb.y + tb.height / 2);
    await page.mouse.down(); await sleep(holdMs);
    if (moveX) await page.mouse.move(tb.x + tb.width / 2 + moveX, tb.y + tb.height / 2);
    await page.mouse.up(); await settle();
  };
  await press(600, 0);
  assert(await third(), 'удалено долгим нажатием');
  await press(80, 6);
  assert(await third(), 'удалено «дёрнувшимся» нажатием');
  await press(60, 0);
  assert(!(await third()), 'короткий клик не удалил');
  assert((await goalPct(R))?.startsWith('☑ 1/2 · '), `после удаления: ${await goalPct(R)}`);
});

await check('R4: последний шаг отметили в строке занятия — отмечены проект и это занятие; «вернуть» откатывает оба', async () => {
  const tick = async t => {
    const s = (await page.evaluateHandle(t => [...document.querySelectorAll('.day')[5].querySelectorAll('.steps .item')].find(i => i.querySelector('.title').textContent.trim() === t), t)).asElement();
    await (await s.$('.ck-hit')).click(); await settle();
  };
  // «Первый» отмечен в проверке R7 — остаётся последний
  assert(!(await dayInfo(5)).find(x => x.title === R).done, 'занятие отмечено раньше времени');
  await tick('Второй');
  assert((await dayInfo(5)).find(x => x.title === R).done, 'занятие не отмечено');
  assert(!(await dayInfo(6)).find(x => x.title === R).done, 'отмечено другое занятие');
  const projectR = (await api('GET', `week/${dayIso(0)}`)).goals.find(g => g.id === rGoal.id).projectId;
  assert((await api('GET', `tasks/${projectR}`)).state === 'Done', 'проект не отмечен');
  assert((await toastText())?.includes('Все шаги сделаны'), `плашка: ${await toastText()}`);
  await toastAction('вернуть'); await settle();
  assert(!(await dayInfo(5)).find(x => x.title === R).done, 'занятие не откатилось');
  assert((await api('GET', `tasks/${projectR}`)).state !== 'Done', 'проект не откатился');
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);

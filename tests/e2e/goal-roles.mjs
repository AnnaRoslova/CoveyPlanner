// @seed — нужны демо-данные (tools/seed-demo.ps1)
// Несколько ролей у цели: в окне цели роли — чипы «● Тело ×» + «+»; цель видна под каждой ролью (одна и та же, прогресс общий),
// в «камнях» считается один раз, у занятий — роли цели. Перетаскивание цели на другую роль — перенос (роль заменяется),
// с Shift — роль добавляется (у призрака подпись «+ роль»).
import puppeteer from 'puppeteer-core';

const BASE = process.env.PLANNER_URL ?? 'http://localhost:5071/';
const SHOTS = process.argv[2] ?? '.';
const results = [];
const consoleErrors = [];
const browser = await puppeteer.launch({
  executablePath: process.env.PLANNER_BROWSER ?? 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  headless: true, defaultViewport: { width: 1600, height: 1300 }, // левая панель помещается целиком
});
const page = await browser.newPage();
page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text()); });
page.on('pageerror', e => consoleErrors.push(e.message));
const sleep = ms => new Promise(r => setTimeout(r, ms));
const settle = () => sleep(600);
function assert(c, m) { if (!c) throw new Error(m); }
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-goal-roles-${results.length}.png` }); }
  await page.keyboard.up('Shift');
  if (await page.$('.editor')) { await page.keyboard.press('Escape'); await settle(); }
  if (await page.$('.editor')) { await page.keyboard.press('Escape'); await settle(); }
}
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.role'); await settle(); }

const titleOf = el => [...el.querySelector('.tx').childNodes].filter(n => !n.classList?.contains('pills') && !n.classList?.contains('note') && !n.classList?.contains('goal-days')).map(n => n.textContent).join('').trim();
const T = `const titleOf = ${titleOf};`;
const roleSel = name => `[...document.querySelectorAll('.role')].find(r => r.querySelector('.role-h .name')?.textContent.trim() === ${JSON.stringify(name)})`;
const goalsOf = name => page.evaluate(`(() => { ${T} const r = ${roleSel(name)}; return [...r.querySelectorAll('.item')].map(i => ({ title: titleOf(i), done: i.classList.contains('done'), progress: i.querySelector('.progress')?.textContent.trim() ?? null })); })()`);
const titlesOf = async name => (await goalsOf(name)).map(g => g.title);
const goalIn = async (role, title) => (await goalsOf(role)).find(g => g.title === title);
const rocks = () => page.$eval('.rocks b', b => b.textContent);
async function boxOf(src) {
  const el = (await page.evaluateHandle(src)).asElement();
  assert(el, `не найдено: ${src.slice(0, 80)}`);
  await el.evaluate(e => e.scrollIntoView({ block: 'nearest' }));
  await sleep(100);
  return el.boundingBox();
}
const goalBox = (role, title) => boxOf(`(() => { ${T} return [...${roleSel(role)}.querySelectorAll('.item')].find(i => titleOf(i) === ${JSON.stringify(title)}); })()`);
const roleBox = role => boxOf(roleSel(role));
const dayItem = (i, title) => page.evaluate(`(() => { ${T} const it = [...document.querySelectorAll('.day')[${i}].querySelectorAll('.item')].find(x => titleOf(x) === ${JSON.stringify(title)}); return it ? { rock: !!it.querySelector('.pill.rock'), dots: it.querySelectorAll('.role-dots i').length } : null; })()`);
async function dragGoal(role, title, toRole, { shift = false, beforeUp } = {}) {
  const from = await goalBox(role, title);
  const to = await roleBox(toRole);
  const sx = from.x + 30, sy = from.y + Math.min(from.height / 2, 12), x = to.x + 40, y = to.y + to.height - 20;
  await page.mouse.move(sx, sy); await page.mouse.down();
  for (let i = 1; i <= 14; i++) { await page.mouse.move(sx + (x - sx) * i / 14, sy + (y - sy) * i / 14); await sleep(16); }
  if (shift) { await page.keyboard.down('Shift'); await page.mouse.move(x, y + 1); }
  await sleep(200);
  if (beforeUp) await beforeUp();
  await page.mouse.up();
  if (shift) await page.keyboard.up('Shift');
  await sleep(1000);
}
async function openGoal(role, title) {
  await page.evaluate(`(() => { ${T} [...${roleSel(role)}.querySelectorAll('.item')].find(i => titleOf(i) === ${JSON.stringify(title)}).querySelector('.title').click(); })()`);
  await settle();
  assert(await page.$('.editor[aria-label="Цель недели"]'), 'окно цели не открылось');
}
const editorRoles = () => page.$$eval('.editor .tags .tag .tag-name', ts => ts.map(t => t.textContent.trim()));

await load();

await check('окно цели: «+» → «Семья» — цель видна и под «Тело», и под «Семья»; камней столько же', async () => {
  const before = await rocks();
  await openGoal('Тело', 'Бег');
  assert(JSON.stringify(await editorRoles()) === '["Тело"]', `роли: ${await editorRoles()}`);
  assert(!(await page.$('.editor .tag-x')), 'у единственной роли есть «×»');
  await page.click('.editor .tag-add'); await settle();
  await page.evaluate(() => [...document.querySelectorAll('.editor .popover .option')].find(o => o.textContent.trim() === 'Семья').click());
  await settle();
  await page.keyboard.press('Escape'); await settle();
  assert(JSON.stringify(await editorRoles()) === '["Тело","Семья"]', `роли: ${await editorRoles()}`);
  await page.screenshot({ path: `${SHOTS}/goal-roles-editor.png` });
  await page.click('.editor .btn:not(.quiet):not(.danger)'); await sleep(1200);
  assert((await titlesOf('Тело')).includes('Бег') && (await titlesOf('Семья')).at(-1) === 'Бег', `Семья: ${await titlesOf('Семья')}`);
  assert(await rocks() === before, `камней: ${before} → ${await rocks()}`);
  await page.screenshot({ path: `${SHOTS}/goal-roles-panel.png` });
  await load();
  assert((await titlesOf('Семья')).includes('Бег'), 'после перезагрузки');
});

await check('прогресс общий: отметили занятие — «Бег» выполнен (1/1) и под «Тело», и под «Семья»; у занятия две точки ролей', async () => {
  await openGoal('Семья', 'Бег');
  await page.evaluate(() => { const s = [...document.querySelectorAll('.editor .stepper .step')].find(b => b.title === 'Меньше'); s.click(); s.click(); });
  await settle();
  await page.click('.editor .btn:not(.quiet):not(.danger)'); await sleep(1000);
  await page.evaluate(`(() => { ${T} [...${roleSel('Тело')}.querySelectorAll('.item')].find(i => titleOf(i) === 'Бег').querySelector('.ck-hit').click(); })()`);
  await sleep(1200);
  const [body, family] = [await goalIn('Тело', 'Бег'), await goalIn('Семья', 'Бег')];
  assert(body.done && family.done, `выполнено: ${JSON.stringify([body, family])}`);
  const days = await page.evaluate(`(() => { ${T} return [...document.querySelectorAll('.day')].map((d, i) => [...d.querySelectorAll('.item')].some(x => titleOf(x) === 'Бег' && x.querySelector('.role-dots')) ? i : -1).filter(i => i >= 0); })()`);
  assert(days.length > 0, 'у занятий «Бега» нет точек двух ролей');
});

await check('перетаскивание «Бег» из «Семья» в «Работа» — «Семья» заменяется на «Работа», «Тело» остаётся', async () => {
  await dragGoal('Семья', 'Бег', 'Работа');
  assert(!(await titlesOf('Семья')).includes('Бег'), 'осталась под «Семьёй»');
  assert((await titlesOf('Работа')).at(-1) === 'Бег', `Работа: ${await titlesOf('Работа')}`);
  assert((await titlesOf('Тело')).includes('Бег'), 'пропала из «Тела»');
});

await check('с Shift: «100 страниц» из «Ум» в «Дух» — роль добавляется, у призрака «+ роль»', async () => {
  let badge = null;
  await dragGoal('Ум', '100 страниц', 'Дух', {
    shift: true,
    beforeUp: async () => {
      badge = await page.$eval('.drag-ghost', g => g.classList.contains('adds-role') ? getComputedStyle(g, '::after').content : null).catch(() => null);
      await page.screenshot({ path: `${SHOTS}/goal-roles-shift.png` });
    },
  });
  assert(badge === '"+ роль"', `подпись: ${badge}`);
  assert((await titlesOf('Ум')).includes('100 страниц') && (await titlesOf('Дух')).includes('100 страниц'), `Ум: ${await titlesOf('Ум')}, Дух: ${await titlesOf('Дух')}`);
  await load();
  assert((await titlesOf('Дух')).includes('100 страниц') && (await titlesOf('Ум')).includes('100 страниц'), 'после перезагрузки');
});

await check('в окне цели «×» снимает роль, последнюю снять нельзя', async () => {
  await openGoal('Дух', '100 страниц');
  assert((await page.$$('.editor .tag-x')).length === 2, 'нет «×» у двух ролей');
  await page.click('.editor .tag-x'); await settle(); // первая по порядку — «Ум»
  assert(JSON.stringify(await editorRoles()) === '["Дух"]', `роли: ${await editorRoles()}`);
  assert(!(await page.$('.editor .tag-x')), 'у последней роли есть «×»');
  await page.click('.editor .btn:not(.quiet):not(.danger)'); await sleep(1000);
  assert(!(await titlesOf('Ум')).includes('100 страниц'), 'осталась под «Умом»');
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));

// @seed — нужны демо-данные (tools/seed-demo.ps1): цель «Бег» (Тело), 3 раза, ~30 мин, заметка «Парк, лёгкий темп»
// Цель — шаблон занятий: окно цели, занятие берёт её значения, «своё» / «как у цели»,
// роль и квадрант занятия — только у цели; длительность: пилюля и сумма в заголовке дня.
import puppeteer from 'puppeteer-core';

const BASE = process.env.PLANNER_URL ?? 'http://localhost:5071/';
const SHOTS = process.argv[2] ?? '.';
const results = [];
const consoleErrors = [];
const browser = await puppeteer.launch({
  executablePath: process.env.PLANNER_BROWSER ?? 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  headless: true, defaultViewport: { width: 1600, height: 1300 },
});
const page = await browser.newPage();
page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text()); });
page.on('pageerror', e => consoleErrors.push(e.message));
const sleep = ms => new Promise(r => setTimeout(r, ms));
const settle = () => sleep(600);
function assert(c, m) { if (!c) throw new Error(m); }
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-template-${results.length}.png` }); }
}
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.role'); await settle(); }

const titleOf = el => [...el.querySelector('.tx').childNodes].filter(n => !n.classList?.contains('pills') && !n.classList?.contains('note') && !n.classList?.contains('days')).map(n => n.textContent).join('').trim();
const T = `const titleOf = ${titleOf};`;
const goalSel = title => `(() => { ${T} return [...document.querySelectorAll('.role .item')].find(i => titleOf(i) === ${JSON.stringify(title)}); })()`;
const dayItemSel = (i, title) => `(() => { ${T} return [...document.querySelectorAll('.day')[${i}].querySelectorAll('.item')].find(it => titleOf(it) === ${JSON.stringify(title)}); })()`;
const dayItem = (i, title) => page.evaluate(`(() => { const it = ${dayItemSel(i, title)}; return it ? {
  time: it.querySelector('.pill.time')?.textContent ?? null,
  dur: it.querySelector('.pill.dur')?.textContent ?? null,
  rock: !!it.querySelector('.pill.rock') } : null; })()`);
const dayLoad = i => page.evaluate(i => document.querySelectorAll('.day')[i].querySelector('.day-h .load')?.textContent ?? null, i);

async function boxOf(src) {
  const el = (await page.evaluateHandle(src)).asElement();
  assert(el, `не найдено: ${src.slice(0, 80)}`);
  await el.evaluate(e => e.scrollIntoView({ block: 'nearest' }));
  await sleep(100);
  return el.boundingBox();
}
async function mouseDrag(from, x, y) {
  const sx = from.x + 30, sy = from.y + Math.min(from.height / 2, 12);
  await page.mouse.move(sx, sy); await page.mouse.down();
  for (let i = 1; i <= 14; i++) { await page.mouse.move(sx + (x - sx) * i / 14, sy + (y - sy) * i / 14); await sleep(16); }
  await sleep(150);
  await page.mouse.up(); await settle();
}
async function openBy(sel, label) {
  const el = (await page.evaluateHandle(sel)).asElement();
  assert(el, 'запись не найдена');
  await el.$eval('.tx', e => e.click());
  await page.waitForSelector(`.editor[aria-label="${label}"]`, { timeout: 3000 });
  await sleep(200);
}
const openGoal = title => openBy(goalSel(title), 'Цель недели');
const openSession = (i, title) => openBy(dayItemSel(i, title), 'Дело');
const clickIn = (sel, text) => page.evaluate((sel, text) =>
  [...document.querySelectorAll(sel)].find(b => b.textContent.trim() === text).click(), sel, text);
const save = async () => { await clickIn('.editor .btn', 'Сохранить'); await settle(); };
async function setTime(value) {
  await page.$eval('.editor input[type=time]', (e, v) => {
    e.value = v; e.dispatchEvent(new Event('change', { bubbles: true }));
  }, value);
  await sleep(150);
}
const fieldHas = (label, sel) => page.evaluate((label, sel) => {
  const f = [...document.querySelectorAll('.editor .field')].find(x => x.querySelector('.label')?.textContent.trim() === label);
  return !!f?.querySelector(sel);
}, label, sel);

await load();

await check('окно цели: время 07:00 и квадрант II — сохраняются', async () => {
  await openGoal('Бег');
  assert(await page.$eval('.editor textarea', e => e.value) === 'Парк, лёгкий темп', 'нет заметки цели');
  await setTime('07:00');
  await clickIn('.editor .segments button', 'II');
  await save();
  await openGoal('Бег');
  assert(await page.$eval('.editor input[type=time]', e => e.value) === '07:00', 'время не сохранилось');
  await page.keyboard.press('Escape'); await settle();
});

await check('цель → среда: занятие по шаблону — 07:00, ~30 мин, «камень»; в заголовке дня «~30 мин»', async () => {
  const from = await boxOf(goalSel('Бег'));
  const wed = await boxOf(`document.querySelectorAll('.day')[2]`);
  await mouseDrag(from, wed.x + 40, wed.y + wed.height - 40);
  const s = await dayItem(2, 'Бег');
  assert(s?.time === '07:00' && s.dur === '~30 мин' && s.rock, JSON.stringify(s));
  const load2 = await dayLoad(2);
  assert(load2 === '~30 мин', `заголовок: ${load2}`);
});

await check('окно занятия: роль и квадрант «задаётся у цели», без выбора', async () => {
  await openSession(2, 'Бег');
  assert(await fieldHas('Роль', '.from-goal'), 'роль можно выбирать');
  assert(await fieldHas('Квадрант', '.from-goal'), 'квадрант можно выбирать');
  assert(!(await page.$('.editor .own')), 'метка «своё» без изменений');
  await page.keyboard.press('Escape'); await settle();
});

await check('своё время у занятия: метка «своё»; изменение цели его не трогает', async () => {
  await openSession(2, 'Бег');
  await setTime('08:15');
  assert(await page.$('.editor .own'), 'нет метки «своё»');
  await save();
  await openGoal('Бег');
  await setTime('06:30');
  await save();
  assert((await dayItem(2, 'Бег')).time === '08:15', `время: ${(await dayItem(2, 'Бег')).time}`);
});

await check('«как у цели» возвращает время цели; дальше занятие снова следует за целью', async () => {
  await openSession(2, 'Бег');
  await clickIn('.editor .as-goal', 'как у цели');
  assert(await page.$eval('.editor input[type=time]', e => e.value) === '06:30', 'время не вернулось');
  await save();
  await openGoal('Бег');
  await setTime('06:45');
  await save();
  assert((await dayItem(2, 'Бег')).time === '06:45', `время: ${(await dayItem(2, 'Бег')).time}`);
});

await check('длительность дела: 1,5 ч — пилюля «~1,5 ч», сумма дня складывается', async () => {
  await page.evaluate(`(() => { ${T} return [...document.querySelectorAll('.day')[2].querySelectorAll('.item')].find(it => titleOf(it) === 'Записаться к стоматологу'); })()`);
  await openSession(2, 'Записаться к стоматологу');
  await clickIn('.editor .segments button', '1,5 ч');
  await save();
  assert((await dayItem(2, 'Записаться к стоматологу')).dur === '~1,5 ч', 'нет пилюли');
  assert(await dayLoad(2) === '~2 ч', `заголовок: ${await dayLoad(2)}`);
  await load();
  assert(await dayLoad(2) === '~2 ч', 'после перезагрузки');
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));

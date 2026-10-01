// @seed — нужны демо-данные (tools/seed-demo.ps1)
// Мелкие правки после шага 7: иконка категории в «Записать мысль», две строки у названия категории, значок заметки.
import puppeteer from 'puppeteer-core';

const BASE = process.env.PLANNER_URL ?? 'http://localhost:5071/';
const SHOTS = process.argv[2] ?? '.';
const results = [];
const consoleErrors = [];
const browser = await puppeteer.launch({
  executablePath: process.env.PLANNER_BROWSER ?? 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  headless: true, defaultViewport: { width: 1300, height: 900 },
});
const page = await browser.newPage();
page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text()); });
const sleep = ms => new Promise(r => setTimeout(r, ms));
function assert(c, m) { if (!c) throw new Error(m); }
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-tweaks-${results.length}.png` }); }
}
const api = (method, path, body) => page.evaluate(async (method, path, body) => {
  const r = await fetch('/api/' + path, { method, headers: { 'Content-Type': 'application/json' }, body: body ? JSON.stringify(body) : undefined });
  return r.status === 204 ? null : r.json();
}, method, path, body);
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day'); await sleep(400); }

await load();

await check('«Записать мысль»: видна только иконка категории, поле не зажато; в списке — названия', async () => {
  const icon = await page.$eval('.cat-pick .cat-icon', e => e.textContent.trim());
  assert(icon === '✈️', `иконка «${icon}»`); // последняя использованная в демо — «Когда-нибудь поехать»
  const inputWidth = await page.$eval('.quick input', e => e.clientWidth);
  assert(inputWidth >= 140, `ширина поля ${inputWidth}`);
  const options = await page.$$eval('.quick select option', os => os.map(o => o.textContent));
  assert(options.includes('💡 Идеи'), `в списке ${options}`);
  const ideas = await page.$$eval('.quick select option', os => os.find(o => o.textContent.includes('Идеи')).value);
  await page.select('.quick select', ideas); await sleep(200);
  assert(await page.$eval('.cat-pick .cat-icon', e => e.textContent.trim()) === '💡', 'иконка не сменилась');
});

await check('категория без эмодзи — короткое название, обрезанное по ширине: «Проекты» и «Покупки» различаются', async () => {
  const a = await api('POST', 'categories', { name: 'Проекты', emoji: null });
  const b = await api('POST', 'categories', { name: 'Покупки', emoji: null });
  const c = await api('POST', 'categories', { name: 'Очень длинное название категории', emoji: null });
  await load();
  const shown = async id => {
    await page.select('.quick select', String(id)); await sleep(200);
    return page.$eval('.cat-pick .cat-icon', e => ({ text: e.textContent.trim(), visible: e.clientWidth, full: e.scrollWidth }));
  };
  const pa = await shown(a.id), pb = await shown(b.id), pc = await shown(c.id);
  assert(pa.text === 'Проекты' && pb.text === 'Покупки', `${pa.text} / ${pb.text}`);
  assert(pa.full <= pa.visible + 1, '«Проекты» обрезано');
  assert(pc.full > pc.visible && pc.visible <= 66, `длинное не обрезано: ${pc.visible}/${pc.full}`);
  assert(await page.$eval('.quick input', e => e.clientWidth) >= 110, 'поле ввода зажато');
});

await check('длинное название категории — две строки, без многоточия в одну строку', async () => {
  await api('POST', 'categories', { name: 'Когда-нибудь поехать далеко-далеко', emoji: '🌍' });
  await load();
  const h = await page.evaluate(() => {
    const n = [...document.querySelectorAll('.cat-h .name')].find(e => e.textContent.includes('далеко-далеко'));
    return { height: n.getBoundingClientRect().height, lh: parseFloat(getComputedStyle(n).lineHeight) };
  });
  assert(h.height > h.lh * 1.5, `высота ${h.height}, строка ${h.lh}`);
  await page.screenshot({ path: `${SHOTS}/tweaks-feed.png`, clip: { x: 1040, y: 0, width: 260, height: 700 } });
});

await check('значок заметки — в дне и в ленте, подсказка с текстом', async () => {
  const week = await api('GET', 'week/' + new Date().toISOString().slice(0, 10));
  const t = week.tasks.find(x => x.title === 'Оплатить интернет');
  await api('PUT', `tasks/${t.id}`, { title: t.title, notes: 'до 5 числа', roleId: null, quadrant: null, startTime: null });
  const board = await api('GET', 'board');
  const s = board.somedayTasks.find(x => x.title === 'Камчатка');
  await api('PUT', `tasks/${s.id}`, { title: s.title, notes: 'летом, вулканы', roleId: null, quadrant: null, startTime: null });
  await load();
  const dayNote = await page.evaluate(() => [...document.querySelectorAll('.day .item')].find(i => i.textContent.includes('Оплатить интернет'))?.querySelector('.note')?.title);
  assert(dayNote === 'до 5 числа', `в дне: ${dayNote}`);
  const withoutNote = await page.evaluate(() => [...document.querySelectorAll('.day .item')].find(i => i.textContent.includes('Написать отчёт'))?.querySelector('.note'));
  assert(!withoutNote, 'значок у дела без заметки');
  await page.evaluate(() => [...document.querySelectorAll('.cat-h')].find(h => h.textContent.includes('Когда-нибудь поехать') && !h.textContent.includes('далеко')).click());
  await sleep(500);
  const feedNote = await page.evaluate(() => [...document.querySelectorAll('.cat-b .item')].find(i => i.textContent.includes('Камчатка'))?.querySelector('.note')?.title);
  assert(feedNote === 'летом, вулканы', `в ленте: ${feedNote}`);
  await page.screenshot({ path: `${SHOTS}/tweaks-notes.png` });
});

await check('корзина закрывается по Esc', async () => {
  await page.click('.trash'); await sleep(500);
  assert(await page.$('.trash-popup'), 'корзина не открылась');
  await page.keyboard.press('Escape'); await sleep(300);
  assert(!(await page.$('.trash-popup')), 'Esc не закрыл корзину');
});

await browser.close();
console.log(results.join('\n'));
console.log(consoleErrors.length ? `\nОшибки консоли:\n${consoleErrors.join('\n')}` : '\nОшибок в консоли нет');

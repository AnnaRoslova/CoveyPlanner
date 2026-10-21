// @seed — нужны демо-данные (tools/seed-demo.ps1)
// «📈 Итоги» и «⚙ Настройки» (docs/specs/batch-ux.md, BI): в шапке недели два значка с подписями; «Итоги» — вкладки
// «Привычки» (по умолчанию) · «Выполненное» · «Отчёт», вкладка запоминается; ссылка «привычка» в окне цели ведёт на «Привычки»;
// «Настройки» — тема (сразу применяется) и «Как это устроено».
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
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-totals-${results.length}.png` }); }
  for (let i = 0; i < 3 && await page.$('.editor, .habits, .done-screen, .report-screen, .help-screen'); i++) { await page.keyboard.press('Escape'); await settle(); }
}
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day'); await settle(); }
const onTab = () => page.$eval('.totals-tabs .tt.on', b => b.textContent.trim());
const clickTab = t => page.evaluate(t => [...document.querySelectorAll('.totals-tabs .tt')].find(b => b.textContent.trim() === t).click(), t);

await load();

await check('BI1: в шапке недели — «Итоги» и «Настройки» с подписями при наведении', async () => {
  const icons = await page.$$eval('.week-h .icon-b', bs => bs.map(b => ({ label: b.getAttribute('aria-label'), title: b.title })));
  assert(JSON.stringify(icons) === JSON.stringify([{ label: 'Итоги', title: 'Итоги (H)' }, { label: 'Настройки', title: 'Настройки' }]), JSON.stringify(icons));
});

await check('BI2: «Итоги» по умолчанию — «Привычки»; вкладки переключают экран', async () => {
  await page.click('.week-h .totals-b'); await settle();
  assert(await onTab() === 'Привычки' && await page.$('.habits'), `вкладка: ${await onTab()}`);
  await clickTab('Выполненное'); await settle();
  assert(await onTab() === 'Выполненное' && await page.$('.done-screen'), 'не «Выполненное»');
  await clickTab('Отчёт'); await settle();
  assert(await onTab() === 'Отчёт' && await page.$('.report-screen'), 'не «Отчёт»');
  await (await page.$('.report-screen')).screenshot({ path: `${SHOTS}/totals-report.png` });
});

await check('BI2: вкладка запоминается — после перезагрузки «Итоги» открываются на «Отчёте»', async () => {
  await load();
  await page.click('.week-h .totals-b'); await settle();
  assert(await onTab() === 'Отчёт', `вкладка: ${await onTab()}`);
  await clickTab('Привычки'); await settle();
});

await check('BI3: «🫙 открыть привычку» в окне цели — «Итоги · Привычки»', async () => {
  await page.evaluate(() => [...document.querySelectorAll('.role .item')].find(i => i.querySelector('.title')?.textContent.trim() === 'Бег').querySelector('.title').click());
  await settle();
  await page.click('.editor .more-b'); await sleep(300);
  await page.evaluate(() => [...document.querySelectorAll('.more-menu .option')].find(o => o.textContent.includes('открыть привычку')).click());
  await settle();
  assert(await page.$('.habits') && await onTab() === 'Привычки', 'не экран привычек');
});

await check('BI5: «Настройки» — тема меняется сразу; «Как это устроено» открывает экран', async () => {
  await page.click('.week-h .settings-b'); await settle();
  assert(await page.$('.editor.settings .themes .theme'), 'нет списка тем');
  await page.click('.editor.settings .theme[data-key="peony"]'); await settle();
  assert(await page.evaluate(() => document.documentElement.dataset.theme) === 'peony', 'тема не сменилась');
  await page.click('.editor.settings .theme[data-key="lotus"]'); await settle();
  await page.click('.editor.settings .help-link'); await settle();
  assert(await page.$('.help-screen'), '«Как это устроено» не открылось');
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);

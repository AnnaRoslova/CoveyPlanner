// @no-autologin — сессия кончилась посреди работы (docs/specs/accounts.md, Q1–Q3, T4): изменение не теряется —
// окно входа поверх экрана (без перезагрузки), «Изменения не сохранены (1)», после входа тем же пользователем — сохранено.
import puppeteer from 'puppeteer-core';
import { lastCode, signIn } from './lib/login.mjs';

const BASE = process.env.PLANNER_URL ?? 'http://localhost:5071/';
const SHOTS = process.argv[2] ?? '.';
const EMAIL = 'e2e@localhost';
const results = [];
const browser = await puppeteer.launch({
  executablePath: process.env.PLANNER_BROWSER ?? 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  headless: true, defaultViewport: { width: 1400, height: 900 },
});
const page = await browser.newPage();
const sleep = ms => new Promise(r => setTimeout(r, ms));
function assert(c, m) { if (!c) throw new Error(m); }
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-session-expired-${results.length}.png` }); }
}
const post = (path, body) => page.evaluate(async (p, b) => (await fetch('/api/' + p, {
  method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Planner': '1' }, body: JSON.stringify(b ?? {}),
})).status, path, body);

await page.goto(BASE, { waitUntil: 'networkidle0', timeout: 60000 });
await signIn(page, BASE, EMAIL);

await check('сессия кончилась → изменение ждёт, поверх экрана окно входа с почтой', async () => {
  // Часы сервера — на полторы минуты вперёд, чтобы новый код можно было запросить сразу (не чаще раза в минуту)
  assert(await post('dev/clock', { days: 0.0011 }) === 204, 'часы не сдвинулись');
  assert(await post('auth/logout-all') === 204, 'выход везде не прошёл');

  await page.type('.quick input', 'Мысль после выхода');
  await page.keyboard.press('Enter');
  await page.waitForSelector('.login-over', { timeout: 10000 });
  const lead = await page.$eval('.login-over .login-lead', e => e.textContent.trim());
  assert(lead === 'Изменения не сохранены (1). Войдите, чтобы сохранить.', lead);
  assert(await page.$eval('.login-over #login-email', i => i.value) === EMAIL, 'почта не подставлена');
  assert(await page.$('.app'), 'планировщик под окном пропал (перезагрузка?)');
});

await check('вход тем же пользователем → изменение сохранено, окно закрылось', async () => {
  await page.click('.login-over .login-form button[type=submit]');
  await page.waitForSelector('.login-over #login-code');
  await page.type('.login-over #login-code', await lastCode(BASE, EMAIL));
  await page.waitForFunction(() => !document.querySelector('.login-over'), { timeout: 10000 });
  let saved = false;
  for (let i = 0; i < 20 && !saved; i++) {
    const board = await page.evaluate(async () => (await (await fetch('/api/board')).json()).somedayTasks.map(t => t.title));
    saved = board.includes('Мысль после выхода');
    if (!saved) await sleep(250);
  }
  assert(saved, 'мысль не сохранилась на сервере');
});

await check('после перезагрузки мысль на месте', async () => {
  await page.reload({ waitUntil: 'networkidle0' });
  await page.waitForSelector('.app', { timeout: 30000 });
  const titles = await page.$$eval('.someday-panel, .feed', ps => ps.map(p => p.textContent).join(' '));
  const all = titles || await page.$eval('body', b => b.textContent);
  assert(all.includes('Мысль после выхода'), 'нет мысли в ленте');
});

await browser.close();
results.forEach(r => console.log(r));
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);

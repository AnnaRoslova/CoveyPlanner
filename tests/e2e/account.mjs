// @no-autologin — «Аккаунт» (docs/specs/accounts.md, P1–P3, S5, T4): устройства («это устройство»), выгрузка JSON,
// удаление с подтверждением почтой → экран входа «до …», вход кодом в эти 7 дней — «Аккаунт восстановлен», данные на месте.
import puppeteer from 'puppeteer-core';
import { lastCode, signIn } from './lib/login.mjs';
import { openThemes } from './lib/nav.mjs';

const BASE = process.env.PLANNER_URL ?? 'http://localhost:5071/';
const SHOTS = process.argv[2] ?? '.';
const ADMIN = 'e2e@localhost';
const USER = 'acc@localhost';
const results = [];
const browser = await puppeteer.launch({
  executablePath: process.env.PLANNER_BROWSER ?? 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  headless: true, defaultViewport: { width: 1400, height: 900 },
});
const admin = await browser.newPage();
const context = await browser.createBrowserContext();
const page = await context.newPage();
const sleep = ms => new Promise(r => setTimeout(r, ms));
function assert(c, m) { if (!c) throw new Error(m); }
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-account-${results.length}.png` }); }
}
const post = (p, path, body) => p.evaluate(async (u, b) => {
  const r = await fetch('/api/' + u, { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Planner': '1' }, body: JSON.stringify(b ?? {}) });
  return r.ok && r.status !== 204 ? r.json() : r.status;
}, path, body);

// Администратор приглашает пользователя; тот регистрируется по ссылке
await admin.goto(BASE, { waitUntil: 'networkidle0', timeout: 60000 });
await signIn(admin, BASE, ADMIN);
const created = await post(admin, 'admin/invites', { email: USER, days: 7 });
await page.goto(created.link, { waitUntil: 'networkidle0', timeout: 60000 });
await page.waitForSelector('.invite-card #login-email');
await page.click('.login-form button[type=submit]');
await page.waitForSelector('#login-code');
await page.type('#login-code', await lastCode(BASE, USER));
await page.waitForSelector('.app', { timeout: 30000 });

await check('«⚙ Настройки → Аккаунт»: почта, это устройство', async () => {
  await openThemes(page);
  await page.click('.account-open');
  await page.waitForSelector('.account-screen .account-sessions li');
  assert((await page.$eval('.account-screen .account-email', e => e.textContent.trim())) === USER, 'почта');
  const sessions = await page.$$eval('.account-sessions li', ls => ls.map(l => l.textContent));
  assert(sessions.length === 1 && sessions[0].includes('это устройство'), sessions.join(' | '));
});

await check('«Выгрузить мои данные» — JSON с моими таблицами', async () => {
  const href = await page.$eval('.export-data', a => a.getAttribute('href'));
  const json = await page.evaluate(async h => await (await fetch(h)).json(), href);
  assert(json.format === 'coveyplanner-export' && json.account.email === USER, JSON.stringify(json).slice(0, 120));
  assert(json.tables.Roles.some(r => r.Name === 'Я') && json.tables.Categories.length === 2, 'нет стартового набора');
});

await check('удаление: кнопка — только при своей почте; удалили → экран входа «войдите кодом до …»', async () => {
  assert(await page.$eval('.delete-account', b => b.disabled), 'кнопка активна без подтверждения');
  await page.type('.delete-confirm', 'чужая@почта.рф');
  assert(await page.$eval('.delete-account', b => b.disabled), 'кнопка активна при чужой почте');
  await page.$eval('.delete-confirm', i => { i.value = ''; });
  await page.type('.delete-confirm', USER);
  await page.waitForFunction(() => !document.querySelector('.delete-account').disabled);
  await page.click('.delete-account');
  await page.waitForSelector('.login-page .deleted-note', { timeout: 30000 });
  const note = await page.$eval('.deleted-note', e => e.textContent);
  assert(note.includes('Аккаунт удалён') && note.includes('войдите кодом до'), note);
});

await check('вход кодом в эти 7 дней — «Аккаунт восстановлен», планировщик на месте', async () => {
  await post(admin, 'dev/clock', { days: 0.0011 }); // новый код — не раньше чем через минуту
  await page.$eval('#login-email', i => { i.value = ''; });
  await page.type('#login-email', USER);
  await page.click('.login-form button[type=submit]');
  await page.waitForSelector('#login-code');
  await page.type('#login-code', await lastCode(BASE, USER));
  await page.waitForSelector('.app', { timeout: 30000 });
  await page.waitForFunction(() => document.body.textContent.includes('Аккаунт восстановлен'), { timeout: 5000 });
});

await browser.close();
results.forEach(r => console.log(r));
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);

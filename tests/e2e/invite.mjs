// @no-autologin — приглашения (docs/specs/accounts.md, I2–I4, U1–U2, N1, Q3, T4): администратор создаёт ссылку, второй браузер
// регистрируется по ней (стартовый набор), в списке — «Использованные»; вход поверх окна другим пользователем — несохранённое не уходит.
import puppeteer from 'puppeteer-core';
import { lastCode, signIn, requestCode, enterCode } from './lib/login.mjs';
import { openThemes } from './lib/nav.mjs';

const BASE = process.env.PLANNER_URL ?? 'http://localhost:5071/';
const SHOTS = process.argv[2] ?? '.';
const ADMIN = 'e2e@localhost';
const NEW = 'new@localhost';
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
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await Promise.race([page.screenshot({ path: `${SHOTS}/fail-invite-${results.length}.png` }).catch(() => {}), sleep(5000)]); }
}
const post = (p, path, body) => p.evaluate(async (u, b) => (await fetch('/api/' + u, {
  method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Planner': '1' }, body: JSON.stringify(b ?? {}),
})).status, path, body);

await page.goto(BASE, { waitUntil: 'networkidle0', timeout: 60000 });
await signIn(page, BASE, ADMIN);
let link = '';
// Гость — отдельным браузером: ввод в страницу администратора не должен зависеть от второго окна
const guestBrowser = await puppeteer.launch({
  executablePath: process.env.PLANNER_BROWSER ?? 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  headless: true, defaultViewport: { width: 1400, height: 900 },
});
const guest = await guestBrowser.newPage();

await check('«⚙ Настройки → Пользователи и приглашения»: создать приглашение — ссылка один раз, «Копировать»', async () => {
  await openThemes(page);
  await page.click('.admin-open');
  await page.waitForSelector('.admin-users tbody tr');
  const users = await page.$$eval('.admin-users tbody tr', rs => rs.map(r => r.textContent));
  assert(users.length === 1 && users[0].includes(ADMIN) && users[0].includes('администратор'), users.join(' | '));
  await page.evaluate(() => [...document.querySelectorAll('.admin-tabs button')].find(b => b.textContent.includes('Приглашения')).click());
  await page.waitForSelector('.create-invite');
  await page.click('.create-invite');
  await page.waitForSelector('.invite-link');
  link = await page.$eval('.invite-link', e => e.textContent.trim());
  assert(/\/invite#[A-Za-z0-9_-]{20,}$/.test(link), link);
  assert(await page.$('.copy-link'), 'нет «Копировать»');
  const active = await page.$$eval('.admin-invites[data-status="Active"] li', ls => ls.length);
  assert(active === 1, `активных ${active}`);
});

await check('по ссылке: «Вас пригласили» → почта → код → планировщик со стартовым набором', async () => {
  await guest.bringToFront();
  await guest.goto(link, { waitUntil: 'networkidle0', timeout: 60000 });
  await guest.waitForSelector('.invite-card #login-email');
  assert((await guest.$eval('.login-title', e => e.textContent)).includes('Вас пригласили'), 'нет заголовка');
  await requestCode(guest, NEW);
  await guest.waitForSelector('#login-code');
  await enterCode(guest, await lastCode(BASE, NEW));
  await guest.waitForSelector('.app', { timeout: 30000 });
  const board = await guest.evaluate(async () => await (await fetch('/api/board')).json());
  const roles = board.roles.map(r => r.name).join(',');
  assert(roles === 'Тело,Ум,Дух,Общение и эмоции,Я,Семья,Работа,Дом', roles);
  assert(board.categories.map(c => c.name).join(',') === 'Идеи,Почитать', board.categories.map(c => c.name).join(','));
});

await check('ссылка второй раз не действует; у администратора — «Использованные» с почтой', async () => {
  const other = await guestBrowser.newPage();
  await other.deleteCookie(...(await other.cookies(BASE)));
  await guest.close();
  await other.goto(link, { waitUntil: 'networkidle0', timeout: 60000 });
  await other.waitForSelector('.invite-card .login-error');
  assert((await other.$eval('.login-error', e => e.textContent)).includes('Приглашение не действует'), 'нет сообщения');
  await other.close();
  await page.bringToFront();
  await page.click('.admin-screen .close');
  await page.waitForFunction(() => !document.querySelector('.admin-screen'));
  await openThemes(page);
  await page.click('.admin-open');
  await page.waitForSelector('.admin-screen');
  await page.evaluate(() => [...document.querySelectorAll('.admin-tabs button')].find(b => b.textContent.includes('Приглашения')).click());
  await page.waitForSelector('.admin-invites[data-status="Used"] li');
  const used = await page.$eval('.admin-invites[data-status="Used"] li', e => e.textContent);
  assert(used.includes(NEW), used);
  await page.click('.admin-screen .close');
  await page.waitForFunction(() => !document.querySelector('.admin-screen'));
});

await check('вышли, сделали изменение, вошли поверх окна другим пользователем → «Изменения прежнего аккаунта (1) не сохранены»', async () => {
  await page.bringToFront();
  assert(await post(page, 'dev/clock', { days: 0.0011 }) === 204, 'часы');
  assert(await post(page, 'auth/logout-all') === 204, 'выход');
  await page.type('.quick input', 'Мысль администратора');
  await page.keyboard.press('Enter');
  await page.waitForSelector('.login-over #login-email', { timeout: 10000 });
  await page.$eval('.login-over #login-email', i => { i.value = ''; });
  await page.type('.login-over #login-email', NEW);
  await page.click('.login-over .login-form button[type=submit]');
  await page.waitForSelector('.login-over #login-code');
  await page.type('.login-over #login-code', await lastCode(BASE, NEW));
  await page.waitForFunction(() => document.querySelector('.login-over .login-title')?.textContent.includes('Другой аккаунт'), { timeout: 10000 });
  const lead = await page.$eval('.login-over .login-lead', e => e.textContent.trim());
  assert(lead === 'Изменения прежнего аккаунта (1) не сохранены — они были для другого пользователя.', lead);
  await page.click('.login-over .login-go');
  await page.waitForSelector('.app', { timeout: 30000 });
  await sleep(500);
  const titles = await page.evaluate(async () => (await (await fetch('/api/board')).json()).somedayTasks.map(t => t.title));
  assert(!titles.includes('Мысль администратора'), 'мысль ушла в чужой аккаунт');
});

await guestBrowser.close();
await browser.close();
results.forEach(r => console.log(r));
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);

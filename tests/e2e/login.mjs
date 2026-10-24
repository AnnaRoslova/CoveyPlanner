// @no-autologin — вход кодом на почту (docs/specs/accounts.md, L1–L7, S4, P4, T4): экран входа, опечатка видна и правится,
// неверный код, настоящий код из почтового ящика разработки, вход держится после перезагрузки, «Выйти», страница «Какие данные мы храним».
import puppeteer from 'puppeteer-core';
import { lastCode, requestCode, enterCode } from './lib/login.mjs';
import { openThemes } from './lib/nav.mjs';

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
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-login-${results.length}.png` }); }
}
const text = sel => page.$eval(sel, e => e.textContent.trim());

await check('без входа — экран входа, а не планировщик; ссылки на почтовый ящик и «Какие данные мы храним»', async () => {
  await page.goto(BASE, { waitUntil: 'networkidle0', timeout: 60000 });
  await page.waitForSelector('.login-page #login-email', { timeout: 30000 });
  assert(!(await page.$('.app')), 'планировщик виден без входа');
  assert(await page.$('.login-mailbox'), 'нет ссылки на почтовый ящик разработки');
  assert(await page.$('a.login-privacy[href="/privacy"]'), 'нет ссылки «Какие данные мы храним»');
});

await check('опечатка в почте: ответ тот же, почта крупно — «изменить» возвращает к полю с введённым', async () => {
  await requestCode(page, 'e2e@locahost');
  await page.waitForSelector('#login-code');
  assert(await text('.login-email') === 'e2e@locahost', `крупно: ${await text('.login-email')}`);
  assert((await text('.login-hint')).includes('Если этот адрес зарегистрирован'), 'нет нейтральной подсказки');
  await page.click('.login-change');
  await page.waitForSelector('#login-email');
  assert(await page.$eval('#login-email', i => i.value) === 'e2e@locahost', 'введённое не сохранилось');
});

await check('неверный код — «Неверный код, осталось попыток: 4»', async () => {
  await requestCode(page, EMAIL);
  await page.waitForSelector('#login-code');
  const real = await lastCode(BASE, EMAIL);
  await enterCode(page, real === '000000' ? '111111' : '000000');
  await page.waitForSelector('.login-error');
  assert(await text('.login-error') === 'Неверный код, осталось попыток: 4', await text('.login-error'));
  assert((await text('.login-resend')).startsWith('Новый код можно запросить через'), await text('.login-resend'));
});

await check('код из письма — вход, планировщик на месте', async () => {
  await enterCode(page, await lastCode(BASE, EMAIL));
  await page.waitForSelector('.app', { timeout: 30000 });
  const days = await page.$$eval('.day', ds => ds.length);
  assert(days === 7, `дней ${days}`);
});

await check('после перезагрузки — всё ещё вошли (cookie)', async () => {
  await page.reload({ waitUntil: 'networkidle0' });
  await page.waitForSelector('.app', { timeout: 30000 });
});

await check('почтовый ящик разработки — страница с письмом', async () => {
  const mail = await browser.newPage();
  await mail.goto(new URL('dev/mailbox', BASE).href);
  const body = await mail.$eval('body', b => b.textContent);
  await mail.close();
  assert(body.includes('Код входа') && body.includes(EMAIL), body.slice(0, 200));
});

await check('«⚙ Настройки» — почта аккаунта; «Выйти» → экран входа', async () => {
  await openThemes(page);
  assert(await text('.account-email') === EMAIL, await text('.account-email'));
  await page.click('.logout');
  await page.waitForSelector('.login-page #login-email', { timeout: 30000 });
  const me = await page.evaluate(async () => (await fetch('/api/auth/me')).status);
  assert(me === 401, `после выхода /api/auth/me: ${me}`);
});

await check('«Какие данные мы храним» — открывается без входа', async () => {
  const context = await browser.createBrowserContext();
  const fresh = await context.newPage();
  await fresh.goto(new URL('privacy', BASE).href, { waitUntil: 'networkidle0', timeout: 60000 });
  await fresh.waitForSelector('.privacy h1');
  const all = await fresh.$eval('.privacy', e => e.textContent);
  await context.close();
  assert(all.includes('Чего не храним') && all.includes('IP-адресов'), all.slice(0, 200));
});

await browser.close();
results.forEach(r => console.log(r));
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);

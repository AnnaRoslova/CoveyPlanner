// @no-autologin — вспомогательное: настоящий вход кодом через почтовый ящик разработки (docs/specs/accounts.md, L, M2, T4).
const sleep = ms => new Promise(r => setTimeout(r, ms));

/** Код из последнего письма на почту (страница /dev/mailbox/latest — только с этого компьютера, режим Folder). */
export async function lastCode(base, email) {
  for (let i = 0; i < 20; i++) {
    const r = await fetch(new URL(`dev/mailbox/latest?to=${encodeURIComponent(email)}`, base));
    if (r.ok) {
      const code = (await r.json()).subject.replace(/\D/g, '');
      if (code.length === 6) return code; // последнее письмо — приглашение, а не код: подождать письма с кодом
    }
    await sleep(250);
  }
  throw new Error(`нет письма для ${email}`);
}

/** Ввести почту (поле очищается) и нажать «Получить код». */
export async function requestCode(page, email) {
  await page.waitForSelector('#login-email');
  await page.$eval('#login-email', i => { i.value = ''; });
  await page.type('#login-email', email);
  await page.click('.login-form button[type=submit]');
}

/** Ввести код — шесть цифр, вход сам. */
export async function enterCode(page, code) {
  await page.waitForSelector('#login-code');
  await page.$eval('#login-code', i => { i.value = ''; });
  await page.type('#login-code', code);
}

/** Войти целиком: экран входа → код из письма → планировщик. */
export async function signIn(page, base, email) {
  await requestCode(page, email);
  await page.waitForSelector('#login-code');
  await enterCode(page, await lastCode(base, email));
  await page.waitForSelector('.app', { timeout: 30000 });
}

/**
 * Нажать кнопку, после которой страница перезагружается («Понятно» в окне «Другой аккаунт»), и дождаться планировщика
 * в новом документе. Не по времени и не по `.app`: прежний `.app` ещё на экране, пока старая страница не выгружена.
 * Признак готовности — метки прежнего документа нет, планировщик с днями на экране, окна входа нет.
 */
export async function clickAndReload(page, selector) {
  const old = await page.evaluate(() => (window.__e2eDocument = Math.random()));
  await Promise.all([page.waitForNavigation({ waitUntil: 'load', timeout: 30000 }), page.click(selector)]);
  await page.waitForFunction(o => window.__e2eDocument !== o && !!document.querySelector('.app .day') && !document.querySelector('.login-over'),
    { timeout: 30000 }, old);
}

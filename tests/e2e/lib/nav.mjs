// Входы из шапки недели (docs/specs/batch-ux.md, BI): в шапке только «📈 Итоги» и «⚙ Настройки».
// openTotals — экран «Итоги» на вкладке «Привычки» / «Выполненное» / «Отчёт»; openThemes — окно «Настройки» со списком тем.
const sleep = ms => new Promise(r => setTimeout(r, ms));

export async function openTotals(page, tab) {
  await page.click('.week-h .totals-b');
  await page.waitForSelector('.totals-tabs');
  await sleep(200);
  const on = await page.$eval('.totals-tabs .tt.on', b => b.textContent.trim());
  if (on !== tab) {
    await page.evaluate(t => [...document.querySelectorAll('.totals-tabs .tt')].find(b => b.textContent.trim() === t).click(), tab);
    await page.waitForFunction(t => document.querySelector('.totals-tabs .tt.on')?.textContent.trim() === t, { timeout: 3000 }, tab);
    await sleep(300);
  }
}

export async function openThemes(page) {
  if (!(await page.$('.editor.settings'))) await page.click('.week-h .settings-b');
  await page.waitForSelector('.editor.settings .themes');
  await sleep(200);
}

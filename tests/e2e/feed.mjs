// Проверка ленты в настоящем Edge (headless) на пустой базе.
import puppeteer from 'puppeteer-core';

const BASE = process.env.PLANNER_URL ?? 'http://localhost:5071/';
const SHOTS = process.argv[2] ?? '.';
const results = [];
const consoleErrors = [];

const browser = await puppeteer.launch({
  executablePath: process.env.PLANNER_BROWSER ?? 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  headless: true,
  defaultViewport: { width: 1440, height: 900 },
});
const page = await browser.newPage();
page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text() + ' ' + (m.location()?.url ?? '')); });
page.on('pageerror', e => consoleErrors.push(e.message));

const sleep = ms => new Promise(r => setTimeout(r, ms));
const settle = () => sleep(400);

async function check(name, fn) {
  try {
    await fn();
    results.push(`OK   ${name}`);
  } catch (e) {
    results.push(`FAIL ${name}: ${e.message}`);
    await page.screenshot({ path: `${SHOTS}/fail-${results.length}.png` });
  }
}

function assert(cond, msg) { if (!cond) throw new Error(msg); }

async function load() {
  await page.goto(BASE, { waitUntil: 'networkidle0' });
  await page.waitForSelector('.app', { timeout: 20000 });
  await settle();
}

// Все элементы по селектору, чей текст содержит подстроку → клик по первому
async function clickText(selector, text) {
  const handles = await page.$$(selector);
  for (const h of handles) {
    const t = await h.evaluate(el => el.textContent);
    if (t.includes(text)) { await h.click(); await settle(); return; }
  }
  throw new Error(`не найдено ${selector} с текстом «${text}»`);
}

const categories = () => page.$$eval('.cat-h .name', els => els.map(e => e.textContent.trim()));
const itemsIn = name => page.evaluate(n => {
  const cat = [...document.querySelectorAll('.cat')].find(c => c.querySelector('.name')?.textContent.trim() === n);
  return cat ? [...cat.querySelectorAll('.cat-b .item .tx')].map(e => e.textContent.trim()) : null;
}, name);
const typeAndEnter = async (selector, text) => { await page.type(selector, text); await page.keyboard.press('Enter'); await settle(); };

await load();

await check('пустая база: подсказка «создайте первую категорию»', async () => {
  const hint = await page.$('.first-category');
  assert(hint, 'нет подсказки');
  assert(!(await page.$('.quick')), 'поле «Записать мысль» не должно показываться');
});

await check('создать первую категорию с эмодзи', async () => {
  await page.click('.first-category'); await settle();
  await page.click('.emoji-btn'); await settle();
  await clickText('.emoji-picker .grid button', '📚');
  await typeAndEnter('.cat-editor .inline-input', 'Книги');
  assert(JSON.stringify(await categories()) === '["Книги"]', `категории: ${await categories()}`);
  const emoji = await page.$eval('.cat-h .emoji', e => e.textContent);
  assert(emoji === '📚', `эмодзи ${emoji}`);
  assert(await page.$('.quick'), 'поле «Записать мысль» не появилось');
});

await check('«Записать мысль»: Enter → наверх категории, поле очищается', async () => {
  await typeAndEnter('.quick input', 'Мысль 1');
  await typeAndEnter('.quick input', 'Мысль 2');
  await sleep(300);
  const items = await itemsIn('Книги');
  assert(JSON.stringify(items) === '["Мысль 2","Мысль 1"]', `в Книгах: ${items}`);
  const value = await page.$eval('.quick input', e => e.value);
  assert(value === '', `поле не очистилось: «${value}»`);
  assert(await page.$('.saved'), 'нет подтверждения «записано»');
});

await check('«+ категория» без эмодзи', async () => {
  await clickText('.panel-scroll > .add', '+ категория');
  await typeAndEnter('.cat-editor .inline-input', 'Идеи');
  assert(JSON.stringify(await categories()) === '["Книги","Идеи"]', `категории: ${await categories()}`);
});

await check('выбор категории в «Записать мысль»', async () => {
  const ideasId = await page.$$eval('.quick select option', os => os.find(o => o.textContent.includes('Идеи')).value);
  await page.select('.quick select', ideasId);
  await typeAndEnter('.quick input', 'Идея');
  const items = await itemsIn('Идеи');
  assert(JSON.stringify(items) === '["Идея"]', `в Идеях: ${items}`);
});

await check('после перезагрузки — те же дела и последняя категория в поле', async () => {
  await load();
  assert(JSON.stringify(await itemsIn('Книги')) === '["Мысль 2","Мысль 1"]', 'Книги');
  const selected = await page.$eval('.quick select', s => s.options[s.selectedIndex].textContent);
  assert(selected.includes('Идеи'), `выбрана ${selected}`);
});

await check('свернуть категорию, состояние запоминается', async () => {
  await clickText('.cat-h', 'Книги');
  assert((await itemsIn('Книги')).length === 0, 'не свернулась');
  await load();
  assert((await itemsIn('Книги')).length === 0, 'после перезагрузки снова раскрыта');
  const count = await page.evaluate(() => [...document.querySelectorAll('.cat')].find(c => c.textContent.includes('Книги')).querySelector('.cnt').textContent);
  assert(count === '2', `счётчик ${count}`);
});

await check('поиск раскрывает совпадения и подсвечивает, Esc очищает', async () => {
  await page.type('.search input', 'мысль'); await settle();
  assert(JSON.stringify(await categories()) === '["Книги"]', `видны ${await categories()}`);
  assert((await itemsIn('Книги')).length === 2, 'совпадения не показаны');
  const marks = await page.$$eval('mark.hit', ms => ms.length);
  assert(marks === 2, `подсветок ${marks}`);
  await page.focus('.search input'); await page.keyboard.press('Escape'); await settle();
  assert(JSON.stringify(await categories()) === '["Книги","Идеи"]', 'поиск не очистился');
  assert((await itemsIn('Книги')).length === 0, 'Книги должны снова быть свёрнуты');
});

await check('поиск без результатов', async () => {
  await page.type('.search input', 'байкал'); await settle();
  assert(await page.$('.nothing'), 'нет «Ничего не нашлось»');
  await page.focus('.search input'); await page.keyboard.press('Escape'); await settle();
});

await check('«+ добавить» в категории: Enter → в конец, поле остаётся для следующего', async () => {
  await clickText('.cat-b .add', '+ добавить');
  await typeAndEnter('.cat-b .inline-input', 'Ещё идея');
  assert(await page.$('.cat-b .inline-input'), 'поле закрылось');
  await page.keyboard.press('Escape'); await settle();
  assert(!(await page.$('.cat-b .inline-input')), 'Esc не закрыл поле');
  assert(JSON.stringify(await itemsIn('Идеи')) === '["Идея","Ещё идея"]', `Идеи: ${await itemsIn('Идеи')}`);
});

await check('переименование: клик по делу — окно, Enter в названии — сохранить', async () => {
  const handles = await page.$$('.cat-b .item');
  for (const h of handles) {
    if ((await h.$eval('.tx', e => e.textContent.trim())) === 'Идея') { await h.click(); break; }
  }
  await settle();
  await page.$eval('.editor[aria-label="Мысль"] .title-input', e => e.select());
  await typeAndEnter('.editor[aria-label="Мысль"] .title-input', 'Идея про хлеб');
  await load();
  assert(JSON.stringify(await itemsIn('Идеи')) === '["Идея про хлеб","Ещё идея"]', `Идеи: ${await itemsIn('Идеи')}`);
});

await check('в корзину и обратно (возврат — наверх категории)', async () => {
  const row = (await page.$$('.cat-b .item'))[1]; // «Ещё идея»
  await row.hover();
  await (await row.$('.row-action')).click(); await settle();
  assert(JSON.stringify(await itemsIn('Идеи')) === '["Идея про хлеб"]', 'не удалилось');
  await page.click('.trash'); await sleep(600);
  const rows = await page.$$eval('.trash-popup .row .text > div:first-child', els => els.map(e => e.textContent));
  assert(JSON.stringify(rows) === '["Ещё идея"]', `в корзине ${rows}`);
  await page.click('.trash-popup .row-action.positive'); await sleep(600);
  assert(JSON.stringify(await itemsIn('Идеи')) === '["Ещё идея","Идея про хлеб"]', `Идеи: ${await itemsIn('Идеи')}`);
  await page.click('.backdrop'); await settle();
  assert(!(await page.$('.trash-popup')), 'окно не закрылось');
});

await check('удалить категорию → дела в корзину; вернуть с выбором категории', async () => {
  const cat = (await page.$$('.cat-h'))[0];            // Книги
  await cat.hover();
  await (await cat.$('.menu')).click(); await settle();
  await clickText('.cat-editor .btn', 'удалить');
  const text = await page.$eval('.cat-editor .confirm span', e => e.textContent);
  assert(text.includes('2 дела'), `подтверждение: ${text}`);
  await clickText('.cat-editor .btn', 'Удалить');
  assert(JSON.stringify(await categories()) === '["Идеи"]', `категории ${await categories()}`);

  await page.click('.trash'); await sleep(600);
  const froms = await page.$$eval('.trash-popup .from', els => els.map(e => e.textContent));
  assert(froms.length === 2 && froms.every(f => f === 'категории больше нет'), `откуда: ${froms}`);
  await page.click('.trash-popup .row-action.positive'); await sleep(600);
  assert(await page.$('.trash-popup .choice select'), 'не спросили категорию');
  await clickText('.trash-popup .choice .btn', 'Вернуть'); await sleep(400);
  const ideas = await itemsIn('Идеи');
  assert(ideas.length === 3 && ideas[0].startsWith('Мысль'), `Идеи: ${ideas}`);
});

await check('удалить навсегда и «очистить всё»', async () => {
  const left = await page.$$eval('.trash-popup .row', rs => rs.length);
  assert(left === 1, `в корзине ${left}`);
  await clickText('.trash-popup .btn', 'очистить всё');
  await clickText('.trash-popup .btn', 'Да');
  assert(await page.$eval('.trash-popup .empty', e => e.textContent) === 'Пусто', 'не очистилось');
  await page.click('.backdrop'); await settle();
  await load();
  await page.click('.trash'); await sleep(600);
  assert(await page.$eval('.trash-popup .empty', e => e.textContent) === 'Пусто', 'после перезагрузки не пусто');
  await page.click('.backdrop'); await settle();
});

await check('сменить эмодзи и название категории', async () => {
  const cat = (await page.$$('.cat-h'))[0];
  await cat.hover();
  await (await cat.$('.menu')).click(); await settle();
  await page.click('.emoji-btn'); await settle();
  await clickText('.emoji-picker .grid button', '💡');
  await page.$eval('.cat-editor .inline-input', e => e.select());
  await page.type('.cat-editor .inline-input', 'Идеи и планы');
  await clickText('.cat-editor .btn', 'Сохранить');
  await load();
  const header = await page.$eval('.cat-h', e => e.textContent.replace(/\s+/g, ' ').trim());
  assert(header.startsWith('💡') && header.includes('Идеи и планы'), `заголовок: ${header}`);
});

await check('нет связи: сообщение, несохранённая запись исчезает', async () => {
  // В опубликованной сборке запросы идут через service worker, а page.setOfflineMode действует
  // только на страницу — «отключаем сеть» и у service worker (через его CDP-сессию)
  const setOffline = async offline => {
    await page.setOfflineMode(offline);
    for (const target of browser.targets().filter(t => t.type() === 'service_worker')) {
      const session = await target.createCDPSession();
      await session.send('Network.enable');
      await session.send('Network.emulateNetworkConditions',
        { offline, latency: 0, downloadThroughput: -1, uploadThroughput: -1 });
    }
  };
  await setOffline(true);
  await typeAndEnter('.quick input', 'Мысль без сети');
  await sleep(800);
  const toast = await page.$eval('.toast', e => e.textContent.trim()).catch(() => null);
  assert(toast === 'Нет связи с сервером.', `сообщение: ${toast}`);
  const all = await page.$$eval('.cat-b .item .tx', els => els.map(e => e.textContent.trim()));
  assert(!all.includes('Мысль без сети'), 'запись осталась');
  assert(!(await page.$('.saved')), 'осталось «записано»');
  await setOffline(false);
});

await page.screenshot({ path: `${SHOTS}/feed-final.png` });
await browser.close();

console.log(results.join('\n'));
console.log(consoleErrors.length ? `\nОшибки консоли:\n${consoleErrors.join('\n')}` : '\nОшибок в консоли нет');

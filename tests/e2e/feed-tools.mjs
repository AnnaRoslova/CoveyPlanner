// @seed — нужны демо-данные (tools/seed-demo.ps1)
// Лента: «+ категория» сверху (новая — в начало) и «свернуть / развернуть всё».
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
const settle = () => sleep(400);
function assert(c, m) { if (!c) throw new Error(m); }
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-feed-tools-${results.length}.png` }); }
}
async function load() { await page.goto(BASE, { waitUntil: 'networkidle0' }); await page.waitForSelector('.day'); await settle(); }
const categoryNames = () => page.$$eval('.someday .cat-h .name', ns => ns.map(n => n.textContent.trim()));
const expanded = () => page.$$eval('.someday .cat', cs => cs.map(c => c.classList.contains('open')));

await load();

await check('сверху ленты: «+ категория» и «свернуть всё» — над категориями', async () => {
  const m = await page.evaluate(() => {
    const tools = document.querySelector('.someday .feed-tools');
    const first = document.querySelector('.someday .cat');
    return tools && {
      add: tools.querySelector('.add')?.textContent.trim(), fold: tools.querySelector('.fold')?.textContent.trim(),
      above: tools.getBoundingClientRect().bottom <= first.getBoundingClientRect().top,
    };
  });
  assert(m && m.add === '+ категория' && m.above, JSON.stringify(m));
  assert(await page.$('.someday .panel-scroll > .add'), 'нижняя «+ категория» пропала');
});

await check('«+ категория» сверху: новая категория встаёт первой и сохраняется', async () => {
  await page.click('.someday .feed-tools .add'); await settle();
  const editorTop = await page.$eval('.someday .cat-editor', e => e.getBoundingClientRect().top);
  const firstTop = await page.$eval('.someday .cat', c => c.getBoundingClientRect().top);
  assert(editorTop < firstTop, 'редактор не сверху');
  await page.keyboard.type('Срочное'); await page.keyboard.press('Enter'); await settle();
  assert((await categoryNames())[0] === 'Срочное', `категории: ${await categoryNames()}`);
  await load();
  assert((await categoryNames())[0] === 'Срочное', `после перезагрузки: ${await categoryNames()}`);
});

await check('«свернуть всё» сворачивает все категории, «развернуть всё» — раскрывает; запоминается', async () => {
  const fold = () => page.$eval('.someday .feed-tools .fold', b => b.textContent.trim());
  if ((await fold()) === 'развернуть всё') { await page.click('.someday .feed-tools .fold'); await settle(); }
  assert(await fold() === 'свернуть всё', `кнопка: ${await fold()}`);
  await page.click('.someday .feed-tools .fold'); await settle();
  assert((await expanded()).every(e => !e), `раскрыты: ${await expanded()}`);
  assert(await fold() === 'развернуть всё', `кнопка: ${await fold()}`);
  await load();
  assert((await expanded()).every(e => !e), 'после перезагрузки раскрыты');
  await page.click('.someday .feed-tools .fold'); await settle();
  assert((await expanded()).every(e => e), `свёрнуты: ${await expanded()}`);
});

const api = (method, path, body) => page.evaluate(async (method, path, body) => {
  const r = await fetch('/api/' + path, { method, headers: { 'Content-Type': 'application/json' }, body: body ? JSON.stringify(body) : undefined });
  return r.status === 204 ? null : r.json();
}, method, path, body);
const feedItem = title => page.evaluateHandle(t => [...document.querySelectorAll('.someday .cat-b .item')]
  .find(i => i.querySelector('.title').textContent.trim() === t), title);
const titlesIn = name => page.evaluate(n => [...[...document.querySelectorAll('.someday .cat')]
  .find(c => c.querySelector('.cat-h .name')?.textContent.trim() === n).querySelectorAll('.item .title')]
  .map(t => t.textContent.trim()), name);
const editor = '.editor[aria-label="Мысль"]';
// Категория — поле «● Книги ▾», клик открывает список
async function chooseCategory(name) {
  await page.click(`${editor} .select-field`); await settle();
  await page.evaluate(n => [...document.querySelectorAll('.editor .popover .option')]
    .find(o => o.querySelector('.option-name').textContent.trim() === n).click(), name);
  await settle();
  assert(!(await page.$('.editor .popover')), 'список не закрылся после выбора');
}

await check('дело ленты: окно — название, подробно, категория; сменили категорию — дело наверху новой', async () => {
  await (await (await feedItem('Сад с травами на балконе')).$('.tx')).click(); await settle();
  assert(await page.$(editor), 'окно не открылось');
  const labels = await page.$$eval(`${editor} .label`, ls => ls.map(l => l.textContent.trim()));
  assert(JSON.stringify(labels) === '["Подробно","Категория","Шаги"]', `поля: ${labels}`); // шаги — дело-чеклист
  await page.type(`${editor} textarea`, 'Базилик, мята, розмарин');
  await chooseCategory('Дом');
  await page.click(`${editor} .btn:not(.quiet):not(.danger)`); await settle();
  assert(!(await page.$(editor)), 'окно не закрылось');
  if (!(await page.$$eval('.someday .cat', cs => cs.find(c => c.textContent.includes('Дом')).classList.contains('open')))) {
    await page.evaluate(() => [...document.querySelectorAll('.someday .cat-h')].find(h => h.textContent.includes('Дом')).click()); await settle();
  }
  assert((await titlesIn('Дом'))[0] === 'Сад с травами на балконе', `Дом: ${await titlesIn('Дом')}`);
  assert(!(await titlesIn('Идеи')).includes('Сад с травами на балконе'), 'осталось в Идеях');
  await load();
  await (await (await feedItem('Сад с травами на балконе')).$('.tx')).click(); await settle();
  assert(await page.$eval(`${editor} textarea`, t => t.value) === 'Базилик, мята, розмарин', 'описание не сохранилось');
});

await check('окно дела ленты: Esc и клик мимо — отмена', async () => {
  await page.$eval(`${editor} .title-input`, e => e.select());
  await page.keyboard.type('Не сохранять'); await page.keyboard.press('Escape'); await settle();
  assert(!(await page.$(editor)), 'Esc не закрыл');
  await (await (await feedItem('Сад с травами на балконе')).$('.tx')).click(); await settle();
  await page.keyboard.type('!!!');
  await page.mouse.click(8, 890); await settle();
  assert(!(await page.$(editor)), 'клик мимо не закрыл');
  assert(await feedItem('Сад с травами на балконе').then(h => h.asElement()), 'название поменялось');
});

const LONG = 'Очень длинная запись, которая точно не помещается в две строки узкой колонки дня и ленты, поэтому её нужно обрезать';
await check('длинные записи — не больше двух строк, с «…» и «⌄» (L1: развернуть по длине)', async () => {
  const ideas = (await api('GET', 'board')).categories.find(c => c.name === 'Идеи');
  await api('POST', 'tasks', { title: LONG, target: { kind: 'someday', categoryId: ideas.id, index: 0 } });
  const today = new Date().toISOString().slice(0, 10);
  await api('POST', 'tasks', { title: LONG, target: { kind: 'day', date: today, index: 0 } });
  await load();
  const m = await page.$$eval('.item', (items, long) => items.filter(i => i.querySelector('.title')?.textContent.trim() === long).map(i => {
    const t = i.querySelector('.title');
    return { lines: t.clientHeight / parseFloat(getComputedStyle(t).lineHeight), ellipsis: getComputedStyle(t).webkitLineClamp,
      long: t.hasAttribute('data-long'), more: getComputedStyle(i.querySelector('.unclamp')).display !== 'none' };
  }), LONG);
  assert(m.length === 2, `записей: ${m.length}`);
  assert(m.every(x => Math.round(x.lines) === 2 && x.ellipsis === '2' && x.long && x.more), JSON.stringify(m));
});

const tipText = () => page.evaluate(() => document.querySelector('.clamp-tip')?.isConnected ? document.querySelector('.clamp-tip').textContent : null);
for (const [where, scope] of [['в дне', '.day'], ['в ленте', '.someday']]) {
  await check(`длинная запись ${where}: через ~0,5 с — подсказка с полным текстом; клик — окно с полным текстом`, async () => {
    await page.mouse.move(5, 5); await settle();
    const row = await page.evaluateHandle((scope, long) => [...document.querySelectorAll(`${scope} .item`)]
      .find(i => i.querySelector('.title')?.textContent.trim() === long), scope, LONG);
    const t = await row.$eval('.title', e => e.getBoundingClientRect().toJSON());
    await page.mouse.move(t.left + 20, t.top + 6);
    await sleep(200);
    assert(await tipText() === null, 'подсказка появилась сразу');
    await sleep(500);
    assert(await tipText() === LONG, `подсказка: ${await tipText()}`);
    const lines = await row.evaluate(i => { const t = i.querySelector('.title'); return Math.round(t.clientHeight / parseFloat(getComputedStyle(t).lineHeight)); });
    assert(lines === 2, `запись развернулась: ${lines} строк`);
    if (scope === '.day') await page.screenshot({ path: `${SHOTS}/clamp-tip.png` });
    await page.mouse.click(t.left + 20, t.top + 6); await settle();
    assert(await tipText() === null, 'подсказка не спряталась при клике');
    assert(await page.$eval('.editor .title-input', e => e.value) === LONG, 'в окне не полный текст');
    await page.keyboard.press('Escape'); await settle();
  });
}

await check('у короткой записи подсказки нет', async () => {
  await page.mouse.move(5, 5); await settle();
  const t = await page.evaluate(() => [...document.querySelectorAll('.day .item .title')].find(e => e.textContent.trim() === 'Кофе с Олей').getBoundingClientRect().toJSON());
  await page.mouse.move(t.left + 10, t.top + 6); await sleep(900);
  assert(await tipText() === null, `подсказка у короткой: ${await tipText()}`);
});

// ---------- «Неразобранное» ----------
const unsortedTitles = () => page.$$eval('.unsorted .item .title', ts => ts.map(t => t.textContent.trim()));
const boxOf = handle => handle.evaluate(e => e.getBoundingClientRect().toJSON());
async function mouseDrag(from, x, y) {
  const sx = from.x + from.width / 2, sy = from.y + from.height / 2;
  await page.mouse.move(sx, sy); await page.mouse.down();
  for (let i = 1; i <= 12; i++) { await page.mouse.move(sx + (x - sx) * i / 12, sy + (y - sy) * i / 12); await sleep(16); }
  await sleep(150); await page.mouse.up(); await settle();
}

await check('«неразобранное» — сверху ленты, над категориями; перетаскивание в категорию и обратно', async () => {
  await api('POST', 'tasks', { title: 'Неразобранная мысль', target: { kind: 'someday', categoryId: null, index: 0 } });
  await load();
  const u = await page.$eval('.unsorted', e => e.getBoundingClientRect().bottom);
  const tools = await page.$eval('.someday .feed-tools', e => e.getBoundingClientRect().top);
  assert(u <= tools, '«неразобранное» не над категориями');
  assert(JSON.stringify(await unsortedTitles()) === '["Неразобранная мысль"]', `неразобранное: ${await unsortedTitles()}`);

  const item = await page.evaluateHandle(() => [...document.querySelectorAll('.unsorted .item')].find(i => i.textContent.includes('Неразобранная мысль')));
  const firstInIdeas = (await titlesIn('Идеи'))[0];
  const target = await boxOf(await (await feedItem(firstInIdeas)).$('.tx'));
  await mouseDrag(await boxOf(item), target.x + 40, target.y + 2);
  assert((await unsortedTitles()).length === 0, 'осталось в неразобранном');
  assert((await titlesIn('Идеи'))[0] === 'Неразобранная мысль', `Идеи: ${await titlesIn('Идеи')}`);

  // Пустое «неразобранное» во время перетаскивания видно как место, куда бросить
  const back = await boxOf(await feedItem('Неразобранная мысль'));
  await page.mouse.move(back.x + back.width / 2, back.y + back.height / 2); await page.mouse.down();
  await page.mouse.move(back.x + back.width / 2, back.y + back.height / 2 + 20, { steps: 4 }); await sleep(100);
  const zone = await page.$eval('.unsorted', e => ({ display: getComputedStyle(e).display, ...e.getBoundingClientRect().toJSON() }));
  assert(zone.display !== 'none' && zone.height >= 30, `место для сброса не видно: ${JSON.stringify(zone)}`);
  for (let i = 1; i <= 10; i++) { await page.mouse.move(zone.x + zone.width / 2, back.y + (zone.y + zone.height / 2 - back.y) * i / 10); await sleep(16); }
  await sleep(150); await page.mouse.up(); await settle();
  assert(JSON.stringify(await unsortedTitles()) === '["Неразобранная мысль"]', `обратно не вернулось: ${await unsortedTitles()}`);
  await load();
  assert(JSON.stringify(await unsortedTitles()) === '["Неразобранная мысль"]', 'после перезагрузки');
});

await check('окно мысли: «неразобранное» в списке категорий переносит в «неразобранное»', async () => {
  await (await (await feedItem('Научиться печь хлеб на закваске')).$('.tx')).click(); await settle();
  await chooseCategory('неразобранное');
  await page.click(`${editor} .btn:not(.quiet):not(.danger)`); await settle();
  assert((await unsortedTitles())[0] === 'Научиться печь хлеб на закваске', `неразобранное: ${await unsortedTitles()}`);
});

async function deleteCategory(name) {
  await page.evaluate(n => [...document.querySelectorAll('.someday .cat-h')].find(h => h.querySelector('.name').textContent.trim() === n)
    .querySelector('.menu').click(), name);
  await settle();
  await page.evaluate(() => [...document.querySelectorAll('.cat-editor .btn')].find(b => b.textContent.trim() === 'удалить').click());
  await settle();
}

await check('удалить непустую категорию: «Переложить в неразобранное» — дела наверху «неразобранного», корзина не растёт', async () => {
  const books = await titlesIn('Книги и фильмы');
  const trashBefore = (await api('GET', 'board')).trashCount;
  await deleteCategory('Книги и фильмы');
  assert(await page.$('.cat-editor .confirm'), 'не спросили');
  await page.evaluate(() => [...document.querySelectorAll('.cat-editor .confirm .btn')].find(b => b.textContent.includes('Переложить')).click());
  await settle();
  assert(!(await categoryNames()).includes('Книги и фильмы'), 'категория осталась');
  assert(JSON.stringify((await unsortedTitles()).slice(0, books.length)) === JSON.stringify(books), `неразобранное: ${await unsortedTitles()}`);
  assert((await api('GET', 'board')).trashCount === trashBefore, 'дела ушли в корзину');
});

await check('удалить непустую категорию: «Отмена» — ничего не меняется', async () => {
  await deleteCategory('Идеи');
  await page.evaluate(() => [...document.querySelectorAll('.cat-editor .confirm .btn')].find(b => b.textContent.trim() === 'Отмена').click());
  await settle();
  assert(!(await page.$('.cat-editor .confirm')), 'вопрос не закрылся');
  await page.keyboard.press('Escape'); await settle(); // закрыть редактор — заголовок категории снова виден
  assert((await categoryNames()).includes('Идеи'), 'категория удалилась');
});

await check('пустая категория удаляется без вопроса', async () => {
  await api('POST', 'categories', { name: 'Пустая', emoji: null });
  await load();
  await deleteCategory('Пустая');
  assert(!(await page.$('.cat-editor .confirm')), 'спросили про пустую');
  assert(!(await categoryNames()).includes('Пустая'), 'не удалилась');
});

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));

// @seed — нужны демо-данные (tools/seed-demo.ps1)
// Темы: контраст текста по WCAG и снимки для сравнения с образцом docs/themes.html.
// Для каждой темы — весь экран и открытые окна (дело, цель, роль, корзина, меню тем).
// Контраст: у каждой видимой надписи цвет текста против фактического фона (подложки складываются
// от страницы вверх, с прозрачностью). Тёмная тема обязана пройти AA целиком: 4.5:1, крупный текст — 3:1.
// Светлые темы: основной текст — AA, второстепенный (SECONDARY: «+ цель», «+ дело», счётчики,
// выполненные дела, подсказки и плейсхолдеры) — не ниже 3:1.
// Снимки — в out/cmp-<тема>-<окно>.png и out/mock-<тема>.png, отчёт о контрасте — out/contrast.json.
import puppeteer from 'puppeteer-core';
import { openTotals, openThemes } from './lib/nav.mjs';
import { makeHabitHistory } from './lib/habits-history.mjs';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const BASE = process.env.PLANNER_URL ?? 'http://localhost:5071/';
const SHOTS = process.argv[2] ?? '.';
const MOCKUP = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../docs/themes.html');
const THEMES = ['lotus', 'peony', 'platinum', 'obsidian'];
const MUST_PASS_AA = ['obsidian'];
const LIGHT = ['lotus', 'peony', 'platinum'];
// Второстепенный текст: счётчики, выполненные дела, подсказки и подписи-оценки, стрелки степпера
// Кнопки системы («+ …», «свернуть всё», «ещё дела», значки — fixes-2026-10.md, D4) — как основной текст: их в этом списке нет
const SECONDARY = ['.other .d', '.other .sep', '.other.done', '.n', '.cnt', '.progress', '.pct', '.done .tx', '.hint', '.sub', '.note', '.rock-note', '.from',
  '.load', '.rocks', '.goal-days', '.unit', '.step', '.tag-empty', '.wheel-unit', '.saved', '.nothing', '.first-category > :not(.add)', '.strip-label', '.m.before', '.legend',
  '.parent-of', '.project-label', '.sum-chip.static', '.menu-hint', '.done-screen .meta', '.done-screen .date', '.done-screen .years', '.done-screen .year', '.done-steps',
  '.report-screen .sub', '.report-screen .roles', '.report-screen .fact', '.report-screen .pct', '.report-screen .period:not(.on)',
  '.q2-of', '.q2-week', '.hit-meta', '.hit.done .hit-title', '.hit.done mark', '.review .meta', '.review .diamond', '.mission .sub'].join(', '); // ◆ — значок (--a-deep, 3:1)
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
function assert(c, m) { if (!c) throw new Error(m); }
async function check(name, fn) {
  try { await fn(); results.push(`OK   ${name}`); }
  catch (e) { results.push(`FAIL ${name}: ${e.message}`); await page.screenshot({ path: `${SHOTS}/fail-contrast-${results.length}.png` }); }
}

// Замер контраста всех надписей внутри scope (по умолчанию — весь документ)
function measure(scopeSelector, secondarySelector) {
  const parse = s => {
    s = s.trim();
    let m = s.match(/^#([0-9a-f]{3,8})$/i);
    if (m) {
      let h = m[1];
      if (h.length <= 4) h = [...h].map(c => c + c).join('');
      const n = i => parseInt(h.slice(i, i + 2), 16);
      return [n(0), n(2), n(4), h.length === 8 ? n(6) / 255 : 1];
    }
    m = s.match(/^rgba?\(([^)]+)\)/);
    if (m) { const p = m[1].split(/[\s,/]+/).filter(Boolean).map(Number); return [p[0], p[1], p[2], p[3] ?? 1]; }
    m = s.match(/^color\(srgb ([^)]+)\)/);
    if (m) { const p = m[1].split(/[\s/]+/).filter(Boolean).map(Number); return [p[0] * 255, p[1] * 255, p[2] * 255, p[3] ?? 1]; }
    return [0, 0, 0, 0];
  };
  const over = (top, under) => {
    const a = top[3];
    return [0, 1, 2].map(i => top[i] * a + under[i] * (1 - a)).concat(1);
  };
  const lum = c => {
    const [r, g, b] = c.slice(0, 3).map(v => { v /= 255; return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4; });
    return 0.2126 * r + 0.7152 * g + 0.0722 * b;
  };
  const ratio = (a, b) => { const [x, y] = [lum(a), lum(b)].sort((p, q) => q - p); return (x + 0.05) / (y + 0.05); };
  const pageColor = parse(getComputedStyle(document.documentElement).getPropertyValue('--page-color'));

  const background = el => {
    const chain = [];
    for (let e = el; e; e = e.parentElement) chain.unshift(e);
    return chain.reduce((acc, e) => over(parse(getComputedStyle(e).backgroundColor), acc), pageColor);
  };
  const opacity = el => { let o = 1; for (let e = el; e; e = e.parentElement) o *= Number(getComputedStyle(e).opacity); return o; };
  const visible = el => {
    const r = el.getBoundingClientRect();
    const cs = getComputedStyle(el);
    return r.width > 0 && r.height > 0 && cs.visibility !== 'hidden' && r.bottom > 0 && r.top < innerHeight && r.right > 0 && r.left < innerWidth;
  };
  const hex = c => '#' + c.slice(0, 3).map(v => Math.round(v).toString(16).padStart(2, '0')).join('');
  const describe = el => (el.className && typeof el.className === 'string' ? '.' + el.className.trim().split(/\s+/).join('.') : el.tagName.toLowerCase());

  const scope = scopeSelector ? document.querySelector(scopeSelector) : document.body;
  const out = [];
  const add = (el, text, colorString, placeholder = false) => {
    const o = opacity(el);
    if (o < 0.05 || !visible(el)) return;
    const bg = background(el);
    const fg0 = parse(colorString);
    const fg = over([fg0[0], fg0[1], fg0[2], fg0[3] * o], bg);
    const cs = getComputedStyle(el);
    const size = parseFloat(cs.fontSize);
    const large = size >= 24 || (size >= 18.66 && Number(cs.fontWeight) >= 700);
    // Значки-эмодзи — картинки, а не текст
    if (/^[\p{Extended_Pictographic}️‍\s]+$/u.test(text)) return;
    out.push({
      text: text.slice(0, 40), where: describe(el), ratio: Math.round(ratio(fg, bg) * 100) / 100, need: large ? 3 : 4.5,
      secondary: !!el.closest(secondarySelector) || !!placeholder, fg: hex(fg), bg: hex(bg),
    });
  };
  for (const el of scope.querySelectorAll('*')) {
    // Недоступные кнопки WCAG из требований к контрасту исключает
    if (el.closest('.drag-ghost, [aria-hidden="true"], .swatches, :disabled')) continue;
    const text = [...el.childNodes].filter(n => n.nodeType === 3).map(n => n.textContent).join('').trim();
    if (text) add(el, text, getComputedStyle(el).color);
    if ((el.tagName === 'INPUT' || el.tagName === 'TEXTAREA') && el.type !== 'time') {
      if (el.value) add(el, el.value, getComputedStyle(el).color);
      else if (el.placeholder) add(el, el.placeholder, getComputedStyle(el, '::placeholder').color, true);
    }
  }
  return out;
}

const report = {};
async function audit(theme, state, scope) {
  const items = await page.evaluate(measure, scope ?? null, SECONDARY);
  // В светлых темах второстепенному тексту достаточно 3:1
  const need = i => LIGHT.includes(theme) && i.secondary ? 3 : i.need;
  const fails = items.filter(i => i.ratio < need(i)).map(i => ({ ...i, need: need(i) })).sort((a, b) => a.ratio - b.ratio);
  report[`${theme}/${state}`] = { checked: items.length, min: Math.min(...items.map(i => i.ratio)), fails };
  return fails;
}
const titleOf = el => [...el.querySelector('.tx').childNodes].filter(n => !n.classList?.contains('pills') && !n.classList?.contains('note') && !n.classList?.contains('goal-days')).map(n => n.textContent).join('').trim();
async function clickItem(scope, title) {
  await page.evaluate((scope, title, src) => {
    const titleOf = eval(src);
    [...document.querySelectorAll(`${scope} .item`)].find(i => titleOf(i) === title).querySelector('.tx').click();
  }, scope, title, titleOf.toString());
  await sleep(400);
}
const escape = async () => { await page.keyboard.press('Escape'); await sleep(300); };
async function setTheme(key) {
  await page.evaluate(k => localStorage.setItem('planner.theme', k), key);
  await page.goto(BASE, { waitUntil: 'networkidle0' });
  await page.waitForSelector('.day');
  await page.evaluate(() => document.fonts.ready);
  await sleep(600);
}

// Привычки с историей (пауза, каникулы) — для снимка и замера экрана «Копилки»
await makeHabitHistory(BASE);

// В корзине должно что-то лежать — для снимка
await page.goto(BASE, { waitUntil: 'networkidle0' });
await page.evaluate(async () => {
  const week = await (await fetch('/api/week/' + new Date().toISOString().slice(0, 10))).json();
  const t = week.tasks.find(x => x.title === 'Оплатить интернет');
  if (t) await fetch(`/api/tasks/${t.id}`, { method: 'DELETE' });
});

// Проект с шагами (подписи «↳ Ремонт» и «☑ 1/2» в днях) и выполненная мысль ленты — для экрана «Выполненное»
await page.evaluate(async () => {
  const d = new Date(), iso = x => `${x.getFullYear()}-${String(x.getMonth() + 1).padStart(2, '0')}-${String(x.getDate()).padStart(2, '0')}`;
  const post = async (url, body, method = 'POST') => {
    const r = await fetch(url, { method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
    const text = await r.text();
    return text ? JSON.parse(text) : null;
  };
  const repair = await post('/api/tasks', { title: 'Ремонт', target: { kind: 'day', date: iso(d), index: 99 } });
  const walls = await post(`/api/tasks/${repair.id}/steps`, { title: 'Стены' });
  await post(`/api/tasks/${repair.id}/steps`, { title: 'Полы' });
  await post(`/api/tasks/${walls.id}/done`, { isDone: true }, 'PUT');
  const next = new Date(d); next.setDate(d.getDate() + (d.getDay() === 0 ? -1 : 1)); // соседний день этой же недели
  await post(`/api/tasks/${walls.id}/move`, { kind: 'day', date: iso(next), index: 99 });
  const board = await (await fetch('/api/board')).json();
  const thought = board.somedayTasks.find(t => t.categoryId !== null);
  if (thought) await post(`/api/tasks/${thought.id}/done`, { isDone: true }, 'PUT');
  // E2: дело «на неделе» без дня — подсвечено «не назначено на день»
  const body = board.roles.find(r => r.name === 'Тело');
  const monday = new Date(d); monday.setDate(d.getDate() - ((d.getDay() + 6) % 7));
  await post('/api/tasks', { title: 'Записаться к врачу', target: { kind: 'week', weekStart: iso(monday), roleId: body.id } });
});

for (const theme of THEMES) {
  await setTheme(theme);
  const shot = name => page.screenshot({ path: `${SHOTS}/cmp-${theme}-${name}.png` });
  const windows = [];

  await check(`${theme}: экран`, async () => {
    assert(await page.evaluate(() => document.documentElement.dataset.theme) === theme, 'тема не включилась');
    const has = await page.evaluate(() => ['.day .parent-of', '.day .project-label', '.someday .cat-b .ck-hit'].filter(q => !document.querySelector(q)));
    assert(has.length === 0, `на экране нет: ${has.join(', ')}`); // «↳», «☑ N/M», чекбокс ленты — в замере
    await shot('screen');
    windows.push(['экран', await audit(theme, 'screen')]);
  });
  await check(`${theme}: сегодняшний день заметно выделен (обводка от 2:1 к обычному дню, фон другой)`, async () => {
    const m = await page.evaluate(() => {
      // Цвета приводим к rgb через canvas — так понимаются и color-mix, и color(srgb …)
      const ctx = document.createElement('canvas').getContext('2d');
      const rgb = c => { ctx.fillStyle = '#000'; ctx.fillStyle = c; ctx.fillRect(0, 0, 1, 1); return [...ctx.getImageData(0, 0, 1, 1).data]; };
      const page = rgb(getComputedStyle(document.documentElement).getPropertyValue('--page-color'));
      const flat = c => c[3] === 255 ? c : [0, 1, 2].map(i => Math.round(c[i] * c[3] / 255 + page[i] * (1 - c[3] / 255))).concat(255);
      const lum = c => 0.2126 * ch(c[0]) + 0.7152 * ch(c[1]) + 0.0722 * ch(c[2]);
      const ch = v => { v /= 255; return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4; };
      const ratio = (a, b) => { const [x, y] = [lum(a), lum(b)].sort((p, q) => q - p); return (x + 0.05) / (y + 0.05); };
      const today = document.querySelector('.day.today');
      const other = document.querySelector('.day:not(.today):not(.weekend)');
      const ring = getComputedStyle(today).boxShadow.match(/(rgba?\([^)]*\)|color\([^)]*\)|#[0-9a-f]+)/i)[1];
      const todayBg = flat(rgb(getComputedStyle(today).backgroundColor));
      const otherBg = flat(rgb(getComputedStyle(other).backgroundColor));
      return { ring: ratio(flat(rgb(ring)), otherBg), bgDiff: Math.max(...[0, 1, 2].map(i => Math.abs(todayBg[i] - otherBg[i]))) };
    });
    assert(m.ring >= 2, `обводка ${m.ring.toFixed(2)}:1`);
    assert(m.bgDiff >= 6, `фон отличается на ${m.bgDiff}`);
  });
  await check(`${theme}: D4 — обводка кнопок от фона под ними не ниже 1.5:1 (обычная, «+ …», значки)`, async () => {
    const weak = await page.evaluate(() => {
      const ctx = document.createElement('canvas').getContext('2d');
      const rgb = c => { ctx.fillStyle = '#000'; ctx.fillStyle = c; ctx.fillRect(0, 0, 1, 1); return [...ctx.getImageData(0, 0, 1, 1).data]; };
      const ch = v => { v /= 255; return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4; };
      const lum = c => 0.2126 * ch(c[0]) + 0.7152 * ch(c[1]) + 0.0722 * ch(c[2]);
      const ratio = (a, b) => { const [x, y] = [lum(a), lum(b)].sort((p, q) => q - p); return (x + 0.05) / (y + 0.05); };
      const over = (c, under) => [0, 1, 2].map(i => Math.round(c[i] * c[3] / 255 + under[i] * (1 - c[3] / 255))).concat(255);
      const page = rgb(getComputedStyle(document.documentElement).getPropertyValue('--page-color'));
      const behind = el => { const chain = []; for (let e = el.parentElement; e; e = e.parentElement) chain.unshift(e); return chain.reduce((acc, e) => over(rgb(getComputedStyle(e).backgroundColor), acc), page); };
      return [...document.querySelectorAll('.btn:not(.primary):not(.danger):not(.pink), .add, .icon-b')]
        .filter(b => b.getBoundingClientRect().width > 0 && !b.closest('[aria-hidden="true"]'))
        .map(b => ({ t: (b.textContent.trim() || b.title).slice(0, 24), r: ratio(over(rgb(getComputedStyle(b).borderTopColor), behind(b)), behind(b)) }))
        .filter(x => x.r < 1.5);
    });
    assert(weak.length === 0, JSON.stringify(weak.slice(0, 6)));
  });
  await check(`${theme}: N1 — недособранная цель: подсветка на месте, полоска слева от фона панели не ниже 1.5:1`, async () => {
    const m = await page.evaluate(() => {
      const row = document.querySelector('.role .item.unplaced:not(.week-nd *)');
      if (!row) return null;
      const ctx = document.createElement('canvas').getContext('2d');
      const rgb = c => { ctx.fillStyle = '#000'; ctx.fillStyle = c; ctx.fillRect(0, 0, 1, 1); return [...ctx.getImageData(0, 0, 1, 1).data]; };
      const ch = v => { v /= 255; return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4; };
      const lum = c => 0.2126 * ch(c[0]) + 0.7152 * ch(c[1]) + 0.0722 * ch(c[2]);
      const ratio = (a, b) => { const [x, y] = [lum(a), lum(b)].sort((p, q) => q - p); return (x + 0.05) / (y + 0.05); };
      const page = rgb(getComputedStyle(document.documentElement).getPropertyValue('--page-color'));
      const flat = (c, under) => c[3] === 255 ? c : [0, 1, 2].map(i => Math.round(c[i] * c[3] / 255 + under[i] * (1 - c[3] / 255))).concat(255);
      const panel = flat(rgb(getComputedStyle(row.closest('.panel')).backgroundColor), page);
      const bar = flat(rgb(getComputedStyle(row).getPropertyValue('--unplaced-bar')), panel);
      return { bar: ratio(bar, panel) };
    });
    assert(m, 'на экране нет недособранной цели');
    assert(m.bar >= 1.5, `полоска ${m.bar.toFixed(2)}:1`);
  });
  await check(`${theme}: E2 — дело «на неделе» без дня: подсветка, подсказка, текст на подложке AA, полоска от фона панели не ниже 1.5:1`, async () => {
    const m = await page.evaluate(() => {
      const row = document.querySelector('.week-nd .item.unplaced');
      if (!row) return null;
      const ctx = document.createElement('canvas').getContext('2d');
      const rgb = c => { ctx.fillStyle = '#000'; ctx.fillStyle = c; ctx.fillRect(0, 0, 1, 1); return [...ctx.getImageData(0, 0, 1, 1).data]; };
      const ch = v => { v /= 255; return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4; };
      const lum = c => 0.2126 * ch(c[0]) + 0.7152 * ch(c[1]) + 0.0722 * ch(c[2]);
      const ratio = (a, b) => { const [x, y] = [lum(a), lum(b)].sort((p, q) => q - p); return (x + 0.05) / (y + 0.05); };
      const page = rgb(getComputedStyle(document.documentElement).getPropertyValue('--page-color'));
      const flat = (c, under) => c[3] === 255 ? c : [0, 1, 2].map(i => Math.round(c[i] * c[3] / 255 + under[i] * (1 - c[3] / 255))).concat(255);
      const chain = []; for (let e = row; e; e = e.parentElement) chain.unshift(e);
      const under = chain.reduce((acc, e) => flat(rgb(getComputedStyle(e).backgroundColor), acc), page);
      const panel = chain.slice(0, chain.indexOf(row.closest('.panel')) + 1).reduce((acc, e) => flat(rgb(getComputedStyle(e).backgroundColor), acc), page);
      const bar = flat(rgb(getComputedStyle(row).getPropertyValue('--unplaced-bar')), panel);
      const title = row.querySelector('.title');
      return { hint: row.getAttribute('title'), text: ratio(flat(rgb(getComputedStyle(title).color), under), under), bar: ratio(bar, panel) };
    });
    assert(m, 'на экране нет подсвеченного дела «на неделе»');
    assert(m.hint === 'не назначено на день', m.hint);
    assert(m.text >= 4.5, `текст ${m.text.toFixed(2)}:1`);
    assert(m.bar >= 1.5, `полоска ${m.bar.toFixed(2)}:1`);
  });
  await check(`${theme}: окно дела`, async () => {
    await clickItem('.day', 'Созвон с заказчиком');
    await page.waitForSelector('.editor[aria-label="Дело"]');
    await shot('task');
    windows.push(['окно дела', await audit(theme, 'task', '.editor')]);
    await escape();
  });
  await check(`${theme}: окно цели`, async () => {
    await clickItem('.role', 'Бег');
    await page.waitForSelector('.editor[aria-label="Цель недели"]');
    await shot('goal');
    windows.push(['окно цели', await audit(theme, 'goal', '.editor')]);
    await escape();
  });
  await check(`${theme}: окно дела — список ролей`, async () => {
    await clickItem('.day', 'Созвон с заказчиком');
    await page.waitForSelector('.editor[aria-label="Дело"]');
    await page.click('.editor .tag-add'); await page.waitForSelector('.editor .popover'); await sleep(300);
    await shot('roles');
    windows.push(['список ролей', await audit(theme, 'roles', '.editor .popover')]);
    await escape(); await escape();
  });
  await check(`${theme}: окно цели — повтор, дни, барабан времени`, async () => {
    await clickItem('.role', 'Бег');
    await page.waitForSelector('.editor[aria-label="Цель недели"]');
    for (let i = 0; i < 14; i++) { const b = await page.$('.editor .sum-chip:not(.static), .editor .adder'); if (!b) break; await b.click(); await new Promise(r => setTimeout(r, 150)); } await sleep(300);
    await page.click('.editor .repeat-row .select-field'); await page.waitForSelector('.editor .popover');
    await page.evaluate(() => [...document.querySelectorAll('.editor .popover .option')].find(o => o.textContent.trim() === 'будни').click());
    await sleep(300);
    windows.push(['повтор и дни', await audit(theme, 'goal-days', '.editor')]);
    await page.click('.editor .wheel-field[aria-label="Время"]'); await page.waitForSelector('.editor .popover'); await sleep(300);
    await shot('wheel');
    windows.push(['барабан', await audit(theme, 'wheel', '.editor .popover')]);
    await escape(); await escape();
  });
  await check(`${theme}: окно роли`, async () => {
    await page.evaluate(() => [...document.querySelectorAll('.role')].find(r => r.querySelector('.role-h .name')?.textContent.trim() === 'Семья').querySelector('.menu').click());
    await page.waitForSelector('.role-editor');
    await sleep(300);
    await shot('role');
    windows.push(['окно роли', await audit(theme, 'role', '.role-editor')]);
    await escape();
  });
  await check(`${theme}: корзина`, async () => {
    await page.click('.trash'); await page.waitForSelector('.trash-popup'); await sleep(500);
    await shot('trash');
    windows.push(['корзина', await audit(theme, 'trash', '.trash-popup')]);
    await escape();
  });
  await check(`${theme}: экран «Копилки»`, async () => {
    await openTotals(page, 'Привычки'); await page.waitForSelector('.habits .jar-card');
    await page.mouse.move(5, 5); await sleep(500); // курсор с кнопки 🫙 — иначе под ним оказывается кнопка экрана (замер в hover)
    await shot('habits');
    windows.push(['копилки', await audit(theme, 'habits', '.habits')]);
    await page.click('.habits .btn.vac-b'); await page.waitForSelector('.editor[aria-label="Каникулы"]'); await sleep(300);
    windows.push(['каникулы', await audit(theme, 'vacations', '.editor[aria-label="Каникулы"]')]);
    await escape(); await escape();
  });
  await check(`${theme}: экран «Выполненное»`, async () => {
    await openTotals(page, 'Выполненное'); await page.waitForSelector('.done-screen .done-row');
    await page.mouse.move(5, 5); await sleep(400);
    await shot('done');
    windows.push(['выполненное', await audit(theme, 'done', '.done-screen')]);
    await escape();
  });
  await check(`${theme}: экран «Отчёт»`, async () => {
    await openTotals(page, 'Отчёт'); await page.waitForSelector('.report-screen .srow');
    await page.mouse.move(5, 5); await sleep(400);
    await shot('report');
    windows.push(['отчёт', await audit(theme, 'report', '.report-screen')]);
    await escape();
  });
  await check(`${theme}: дашборд «Отчёта»`, async () => {
    await openTotals(page, 'Отчёт'); await page.waitForSelector('.report-screen .view-switch');
    await page.evaluate(() => [...document.querySelectorAll('.view-switch .vs')].find(b => b.textContent.trim() === 'Дашборд').click());
    await page.waitForSelector('.dash'); await page.mouse.move(5, 5); await sleep(400);
    await shot('dashboard');
    windows.push(['дашборд', await audit(theme, 'dashboard', '.report-screen')]);
    await page.evaluate(() => [...document.querySelectorAll('.view-switch .vs')].find(b => b.textContent.trim() === 'Список').click());
    await sleep(300);
    await escape();
  });
  await check(`${theme}: «Моя миссия»`, async () => {
    await openThemes(page);
    await page.click('.editor.settings .mission-link');
    await page.waitForSelector('.mission .vision'); await page.mouse.move(5, 5); await sleep(300);
    await shot('mission');
    windows.push(['моя миссия', await audit(theme, 'mission', '.mission')]);
    await escape();
  });
  await check(`${theme}: недельный обзор`, async () => {
    await page.keyboard.press('KeyW');
    await page.waitForSelector('.review .row'); await page.mouse.move(5, 5); await sleep(300);
    await shot('review');
    windows.push(['недельный обзор', await audit(theme, 'review', '.review')]);
    await escape();
  });
  await check(`${theme}: быстрый поиск`, async () => {
    await page.keyboard.down('Control'); await page.keyboard.press('KeyK'); await page.keyboard.up('Control');
    await page.waitForSelector('.search-box input'); await page.keyboard.type('а');
    await page.waitForSelector('.search-box .hit'); await page.mouse.move(5, 5); await sleep(300);
    await shot('search');
    windows.push(['быстрый поиск', await audit(theme, 'search', '.search-box')]);
    await escape();
  });
  await check(`${theme}: меню тем`, async () => {
    await openThemes(page); await sleep(100);
    await shot('menu');
    windows.push(['настройки и темы', await audit(theme, 'menu', '.editor.settings')]);
    await escape();
  });

  if (MUST_PASS_AA.includes(theme) || LIGHT.includes(theme)) {
    const what = LIGHT.includes(theme) ? 'основной текст — AA, второстепенный — от 3:1' : 'весь текст не ниже WCAG AA';
    await check(`${theme}: контраст: ${what}`, async () => {
      const bad = windows.flatMap(([w, fails]) => fails.map(f => `${w}: «${f.text}» ${f.where} ${f.ratio} < ${f.need}`));
      assert(bad.length === 0, `${bad.length} надписей: ${bad.slice(0, 8).join(' | ')}`);
    });
  }
}

// Образец для сравнения
const mock = await browser.newPage();
await mock.setViewport({ width: 1600, height: 900 });
await mock.goto('file:///' + MOCKUP.replace(/\\/g, '/'), { waitUntil: 'networkidle0' }).catch(() => {});
for (const theme of THEMES) {
  await mock.click(`.thsw button[data-t="${theme}"]`).catch(() => {});
  await sleep(400);
  await mock.screenshot({ path: `${SHOTS}/mock-${theme}.png` });
}

fs.writeFileSync(`${SHOTS}/contrast.json`, JSON.stringify(report, null, 1));
await page.evaluate(() => localStorage.setItem('planner.theme', 'lotus'));

await check('в консоли нет ошибок', async () => {
  assert(consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 600));
});

await browser.close();
console.log(results.join('\n'));

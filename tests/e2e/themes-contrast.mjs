// @seed — нужны демо-данные (tools/seed-demo.ps1)
// Темы: контраст текста по WCAG и снимки для сравнения с образцом docs/themes.html.
// Для каждой темы — весь экран и открытые окна (дело, цель, роль, корзина, меню тем).
// Контраст: у каждой видимой надписи цвет текста против фактического фона (подложки складываются
// от страницы вверх, с прозрачностью). Тёмная тема обязана пройти AA: 4.5:1, крупный текст — 3:1.
// Снимки — в out/cmp-<тема>-<окно>.png и out/mock-<тема>.png, отчёт о контрасте — out/contrast.json.
import puppeteer from 'puppeteer-core';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const BASE = process.env.PLANNER_URL ?? 'http://localhost:5071/';
const SHOTS = process.argv[2] ?? '.';
const MOCKUP = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../docs/themes.html');
const THEMES = ['lotus', 'peony', 'platinum', 'obsidian'];
const MUST_PASS_AA = ['obsidian'];
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
function measure(scopeSelector) {
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
  const describe = el => (el.className && typeof el.className === 'string' ? '.' + el.className.trim().split(/\s+/).join('.') : el.tagName.toLowerCase());

  const scope = scopeSelector ? document.querySelector(scopeSelector) : document.body;
  const out = [];
  const add = (el, text, colorString) => {
    const o = opacity(el);
    if (o < 0.05 || !visible(el)) return;
    const bg = background(el);
    const fg0 = parse(colorString);
    const fg = over([fg0[0], fg0[1], fg0[2], fg0[3] * o], bg);
    const cs = getComputedStyle(el);
    const size = parseFloat(cs.fontSize);
    const large = size >= 24 || (size >= 18.66 && Number(cs.fontWeight) >= 700);
    out.push({ text: text.slice(0, 40), where: describe(el), ratio: Math.round(ratio(fg, bg) * 100) / 100, need: large ? 3 : 4.5 });
  };
  for (const el of scope.querySelectorAll('*')) {
    // Недоступные кнопки WCAG из требований к контрасту исключает
    if (el.closest('.drag-ghost, [aria-hidden="true"], .swatches, :disabled')) continue;
    const text = [...el.childNodes].filter(n => n.nodeType === 3).map(n => n.textContent).join('').trim();
    if (text) add(el, text, getComputedStyle(el).color);
    if ((el.tagName === 'INPUT' || el.tagName === 'TEXTAREA') && el.type !== 'time') {
      if (el.value) add(el, el.value, getComputedStyle(el).color);
      else if (el.placeholder) add(el, el.placeholder, getComputedStyle(el, '::placeholder').color);
    }
  }
  return out;
}

const report = {};
async function audit(theme, state, scope) {
  const items = await page.evaluate(measure, scope ?? null);
  const fails = items.filter(i => i.ratio < i.need).sort((a, b) => a.ratio - b.ratio);
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

// В корзине должно что-то лежать — для снимка
await page.goto(BASE, { waitUntil: 'networkidle0' });
await page.evaluate(async () => {
  const week = await (await fetch('/api/week/' + new Date().toISOString().slice(0, 10))).json();
  const t = week.tasks.find(x => x.title === 'Оплатить интернет');
  if (t) await fetch(`/api/tasks/${t.id}`, { method: 'DELETE' });
});

for (const theme of THEMES) {
  await setTheme(theme);
  const shot = name => page.screenshot({ path: `${SHOTS}/cmp-${theme}-${name}.png` });
  const windows = [];

  await check(`${theme}: экран`, async () => {
    assert(await page.evaluate(() => document.documentElement.dataset.theme) === theme, 'тема не включилась');
    await shot('screen');
    windows.push(['экран', await audit(theme, 'screen')]);
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
  await check(`${theme}: меню тем`, async () => {
    await page.click('.palette-b'); await page.waitForSelector('.themes'); await sleep(300);
    await shot('menu');
    windows.push(['меню тем', await audit(theme, 'menu', '.themes')]);
    await escape();
  });

  if (MUST_PASS_AA.includes(theme)) {
    await check(`${theme}: контраст текста не ниже WCAG AA`, async () => {
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

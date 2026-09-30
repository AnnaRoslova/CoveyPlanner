// Перетаскивание на pointer events (HTML5 drag-and-drop плохо работает на iOS Safari).
//
// Разметка (её задают Razor-компоненты, движок ничего не знает о делах и категориях):
//   что тащим:   data-drag="<вид>" data-id="<Id>"            — например, data-drag="someday"
//   куда бросаем: data-drop="<зона>" data-key="<ключ>" data-accept="<виды через пробел>"
//                 data-drop-index="0" — зона-«мишень» с фиксированной позицией (заголовок категории)
//                 data-fixed-sort — в зоне есть записи со своим местом (data-fixed: дела со временем в дне);
//                   позиция считается только среди остальных, а запись с data-fixed ставится сама (по времени)
//                 data-list-head — внутри зоны: заголовок, под которым начинается список (для пустого дня)
//   запись без перетаскивания: data-press — к ней применяются те же правила клика (например, во время поиска)
//
// Мышь: перетаскивание начинается после сдвига на 8px.
// Палец/перо: после долгого нажатия ~300 мс без движения (иначе это прокрутка).
// Клик по записи — только короткое нажатие (< 250 мс) почти без движения (< 4px): долгое нажатие или
// «дёрнувшаяся» запись — не клик. После любого сброса/отмены клики полсекунды не принимаются
// (иначе отпускание кнопки над новым местом открывало бы окно редактирования).
// Автопрокрутка у края прокручиваемой области — после короткой задержки (не при пролёте мимо края).
// Призрак записи, линия вставки и подсветка зоны — отдельные элементы в <body>,
// DOM, которым управляет Blazor, не трогаем (кроме класса drag-source у исходной записи).
// Результат: .NET-метод OnDrop({ kind, id, zone, key, index }).

const DRAG_THRESHOLD = 8;      // px — сдвиг мыши, после которого это уже перетаскивание
const TOUCH_DELAY = 300;       // мс — долгое нажатие пальцем
const TOUCH_TOLERANCE = 8;     // px — палец «дрогнул» за это время, но это ещё не прокрутка
const CLICK_MAX_MS = 250;      // мс — нажатие дольше — не клик
const CLICK_MAX_MOVE = 4;      // px — сдвиг больше — не клик
const AFTER_DROP_MS = 500;     // мс — после сброса клики не принимаются
const EDGE = 48;               // px — зона у края прокручиваемой области для автопрокрутки
const AUTOSCROLL_DELAY = 400;  // мс — сколько держать у края, прежде чем начнётся прокрутка
const MAX_SPEED = 16;          // px за кадр

let dotnet = null;
let pending = null;  // нажали на запись, но перетаскивание ещё не началось
let drag = null;     // идёт перетаскивание
let ignoreClicksUntil = 0;

export function init(dotnetRef) {
    dotnet = dotnetRef;
    document.addEventListener('pointerdown', onPointerDown);
    window.addEventListener('pointermove', onPointerMove);
    window.addEventListener('pointerup', onPointerUp);
    window.addEventListener('pointercancel', () => cancel());
    window.addEventListener('keydown', e => { if (e.key === 'Escape' && drag) cancel(); });
    window.addEventListener('blur', () => cancel());
    // Во время перетаскивания пальцем не даём странице прокручиваться
    document.addEventListener('touchmove', e => { if (drag) e.preventDefault(); }, { passive: false });
    // Долгое нажатие не должно открывать контекстное меню
    document.addEventListener('contextmenu', e => { if (pending || drag) e.preventDefault(); });
    // Клики, которые не должны сработать (после сброса, долгое/дёрнувшееся нажатие), гасим до Blazor.
    // Клик с клавиатуры (Enter/пробел, detail = 0) не трогаем.
    window.addEventListener('click', e => {
        if (e.detail !== 0 && performance.now() < ignoreClicksUntil) {
            e.stopPropagation();
            e.preventDefault();
        }
    }, true);
}

function ignoreClicks(ms) {
    ignoreClicksUntil = Math.max(ignoreClicksUntil, performance.now() + ms);
}

function onPointerDown(e) {
    if (drag || (e.pointerType === 'mouse' && e.button !== 0)) return;
    const el = e.target.closest('[data-drag], [data-press]');
    if (!el) return;
    // Кнопки и поля внутри записи (чекбокс, «удалить») работают как обычно
    if (e.target.closest('button, input, textarea, select, a, [data-no-drag]')) return;

    pending = {
        el,
        canDrag: !!el.dataset.drag && !!el.dataset.id,
        pointerId: e.pointerId, pointerType: e.pointerType,
        startX: e.clientX, startY: e.clientY, startTime: performance.now(),
        maxDist: 0, timer: 0,
    };
    if (e.pointerType !== 'mouse' && pending.canDrag) {
        pending.timer = setTimeout(() => {
            if (pending) {
                navigator.vibrate?.(15);
                start(pending.startX, pending.startY);
            }
        }, TOUCH_DELAY);
    }
}

function onPointerMove(e) {
    if (pending && e.pointerId === pending.pointerId) {
        const dist = Math.hypot(e.clientX - pending.startX, e.clientY - pending.startY);
        pending.maxDist = Math.max(pending.maxDist, dist);
        if (pending.pointerType === 'mouse') {
            if (pending.canDrag && dist >= DRAG_THRESHOLD) start(e.clientX, e.clientY);
        } else if (dist > TOUCH_TOLERANCE) {
            clearPending(); // палец поехал до долгого нажатия — это прокрутка
        }
        return;
    }

    if (drag && e.pointerId === drag.pointerId) {
        e.preventDefault();
        drag.x = e.clientX;
        drag.y = e.clientY;
        moveGhost();
        updateTarget();
    }
}

function onPointerUp(e) {
    if (pending && e.pointerId === pending.pointerId) {
        // Не перетаскивание. Клик — только если нажатие короткое и почти без движения
        const isClick = performance.now() - pending.startTime < CLICK_MAX_MS && pending.maxDist < CLICK_MAX_MOVE;
        if (!isClick) ignoreClicks(100); // клик придёт сразу за отпусканием — погасим его
        clearPending();
        return;
    }
    if (!drag || e.pointerId !== drag.pointerId) return;

    const { kind, id, target } = drag;
    finish();

    if (target) {
        dotnet?.invokeMethodAsync('OnDrop', { kind, id, zone: target.zone, key: target.key, index: target.index });
    }
}

function start(x, y) {
    const { el, pointerId, startX, startY } = pending;
    clearPending();

    const rect = el.getBoundingClientRect();
    const ghost = el.cloneNode(true);
    ghost.classList.add('drag-ghost');
    ghost.removeAttribute('data-drag');
    ghost.style.width = `${rect.width}px`;
    ghost.style.left = '0px';
    ghost.style.top = '0px';
    document.body.appendChild(ghost);

    const line = document.createElement('div');
    line.className = 'drop-line';
    line.hidden = true;
    const zoneBox = document.createElement('div');
    zoneBox.className = 'drop-zone-box';
    zoneBox.hidden = true;
    document.body.append(zoneBox, line);

    drag = {
        el, pointerId, ghost, line, zoneBox,
        kind: el.dataset.drag,
        id: Number(el.dataset.id),
        offsetX: startX - rect.left,
        offsetY: startY - rect.top,
        height: rect.height,
        x, y,
        target: null,
        raf: 0,
    };

    el.classList.add('drag-source');
    document.body.classList.add('dragging');
    window.getSelection()?.removeAllRanges();
    moveGhost();
    updateTarget();
    drag.raf = requestAnimationFrame(autoScroll);
}

function moveGhost() {
    drag.ghost.style.transform = `translate(${drag.x - drag.offsetX}px, ${drag.y - drag.offsetY}px) rotate(1.5deg)`;
}

// Куда сейчас упадёт запись: зона под указателем, принимающая этот вид, и позиция в ней
function updateTarget() {
    // Ближайшая зона, принимающая этот вид; зоны бывают вложенными (роль — зона для целей
    // внутри списка ролей), поэтому, если ближайшая не принимает, ищем выше
    const hit = document.elementFromPoint(drag.x, drag.y);
    let zone = hit?.closest('[data-drop]');
    while (zone && !accepts(zone)) zone = zone.parentElement?.closest('[data-drop]');

    if (!zone) {
        drag.target = null;
        drag.line.hidden = true;
        drag.zoneBox.hidden = true;
        return;
    }

    const zoneRect = zone.getBoundingClientRect();
    showBox(drag.zoneBox, zoneRect);

    // Зона-«мишень» (заголовок категории): позиция фиксированная, линию не рисуем
    if (zone.dataset.dropIndex !== undefined) {
        drag.target = { zone: zone.dataset.drop, key: zone.dataset.key ?? null, index: Number(zone.dataset.dropIndex) };
        drag.line.hidden = true;
        return;
    }

    // Записи этой зоны (без самой перетаскиваемой и без записей вложенных зон)
    const items = [...zone.querySelectorAll('[data-drag]')]
        .filter(item => item !== drag.el && ownZone(item) === zone);
    const autoSorted = zone.hasAttribute('data-fixed-sort');

    // Запись со своим местом (дело со временем): встанет сама, по времени — только подсветка дня
    if (autoSorted && drag.el.hasAttribute('data-fixed')) {
        drag.target = { zone: zone.dataset.drop, key: zone.dataset.key ?? null, index: 0 };
        drag.line.hidden = true;
        return;
    }

    // Позиция — среди записей с ручным порядком (в дне: дела без времени, они всегда ниже дел со временем)
    const slots = autoSorted ? items.filter(item => !item.hasAttribute('data-fixed')) : items;
    const probe = probeY(zone);
    let index = slots.length;
    for (let i = 0; i < slots.length; i++) {
        const r = slots[i].getBoundingClientRect();
        if (probe < r.top + r.height / 2) { index = i; break; }
    }
    drag.target = { zone: zone.dataset.drop, key: zone.dataset.key ?? null, index };

    // Линия вставки: над записью с номером index, под последней — или под заголовком/записями со своим местом
    let lineY;
    if (index < slots.length) lineY = slots[index].getBoundingClientRect().top - 2;
    else if (slots.length > 0) lineY = slots[slots.length - 1].getBoundingClientRect().bottom + 1;
    else if (items.length > 0) lineY = items[items.length - 1].getBoundingClientRect().bottom + 1;
    else lineY = (zone.querySelector('[data-list-head]')?.getBoundingClientRect().bottom ?? zoneRect.top + 4) + 2;

    const sample = (items[0] ?? zone).getBoundingClientRect();
    const left = items[0] ? sample.left : zoneRect.left + 8;
    const right = items[0] ? sample.right : zoneRect.right - 8;
    Object.assign(drag.line.style, { left: `${left}px`, top: `${lineY - 1}px`, width: `${right - left}px` });
    drag.line.hidden = false;
}

// По какой высоте считать позицию. Внутри своего списка — по краю призрака, который «наезжает» на соседа:
// тащим вверх — верхний край, вниз — нижний. Запись меняется местами с соседней, как только край
// призрака пересёк середину соседа (глаз следит за призраком, а не за указателем; в узком дне записи
// бывают в 2–3 строки, и по центру призрака пришлось бы тащить на целую высоту записи).
// Из другой зоны — по указателю.
function probeY(zone) {
    if (ownZone(drag.el) !== zone) return drag.y;
    const src = drag.el.getBoundingClientRect();
    const ghostTop = drag.y - drag.offsetY;
    const ghostBottom = ghostTop + drag.height;
    return ghostTop + drag.height / 2 < src.top + src.height / 2 ? ghostTop : ghostBottom;
}

function accepts(zone) {
    return (zone.dataset.accept ?? '').split(' ').includes(drag.kind);
}

// Зона, в списке которой стоит запись. Сама запись тоже может быть зоной (карточка роли —
// зона для целей), поэтому ищем начиная с родителя.
function ownZone(item) {
    return item.parentElement?.closest('[data-drop]') ?? null;
}

function showBox(box, rect) {
    Object.assign(box.style, {
        left: `${rect.left}px`, top: `${rect.top}px`, width: `${rect.width}px`, height: `${rect.height}px`,
    });
    box.hidden = false;
}

// Автопрокрутка: у края прокручиваемой области под указателем (лента, неделя) листаем в ту сторону
function autoScroll(now) {
    if (!drag) return;
    const scroller = scrollableAt(drag.x, drag.y);
    let dx = 0, dy = 0;
    if (scroller) {
        const r = scroller.getBoundingClientRect();
        dy = scroller.scrollHeight > scroller.clientHeight ? edgeSpeed(drag.y, r.top, r.bottom) : 0;
        dx = scroller.scrollWidth > scroller.clientWidth ? edgeSpeed(drag.x, r.left, r.right) : 0;
    }

    if (dx || dy) {
        // Прокручиваем, только если указатель задержался у края (а не пролетел мимо или бросает у края)
        if (drag.edgeScroller !== scroller) {
            drag.edgeScroller = scroller;
            drag.edgeSince = now;
        }
        if (now - drag.edgeSince >= AUTOSCROLL_DELAY) {
            scroller.scrollTop += dy;
            scroller.scrollLeft += dx;
            updateTarget();
        }
    } else {
        drag.edgeScroller = null;
    }
    drag.raf = requestAnimationFrame(autoScroll);
}

function edgeSpeed(pos, start, end) {
    if (pos < start + EDGE) return -Math.ceil(MAX_SPEED * (1 - Math.max(0, pos - start) / EDGE));
    if (pos > end - EDGE) return Math.ceil(MAX_SPEED * (1 - Math.max(0, end - pos) / EDGE));
    return 0;
}

function scrollableAt(x, y) {
    for (let el = document.elementFromPoint(x, y); el && el !== document.body; el = el.parentElement) {
        const style = getComputedStyle(el);
        const scrollY = /(auto|scroll)/.test(style.overflowY) && el.scrollHeight > el.clientHeight;
        const scrollX = /(auto|scroll)/.test(style.overflowX) && el.scrollWidth > el.clientWidth;
        if (scrollY || scrollX) return el;
    }
    return null;
}

function cancel() {
    clearPending();
    if (drag) finish();
}

function clearPending() {
    if (pending?.timer) clearTimeout(pending.timer);
    pending = null;
}

function finish() {
    cancelAnimationFrame(drag.raf);
    drag.ghost.remove();
    drag.line.remove();
    drag.zoneBox.remove();
    drag.el.classList.remove('drag-source');
    document.body.classList.remove('dragging');
    drag = null;
    // Сброс или отмена: отпускание кнопки над новым местом — не клик
    ignoreClicks(AFTER_DROP_MS);
}

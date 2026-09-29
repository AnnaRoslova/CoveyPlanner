// Перетаскивание на pointer events (HTML5 drag-and-drop плохо работает на iOS Safari).
//
// Разметка (её задают Razor-компоненты, движок ничего не знает о делах и категориях):
//   что тащим:   data-drag="<вид>" data-id="<Id>"            — например, data-drag="someday"
//   куда бросаем: data-drop="<зона>" data-key="<ключ>" data-accept="<виды через пробел>"
//                 data-drop-index="0" — зона-«мишень» с фиксированной позицией (заголовок категории)
//
// Мышь: перетаскивание начинается после сдвига на 5px (иначе это обычный клик).
// Палец/перо: после долгого нажатия ~300 мс без движения (иначе это прокрутка).
// Призрак записи, линия вставки, подсветка зоны и автопрокрутка у краёв — отдельные элементы в <body>,
// DOM, которым управляет Blazor, не трогаем (кроме класса drag-source у исходной записи).
// Результат: .NET-метод OnDrop({ kind, id, zone, key, index }).

const MOUSE_THRESHOLD = 5;   // px — сдвиг мыши, после которого это уже перетаскивание
const TOUCH_DELAY = 300;     // мс — долгое нажатие пальцем
const TOUCH_TOLERANCE = 8;   // px — палец «дрогнул» за это время, но это ещё не прокрутка
const EDGE = 48;             // px — зона у края прокручиваемой области для автопрокрутки
const MAX_SPEED = 16;        // px за кадр

let dotnet = null;
let pending = null;  // нажали, но перетаскивание ещё не началось
let drag = null;     // идёт перетаскивание
let suppressClick = false;

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
    // Клик сразу после перетаскивания — не клик (не открывать редактирование)
    window.addEventListener('click', e => {
        if (suppressClick) { e.stopPropagation(); e.preventDefault(); suppressClick = false; }
    }, true);
}

function onPointerDown(e) {
    if (drag || (e.pointerType === 'mouse' && e.button !== 0)) return;
    const el = e.target.closest('[data-drag]');
    if (!el || !el.dataset.drag || !el.dataset.id) return;
    // Кнопки и поля внутри записи (чекбокс, «удалить») работают как обычно
    if (e.target.closest('button, input, textarea, select, a, [data-no-drag]')) return;

    pending = { el, pointerId: e.pointerId, pointerType: e.pointerType, startX: e.clientX, startY: e.clientY, timer: 0 };
    if (e.pointerType !== 'mouse') {
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
        if (pending.pointerType === 'mouse') {
            if (dist >= MOUSE_THRESHOLD) start(e.clientX, e.clientY);
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
        clearPending();
        return;
    }
    if (!drag || e.pointerId !== drag.pointerId) return;

    const { kind, id, target } = drag;
    finish();
    suppressClick = true;
    setTimeout(() => { suppressClick = false; }, 0);

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
    const hit = document.elementFromPoint(drag.x, drag.y);
    const zone = hit?.closest('[data-drop]');
    const accepts = zone && (zone.dataset.accept ?? '').split(' ').includes(drag.kind);

    if (!accepts) {
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

    // Список: позиция среди записей этой зоны (без самой перетаскиваемой и без вложенных зон)
    const items = [...zone.querySelectorAll('[data-drag]')]
        .filter(item => item !== drag.el && item.closest('[data-drop]') === zone);
    let index = items.length;
    for (let i = 0; i < items.length; i++) {
        const r = items[i].getBoundingClientRect();
        if (drag.y < r.top + r.height / 2) { index = i; break; }
    }
    drag.target = { zone: zone.dataset.drop, key: zone.dataset.key ?? null, index };

    // Линия вставки: над записью с номером index или под последней
    let lineY;
    if (items.length === 0) lineY = zoneRect.top + 6;
    else if (index < items.length) lineY = items[index].getBoundingClientRect().top - 2;
    else lineY = items[items.length - 1].getBoundingClientRect().bottom + 1;

    const left = items[0]?.getBoundingClientRect().left ?? zoneRect.left + 8;
    const right = items[0]?.getBoundingClientRect().right ?? zoneRect.right - 8;
    Object.assign(drag.line.style, { left: `${left}px`, top: `${lineY - 1}px`, width: `${right - left}px` });
    drag.line.hidden = false;
}

function showBox(box, rect) {
    Object.assign(box.style, {
        left: `${rect.left}px`, top: `${rect.top}px`, width: `${rect.width}px`, height: `${rect.height}px`,
    });
    box.hidden = false;
}

// Автопрокрутка: у края прокручиваемой области под указателем (лента, неделя) листаем в ту сторону
function autoScroll() {
    if (!drag) return;
    const scroller = scrollableAt(drag.x, drag.y);
    if (scroller) {
        const r = scroller.getBoundingClientRect();
        const dy = edgeSpeed(drag.y, r.top, r.bottom);
        const dx = edgeSpeed(drag.x, r.left, r.right);
        if (dy && scroller.scrollHeight > scroller.clientHeight) scroller.scrollTop += dy;
        if (dx && scroller.scrollWidth > scroller.clientWidth) scroller.scrollLeft += dx;
        if (dx || dy) updateTarget();
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
}

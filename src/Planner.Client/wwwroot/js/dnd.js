// Перетаскивание на pointer events (HTML5 drag-and-drop плохо работает на iOS Safari).
//
// Разметка (её задают Razor-компоненты, движок ничего не знает о делах и категориях):
//   что тащим:   data-drag="<вид>" data-id="<Id>"            — например, data-drag="someday"
//   куда бросаем: data-drop="<зона>" data-key="<ключ>" data-accept="<виды через пробел>"
//                 data-drop-index="0" — зона-«мишень» с фиксированной позицией (заголовок категории)
//                 data-drop-middle — зона принимает только серединой (у краёв — внешняя зона)
//                 data-drop-hover — зона подсвечивается сама (класс drop-hover, например корзина), без общей рамки
//                 data-fixed-sort — в зоне есть записи со своим местом (data-fixed: дела со временем в дне);
//                   позиция считается только среди остальных, а запись с data-fixed ставится сама (по времени)
//                 data-list-head — внутри зоны: заголовок, под которым начинается список (для пустого дня)
//   запись без перетаскивания: data-press — к ней применяются те же правила клика (например, во время поиска)
//   листание:    data-flip="-1|1" data-flip-accept="<виды>" — края недели и стрелки в шапке: задержать над ним запись ~0,5 с —
//                (data-flip-overlay — накладка: сброс ищется под ней). .NET OnFlipWeek(direction, id) листает неделю (держат дальше — листает дальше); если потом бросили мимо
//                или нажали Esc — OnFlipCancel (неделя возвращается)
//
// Мышь: перетаскивание начинается после сдвига на 8px.
// Палец/перо: после долгого нажатия ~300 мс без движения (иначе это прокрутка).
// Клик по записи — только короткое нажатие (< 250 мс) почти без движения (< 4px): долгое нажатие или
// «дёрнувшаяся» запись — не клик. После любого сброса/отмены клики полсекунды не принимаются
// (иначе отпускание кнопки над новым местом открывало бы окно редактирования).
// Автопрокрутка у края прокручиваемой области — после короткой задержки (не при пролёте мимо края).
// Призрак записи, линия вставки и подсветка зоны — отдельные элементы в <body>,
// DOM, которым управляет Blazor, не трогаем (кроме класса drag-source у исходной записи).
// Результат: .NET-метод OnDrop({ kind, id, zone, key, index, from, shift }): from — ключ зоны, откуда тащили
// (у цели — роль, под которой она показана), shift — при сбросе был зажат Shift (цель → роль: добавить роль,
// а не заменить; пока Shift зажат над другой ролью, у призрака подпись «+ роль»).

const DRAG_THRESHOLD = 8;      // px — сдвиг мыши, после которого это уже перетаскивание
const TOUCH_DELAY = 300;       // мс — долгое нажатие пальцем
const TOUCH_TOLERANCE = 8;     // px — палец «дрогнул» за это время, но это ещё не прокрутка
const CLICK_MAX_MS = 250;      // мс — нажатие дольше — не клик
const CLICK_MAX_MOVE = 4;      // px — сдвиг больше — не клик
const AFTER_DROP_MS = 500;     // мс — после сброса клики не принимаются
const EDGE = 48;               // px — зона у края прокручиваемой области для автопрокрутки
const AUTOSCROLL_DELAY = 400;  // мс — сколько держать у края, прежде чем начнётся прокрутка
const MAX_SPEED = 16;          // px за кадр
const FLIP_DELAY = 500;        // мс — сколько задержать запись над краем недели или стрелкой, чтобы перелистнуть
const FLIP_REPEAT = 700;       // мс — держат дальше: следующая неделя через столько

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
    window.addEventListener('keydown', e => {
        if (e.key === 'Escape' && drag) cancel();
        if (e.key === 'Shift' && drag) setShift(true);
    });
    window.addEventListener('keyup', e => { if (e.key === 'Shift' && drag) setShift(false); });
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
        drag.shift = e.shiftKey;
        drag.x = e.clientX;
        drag.y = e.clientY;
        moveGhost();
        updateTarget();
        updateAddHint();
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

    const { kind, id, target, flipped, from } = drag;
    const shift = e.shiftKey;
    finish();

    if (target) {
        dotnet?.invokeMethodAsync('OnDrop', { kind, id, zone: target.zone, key: target.key, index: target.index, from, shift });
    } else if (flipped) {
        dotnet?.invokeMethodAsync('OnFlipCancel'); // листали, но бросили мимо — неделя возвращается
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
        from: ownZone(el)?.dataset.key ?? null,
        shift: false,
        offsetX: startX - rect.left,
        offsetY: startY - rect.top,
        height: rect.height,
        x, y,
        target: null,
        hoverZone: null,
        flipEl: null,   // стрелка недели под записью
        flipAt: 0,      // когда перелистнуть
        flipped: false, // уже листали
        raf: 0,
    };

    el.classList.add('drag-source');
    document.body.classList.add('dragging');
    document.body.dataset.dragKind = drag.kind; // по нему проступают, например, края недели для листания
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
    const hit = elementUnder(drag.x, drag.y);
    let zone = hit?.closest('[data-drop]');
    while (zone && (!accepts(zone) || atEdge(zone))) zone = zone.parentElement?.closest('[data-drop]');

    setHoverZone(zone?.hasAttribute('data-drop-hover') ? zone : null);
    if (!zone) {
        drag.target = null;
        drag.line.hidden = true;
        drag.zoneBox.hidden = true;
        return;
    }

    const zoneRect = zone.getBoundingClientRect();
    if (drag.hoverZone) drag.zoneBox.hidden = true;
    else showBox(drag.zoneBox, zoneRect);

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
// Shift над другой ролью — цель добавит эту роль, а не перейдёт к ней: подпись «+ роль» у призрака
function setShift(on) {
    drag.shift = on;
    updateAddHint();
}

function updateAddHint() {
    const t = drag.target;
    const adds = drag.shift && drag.kind === 'goal' && t?.zone === 'role' && t.key !== drag.from;
    drag.ghost.classList.toggle('adds-role', !!adds);
}

function probeY(zone) {
    if (ownZone(drag.el) !== zone) return drag.y;
    const src = drag.el.getBoundingClientRect();
    const ghostTop = drag.y - drag.offsetY;
    const ghostBottom = ghostTop + drag.height;
    return ghostTop + drag.height / 2 < src.top + src.height / 2 ? ghostTop : ghostBottom;
}

// Зона с data-drop-hover под указателем — класс drop-hover (единственный класс, который ставим на разметку Blazor,
// кроме drag-source; снимаем его сами)
function setHoverZone(zone) {
    if (drag.hoverZone === zone) return;
    drag.hoverZone?.classList.remove('drop-hover');
    zone?.classList.add('drop-hover');
    drag.ghost.classList.toggle('over-target', !!zone); // призрак бледнеет — подсветку и подпись под ним видно
    drag.hoverZone = zone;
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

    checkFlip(now);
    // После листания дни перерисованы — место сброса пересчитываем, даже если указатель стоит
    if (drag.flipped) updateTarget();
    drag.raf = requestAnimationFrame(autoScroll);
}

// Стрелка недели под записью: задержали — листаем, держат дальше — листаем дальше
function checkFlip(now) {
    const arrow = document.elementFromPoint(drag.x, drag.y)?.closest('[data-flip]');
    let el = arrow && (arrow.dataset.flipAccept ?? '').split(' ').includes(drag.kind) ? arrow : null;

    // Перетаскивание началось под краем недели (запись у левого края понедельника): пока не вышли из края,
    // он не листает — иначе перестановка внутри дня перелистывала бы неделю
    if (drag.flipBlocked === undefined) drag.flipBlocked = el;
    if (el !== drag.flipBlocked) drag.flipBlocked = null;
    else if (el) el = null;
    if (el !== drag.flipEl) {
        drag.flipEl?.classList.remove('flip-armed');
        drag.ghost.classList.toggle('over-flip', !!el); // призрак бледнеет — стрелку под ним видно
        drag.flipEl = el;
        drag.flipAt = now + FLIP_DELAY;
        if (el) charge(el);
        return;
    }

    if (el && now >= drag.flipAt) {
        drag.flipped = true;
        drag.flipAt = now + FLIP_REPEAT;
        dotnet?.invokeMethodAsync('OnFlipWeek', Number(el.dataset.flip), drag.id);
        charge(el);
    }
}

// Класс flip-armed заново — чтобы анимация «зарядки» стрелки началась сначала
function charge(el) {
    el.classList.remove('flip-armed');
    void el.offsetWidth;
    el.classList.add('flip-armed');
}

function edgeSpeed(pos, start, end) {
    if (pos < start + EDGE) return -Math.ceil(MAX_SPEED * (1 - Math.max(0, pos - start) / EDGE));
    if (pos > end - EDGE) return Math.ceil(MAX_SPEED * (1 - Math.max(0, end - pos) / EDGE));
    return 0;
}

function scrollableAt(x, y) {
    for (let el = elementUnder(x, y); el && el !== document.body; el = el.parentElement) {
        const style = getComputedStyle(el);
        const scrollY = /(auto|scroll)/.test(style.overflowY) && el.scrollHeight > el.clientHeight;
        const scrollX = /(auto|scroll)/.test(style.overflowX) && el.scrollWidth > el.clientWidth;
        if (scrollY || scrollX) return el;
    }
    return null;
}

function cancel() {
    clearPending();
    if (drag) {
        const { flipped } = drag;
        finish();
        if (flipped) dotnet?.invokeMethodAsync('OnFlipCancel'); // Esc после листания — неделя возвращается
    }
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
    drag.hoverZone?.classList.remove('drop-hover');
    drag.flipEl?.classList.remove('flip-armed');
    document.body.classList.remove('dragging');
    delete document.body.dataset.dragKind;
    drag = null;
    // Сброс или отмена: отпускание кнопки над новым местом — не клик
    ignoreClicks(AFTER_DROP_MS);
}

// Элемент под указателем, «сквозь» прозрачные для сброса накладки (data-flip-overlay — края недели для листания):
// бросить дело у края понедельника можно, а неделя листается, только если задержаться над краем
function elementUnder(x, y) {
    return document.elementsFromPoint(x, y).find(el => !el.closest('[data-flip-overlay]')) ?? null;
}

// Зона с data-drop-middle (строка цели: «сделать занятием этой цели») принимает только серединой: у верхнего и нижнего края
// (по 30% высоты) сброс проходит к внешней зоне — например, новая цель между строками
function atEdge(zone) {
    if (!zone.hasAttribute('data-drop-middle')) return false;
    const r = zone.getBoundingClientRect();
    const edge = r.height * 0.3;
    return drag.y < r.top + edge || drag.y > r.bottom - edge;
}

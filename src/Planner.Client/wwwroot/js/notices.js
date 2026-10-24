// Место уведомлений (docs/specs/polish-1.md, P1–P6): плашки и «Нет связи» — в свободной части шапки недели,
// между подписью периода («Сегодня») и значками «📈 ⚙»; там нет ни записей, ни кнопок, ни мест броска.
// Пока уведомление на экране, место пересчитывается каждый кадр (свернули панель, сменили неделю, окно изменило размер — P4).
// Окно на этом месте (обзор, окно дела, экраны) или шапка уже 220 px — класс free: внизу по центру экрана (P5).

const MIN_WIDTH = 220;
const GAP = 12;
const WINDOWS = '.editor, .review, .help-screen, .mission, .search-box, .habits, .done-screen, .report-screen, .screen-window, .login-card';

let box = null;
let running = false;

const intersects = (a, b) => a.left < b.right && b.left < a.right && a.top < b.bottom && b.top < a.bottom;

/** Свободная часть шапки недели или null. */
function slot() {
    const head = document.querySelector('.week .week-h');
    if (!head) {
        return null;
    }

    const h = head.getBoundingClientRect();
    if (h.width === 0 || h.height === 0) {
        return null;
    }

    let left = h.left + GAP;
    for (const el of head.querySelectorAll('.week-nav, .wt, .today-b')) {
        left = Math.max(left, el.getBoundingClientRect().right + GAP);
    }

    // Счёт «камней» — только текст (P2): место — до первого значка
    const icon = head.querySelector('.icon-b');
    const right = (icon ? icon.getBoundingClientRect().left : h.right) - GAP;
    const rect = { left, right, top: h.top, bottom: h.bottom };
    if (right - left < MIN_WIDTH) {
        return null;
    }

    for (const w of document.querySelectorAll(WINDOWS)) {
        if (intersects(rect, w.getBoundingClientRect())) {
            return null;
        }
    }

    return rect;
}

function place() {
    if (!box?.isConnected || box.children.length === 0) {
        running = false;
        return;
    }

    const s = slot();
    if (s) {
        box.classList.remove('free');
        Object.assign(box.style, {
            left: `${s.left}px`, top: `${s.top}px`, width: `${s.right - s.left}px`, height: `${s.bottom - s.top}px`, right: 'auto', bottom: 'auto',
        });
    } else if (!box.classList.contains('free')) {
        box.removeAttribute('style');
        box.classList.add('free');
    }

    requestAnimationFrame(place);
}

function start() {
    if (!running && box?.children.length) {
        running = true;
        place();
    }
}

export function watch(element) {
    box = element;
    new MutationObserver(start).observe(box, { childList: true });
    start();
}

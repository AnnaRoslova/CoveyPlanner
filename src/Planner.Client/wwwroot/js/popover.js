// Всплывающее окошко у поля (Popover.razor): список ролей, категорий, барабан времени.
// Стоит position: fixed под своей кнопкой-«якорем» (не помещается снизу — над ней) и не обрезается
// прокруткой окна; при прокрутке и изменении размера переставляется. Закрывается кликом мимо и по Esc
// (как слой js/dismiss.js). Стрелки ↑↓ в списке (data-arrows) переводят фокус между пунктами .option.
import { register, unregister } from './dismiss.js';

const GAP = 4;
const MARGIN = 8;
const opened = new Map();

export function open(popup, anchor, dotnet) {
    const id = register(popup, dotnet, anchor);
    const place = () => placeNear(popup, anchor);
    const onKey = e => moveFocus(popup, e);
    place();
    window.addEventListener('scroll', place, true);
    window.addEventListener('resize', place);
    popup.addEventListener('keydown', onKey);
    opened.set(id, () => {
        window.removeEventListener('scroll', place, true);
        window.removeEventListener('resize', place);
        popup.removeEventListener('keydown', onKey);
        // Окошко закрыли с фокусом внутри (Esc, выбор пункта) — фокус обратно на кнопку, чтобы Esc дальше закрывал окно
        if (!document.activeElement || document.activeElement === document.body || popup.contains(document.activeElement)) {
            anchor.focus({ preventScroll: true });
        }
    });

    // Фокус — внутрь: Esc и стрелки достаются окошку, а не окну под ним
    const first = popup.querySelector('.option.on, .option, [tabindex="0"]') ?? popup;
    first.focus({ preventScroll: true });
    return id;
}

export function close(id) {
    unregister(id);
    opened.get(id)?.();
    opened.delete(id);
}

function placeNear(popup, anchor) {
    if (!anchor.isConnected) {
        return;
    }

    const a = anchor.getBoundingClientRect();
    popup.style.minWidth = `${Math.round(a.width)}px`;
    const p = popup.getBoundingClientRect();
    const below = window.innerHeight - a.bottom - GAP - MARGIN;
    const top = p.height <= below || a.top < p.height + GAP + MARGIN
        ? a.bottom + GAP
        : a.top - GAP - p.height;
    const left = Math.min(a.left, window.innerWidth - p.width - MARGIN);
    popup.style.top = `${Math.max(MARGIN, Math.round(top))}px`;
    popup.style.left = `${Math.max(MARGIN, Math.round(left))}px`;
}

function moveFocus(popup, e) {
    if (!popup.hasAttribute('data-arrows') || (e.key !== 'ArrowDown' && e.key !== 'ArrowUp')) {
        return;
    }

    const options = [...popup.querySelectorAll('.option')];
    if (options.length === 0) {
        return;
    }

    e.preventDefault();
    const index = options.indexOf(document.activeElement);
    const next = e.key === 'ArrowDown'
        ? options[(index + 1) % options.length]
        : options[(index - 1 + options.length) % options.length];
    next.focus({ preventScroll: true });
}

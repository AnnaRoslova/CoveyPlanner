// Горячие клавиши (docs/specs/batch-ux.md, BK) — одна точка обработки; действия выполняет сервис Hotkeys (C#).
// Клавиши — физические (e.code): в русской раскладке N — та же клавиша, что «т».
// BK1: без Ctrl — не во время набора текста и не при открытом окне/экране/списке; с Alt и Meta — не перехватываются.
// Ctrl+K — всегда (и из поля, и из окна); Ctrl+Z — «Вернуть» на плашке, только вне поля ввода; Shift+→ — в окне дела или мысли (polish-1.md, W5).
const PLAIN = {
    KeyN: 'thought', KeyD: 'day', ArrowLeft: 'prev', ArrowRight: 'next', KeyT: 'today',
    KeyH: 'totals', KeyP: 'tabs', KeyW: 'review', KeyG: 'jump',
};

const OVERLAYS = '.editor-backdrop, .habits-backdrop, .done-backdrop, .report-backdrop, .help-backdrop, .search-backdrop, ' +
    '.overlay-backdrop, .picker-backdrop, .popover, body.dragging';

const isTyping = el => !!el && (el.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(el.tagName));

let host = null;

/**
 * Shift+→ в окне дела или мысли (docs/specs/polish-1.md, W5–W6): нажать кнопку «→ след. неделя» этого окна.
 * Не при открытом списке; в поле текста — только когда курсор в конце и ничего не выделено (иначе это выделение текста).
 * Кнопки нет (занятие цели) — клавиша ничего не делает.
 */
function nextWeekButton(e) {
    if (e.code !== 'ArrowRight' || !e.shiftKey || e.ctrlKey || e.altKey || e.metaKey || document.querySelector('.popover')) {
        return null;
    }

    const button = document.querySelector('.editor .next-week-b');
    if (!button) {
        return null;
    }

    const el = document.activeElement;
    if (isTyping(el)) {
        let atEnd = false;
        try {
            atEnd = el.selectionStart === el.value.length && el.selectionEnd === el.value.length;
        } catch {
            // Поле без выделения текста (число, дата) — Shift+→ остаётся полю
        }
        if (!atEnd) {
            return null;
        }
    }

    return button;
}

function actionOf(e) {
    if (e.altKey || e.metaKey) {
        return null;
    }

    if (e.ctrlKey) {
        if (e.code === 'KeyK') {
            return 'search';
        }

        return e.code === 'KeyZ' && !e.shiftKey && !isTyping(document.activeElement) ? 'undo' : null;
    }

    if (isTyping(document.activeElement) || document.querySelector(OVERLAYS)) {
        return null;
    }

    if (e.code === 'Slash' || e.key === '/' || e.key === '?') {
        return e.shiftKey || e.key === '?' ? 'help' : 'search';
    }

    return e.shiftKey ? null : PLAIN[e.code] ?? null;
}

/** Ждать элемент до timeout мс (после действия Blazor дорисовывает экран). */
async function waitFor(selector, timeout = 2000) {
    for (const end = Date.now() + timeout; Date.now() < end;) {
        const el = document.querySelector(selector);
        if (el) {
            return el;
        }
        await new Promise(r => requestAnimationFrame(r));
    }
    return null;
}

async function onKeyDown(e) {
    const nextWeek = nextWeekButton(e);
    if (nextWeek) {
        e.preventDefault();
        nextWeek.click();
        return;
    }

    const action = actionOf(e);
    if (!action || !host) {
        return;
    }

    e.preventDefault();
    await host.invokeMethodAsync('Run', action);
    if (action === 'thought') {
        (await waitFor('.someday .quick input'))?.focus();
    } else if (action === 'day') {
        // Выделенный день (кликом по нему) — иначе сегодняшний (polish-2.md, A4)
        (document.querySelector('.day.picked .day-plus') ?? await waitFor('.day.today .day-plus'))?.click();
    }
}

export function watch(dotnet) {
    host = dotnet;
    document.addEventListener('keydown', e => { onKeyDown(e); });
}

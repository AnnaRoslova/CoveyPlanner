// Горячие клавиши (docs/specs/batch-ux.md, BK) — одна точка обработки; действия выполняет сервис Hotkeys (C#).
// Клавиши — физические (e.code): в русской раскладке N — та же клавиша, что «т».
// BK1: без Ctrl — не во время набора текста и не при открытом окне/экране/списке; с Alt и Meta — не перехватываются.
// Ctrl+K — всегда (и из поля, и из окна); Ctrl+Z — «Вернуть» на плашке, только вне поля ввода.
const PLAIN = {
    KeyN: 'thought', KeyD: 'day', ArrowLeft: 'prev', ArrowRight: 'next', KeyT: 'today',
    KeyH: 'totals', KeyP: 'tabs', KeyW: 'review', KeyG: 'jump',
};

const OVERLAYS = '.editor-backdrop, .habits-backdrop, .done-backdrop, .report-backdrop, .help-backdrop, .search-backdrop, ' +
    '.overlay-backdrop, .picker-backdrop, .popover, body.dragging';

const isTyping = el => !!el && (el.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(el.tagName));

let host = null;

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
    const action = actionOf(e);
    if (!action || !host) {
        return;
    }

    e.preventDefault();
    await host.invokeMethodAsync('Run', action);
    if (action === 'thought') {
        (await waitFor('.someday .quick input'))?.focus();
    } else if (action === 'day') {
        (await waitFor('.day.today .add'))?.click();
    }
}

export function watch(dotnet) {
    host = dotnet;
    document.addEventListener('keydown', e => { onKeyDown(e); });
}

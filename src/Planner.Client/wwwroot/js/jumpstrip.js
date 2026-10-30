// Лента недель мини-календаря «перейти к дате» (WeekJump.razor; docs/specs/polish-3.md, C). Колесо мыши и свайп листают её
// сами (overflow: auto); здесь — только сообщения в .NET: какая неделя в середине окошка (подпись месяца, C2) и что лента
// подходит к краю — пора дорастить (C5). Дорастили сверху — прокрутка сдвигается на высоту добавленного, лента не прыгает.
const EDGE_ROWS = 4; // за сколько строк до края дорастать
const strips = new Map();
let nextId = 1;

export function init(strip, dotnet) {
    const state = { strip, dotnet, pending: false, extending: false, anchor: null, dead: false };
    const rowHeight = () => strip.querySelector('.jump-week')?.offsetHeight || 34;
    const call = (method, ...args) => state.dead ? Promise.resolve() : dotnet.invokeMethodAsync(method, ...args).catch(() => { });

    state.report = async () => {
        state.pending = false;
        if (state.dead) {
            return;
        }

        const middle = strip.scrollTop + strip.clientHeight / 2;
        const row = [...strip.querySelectorAll('.jump-week')].find(r => r.offsetTop + r.offsetHeight > middle);
        if (row) {
            call('OnMiddleWeek', row.dataset.week);
        }

        if (state.extending) {
            return;
        }

        const edge = rowHeight() * EDGE_ROWS;
        if (strip.scrollTop < edge) {
            state.extending = true;
            state.anchor = { height: strip.scrollHeight, top: strip.scrollTop };
            await call('ExtendAsync', -1); // сдвиг прокрутки — в afterExtend, когда Blazor дорисует
        } else if (strip.scrollHeight - strip.scrollTop - strip.clientHeight < edge) {
            state.extending = true;
            state.anchor = null;
            await call('ExtendAsync', 1);
        }
    };

    state.onScroll = () => {
        if (!state.pending) {
            state.pending = true;
            requestAnimationFrame(state.report);
        }
    };
    strip.addEventListener('scroll', state.onScroll, { passive: true });

    const id = nextId++;
    strips.set(id, state);
    return id;
}

/** Лента дорисована после ExtendAsync: сверху — вернуть на место то, что было на экране. */
export function afterExtend(id) {
    const state = strips.get(id);
    if (!state) {
        return;
    }

    if (state.anchor) {
        state.strip.scrollTop = state.anchor.top + (state.strip.scrollHeight - state.anchor.height);
        state.anchor = null;
    }

    state.extending = false;
}

/**
 * Неделю (понедельник yyyy-MM-dd) — к верху окошка: открытие, стрелки «‹ ›», набранная дата. Сразу (не плавно) — и сообщить
 * середину: прокрутка могла не сдвинуться (то же место в перестроенной ленте), а подпись должна быть про то, что видно.
 */
export function scrollToWeek(id, week, smooth) {
    const state = strips.get(id);
    const row = state?.strip.querySelector(`.jump-week[data-week="${week}"]`);
    if (!row) {
        return;
    }

    state.strip.scrollTo({ top: row.offsetTop, behavior: smooth ? 'smooth' : 'instant' });
    if (!smooth) {
        state.report();
    }
}

/** Выбранный стрелками день ушёл за край — подтянуть его неделю в окошко (C4). */
export function reveal(id, week) {
    const state = strips.get(id);
    const row = state?.strip.querySelector(`.jump-week[data-week="${week}"]`);
    if (!row) {
        return;
    }

    const strip = state.strip;
    if (row.offsetTop < strip.scrollTop) {
        strip.scrollTop = row.offsetTop;
    } else if (row.offsetTop + row.offsetHeight > strip.scrollTop + strip.clientHeight) {
        strip.scrollTop = row.offsetTop + row.offsetHeight - strip.clientHeight;
    }
}

/** Календарь закрыли: больше ничего не сообщать в .NET. */
export function dispose(id) {
    const state = strips.get(id);
    if (state) {
        state.dead = true;
        state.strip.removeEventListener('scroll', state.onScroll);
        strips.delete(id);
    }
}

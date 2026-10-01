// Длинные названия записей — не больше двух строк (CSS: .clamp в ItemRow). Здесь только отмечаем,
// у каких записей текст длиннее (атрибут data-long): им нужны кликабельное «…» и стрелка «развернуть».
// Размер записи меняется (ширина колонки, шрифт, текст) — пересчитываем.
const resize = new ResizeObserver(entries => entries.forEach(e => mark(e.target)));
const watched = new WeakSet();

function mark(el) {
    const expanded = el.classList.contains('expanded');
    const lineHeight = parseFloat(getComputedStyle(el).lineHeight) || 17;
    // Развёрнутая запись остаётся «длинной», если в ней больше двух строк — чтобы её можно было свернуть
    const long = expanded ? el.clientHeight > lineHeight * 2.5 : el.scrollHeight > el.clientHeight + 1;
    el.toggleAttribute('data-long', long);
}

function scan(root) {
    const found = root.matches?.('.clamp') ? [root] : [];
    found.push(...(root.querySelectorAll?.('.clamp') ?? []));
    for (const el of found) {
        if (!watched.has(el)) {
            watched.add(el);
            resize.observe(el); // сразу вызовет mark
        }
    }
}

export function watch() {
    scan(document);
    new MutationObserver(mutations => {
        for (const m of mutations) {
            m.addedNodes.forEach(n => n.nodeType === Node.ELEMENT_NODE && scan(n));
            // Текст поменялся, а высота блока (две строки) — нет: ResizeObserver об этом не узнает
            const host = (m.target.nodeType === Node.ELEMENT_NODE ? m.target : m.target.parentElement)?.closest('.clamp');
            if (host) {
                mark(host);
            }
        }
    }).observe(document.body, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ['class'] });
}

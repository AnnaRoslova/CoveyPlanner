// Длинные названия записей — не больше двух строк (CSS: .clamp в ItemRow). Здесь отмечаем, у каких записей текст
// обрезан (атрибут data-long), и показываем у них подсказку с полным текстом: навели и задержали указатель ~0,5 с.
// У необрезанных подсказки нет. Во время перетаскивания, при нажатии и прокрутке подсказка прячется.
// Размер записи меняется (ширина колонки, шрифт, текст) — пересчитываем.
const TIP_DELAY = 500; // мс — сколько задержать указатель над записью

const resize = new ResizeObserver(entries => entries.forEach(e => mark(e.target)));
const watched = new WeakSet();

let tip = null;       // элемент подсказки (один на страницу)
let hovered = null;   // обрезанное название под указателем
let timer = 0;

function mark(el) {
    el.toggleAttribute('data-long', el.scrollHeight > el.clientHeight + 1);
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

// Запись, над которой указатель: подсказка — только у обрезанного названия
function onPointerOver(e) {
    if (e.pointerType !== 'mouse') return; // на сенсорном экране наведения нет — полный текст в окне по клику
    const item = e.target.closest?.('.item');
    const title = item?.querySelector('.clamp');
    if (title === hovered) return;
    hideTip();
    hovered = title?.hasAttribute('data-long') ? title : null;
    if (hovered) timer = setTimeout(showTip, TIP_DELAY);
}

function showTip() {
    if (!hovered?.isConnected || document.body.classList.contains('dragging')) return;
    tip ??= Object.assign(document.createElement('div'), { className: 'clamp-tip', role: 'tooltip' });
    tip.textContent = hovered.textContent.trim();
    document.body.appendChild(tip);

    // Под записью, а если внизу нет места — над ней; не выходя за края окна
    const item = hovered.closest('.item') ?? hovered;
    const r = item.getBoundingClientRect();
    const t = tip.getBoundingClientRect();
    const left = Math.min(Math.max(8, r.left), window.innerWidth - t.width - 8);
    const below = r.bottom + 4;
    const top = below + t.height > window.innerHeight - 8 ? r.top - t.height - 4 : below;
    Object.assign(tip.style, { left: `${left}px`, top: `${top}px` });
}

function hideTip() {
    clearTimeout(timer);
    tip?.remove();
}

function reset() {
    hideTip();
    hovered = null;
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
        // Запись с подсказкой исчезла (удалили, перерисовали)
        if (hovered && !hovered.isConnected) reset();
    }).observe(document.body, { childList: true, subtree: true, characterData: true });

    document.addEventListener('pointerover', onPointerOver);
    document.addEventListener('pointerdown', reset, true);
    document.addEventListener('wheel', reset, { capture: true, passive: true });
    document.addEventListener('scroll', reset, true);
    window.addEventListener('blur', reset);
}

// Встроенные редакторы (Dismissable.razor) закрываются, как окна: кликом мимо и по Esc.
// Каждый открытый редактор — «слой». Клик вне слоя закрывает его; Esc закрывает верхний
// (последний открытый) слой. Закрываем по самому клику, а не по нажатию кнопки: иначе редактор
// исчезал бы между нажатием и отпусканием, всё ниже сдвигалось, и клик (например, по «⋯»
// другой категории) приходился бы мимо. Клик, начатый внутри слоя (выделение текста в поле
// с протяжкой наружу), слой не закрывает.
// Всплывающее окошко (js/popover.js) — тоже слой; клик по его кнопке-«якорю» не считается кликом мимо
// (кнопка сама открывает и закрывает окошко). Esc в поле с data-esc-local слои не закрывает — его
// обрабатывает само поле (например, отмена ввода числа в барабане).
const layers = [];
let nextId = 1;
let installed = false;
let downTarget = null;

function install() {
    installed = true;
    document.addEventListener('pointerdown', e => downTarget = e.target, true);
    document.addEventListener('click', e => {
        for (const layer of [...layers]) {
            const inside = node => node instanceof Node
                && (layer.element.contains(node) || (layer.anchor?.contains(node) ?? false));
            if (layer.element?.isConnected && !inside(e.target) && !inside(downTarget)) {
                layer.dotnet.invokeMethodAsync('Dismiss', true);
            }
        }
    }, true);
    document.addEventListener('keydown', e => {
        const top = layers.findLast(l => l.element?.isConnected);
        if (e.key === 'Escape' && top && !e.target.closest?.('[data-esc-local]')) {
            top.dotnet.invokeMethodAsync('Dismiss', false);
        }
    }, true);
}

export function register(element, dotnet, anchor = null) {
    if (!installed) {
        install();
    }

    const id = nextId++;
    layers.push({ id, element, dotnet, anchor });
    return id;
}

export function unregister(id) {
    const index = layers.findIndex(l => l.id === id);
    if (index >= 0) {
        layers.splice(index, 1);
    }
}

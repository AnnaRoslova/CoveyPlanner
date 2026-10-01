// Тема оформления: атрибут data-theme на <html> и выбор в localStorage (ThemeState.cs).
// При загрузке тему ставит маленький скрипт в index.html — до первой отрисовки.
const Key = 'planner.theme';

export function getTheme() {
    return document.documentElement.dataset.theme || 'lotus';
}

export function setTheme(name) {
    document.documentElement.dataset.theme = name;
    syncWindowColor();
    try {
        localStorage.setItem(Key, name);
    } catch {
        // Хранилище недоступно (приватный режим) — тема действует до перезагрузки
    }
}

// Рамка окна установленного приложения (<meta name="theme-color">) — в цвет фона темы (--page-color)
function syncWindowColor() {
    const color = getComputedStyle(document.documentElement).getPropertyValue('--page-color').trim();
    const meta = document.querySelector('meta[name="theme-color"]');
    if (meta && color) {
        meta.content = color;
    }
}

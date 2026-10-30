// Сообщает .NET (LayoutState.OnResize) ширину окна: сразу и при каждом изменении, не чаще раза в кадр.
export function watchWidth(dotnet) {
    let pending = false;
    const report = () => {
        pending = false;
        dotnet.invokeMethodAsync('OnResize', window.innerWidth);
    };
    window.addEventListener('resize', () => {
        if (!pending) {
            pending = true;
            requestAnimationFrame(report);
        }
    });
    return window.innerWidth;
}

// Сообщает .NET (TodayWatcher.OnActivated), что окно снова активно: вкладка стала видимой
// или окно получило фокус. Там проверяется, не наступил ли новый день.
export function watchActivation(dotnet) {
    const report = () => {
        if (document.visibilityState === 'visible') {
            dotnet.invokeMethodAsync('OnActivated');
        }
    };
    document.addEventListener('visibilitychange', report);
    window.addEventListener('focus', report);
}

// Сообщает .NET (TodayWatcher.OnHidden), что окно ушло в фон или закрывается: отложенное удаление
// («Удалено · Вернуть») нужно отправить на сервер сейчас, а не через несколько секунд.
export function watchHidden(dotnet) {
    const report = () => {
        if (document.visibilityState === 'hidden') {
            dotnet.invokeMethodAsync('OnHidden');
        }
    };
    document.addEventListener('visibilitychange', report);
    window.addEventListener('pagehide', () => dotnet.invokeMethodAsync('OnHidden'));
}

// Устройство без наведения (касание): подсказки «при наведении» там показываются иначе (DayHintState, polish-3.md, H3).
export function isTouch() {
    return window.matchMedia('(hover: none)').matches;
}

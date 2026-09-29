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

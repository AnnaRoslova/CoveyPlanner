// Нет связи с сервером (docs/specs/batch-ux.md, BN): событие online — повторить сразу; закрыть вкладку, пока есть
// несохранённые изменения, — браузер спросит (beforeunload, BN5). Число несохранённых сообщает OfflineBanner.
let unsaved = 0;

export function watch(dotnet) {
    window.addEventListener('online', () => dotnet.invokeMethodAsync('RetryNow'));
    window.addEventListener('beforeunload', e => {
        if (unsaved > 0) {
            e.preventDefault();
            e.returnValue = '';
        }
    });
}

export function setUnsaved(count) {
    unsaved = count;
}

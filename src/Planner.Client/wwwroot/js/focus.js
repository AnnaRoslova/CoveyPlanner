// Фокус в только что раскрытом поле окна (docs/specs/ui-declutter.md, U2.2): «+ длительность» — фокус на барабан, «+ заметки» — в текст.
export function focusField(key) {
    const field = document.querySelector(`.editor [data-field="${key}"]`);
    const target = field?.querySelector('textarea, input:not([type="hidden"]), button, [tabindex]:not([tabindex="-1"])');
    target?.focus({ preventScroll: false });
}

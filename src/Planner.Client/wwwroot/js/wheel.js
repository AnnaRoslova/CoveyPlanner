// Барабан (WheelColumn.razor): колонка чисел, как в будильнике iPhone. Значения рисует Blazor
// (выбранное — по центру), а здесь — жесты: перетаскивание мышью и пальцем (лента едет за пальцем,
// каждые ROW пикселей — шаг), колесо мыши и трекпад, короткое нажатие по строке (по центру — ввод числа,
// по соседней — выбрать её). Шаги уходят в .NET: Step(+1 / −1), Tap(смещение строки от центра).
const ROW = 34;
const VISIBLE = 5;
const WHEEL_STEP = 40; // пикселей прокрутки трекпада на шаг
const TAP_SLOP = 4;
const attached = new Map();
let nextId = 1;

export function attach(wheel, track, dotnet) {
    let drag = null;
    let wheelRest = 0;

    const shift = y => track.style.transform = y ? `translateY(${y}px)` : '';
    const step = delta => dotnet.invokeMethodAsync('Step', delta);

    const down = e => {
        if (e.button !== 0 || e.target.closest('input')) {
            return;
        }

        wheel.setPointerCapture(e.pointerId);
        track.classList.remove('settle');
        drag = { id: e.pointerId, startY: e.clientY, lastY: e.clientY, offset: 0, moved: false };
        wheel.focus({ preventScroll: true });
        e.preventDefault();
    };

    const move = e => {
        if (!drag || e.pointerId !== drag.id) {
            return;
        }

        drag.offset += e.clientY - drag.lastY;
        drag.lastY = e.clientY;
        drag.moved ||= Math.abs(e.clientY - drag.startY) > TAP_SLOP;
        // Тянем вниз — сверху приходят меньшие числа; вверх — большие
        while (drag.offset >= ROW / 2) {
            drag.offset -= ROW;
            step(-1);
        }
        while (drag.offset <= -ROW / 2) {
            drag.offset += ROW;
            step(1);
        }
        shift(drag.offset);
    };

    const up = e => {
        if (!drag || e.pointerId !== drag.id) {
            return;
        }

        if (!drag.moved) {
            const y = e.clientY - wheel.getBoundingClientRect().top;
            const row = Math.floor(y / ROW) - Math.floor(VISIBLE / 2);
            dotnet.invokeMethodAsync('Tap', Math.max(-2, Math.min(2, row)));
        }

        drag = null;
        track.classList.add('settle');
        shift(0);
    };

    const scroll = e => {
        e.preventDefault();
        const delta = e.deltaMode === 0 ? e.deltaY : e.deltaY * ROW;
        // Щелчок колеса мыши — один шаг; трекпад — по накопленному пути
        if (Math.abs(delta) >= 50) {
            wheelRest = 0;
            step(Math.sign(delta));
            return;
        }

        wheelRest += delta;
        while (Math.abs(wheelRest) >= WHEEL_STEP) {
            step(Math.sign(wheelRest));
            wheelRest -= Math.sign(wheelRest) * WHEEL_STEP;
        }
    };

    wheel.addEventListener('pointerdown', down);
    wheel.addEventListener('pointermove', move);
    wheel.addEventListener('pointerup', up);
    wheel.addEventListener('pointercancel', up);
    wheel.addEventListener('wheel', scroll, { passive: false });

    const id = nextId++;
    attached.set(id, () => {
        wheel.removeEventListener('pointerdown', down);
        wheel.removeEventListener('pointermove', move);
        wheel.removeEventListener('pointerup', up);
        wheel.removeEventListener('pointercancel', up);
        wheel.removeEventListener('wheel', scroll);
    });
    return id;
}

export function detach(id) {
    attached.get(id)?.();
    attached.delete(id);
}

// Поле ввода числа: фокус и выделение (после клика по центру).
export function focusInput(input) {
    input.focus({ preventScroll: true });
    input.select();
}

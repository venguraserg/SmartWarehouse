// Ручной сканер штрих- и QR-кодов (USB / Bluetooth) работает как клавиатура: очень быстро «печатает» код
// и нажимает Enter (или Tab). Человек так быстро не печатает — по скорости ввод сканера и узнаём.
// Распознанный код передаётся в приложение (OnHardwareScan), а «напечатанное» в поле — убирается.
//
// Не перехватываем: поля поиска (type=search) и поля с атрибутом data-scanner="off" — туда сканер пишет как обычно.

const MAX_GAP_MS = 50;      // наибольший промежуток между символами сканера
const MAX_AVG_GAP_MS = 30;  // средний промежуток — у человека обычно 80–200 мс
const MIN_LENGTH = 4;       // короче — не код

let dotnet = null;
let attached = false;

let buffer = "";
let gaps = [];
let last = 0;
let target = null;
let savedValue = null;

export function attach(ref) {
    dotnet = ref;
    if (!attached) {
        document.addEventListener("keydown", onKeyDown, true);
        attached = true;
    }
}

export function detach() {
    dotnet = null;
    reset();
}

function reset() {
    buffer = "";
    gaps = [];
    target = null;
    savedValue = null;
}

function passThrough(el) {
    if (!el || !el.closest) return false;
    if (el.closest('[data-scanner="off"]')) return true;
    return el.tagName === "INPUT" && el.type === "search";
}

function hasValue(el) {
    return el && (el.tagName === "INPUT" || el.tagName === "TEXTAREA") && typeof el.value === "string";
}

function looksLikeScanner() {
    if (buffer.length < MIN_LENGTH || gaps.length === 0) return false;
    const max = Math.max(...gaps);
    const avg = gaps.reduce((a, b) => a + b, 0) / gaps.length;
    return max <= MAX_GAP_MS && avg <= MAX_AVG_GAP_MS;
}

function onKeyDown(e) {
    if (!dotnet || e.isComposing) return;
    if (e.ctrlKey || e.altKey || e.metaKey) { reset(); return; }

    const el = e.target;
    if (passThrough(el)) { reset(); return; }

    const now = performance.now();

    if (e.key === "Enter" || e.key === "Tab") {
        const fresh = now - last <= MAX_GAP_MS * 2;
        if (fresh && looksLikeScanner()) {
            e.preventDefault();
            e.stopPropagation();
            const code = buffer;

            // Убираем из поля то, что успел «напечатать» сканер.
            if (target && savedValue !== null && hasValue(target)) {
                target.value = savedValue;
                target.dispatchEvent(new Event("input", { bubbles: true }));
            }
            reset();
            navigator.vibrate?.(60);
            dotnet.invokeMethodAsync("OnHardwareScan", code).catch(() => { /* страница закрыта */ });
            return;
        }
        reset();
        return;
    }

    // Shift, CapsLock и прочие служебные клавиши — пропускаем, не сбрасывая набор.
    if (e.key.length !== 1) return;

    if (buffer.length === 0 || now - last > MAX_GAP_MS) {
        // Начало нового набора: запоминаем поле и его значение до первого символа.
        buffer = "";
        gaps = [];
        target = el;
        savedValue = hasValue(el) ? el.value : null;
    } else {
        gaps.push(now - last);
    }
    buffer += e.key;
    last = now;
}

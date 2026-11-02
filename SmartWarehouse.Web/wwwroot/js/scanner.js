// Сканирование QR и штрих-кодов камерой телефона.
// 1) Встроенный в браузер BarcodeDetector (Chrome / Edge на Android) — без загрузки библиотек.
// 2) Иначе (Safari на iPhone и др.) — библиотека html5-qrcode: сначала из /lib (если администратор
//    положил файл на сервер), затем из интернета (jsDelivr).
// Камера в браузере доступна только по https (или на localhost) — это ограничение браузеров.

const LIB_LOCAL = "/lib/html5-qrcode.min.js";
const LIB_CDN = "https://cdn.jsdelivr.net/npm/html5-qrcode@2.3.8/html5-qrcode.min.js";

let stream = null;
let timer = null;
let html5 = null;
let stopped = true;

export function isSecure() {
    return window.isSecureContext === true;
}

/** Запускает сканер в контейнере; при распознавании вызывает dotnet.OnScanned(код). Возвращает "ok" или текст ошибки. */
export async function start(containerId, dotnet) {
    if (!isSecure()) return "insecure";
    if (!navigator.mediaDevices?.getUserMedia) return "Браузер не даёт доступ к камере.";
    await stop();
    stopped = false;

    const container = document.getElementById(containerId);
    if (!container) return "Нет области для камеры.";

    try {
        if ("BarcodeDetector" in window) {
            const supported = await BarcodeDetector.getSupportedFormats();
            if (supported.length > 0) {
                return await startNative(container, dotnet, supported);
            }
        }
        return await startLibrary(containerId, dotnet);
    } catch (e) {
        await stop();
        if (e && e.name === "NotAllowedError") return "Нет разрешения на камеру — разрешите доступ в настройках браузера.";
        if (e && e.name === "NotFoundError") return "Камера не найдена.";
        return "Не удалось включить камеру: " + (e?.message ?? e);
    }
}

async function startNative(container, dotnet, formats) {
    const detector = new BarcodeDetector({ formats });
    stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: { ideal: "environment" } }, audio: false });

    container.innerHTML = "";
    const video = document.createElement("video");
    video.setAttribute("playsinline", "");
    video.muted = true;
    video.srcObject = stream;
    video.className = "scanner-video";
    container.appendChild(video);
    await video.play();

    const tick = async () => {
        if (stopped) return;
        try {
            const codes = await detector.detect(video);
            if (codes.length > 0 && codes[0].rawValue) {
                const value = codes[0].rawValue;
                await stop();
                navigator.vibrate?.(80);
                await dotnet.invokeMethodAsync("OnScanned", value);
                return;
            }
        } catch {
            // кадр ещё не готов — пробуем дальше
        }
        timer = setTimeout(tick, 200);
    };
    tick();
    return "ok";
}

async function startLibrary(containerId, dotnet) {
    if (!window.Html5Qrcode) {
        const loaded = (await loadScript(LIB_LOCAL)) || (await loadScript(LIB_CDN));
        if (!loaded || !window.Html5Qrcode)
            return "nolib";
    }
    html5 = new window.Html5Qrcode(containerId, { verbose: false });
    await html5.start(
        { facingMode: "environment" },
        { fps: 10, qrbox: (w, h) => { const s = Math.floor(Math.min(w, h) * 0.75); return { width: s, height: s }; } },
        async (text) => {
            if (stopped) return;
            await stop();
            navigator.vibrate?.(80);
            await dotnet.invokeMethodAsync("OnScanned", text);
        },
        () => { /* код в кадре не найден — обычная ситуация */ });
    return "ok";
}

function loadScript(src) {
    return new Promise((resolve) => {
        const s = document.createElement("script");
        s.src = src;
        s.async = true;
        s.onload = () => resolve(true);
        s.onerror = () => { s.remove(); resolve(false); };
        document.head.appendChild(s);
    });
}

export async function stop() {
    stopped = true;
    if (timer) { clearTimeout(timer); timer = null; }
    if (stream) { stream.getTracks().forEach(t => t.stop()); stream = null; }
    if (html5) {
        try { await html5.stop(); } catch { /* уже остановлен */ }
        try { html5.clear(); } catch { /* ignore */ }
        html5 = null;
    }
}

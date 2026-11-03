// Поля кодов (склад, место хранения, артикул, штрих-код) — только латиница: как будто при фокусе на поле
// включена английская раскладка. Буквы, набранные в русской раскладке, сразу заменяются латинскими
// с той же клавиши («ЫЗ-1» → «SP-1»). Переключить раскладку системы сайт не может — поэтому так.
//
// Поле помечается атрибутом data-latin; data-latin="upper" — ещё и в верхний регистр.
// Обработчик стоит на всём документе в фазе перехвата: срабатывает раньше Blazor, и в приложение
// попадает уже исправленный текст.
(function () {
    const ru = "йцукенгшщзхъфывапролджэячсмитьбюёЙЦУКЕНГШЩЗХЪФЫВАПРОЛДЖЭЯЧСМИТЬБЮЁ";
    const en = "qwertyuiop[]asdfghjkl;'zxcvbnm,.`QWERTYUIOP{}ASDFGHJKL:\"ZXCVBNM<>~";
    const map = new Map();
    for (let i = 0; i < ru.length; i++) map.set(ru[i], en[i]);

    function toLatin(text, upper) {
        let result = "";
        for (const c of text) result += map.get(c) ?? c;
        return upper ? result.toUpperCase() : result;
    }

    document.addEventListener("input", e => {
        const el = e.target;
        if (!(el instanceof HTMLInputElement) || !el.hasAttribute("data-latin")) return;

        const upper = el.getAttribute("data-latin") === "upper";
        const value = el.value;
        const fixed = toLatin(value, upper);
        if (fixed === value) return;

        // Длина не меняется (замена символ на символ) — курсор остаётся на месте.
        const start = el.selectionStart, end = el.selectionEnd;
        el.value = fixed;
        try { el.setSelectionRange(start, end); } catch { /* поле без выделения */ }
    }, true);
})();

// Быстрое создание элемента справочника (.quick-add) стоит внутри формы документа или позиции:
// Enter в его поле не должен отправлять всю форму (провести документ) — он нажимает «Создать» самого блока.
(function () {
    document.addEventListener("keydown", e => {
        if (e.key !== "Enter" || !(e.target instanceof HTMLInputElement)) return;
        const box = e.target.closest(".quick-add");
        if (!box) return;
        e.preventDefault();
        const create = box.querySelector("button.btn-primary:not([disabled])");
        if (create) create.click();
    }, true);
})();

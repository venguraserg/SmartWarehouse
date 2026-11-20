// Оформление интерфейса: применить сразу (без перезагрузки) и запомнить в cookie для следующих страниц.
// За учётной записью выбор сохраняет сервер (api/auth/theme) — при входе cookie ставится из него.
window.swTheme = {
    get: function () { return document.documentElement.getAttribute('data-theme'); },
    set: function (key) {
        document.documentElement.setAttribute('data-theme', key);
        document.cookie = 'sw.theme=' + encodeURIComponent(key) + '; path=/; max-age=31536000; samesite=lax';
    }
};

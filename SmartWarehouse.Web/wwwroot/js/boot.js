// Экран запуска: пока интерактивная часть (Blazor) не подключилась, вместо белого листа — «Загрузка…».
// Если за 15 секунд не подключилась — причина и данные для поддержки: версия браузера / Android WebView
// и ошибки скриптов. Написано на старом JavaScript (ES5), чтобы сработать и в очень старом WebView на ТСД.
(function () {
    var errors = [];
    window.addEventListener('error', function (e) {
        errors.push((e.message || 'ошибка') + (e.filename ? ' — ' + e.filename.split('/').pop() + ':' + e.lineno : ''));
    }, true);
    window.addEventListener('unhandledrejection', function (e) {
        var r = e.reason;
        errors.push('Promise: ' + (r && r.message ? r.message : String(r)));
    });

    function started() {
        // Разметка основного макета появляется, только когда интерактивная часть запустилась.
        return !!document.getElementById('blazor-error-ui');
    }

    function chromeVersion() {
        var m = /Chrome\/(\d+)/.exec(navigator.userAgent);
        return m ? parseInt(m[1], 10) : null;
    }

    var startedAt = Date.now();
    var timer = setInterval(function () {
        var boot = document.getElementById('sw-boot');
        if (!boot) { clearInterval(timer); return; }
        if (started()) {
            boot.parentNode.removeChild(boot);
            clearInterval(timer);
            return;
        }
        if (Date.now() - startedAt < 15000) return;
        clearInterval(timer);

        var problem = document.getElementById('sw-boot-problem');
        var details = document.getElementById('sw-boot-details');
        var hint = document.getElementById('sw-boot-hint');
        var chrome = chromeVersion();
        if (chrome !== null && chrome < 100 && hint) {
            hint.textContent = 'Похоже, браузер устарел (Chrome ' + chrome + '). На телефоне или ТСД обновите ' +
                '«Android System WebView» и «Google Chrome» в Play Маркете (или попросите IT), затем откройте приложение заново.';
        }
        if (details) {
            details.textContent =
                'Браузер: ' + navigator.userAgent + '\n' +
                'Адрес: ' + location.href + '\n' +
                'WebSocket: ' + (typeof WebSocket !== 'undefined' ? 'есть' : 'нет') + '\n' +
                (errors.length ? 'Ошибки:\n' + errors.join('\n') : 'Ошибок скриптов не было — возможно, нет связи с сервером.');
        }
        if (problem) problem.removeAttribute('hidden');
        var loading = document.getElementById('sw-boot-loading');
        if (loading) loading.setAttribute('hidden', '');
    }, 300);
})();

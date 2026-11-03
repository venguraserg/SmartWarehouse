// Связь с приложением «Склад» для Android (mobile/android). В обычном браузере window.SmartWarehouseApp нет —
// тогда всё это ничего не делает.
//
// Приложение сканирует камерой или встроенным сканером ТСД и передаёт код в window.swOnNativeScan(code):
// дальше код идёт тем же путём, что от ручного сканера (keyboard-scanner.js → MainLayout.OnHardwareScan):
// в документе добавляется строка, на остальных страницах открывается ячейка или позиция.
(function () {
    window.swApp = {
        isNative: () => !!window.SmartWarehouseApp,
        scan: () => window.SmartWarehouseApp && window.SmartWarehouseApp.scanCamera(),
        openSettings: () => window.SmartWarehouseApp && window.SmartWarehouseApp.openSettings(),
        version: () => (window.SmartWarehouseApp ? window.SmartWarehouseApp.version() : null)
    };

    window.swOnNativeScan = code => {
        if (window.swHardwareScan) window.swHardwareScan(code);
    };
})();

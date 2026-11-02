namespace SmartWarehouse.Web.Services;

/// <summary>
/// Код с ручного сканера (USB / Bluetooth). Ввод сканера перехватывается на любой странице (MainLayout).
/// Страница, которая умеет принимать коды (например, документ — добавить позицию строкой), подписывается сюда;
/// если подписчика нет или он код не принял — открывается найденная ячейка или позиция.
/// Сервис живёт в пределах вкладки (circuit), как и страница.
/// </summary>
public class ScannerHub
{
    private Func<string, Task<bool>>? handler;

    /// <summary>Страница принимает коды. Вернуть true — код обработан страницей.</summary>
    public IDisposable Subscribe(Func<string, Task<bool>> onScan)
    {
        handler = onScan;
        return new Subscription(this, onScan);
    }

    /// <summary>Передать код странице. false — подписчика нет или он код не принял.</summary>
    public async Task<bool> DispatchAsync(string code) => handler is { } h && await h(code);

    private sealed class Subscription(ScannerHub hub, Func<string, Task<bool>> onScan) : IDisposable
    {
        public void Dispose()
        {
            if (hub.handler == onScan)
                hub.handler = null;
        }
    }
}

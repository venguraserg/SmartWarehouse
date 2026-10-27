using Microsoft.JSInterop;

namespace SmartWarehouse.Web.Services;

/// <summary>
/// Запоминание мелочей в браузере пользователя (кто проводит документы, последняя ячейка прихода и т.п.),
/// чтобы подставлять их в формы. Если браузер не даёт доступ к хранилищу — просто ничего не запоминается.
/// </summary>
public class BrowserStore(IJSRuntime js)
{
    private const string Prefix = "sw.";

    public async Task<string?> GetAsync(string key)
    {
        try
        {
            return await js.InvokeAsync<string?>("localStorage.getItem", Prefix + key);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException or JSDisconnectedException)
        {
            return null;
        }
    }

    public async Task<int?> GetIntAsync(string key) =>
        int.TryParse(await GetAsync(key), out var value) ? value : null;

    public async Task SetAsync(string key, string? value)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(value))
                await js.InvokeVoidAsync("localStorage.removeItem", Prefix + key);
            else
                await js.InvokeVoidAsync("localStorage.setItem", Prefix + key, value.Trim());
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException or JSDisconnectedException)
        {
            // хранилище недоступно — не критично
        }
    }

    public Task SetAsync(string key, int? value) => SetAsync(key, value?.ToString());
}

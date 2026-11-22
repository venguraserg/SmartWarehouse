using System.Globalization;
using Microsoft.AspNetCore.Components.Authorization;

namespace SmartWarehouse.Web.Services;

/// <summary>
/// Черновик запроса на выдачу: позиции добавляют прямо из остатков, номенклатуры и карточки позиции,
/// а отправляют кладовщику на странице «Новый запрос». Хранится в браузере пользователя (переживает
/// обновление страницы), у каждого пользователя свой — на общем ТСД черновики не смешиваются.
/// </summary>
public sealed class RequestCart(BrowserStore store, AuthenticationStateProvider auth)
{
    public sealed record Line(int ItemId, decimal Quantity);

    private readonly List<Line> lines = [];
    private Task? loading;
    private string key = "request-cart";

    /// <summary>Состав изменился — обновить счётчики и кнопки.</summary>
    public event Action? Changed;

    public IReadOnlyList<Line> Lines => lines;

    public int Count => lines.Count;

    public bool Contains(int itemId) => lines.Any(l => l.ItemId == itemId);

    /// <summary>Прочитать черновик из браузера. Вызывать после первой отрисовки (нужен JS); повторные вызовы — бесплатно.</summary>
    public Task LoadAsync() => loading ??= LoadCoreAsync();

    private async Task LoadCoreAsync()
    {
        var user = (await auth.GetAuthenticationStateAsync()).User;
        key = $"request-cart.{user.Identity?.Name?.ToLowerInvariant() ?? "anon"}";
        lines.Clear();
        lines.AddRange(Parse(await store.GetAsync(key)));
        Changed?.Invoke();
    }

    public async Task AddAsync(int itemId, decimal quantity = 1)
    {
        await LoadAsync();
        if (Contains(itemId))
            return;
        lines.Add(new Line(itemId, quantity));
        await SaveAsync();
    }

    public async Task SetQuantityAsync(int itemId, decimal quantity)
    {
        await LoadAsync();
        var i = lines.FindIndex(l => l.ItemId == itemId);
        if (i < 0 || lines[i].Quantity == quantity)
            return;
        lines[i] = lines[i] with { Quantity = quantity };
        await SaveAsync();
    }

    public async Task RemoveAsync(int itemId)
    {
        await LoadAsync();
        if (lines.RemoveAll(l => l.ItemId == itemId) > 0)
            await SaveAsync();
    }

    public async Task ClearAsync()
    {
        await LoadAsync();
        lines.Clear();
        await SaveAsync();
    }

    private async Task SaveAsync()
    {
        await store.SetAsync(key, string.Join(';', lines.Select(l =>
            string.Create(CultureInfo.InvariantCulture, $"{l.ItemId}:{l.Quantity}"))));
        Changed?.Invoke();
    }

    /// <summary>«12:1;40:2.5» → строки; испорченное пропускается.</summary>
    public static IEnumerable<Line> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;
        var seen = new HashSet<int>();
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split(':');
            if (pair.Length == 2
                && int.TryParse(pair[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0
                && decimal.TryParse(pair[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var qty) && qty > 0
                && seen.Add(id))
                yield return new Line(id, qty);
        }
    }
}

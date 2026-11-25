using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.Application.Services;

/// <summary>Одна наклейка: QR-код и подписи.</summary>
/// <param name="Payload">Что зашито в QR (ссылка или код).</param>
/// <param name="Code">Крупно: код ячейки или артикул.</param>
/// <param name="Title">Мельче: наименование.</param>
/// <param name="Note">Ещё мельче: склад, единица, штрих-код.</param>
/// <param name="Big">Для места: крупная надпись — короткий адрес («C3 · п.1 · яч.1»); null — наклейка товара.</param>
/// <param name="Area">Для места: этаж или зона («1-й этаж», «У ворот»).</param>
public record LabelData(string Payload, string Code, string? Title, string? Note, string? Big = null, string? Area = null);

/// <summary>Строка таблички стеллажа: полка («п.2», «пол») и пометка («ремни», «яч. 1–4»).</summary>
public record RackSignShelf(string Label, string? Hint);

/// <summary>Табличка стеллажа (A5): крупно код стеллажа, этаж, описание, QR, полки сверху вниз.</summary>
public record RackSignData(string Payload, string FullCode, string Rack, string? Area, string? Note, IReadOnlyList<RackSignShelf> Shelves);

/// <summary>Сетка наклеек на листе A4 (размеры стандартных листов самоклеящейся бумаги).</summary>
public record LabelLayout(string Id, string Title, int Columns, int Rows, double WidthMm, double HeightMm)
{
    public int PerSheet => Columns * Rows;

    public static readonly IReadOnlyList<LabelLayout> All =
    [
        new("3x7", "21 на листе — 70 × 42,3 мм (3 × 7)", 3, 7, 70, 42.3),
        new("3x8", "24 на листе — 70 × 37 мм (3 × 8)", 3, 8, 70, 37),
        new("4x10", "40 на листе — 48,5 × 25,4 мм (4 × 10)", 4, 10, 48.5, 25.4),
        new("2x5", "10 на листе — 105 × 57 мм (2 × 5)", 2, 5, 105, 57)
    ];

    public const string DefaultId = "3x8";

    public static LabelLayout Find(string? id) =>
        All.FirstOrDefault(l => string.Equals(l.Id, id ?? DefaultId, StringComparison.OrdinalIgnoreCase))
        ?? throw new DomainException($"Неизвестная раскладка листа «{id}». Доступны: {string.Join(", ", All.Select(l => l.Id))}.");
}

/// <summary>Построение PDF с наклейками (реализация — в Infrastructure).</summary>
public interface ILabelPdfRenderer
{
    byte[] Render(IReadOnlyList<LabelData> labels, LabelLayout layout, int skip);

    /// <summary>Таблички стеллажей (по листу A5 на каждую), затем листы наклеек.</summary>
    byte[] Render(IReadOnlyList<RackSignData> signs, IReadOnlyList<LabelData> labels, LabelLayout layout, int skip);
}

public interface ILabelService
{
    Task<(byte[] Pdf, string FileName)> RenderAsync(LabelRequest request, CancellationToken ct = default);
}

public class LabelService(ILocationRepository locations, IStockRepository stock, ILabelPdfRenderer renderer, ISettingsService settings) : ILabelService
{
    public const int MaxLabels = 1000;

    public async Task<(byte[] Pdf, string FileName)> RenderAsync(LabelRequest request, CancellationToken ct = default)
    {
        var layout = LabelLayout.Find(request.Layout);
        var ids = (request.Ids ?? Array.Empty<int>()).Distinct().ToList();
        if (ids.Count == 0)
            throw new DomainException("Выберите, для чего печатать наклейки.");
        if (request.Copies is < 1 or > 50)
            throw new DomainException("Копий одной наклейки — от 1 до 50.");
        if (request.Skip < 0 || request.Skip >= layout.PerSheet)
            throw new DomainException($"Пропустить можно от 0 до {layout.PerSheet - 1} наклеек.");
        if ((long)ids.Count * request.Copies > MaxLabels)
            throw new DomainException($"За один раз — не больше {MaxLabels} наклеек.");

        // В QR — сам код места / артикул или ссылка (см. настройки). Адрес для ссылок — из настроек, а если не задан — адрес сайта из запроса.
        var options = await settings.GetLabelSettingsAsync(ct);
        var requestUrl = NormalizeBaseUrl(request.BaseUrl);
        var payload = options.QrContent == QrContent.Link
            ? new Payloads(code => LabelLinks.ForLocation(options.BaseUrl ?? requestUrl, code), sku => LabelLinks.ForItem(options.BaseUrl ?? requestUrl, sku))
            : null;

        if (request.Kind == LabelKind.Rack)
            return await RackAsync(ids, request, layout, payload, ct);

        var labels = request.Kind == LabelKind.Location
            ? await LocationLabelsAsync(ids, payload, ct)
            : await ItemLabelsAsync(ids, payload, ct);

        var copies = labels.SelectMany(l => Enumerable.Repeat(l, request.Copies)).ToList();
        var name = request.Kind == LabelKind.Location ? "Наклейки_ячейки" : "Наклейки_номенклатура";
        return (renderer.Render(copies, layout, request.Skip), $"{name}.pdf");
    }

    /// <summary>
    /// Стеллаж целиком: табличка A5 и наклейки — в порядке, как клеить: полки сверху вниз (пол — последним),
    /// внутри полки ячейки по номеру. У полки с ячейками — наклейки только ячеек.
    /// </summary>
    private async Task<(byte[] Pdf, string FileName)> RackAsync(
        List<int> ids, LabelRequest request, LabelLayout layout, Payloads? payload, CancellationToken ct)
    {
        if (!request.Sign && !request.Stickers)
            throw new DomainException("Выберите, что печатать: табличку стеллажа, наклейки или и то и другое.");
        var tree = await TreeAsync(ids, ct);
        var signs = new List<RackSignData>();
        var stickers = new List<LabelData>();
        foreach (var id in ids)
        {
            var rack = tree[id];
            if (rack.Type != LocationType.Rack)
                throw new DomainException($"{rack.FullCode} — не стеллаж. Для отдельных мест — вкладка «Места».");

            var address = Address(tree, rack.Id);
            var shelves = Ordered(rack.Children.Where(c => c.Type == LocationType.Shelf)).AsEnumerable().Reverse().ToList();
            if (request.Sign)
                signs.Add(new RackSignData(
                    payload?.Location(rack.FullCode) ?? rack.FullCode, rack.FullCode, rack.Code, address.Area,
                    PlaceAddress.CustomName(rack),
                    shelves.Select(s => new RackSignShelf(PlaceAddress.IsFloor(s) ? "пол" : $"п.{s.Code}", ShelfHint(s))).ToList()));

            if (!request.Stickers)
                continue;
            var places = new List<LocationNodeDto>();
            foreach (var shelf in shelves)
            {
                var cells = Ordered(shelf.Children);
                places.AddRange(cells.Count > 0 ? cells : new List<LocationNodeDto> { shelf });
            }
            places.AddRange(Ordered(rack.Children.Where(c => c.Type != LocationType.Shelf)));
            if (places.Count == 0)
                places.Add(rack);
            stickers.AddRange(places.Select(p => PlaceLabel(tree, p, payload)));
        }

        if ((long)stickers.Count * request.Copies > MaxLabels)
            throw new DomainException($"За один раз — не больше {MaxLabels} наклеек.");
        var copies = stickers.SelectMany(l => Enumerable.Repeat(l, request.Copies)).ToList();
        var name = ids.Count == 1 ? $"Стеллаж_{tree[ids[0]].Code}" : "Стеллажи";
        return (renderer.Render(signs, copies, layout, request.Skip), $"{name}.pdf");
    }

    /// <summary>Пометка полки на табличке: своё описание, «яч. 1–4», для пола — «под стеллажом».</summary>
    private static string? ShelfHint(LocationNodeDto shelf)
    {
        if (PlaceAddress.CustomName(shelf) is string custom)
            return custom;
        var cells = Ordered(shelf.Children.Where(c => c.Type == LocationType.Cell));
        if (cells.Count > 0)
            return cells.Count == 1 ? $"яч. {cells[0].Code}" : $"яч. {cells[0].Code}–{cells[^1].Code}";
        return PlaceAddress.IsFloor(shelf) ? "под стеллажом" : null;
    }

    /// <summary>По номеру, если код — число («2» раньше «10»), иначе по алфавиту.</summary>
    private static List<LocationNodeDto> Ordered(IEnumerable<LocationNodeDto> nodes) =>
        nodes.OrderBy(n => int.TryParse(n.Code, out var x) ? x : int.MaxValue)
             .ThenBy(n => n.Code, StringComparer.OrdinalIgnoreCase)
             .ToList();

    private static LabelData PlaceLabel(IReadOnlyDictionary<int, LocationNodeDto> tree, LocationNodeDto place, Payloads? payload)
    {
        var a = Address(tree, place.Id);
        return new LabelData(
            payload?.Location(place.FullCode) ?? place.FullCode,
            place.FullCode,
            PlaceAddress.CustomName(place), // описание самого места, не родителя
            null,
            Big: a.Sticker,
            Area: a.Area is not null && a.Area != a.Full ? a.Area : null);
    }

    private static PlaceAddress Address(IReadOnlyDictionary<int, LocationNodeDto> tree, int id)
    {
        var chain = new List<LocationNodeDto>();
        for (int? at = id; at is int i && tree.TryGetValue(i, out var node); at = node.ParentId)
            chain.Insert(0, node);
        return PlaceAddress.Build(chain);
    }

    /// <summary>Все места складов, где лежат эти места, — деревом (нужны родители для адреса и дети для стеллажа).</summary>
    private async Task<Dictionary<int, LocationNodeDto>> TreeAsync(List<int> ids, CancellationToken ct)
    {
        var found = (await locations.GetLocationsAsync(ids, ct)).ToDictionary(l => l.Id);
        var missing = ids.Where(id => !found.ContainsKey(id)).ToList();
        if (missing.Count > 0)
            throw new NotFoundException($"Места хранения не найдены: {string.Join(", ", missing.Take(10))}.");

        var all = new List<StorageLocation>();
        foreach (var warehouseId in found.Values.Select(l => l.WarehouseId).Distinct())
            all.AddRange(await locations.ListLocationsAsync(warehouseId, ct));
        var byParent = all.ToLookup(l => l.ParentId);
        var tree = new Dictionary<int, LocationNodeDto>();
        foreach (var root in byParent[null])
            Build(root);
        // Места, чей родитель не загрузился (не должно быть), — без родителя.
        foreach (var l in all.Where(l => !tree.ContainsKey(l.Id)))
            Build(l);
        return tree;

        LocationNodeDto Build(StorageLocation l)
        {
            var node = new LocationNodeDto(l.Id, l.ParentId, l.Type, l.Code, l.FullCode, l.Name,
                byParent[l.Id].Select(Build).ToList());
            tree[l.Id] = node;
            return node;
        }
    }

    /// <summary>Как строить ссылку для QR; null — сам код.</summary>
    private sealed record Payloads(Func<string, string> Location, Func<string, string> Item);

    private async Task<List<LabelData>> LocationLabelsAsync(List<int> ids, Payloads? payload, CancellationToken ct)
    {
        var tree = await TreeAsync(ids, ct);
        // Порядок — по коду: так наклейки удобно клеить подряд по стеллажу.
        return ids
            .Select(id => tree[id])
            .OrderBy(l => l.FullCode, StringComparer.Ordinal)
            .Select(l => PlaceLabel(tree, l, payload))
            .ToList();
    }

    private async Task<List<LabelData>> ItemLabelsAsync(List<int> ids, Payloads? payload, CancellationToken ct)
    {
        var found = await stock.GetItemsAsync(ids, ct);
        var missing = ids.Where(id => !found.ContainsKey(id)).ToList();
        if (missing.Count > 0)
            throw new NotFoundException($"Позиции не найдены: {string.Join(", ", missing.Take(10))}.");

        return ids
            .Select(id => found[id])
            .Select(i => new LabelData(payload?.Item(i.Sku) ?? i.Sku, i.Sku, i.Name, i.Unit.Code))
            .ToList();
    }

    /// <summary>Адрес веб-приложения для ссылок в QR: только http(s), без пути запроса.</summary>
    internal static string? NormalizeBaseUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new DomainException("Адрес приложения для QR-кодов должен начинаться с http:// или https://.");
        return uri.GetLeftPart(UriPartial.Authority) + uri.AbsolutePath.TrimEnd('/');
    }
}

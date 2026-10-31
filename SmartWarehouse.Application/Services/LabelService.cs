using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.Application.Services;

/// <summary>Одна наклейка: QR-код и подписи.</summary>
/// <param name="Payload">Что зашито в QR (ссылка или код).</param>
/// <param name="Code">Крупно: код ячейки или артикул.</param>
/// <param name="Title">Мельче: наименование.</param>
/// <param name="Note">Ещё мельче: склад, единица, штрих-код.</param>
public record LabelData(string Payload, string Code, string? Title, string? Note);

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
}

public interface ILabelService
{
    Task<(byte[] Pdf, string FileName)> RenderAsync(LabelRequest request, CancellationToken ct = default);
}

public class LabelService(ILocationRepository locations, IStockRepository stock, ILabelPdfRenderer renderer) : ILabelService
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

        var baseUrl = NormalizeBaseUrl(request.BaseUrl);
        var labels = request.Kind == LabelKind.Location
            ? await LocationLabelsAsync(ids, baseUrl, ct)
            : await ItemLabelsAsync(ids, baseUrl, ct);

        var copies = labels.SelectMany(l => Enumerable.Repeat(l, request.Copies)).ToList();
        var name = request.Kind == LabelKind.Location ? "Наклейки_ячейки" : "Наклейки_номенклатура";
        return (renderer.Render(copies, layout, request.Skip), $"{name}.pdf");
    }

    private async Task<List<LabelData>> LocationLabelsAsync(List<int> ids, string? baseUrl, CancellationToken ct)
    {
        var found = (await locations.GetLocationsAsync(ids, ct)).ToDictionary(l => l.Id);
        var missing = ids.Where(id => !found.ContainsKey(id)).ToList();
        if (missing.Count > 0)
            throw new NotFoundException($"Места хранения не найдены: {string.Join(", ", missing.Take(10))}.");

        // Порядок — по коду: так наклейки удобно клеить подряд по стеллажу.
        return found.Values
            .OrderBy(l => l.FullCode, StringComparer.Ordinal)
            .Select(l => new LabelData(
                LabelLinks.ForLocation(baseUrl, l.FullCode),
                l.FullCode,
                l.Name,
                $"{LocationService.TypeName(l.Type)}, склад {l.Warehouse.Code}"))
            .ToList();
    }

    private async Task<List<LabelData>> ItemLabelsAsync(List<int> ids, string? baseUrl, CancellationToken ct)
    {
        var found = await stock.GetItemsAsync(ids, ct);
        var missing = ids.Where(id => !found.ContainsKey(id)).ToList();
        if (missing.Count > 0)
            throw new NotFoundException($"Позиции не найдены: {string.Join(", ", missing.Take(10))}.");

        return ids
            .Select(id => found[id])
            .Select(i => new LabelData(LabelLinks.ForItem(baseUrl, i.Sku), i.Sku, i.Name, i.Unit.Code))
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

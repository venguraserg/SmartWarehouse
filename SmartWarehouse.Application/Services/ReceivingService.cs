using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.Application.Services;

public interface IReceivingService
{
    /// <summary>
    /// Приём по накладной на внутреннее перемещение (файл 1С). <paramref name="apply"/> = false — только проверка.
    /// Всё принимается в место «Приёмка» склада; по ячейкам кладовщик раскладывает потом.
    /// </summary>
    Task<InvoiceImportResult> ImportInvoiceAsync(InvoiceFile invoice, int warehouseId, bool apply, ImportActor actor, CancellationToken ct = default);

    /// <summary>Что лежит в «Приёмке» всех складов и ждёт размещения.</summary>
    Task<IReadOnlyList<PendingPlacementDto>> ListPendingAsync(CancellationToken ct = default);

    /// <summary>Отметить расхождение: по накладной одно количество, фактически — другое. Остаток не меняется.</summary>
    Task<DiscrepancyDto> ReportDiscrepancyAsync(ReportDiscrepancyRequest request, string? by, CancellationToken ct = default);

    Task<IReadOnlyList<DiscrepancyDto>> ListDiscrepanciesAsync(bool includeResolved, CancellationToken ct = default);

    /// <summary>Закрыть расхождение (бухгалтерия исправила документ, товар довезли и т.п.).</summary>
    Task<DiscrepancyDto> ResolveDiscrepancyAsync(int id, string? resolution, CancellationToken ct = default);
}

/// <summary>
/// Поступление учётных материалов без ручного ввода: накладная из 1С → партии в «Приёмке» → размещение по ячейкам.
/// В учёте главная 1С, поэтому принимается ровно то, что в накладной; если фактически привезли иначе —
/// кладовщик отмечает расхождение, оно идёт в отчёт для бухгалтерии.
/// </summary>
public class ReceivingService(
    ICatalogService catalog,
    ILocationService locations,
    IStockService stock,
    IStockRepository repo,
    TimeProvider clock) : IReceivingService
{
    /// <summary>Код места «Приёмка» (верхний уровень склада): WH1-RCV.</summary>
    public const string ReceptionCode = "RCV";

    public const string ReceptionName = "Приёмка — ожидает размещения";

    public static string Title(string number, DateOnly date) => $"Накладная № {number} от {date:dd.MM.yyyy}";

    public async Task<InvoiceImportResult> ImportInvoiceAsync(InvoiceFile invoice, int warehouseId, bool apply, ImportActor actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(invoice.Number) || invoice.Date is not DateOnly date)
            throw new DomainException("В файле не найдены номер и дата накладной. Нужна «Накладная на внутреннее перемещение материалов» из 1С.");
        if (invoice.Lines.Count == 0)
            throw new DomainException("В накладной нет строк с материалами.");

        var warehouse = (await locations.ListWarehousesAsync(ct)).FirstOrDefault(w => w.Id == warehouseId)
            ?? throw new NotFoundException("Склад не найден — создайте его в «Складах и местах».");
        var title = Title(invoice.Number.Trim(), date);
        var receptionFull = warehouse.Code + CodeRules.Separator + ReceptionCode;
        var issues = new List<ImportIssue>(invoice.Issues);

        if (await repo.ReceiptExistsAsync(title, ct))
        {
            if (apply)
                throw new DomainException($"{title} уже загружена — повторно не принимается.");
            return new InvoiceImportResult(false, title, true, invoice.Lines.Count, 0, 0, 0, receptionFull, [], issues, []);
        }

        var units = (await catalog.ListUnitsAsync(ct)).ToDictionary(u => u.Code, StringComparer.OrdinalIgnoreCase);
        // Код 1С в накладной — с нулями впереди (0000000917), в приложении — без: сравниваем без нулей.
        var items = (await catalog.ListItemsAsync(null, null, ct))
            .GroupBy(i => ReconcileService.Key(i.Sku), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // ---------- Строки → партии: одна на код и цену ----------
        var merged = new Dictionary<(string Sku, decimal Price), Planned>();
        var order = new List<Planned>();
        foreach (var line in invoice.Lines)
        {
            string sku;
            try
            {
                sku = CodeRules.NormalizeSku(ReconcileService.Key(line.Code));
            }
            catch (DomainException e)
            {
                issues.Add(new ImportIssue(line.Line, line.Code, e.Message));
                continue;
            }
            var name = line.Name.Trim();
            if (name.Length == 0)
            {
                issues.Add(new ImportIssue(line.Line, sku, "Нет наименования."));
                continue;
            }
            if (name.Length > 256)
                name = name[..256];
            if (line.Quantity <= 0)
            {
                issues.Add(new ImportIssue(line.Line, sku, "Количество должно быть больше нуля."));
                continue;
            }
            var unit = UnitAliases.Normalize(line.Unit);
            if (unit.Length == 0)
            {
                issues.Add(new ImportIssue(line.Line, sku, "Нет единицы измерения."));
                continue;
            }
            // Цена: из суммы (в накладной цена может быть округлена), иначе — как указана.
            var price = line.Amount is decimal amount ? amount / line.Quantity : line.Price;
            if (price is not decimal p || p < 0)
            {
                issues.Add(new ImportIssue(line.Line, sku, "Нет цены и суммы."));
                continue;
            }
            p = decimal.Round(p, 4, MidpointRounding.AwayFromZero);

            items.TryGetValue(sku, out var existing);
            if (existing is not null && !string.Equals(UnitAliases.Normalize(existing.UnitCode), unit, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new ImportIssue(line.Line, sku, $"Позиция {sku} заведена с единицей «{existing.UnitCode}», в накладной — «{line.Unit}»."));
                continue;
            }
            var unitCode = existing?.UnitCode ?? unit;
            var fractional = units.TryGetValue(unitCode, out var u) ? u.IsFractional : UnitAliases.Fractional.Contains(unitCode);
            if (!fractional && line.Quantity != decimal.Truncate(line.Quantity))
            {
                issues.Add(new ImportIssue(line.Line, sku, $"Дробное количество {line.Quantity:0.###} в «{unitCode}»."));
                continue;
            }

            if (merged.TryGetValue((sku, p), out var same))
            {
                same.Quantity += line.Quantity;
                same.Amount += line.Amount ?? line.Quantity * p;
            }
            else
            {
                var planned = new Planned(sku, name, unitCode, p, existing) { Quantity = line.Quantity, Amount = line.Amount ?? line.Quantity * p };
                merged[(sku, p)] = planned;
                order.Add(planned);
            }
        }

        // Возможное задвоение: позиция уже введена вручную как учётная и ещё лежит на складе.
        var existingIds = order.Where(o => o.Existing is not null).Select(o => o.Existing!.Id).Distinct().ToList();
        var manual = (await repo.ListManualAccountedAsync(existingIds, ct)).ToLookup(m => m.ItemId);
        var newUnits = order.Select(o => o.Unit).Distinct(StringComparer.OrdinalIgnoreCase).Where(x => !units.ContainsKey(x)).ToList();
        var newItems = order.Where(o => o.Existing is null).Select(o => o.Sku).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        List<InvoiceBatchDto> Preview() => order
            .Select(o => new InvoiceBatchDto(o.Sku, o.Existing?.Name ?? o.Name, o.Unit, o.Quantity, o.Price,
                decimal.Round(o.Amount, 2), o.Existing is null,
                o.Existing is not null && manual[o.Existing.Id].FirstOrDefault() is { } m
                    ? $"Уже есть учётная партия, введённая вручную ({m.DocumentNumber}, {m.Quantity:0.###}) — не задвоение ли?"
                    : null))
            .ToList();
        var value = decimal.Round(order.Sum(o => o.Amount), 2);

        if (!apply)
            return new InvoiceImportResult(false, title, false, invoice.Lines.Count, newItems.Count, newUnits.Count, value, receptionFull, Preview(), issues, []);
        if (order.Count == 0)
            throw new DomainException("Ни одна строка накладной не подходит для приёма — см. замечания при проверке.");

        // ---------- Приём ----------
        foreach (var code in newUnits)
        {
            var unit = await catalog.CreateUnitAsync(new CreateUnitRequest(code, code, UnitAliases.Fractional.Contains(code)), ct);
            units[unit.Code] = unit;
        }
        foreach (var o in order.Where(o => o.Existing is null))
        {
            if (!items.TryGetValue(o.Sku, out var item))
            {
                item = await catalog.CreateItemAsync(new CreateItemRequest(o.Sku, o.Name, null, units[o.Unit].Id, 0, null, null), ct);
                items[o.Sku] = item;
            }
            o.Existing = item;
        }

        var receptionId = await EnsureReceptionAsync(warehouse, ct);
        var occurred = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var documents = new List<long>();
        foreach (var chunk in order.Chunk(StockService.MaxLines))
        {
            var lines = chunk.Select(o => new DocumentLineRequest(o.Existing!.Id, o.Quantity, null, receptionId,
                UnitPrice: o.Price, BatchDate: occurred, BatchNote: title)).ToList();
            var doc = await stock.PostDocumentAsync(new CreateDocumentRequest(
                MovementType.Receipt, title, null, actor.Name,
                "Приём по накладной на внутреннее перемещение (файл 1С)" + (invoice.Account is { Length: > 0 } acc ? $", счёт {acc}" : ""),
                lines, PerformedById: actor.EmployeeId, Origin: BatchOrigin.Invoice), ct);
            documents.Add(doc.Id);
        }

        return new InvoiceImportResult(true, title, false, invoice.Lines.Count, newItems.Count, newUnits.Count, value, receptionFull, Preview(), issues, documents);
    }

    private async Task<int> EnsureReceptionAsync(WarehouseDto warehouse, CancellationToken ct)
    {
        var tree = await locations.GetLocationTreeAsync(warehouse.Id, ct);
        if (tree.FirstOrDefault(n => string.Equals(n.Code, ReceptionCode, StringComparison.OrdinalIgnoreCase)) is { } existing)
            return existing.Id;
        var created = await locations.AddLocationAsync(warehouse.Id,
            new CreateLocationRequest(null, LocationType.Cell, ReceptionCode, ReceptionName), ct);
        return created.Id;
    }

    public async Task<IReadOnlyList<PendingPlacementDto>> ListPendingAsync(CancellationToken ct = default)
    {
        var open = (await repo.ListDiscrepanciesAsync(includeResolved: false, ct)).Select(d => d.BatchId).ToHashSet();
        var result = new List<PendingPlacementDto>();
        foreach (var warehouse in await locations.ListWarehousesAsync(ct))
        {
            var tree = await locations.GetLocationTreeAsync(warehouse.Id, ct);
            if (tree.FirstOrDefault(n => string.Equals(n.Code, ReceptionCode, StringComparison.OrdinalIgnoreCase)) is not { } reception)
                continue;
            foreach (var b in await repo.ListLocationStockAsync(reception.Id, ct))
                result.Add(new PendingPlacementDto(reception.Id, reception.FullCode, b.ItemId, b.Item.Sku, b.Item.Name,
                    b.Item.Unit?.Code ?? "", b.Item.Unit?.IsFractional ?? false, b.BatchId, b.Batch.Note, b.Batch.ReceivedAt,
                    b.Quantity, b.Batch.UnitPrice, open.Contains(b.BatchId)));
        }
        return result;
    }

    public async Task<DiscrepancyDto> ReportDiscrepancyAsync(ReportDiscrepancyRequest request, string? by, CancellationToken ct = default)
    {
        var batch = await repo.GetBatchAsync(request.BatchId, ct) ?? throw new NotFoundException("Партия не найдена.");
        if (request.Actual < 0)
            throw new DomainException("Фактическое количество не может быть отрицательным.");
        var expected = await repo.GetBatchReceivedAsync(batch.Id, ct);
        if (request.Actual == expected)
            throw new DomainException($"Фактически {request.Actual:0.###} — столько же, сколько по накладной. Расхождения нет.");
        var comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        if (comment is { Length: > 500 })
            throw new DomainException("Комментарий — не длиннее 500 символов.");

        var discrepancy = new ReceiptDiscrepancy
        {
            BatchId = batch.Id, Expected = expected, Actual = request.Actual, Comment = comment,
            CreatedAt = clock.GetUtcNow(), CreatedBy = by
        };
        repo.AddDiscrepancy(discrepancy);
        await repo.SaveChangesAsync(ct);
        return ToDto(await repo.GetDiscrepancyAsync(discrepancy.Id, ct) ?? discrepancy);
    }

    public async Task<IReadOnlyList<DiscrepancyDto>> ListDiscrepanciesAsync(bool includeResolved, CancellationToken ct = default) =>
        (await repo.ListDiscrepanciesAsync(includeResolved, ct)).Select(ToDto).ToList();

    public async Task<DiscrepancyDto> ResolveDiscrepancyAsync(int id, string? resolution, CancellationToken ct = default)
    {
        var discrepancy = await repo.GetDiscrepancyAsync(id, ct) ?? throw new NotFoundException("Расхождение не найдено.");
        if (discrepancy.ResolvedAt is not null)
            throw new DomainException("Расхождение уже закрыто.");
        var text = string.IsNullOrWhiteSpace(resolution) ? null : resolution.Trim();
        if (text is { Length: > 500 })
            throw new DomainException("Комментарий — не длиннее 500 символов.");
        discrepancy.ResolvedAt = clock.GetUtcNow();
        discrepancy.Resolution = text;
        await repo.SaveChangesAsync(ct);
        return ToDto(discrepancy);
    }

    private static DiscrepancyDto ToDto(ReceiptDiscrepancy d) =>
        new(d.Id, d.BatchId, d.Batch.ItemId, d.Batch.Item.Sku, d.Batch.Item.Name, d.Batch.Item.Unit?.Code ?? "", d.Batch.Note,
            d.Expected, d.Actual, d.Comment, d.CreatedAt, d.CreatedBy, d.ResolvedAt, d.Resolution);

    private sealed class Planned(string sku, string name, string unit, decimal price, ItemDto? existing)
    {
        public string Sku { get; } = sku;
        public string Name { get; } = name;
        public string Unit { get; } = unit;
        public decimal Price { get; } = price;
        public ItemDto? Existing { get; set; } = existing;
        public decimal Quantity { get; set; }
        public decimal Amount { get; set; }
    }
}

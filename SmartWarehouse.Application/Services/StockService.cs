using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.Application.Services;

/// <summary>
/// Ядро учёта. Остаток меняется только проведением документа, напрямую его изменить нельзя.
/// Документ проводится целиком или не проводится вовсе; уход в минус запрещён.
/// </summary>
public class StockService(IStockRepository repo, TimeProvider clock) : IStockService
{
    public const int MaxLines = 500;

    public async Task<DocumentDto> PostDocumentAsync(CreateDocumentRequest request, CancellationToken ct = default)
    {
        // ---------- 1. Шапка ----------
        if (!Enum.IsDefined(request.Type))
            throw new DomainException($"Неизвестный тип документа: {request.Type}.");

        var lines = request.Lines ?? Array.Empty<DocumentLineRequest>();
        if (lines.Count == 0)
            throw new DomainException("Документ должен содержать хотя бы одну строку.");
        if (lines.Count > MaxLines)
            throw new DomainException($"В документе не может быть больше {MaxLines} строк.");

        if (request.EquipmentId is int equipmentId)
        {
            if (request.Type is not (MovementType.Issue or MovementType.WriteOff))
                throw new DomainException("Оборудование указывается только для выдачи и списания.");
            if (!await repo.EquipmentExistsAsync(equipmentId, ct))
                throw new NotFoundException($"Оборудование {equipmentId} не найдено.");
        }

        // ---------- 2. Строки: формат ----------
        for (var i = 0; i < lines.Count; i++)
            ValidateLineShape(request.Type, lines[i], i + 1);

        // ---------- 3. Строки: справочные данные ----------
        var items = await repo.GetItemsAsync(lines.Select(l => l.ItemId).Distinct(), ct);
        var locationIds = lines
            .SelectMany(l => new[] { l.FromLocationId, l.ToLocationId })
            .OfType<int>()
            .Distinct();
        var locations = await repo.GetLocationsAsync(locationIds, ct);

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var n = i + 1;

            if (!items.TryGetValue(line.ItemId, out var item))
                throw new NotFoundException($"Строка {n}: позиция {line.ItemId} не найдена.");
            if (!item.IsActive)
                throw new DomainException($"Строка {n}: позиция «{item.Name}» неактивна, операции с ней запрещены.");
            if (!item.Unit.IsFractional && line.Quantity != decimal.Truncate(line.Quantity))
                throw new DomainException($"Строка {n}: «{item.Name}» учитывается в «{item.Unit.Code}», количество должно быть целым.");

            foreach (var locationId in new[] { line.FromLocationId, line.ToLocationId }.OfType<int>())
            {
                if (!locations.TryGetValue(locationId, out var location))
                    throw new NotFoundException($"Строка {n}: место хранения {locationId} не найдено.");
                if (location.HasChildren)
                    throw new DomainException($"Строка {n}: {location.FullCode} содержит вложенные места — укажите конкретную ячейку.");
            }
        }

        // ---------- 4. Расчёт новых остатков (без изменения данных) ----------
        var stored = new Dictionary<(int ItemId, int LocationId), StockBalance?>();
        var projected = new Dictionary<(int ItemId, int LocationId), decimal>();

        async Task<decimal> CurrentAsync((int ItemId, int LocationId) key)
        {
            if (!projected.TryGetValue(key, out var quantity))
            {
                var balance = await repo.GetBalanceAsync(key.ItemId, key.LocationId, ct);
                stored[key] = balance;
                quantity = balance?.Quantity ?? 0;
                projected[key] = quantity;
            }
            return quantity;
        }

        foreach (var line in lines)
        {
            if (line.FromLocationId is int fromId)
            {
                var key = (line.ItemId, fromId);
                var available = await CurrentAsync(key);
                if (available < line.Quantity)
                    throw new InsufficientStockException(items[line.ItemId].Sku, locations[fromId].FullCode, available, line.Quantity);
                projected[key] = available - line.Quantity;
            }

            if (line.ToLocationId is int toId)
            {
                var key = (line.ItemId, toId);
                projected[key] = await CurrentAsync(key) + line.Quantity;
            }
        }

        // ---------- 5. Применение: всё сохраняется одной транзакцией ----------
        foreach (var (key, quantity) in projected)
        {
            var balance = stored[key];
            if (balance is not null)
                balance.Quantity = quantity;
            else if (quantity > 0)
                repo.AddBalance(new StockBalance { ItemId = key.ItemId, LocationId = key.LocationId, Quantity = quantity });
        }

        var now = clock.GetUtcNow();
        var document = new StockDocument
        {
            Type = request.Type,
            OccurredAt = now,
            ExternalNumber = Clean(request.ExternalNumber),
            EquipmentId = request.EquipmentId,
            PerformedBy = Clean(request.PerformedBy),
            Comment = Clean(request.Comment)
        };

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            document.Lines.Add(new StockMovement
            {
                Document = document,
                LineNo = i + 1,
                Type = request.Type,
                OccurredAt = now,
                ItemId = line.ItemId,
                FromLocationId = line.FromLocationId,
                ToLocationId = line.ToLocationId,
                Quantity = line.Quantity
            });
        }

        repo.AddDocument(document);
        await repo.SaveChangesAsync(ct);

        return ToDto(document, line =>
        {
            var item = items[line.ItemId];
            return new DocumentLineDto(
                line.LineNo, item.Id, item.Sku, item.Name, item.Unit.Code, line.Quantity,
                line.FromLocationId is int f ? locations[f].FullCode : null,
                line.ToLocationId is int t ? locations[t].FullCode : null);
        });
    }

    public async Task<DocumentDto> GetDocumentAsync(long id, CancellationToken ct = default)
    {
        var document = await repo.GetDocumentAsync(id, ct)
            ?? throw new NotFoundException($"Документ {id} не найден.");

        return ToDto(document, line => new DocumentLineDto(
            line.LineNo, line.ItemId, line.Item.Sku, line.Item.Name, line.Item.Unit.Code, line.Quantity,
            line.FromLocation?.FullCode, line.ToLocation?.FullCode));
    }

    public async Task<IReadOnlyList<DocumentSummaryDto>> ListDocumentsAsync(MovementType? type, int take = 100, CancellationToken ct = default)
    {
        var headers = await repo.ListDocumentsAsync(type, Math.Clamp(take, 1, 500), ct);
        return headers
            .Select(h => new DocumentSummaryDto(
                h.Document.Id, h.Document.Number, h.Document.Type, h.Document.OccurredAt,
                h.Document.ExternalNumber, h.Document.PerformedBy, h.LineCount))
            .ToList();
    }

    public async Task<IReadOnlyList<StockBalanceDto>> GetBalancesAsync(int itemId, CancellationToken ct = default)
    {
        await EnsureItemExistsAsync(itemId, ct);
        var balances = await repo.GetBalancesForItemAsync(itemId, ct);
        return balances
            .Select(b => new StockBalanceDto(b.LocationId, b.Location.FullCode, b.Quantity))
            .ToList();
    }

    public async Task<IReadOnlyList<MovementDto>> GetMovementsAsync(int itemId, int take = 100, CancellationToken ct = default)
    {
        await EnsureItemExistsAsync(itemId, ct);
        var movements = await repo.GetMovementsForItemAsync(itemId, Math.Clamp(take, 1, 1000), ct);
        return movements
            .Select(m => new MovementDto(
                m.DocumentId, m.Document.Number, m.Type, m.OccurredAt, m.Quantity,
                m.FromLocation?.FullCode, m.ToLocation?.FullCode,
                m.Document.EquipmentId, m.Document.PerformedBy))
            .ToList();
    }

    public async Task<IReadOnlyList<LowStockItemDto>> GetBelowMinimumAsync(CancellationToken ct = default)
    {
        var rows = await repo.GetItemsBelowMinimumAsync(ct);
        return rows
            .Select(r => new LowStockItemDto(
                r.Item.Id,
                r.Item.Sku,
                r.Item.Name,
                r.Item.Unit.Code,
                r.Total,
                r.Item.MinStock,
                Math.Max(0, (r.Item.MaxStock ?? r.Item.MinStock) - r.Total)))
            .OrderBy(x => x.Name)
            .ToList();
    }

    private async Task EnsureItemExistsAsync(int itemId, CancellationToken ct)
    {
        if (!await repo.ItemExistsAsync(itemId, ct))
            throw new NotFoundException($"Позиция {itemId} не найдена.");
    }

    private static void ValidateLineShape(MovementType type, DocumentLineRequest line, int n)
    {
        if (line.Quantity <= 0)
            throw new DomainException($"Строка {n}: количество должно быть больше нуля.");

        var hasFrom = line.FromLocationId.HasValue;
        var hasTo = line.ToLocationId.HasValue;

        switch (type)
        {
            case MovementType.Receipt:
                if (!hasTo || hasFrom)
                    throw new DomainException($"Строка {n}: для прихода укажите только место, куда поступает товар.");
                break;

            case MovementType.Issue:
            case MovementType.WriteOff:
                if (!hasFrom || hasTo)
                    throw new DomainException($"Строка {n}: для выдачи и списания укажите только место, откуда берётся товар.");
                break;

            case MovementType.Transfer:
                if (!hasFrom || !hasTo)
                    throw new DomainException($"Строка {n}: для перемещения укажите место отправления и место назначения.");
                if (line.FromLocationId == line.ToLocationId)
                    throw new DomainException($"Строка {n}: место отправления и место назначения совпадают.");
                break;

            case MovementType.Adjustment:
                if (hasFrom == hasTo)
                    throw new DomainException($"Строка {n}: для корректировки укажите ровно одно место (куда — увеличить, откуда — уменьшить).");
                break;
        }
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DocumentDto ToDto(StockDocument d, Func<StockMovement, DocumentLineDto> mapLine) => new(
        d.Id, d.Number, d.Type, d.OccurredAt, d.ExternalNumber, d.EquipmentId, d.PerformedBy, d.Comment,
        d.Lines.OrderBy(l => l.LineNo).Select(mapLine).ToList());
}

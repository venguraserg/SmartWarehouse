using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.Application.Services;

/// <summary>
/// Ядро учёта. Остаток меняется только вместе с записью в журнале движений,
/// напрямую его изменить нельзя. Уход в минус запрещён.
/// </summary>
public class StockService(IStockRepository repo, TimeProvider clock) : IStockService
{
    public async Task<MovementDto> RegisterAsync(MovementRequest request, CancellationToken ct = default)
    {
        if (request.Quantity <= 0)
            throw new DomainException("Количество должно быть больше нуля.");

        ValidateDirection(request);

        var item = await repo.GetItemAsync(request.ItemId, ct)
            ?? throw new NotFoundException($"Позиция {request.ItemId} не найдена.");

        if (!item.IsActive)
            throw new DomainException($"Позиция «{item.Name}» неактивна, операции с ней запрещены.");

        if (request.FromLocationId is int fromCheck && !await repo.LocationExistsAsync(fromCheck, ct))
            throw new NotFoundException($"Место хранения {fromCheck} не найдено.");

        if (request.ToLocationId is int toCheck && !await repo.LocationExistsAsync(toCheck, ct))
            throw new NotFoundException($"Место хранения {toCheck} не найдено.");

        if (request.EquipmentId is int equipmentId && !await repo.EquipmentExistsAsync(equipmentId, ct))
            throw new NotFoundException($"Оборудование {equipmentId} не найдено.");

        // Сначала уменьшаем остаток источника (с проверкой), затем увеличиваем остаток приёмника.
        if (request.FromLocationId is int fromId)
        {
            var source = await repo.GetBalanceAsync(item.Id, fromId, ct);
            if (source is null || source.Quantity < request.Quantity)
                throw new InsufficientStockException(item.Id, fromId, source?.Quantity ?? 0, request.Quantity);

            source.Quantity -= request.Quantity;
        }

        if (request.ToLocationId is int toId)
        {
            var target = await repo.GetBalanceAsync(item.Id, toId, ct);
            if (target is null)
            {
                target = new StockBalance { ItemId = item.Id, LocationId = toId };
                repo.AddBalance(target);
            }

            target.Quantity += request.Quantity;
        }

        var movement = new StockMovement
        {
            Type = request.Type,
            ItemId = item.Id,
            FromLocationId = request.FromLocationId,
            ToLocationId = request.ToLocationId,
            Quantity = request.Quantity,
            OccurredAt = clock.GetUtcNow(),
            EquipmentId = request.EquipmentId,
            PerformedBy = request.PerformedBy,
            Document = request.Document,
            Comment = request.Comment
        };

        repo.AddMovement(movement);
        await repo.SaveChangesAsync(ct);

        return ToDto(movement);
    }

    public async Task<IReadOnlyList<StockBalanceDto>> GetBalancesAsync(int itemId, CancellationToken ct = default)
    {
        await EnsureItemExistsAsync(itemId, ct);
        var balances = await repo.GetBalancesForItemAsync(itemId, ct);
        return balances
            .Select(b => new StockBalanceDto(b.LocationId, b.Location.Code, b.Quantity))
            .ToList();
    }

    public async Task<IReadOnlyList<MovementDto>> GetMovementsAsync(int itemId, int take = 100, CancellationToken ct = default)
    {
        await EnsureItemExistsAsync(itemId, ct);
        var movements = await repo.GetMovementsForItemAsync(itemId, Math.Clamp(take, 1, 1000), ct);
        return movements.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<LowStockItemDto>> GetBelowMinimumAsync(CancellationToken ct = default)
    {
        var rows = await repo.GetItemsBelowMinimumAsync(ct);
        return rows
            .Select(r => new LowStockItemDto(
                r.Item.Id,
                r.Item.Sku,
                r.Item.Name,
                r.Item.Unit,
                r.Total,
                r.Item.MinStock,
                Math.Max(0, (r.Item.MaxStock ?? r.Item.MinStock) - r.Total)))
            .OrderBy(x => x.Name)
            .ToList();
    }

    private async Task EnsureItemExistsAsync(int itemId, CancellationToken ct)
    {
        if (await repo.GetItemAsync(itemId, ct) is null)
            throw new NotFoundException($"Позиция {itemId} не найдена.");
    }

    private static void ValidateDirection(MovementRequest r)
    {
        var hasFrom = r.FromLocationId.HasValue;
        var hasTo = r.ToLocationId.HasValue;

        switch (r.Type)
        {
            case MovementType.Receipt:
                if (!hasTo || hasFrom)
                    throw new DomainException("Для прихода укажите только место, куда поступает товар.");
                break;

            case MovementType.Issue:
            case MovementType.WriteOff:
                if (!hasFrom || hasTo)
                    throw new DomainException("Для выдачи и списания укажите только место, откуда берётся товар.");
                break;

            case MovementType.Transfer:
                if (!hasFrom || !hasTo)
                    throw new DomainException("Для перемещения укажите место отправления и место назначения.");
                if (r.FromLocationId == r.ToLocationId)
                    throw new DomainException("Место отправления и место назначения совпадают.");
                break;

            case MovementType.Adjustment:
                if (hasFrom == hasTo)
                    throw new DomainException("Для корректировки укажите ровно одно место: To увеличивает остаток, From уменьшает.");
                break;

            default:
                throw new DomainException($"Неизвестный тип операции: {r.Type}.");
        }
    }

    private static MovementDto ToDto(StockMovement m) => new(
        m.Id, m.Type, m.ItemId, m.FromLocationId, m.ToLocationId, m.Quantity,
        m.OccurredAt, m.EquipmentId, m.PerformedBy, m.Document, m.Comment);
}

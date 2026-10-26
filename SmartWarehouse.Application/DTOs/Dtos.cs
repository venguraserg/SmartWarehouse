using SmartWarehouse.Domain.Enums;

namespace SmartWarehouse.Application.DTOs;

// ---------- Номенклатура ----------

public record CreateItemRequest(
    string Sku,
    string Name,
    string? Category,
    string? Unit,
    decimal MinStock,
    decimal? MaxStock,
    string? Description);

public record ItemDto(
    int Id,
    string Sku,
    string Name,
    string? Category,
    string Unit,
    decimal MinStock,
    decimal? MaxStock,
    string? Description,
    bool IsActive);

// ---------- Склады и места хранения ----------

public record CreateWarehouseRequest(string Name, string? Description);

public record CreateLocationRequest(string Code, string? Name);

public record LocationDto(int Id, int WarehouseId, string Code, string? Name);

public record WarehouseDto(int Id, string Name, string? Description, IReadOnlyList<LocationDto> Locations);

// ---------- Движения и остатки ----------

/// <summary>
/// Запрос на складскую операцию. Количество всегда положительное;
/// направление задают <see cref="FromLocationId"/> и <see cref="ToLocationId"/>:
/// приход — только To, выдача и списание — только From, перемещение — оба,
/// корректировка — ровно одно из двух (To увеличивает остаток, From уменьшает).
/// </summary>
public record MovementRequest(
    MovementType Type,
    int ItemId,
    decimal Quantity,
    int? FromLocationId,
    int? ToLocationId,
    int? EquipmentId,
    string? PerformedBy,
    string? Document,
    string? Comment);

public record MovementDto(
    long Id,
    MovementType Type,
    int ItemId,
    int? FromLocationId,
    int? ToLocationId,
    decimal Quantity,
    DateTimeOffset OccurredAt,
    int? EquipmentId,
    string? PerformedBy,
    string? Document,
    string? Comment);

public record StockBalanceDto(int LocationId, string LocationCode, decimal Quantity);

/// <summary>Позиция, остаток которой ниже минимума.</summary>
/// <param name="ToOrder">Сколько нужно заказать, чтобы дойти до максимума (или до минимума, если максимум не задан).</param>
public record LowStockItemDto(
    int ItemId,
    string Sku,
    string Name,
    string Unit,
    decimal Total,
    decimal MinStock,
    decimal ToOrder);

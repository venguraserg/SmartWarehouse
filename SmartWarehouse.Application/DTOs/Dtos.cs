using SmartWarehouse.Domain.Enums;

namespace SmartWarehouse.Application.DTOs;

// ---------- Справочники ----------

public record CreateCategoryRequest(string Name, string? Description);

public record CategoryDto(int Id, string Name, string? Description);

public record CreateUnitRequest(string Code, string Name, bool IsFractional);

public record UnitDto(int Id, string Code, string Name, bool IsFractional);

// ---------- Номенклатура ----------

public record CreateItemRequest(
    string Sku,
    string Name,
    int? CategoryId,
    int UnitId,
    decimal MinStock,
    decimal? MaxStock,
    string? Description);

public record ItemDto(
    int Id,
    string Sku,
    string Name,
    int? CategoryId,
    string? CategoryName,
    int UnitId,
    string UnitCode,
    decimal MinStock,
    decimal? MaxStock,
    string? Description,
    bool IsActive);

// ---------- Склады и места хранения ----------

public record CreateWarehouseRequest(string Code, string Name, string? Description);

public record WarehouseDto(int Id, string Code, string Name, string? Description);

/// <param name="ParentId">Родительское место; null — верхний уровень склада.</param>
/// <param name="Code">Код внутри родителя (буквы и цифры), например «A» или «03».</param>
public record CreateLocationRequest(int? ParentId, LocationType Type, string Code, string? Name);

public record LocationDto(
    int Id,
    int WarehouseId,
    int? ParentId,
    LocationType Type,
    string Code,
    string FullCode,
    string? Name);

/// <summary>Узел дерева мест хранения.</summary>
public record LocationNodeDto(
    int Id,
    int? ParentId,
    LocationType Type,
    string Code,
    string FullCode,
    string? Name,
    IReadOnlyList<LocationNodeDto> Children);

/// <summary>Что лежит в месте хранения (например, после сканирования QR ячейки).</summary>
public record LocationContentDto(int ItemId, string Sku, string Name, string UnitCode, decimal Quantity);

// ---------- Документы и движения ----------

/// <summary>
/// Строка документа. Количество положительное; места задаются по типу документа:
/// приход — только To, выдача и списание — только From, перемещение — оба,
/// корректировка — ровно одно (To увеличивает остаток, From уменьшает).
/// </summary>
public record DocumentLineRequest(int ItemId, decimal Quantity, int? FromLocationId, int? ToLocationId);

public record CreateDocumentRequest(
    MovementType Type,
    string? ExternalNumber,
    int? EquipmentId,
    string? PerformedBy,
    string? Comment,
    IReadOnlyList<DocumentLineRequest> Lines);

public record DocumentLineDto(
    int LineNo,
    int ItemId,
    string Sku,
    string ItemName,
    string UnitCode,
    decimal Quantity,
    string? FromLocation,
    string? ToLocation);

public record DocumentDto(
    long Id,
    string Number,
    MovementType Type,
    DateTimeOffset OccurredAt,
    string? ExternalNumber,
    int? EquipmentId,
    string? PerformedBy,
    string? Comment,
    IReadOnlyList<DocumentLineDto> Lines);

/// <summary>Документ в списке (без строк).</summary>
public record DocumentSummaryDto(
    long Id,
    string Number,
    MovementType Type,
    DateTimeOffset OccurredAt,
    string? ExternalNumber,
    string? PerformedBy,
    int LineCount);

/// <summary>Строка истории движений по позиции.</summary>
public record MovementDto(
    long DocumentId,
    string DocumentNumber,
    MovementType Type,
    DateTimeOffset OccurredAt,
    decimal Quantity,
    string? FromLocation,
    string? ToLocation,
    int? EquipmentId,
    string? PerformedBy);

// ---------- Остатки ----------

public record StockBalanceDto(int LocationId, string LocationCode, decimal Quantity);

/// <summary>Позиция, остаток которой ниже минимума.</summary>
/// <param name="ToOrder">Сколько нужно пополнить, чтобы дойти до максимума (или до минимума, если максимум не задан).</param>
public record LowStockItemDto(
    int ItemId,
    string Sku,
    string Name,
    string UnitCode,
    decimal Total,
    decimal MinStock,
    decimal ToOrder);

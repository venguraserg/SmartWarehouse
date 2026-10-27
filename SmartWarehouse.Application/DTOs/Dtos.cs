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
    bool IsActive,
    long? ImageVersion);

/// <summary>Изменение позиции. Артикул не меняется; единицу можно сменить, только пока по позиции не было движений.</summary>
public record UpdateItemRequest(
    string Name,
    int? CategoryId,
    int UnitId,
    decimal MinStock,
    decimal? MaxStock,
    string? Description,
    bool IsActive);

public record ItemImageDto(byte[] Data, string ContentType);

// ---------- Сотрудники ----------

public record SaveEmployeeRequest(
    string? PersonnelNumber,
    string LastName,
    string FirstName,
    string? MiddleName,
    string? Position,
    string? Department,
    string? Phone,
    bool IsActive = true);

public record EmployeeDto(
    int Id,
    string? PersonnelNumber,
    string LastName,
    string FirstName,
    string? MiddleName,
    string? Position,
    string? Department,
    string? Phone,
    bool IsActive,
    string FullName,
    string ShortName);

/// <summary>
/// Движение по подотчёту сотрудника: выдача (количество со знаком «+»),
/// списание и возврат (со знаком «−»).
/// </summary>
/// <param name="Location">Для выдачи — откуда выдано, для возврата — куда вернули.</param>
public record EmployeeMovementDto(
    long DocumentId,
    string DocumentNumber,
    MovementType Type,
    DateTimeOffset OccurredAt,
    int ItemId,
    string Sku,
    string ItemName,
    decimal Quantity,
    string UnitCode,
    string? Location,
    int? EquipmentId);

/// <summary>Что числится за сотрудником (подотчёт).</summary>
public record CustodyRowDto(
    int EmployeeId,
    string EmployeeName,
    string? Position,
    bool EmployeeIsActive,
    int ItemId,
    string Sku,
    string ItemName,
    string UnitCode,
    long? ImageVersion,
    decimal Quantity);

// ---------- Оборудование ----------

public record CreateEquipmentRequest(int? ParentId, string Code, string Name);

public record EquipmentDto(int Id, int? ParentId, string Code, string Name);

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
    IReadOnlyList<DocumentLineRequest> Lines,
    int? PerformedById = null,
    int? ReceivedById = null);

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
    IReadOnlyList<DocumentLineDto> Lines,
    int? PerformedById,
    int? ReceivedById,
    string? ReceivedBy);

/// <summary>Документ в списке (без строк).</summary>
public record DocumentSummaryDto(
    long Id,
    string Number,
    MovementType Type,
    DateTimeOffset OccurredAt,
    string? ExternalNumber,
    string? PerformedBy,
    int LineCount,
    string? ReceivedBy);

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
    string? PerformedBy,
    string? ReceivedBy);

// ---------- Остатки ----------

/// <summary>Фильтры просмотра остатков. Все необязательные.</summary>
/// <param name="WarehouseId">Склад.</param>
/// <param name="Location">
/// Код места: показывается само место и всё, что внутри него («СК1-A» — весь стеллаж A).
/// Можно без кода склада: «A-01» найдёт «СК1-A-01» и «СК2-A-01».
/// </param>
/// <param name="Search">Часть наименования или артикула.</param>
/// <param name="CategoryId">Категория.</param>
/// <param name="BelowMinimum">Только позиции, у которых общий остаток ниже минимума.</param>
public record StockQuery(
    int? WarehouseId = null,
    string? Location = null,
    string? Search = null,
    int? CategoryId = null,
    bool BelowMinimum = false);

/// <summary>Строка остатков: сколько позиции лежит в конкретном месте.</summary>
public record StockRowDto(
    int ItemId,
    string Sku,
    string ItemName,
    string? CategoryName,
    string UnitCode,
    long? ImageVersion,
    decimal MinStock,
    int LocationId,
    string LocationCode,
    string? LocationName,
    int WarehouseId,
    string WarehouseCode,
    decimal Quantity);

/// <param name="Truncated">Строк больше лимита — уточните фильтры.</param>
public record StockSearchResult(IReadOnlyList<StockRowDto> Rows, bool Truncated);

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

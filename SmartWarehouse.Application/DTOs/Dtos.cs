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
    string? Description,
    string? Barcode = null);

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
    long? ImageVersion,
    string? Barcode = null);

/// <summary>Изменение позиции. Артикул не меняется; единицу можно сменить, только пока по позиции не было движений.</summary>
public record UpdateItemRequest(
    string Name,
    int? CategoryId,
    int UnitId,
    decimal MinStock,
    decimal? MaxStock,
    string? Description,
    bool IsActive,
    string? Barcode = null);

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
    bool IsActive = true,
    int? ManagerId = null);

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
    string ShortName,
    int? ManagerId,
    string? ManagerName);

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
    decimal Quantity,
    DateTimeOffset? HeldSince = null,
    decimal Reserved = 0);

// ---------- Учётные записи ----------

public record LoginRequest(string Login, string Password);

/// <summary>Вошедший пользователь.</summary>
public record CurrentUserDto(
    int Id,
    string Login,
    string DisplayName,
    int? EmployeeId,
    UserRole Roles,
    bool MustChangePassword);

/// <summary>Ответ на вход: токен для запросов к API и сведения о пользователе.</summary>
public record LoginResponse(string Token, DateTimeOffset ExpiresAt, CurrentUserDto User);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record UserDto(
    int Id,
    string Login,
    int? EmployeeId,
    string? EmployeeName,
    UserRole Roles,
    bool IsActive,
    bool MustChangePassword,
    DateTimeOffset? LastLoginAt);

/// <param name="TemporaryPassword">Временный пароль — при первом входе пользователь сменит его на свой.</param>
public record CreateUserRequest(string Login, int? EmployeeId, UserRole Roles, string TemporaryPassword);

public record UpdateUserRequest(int? EmployeeId, UserRole Roles, bool IsActive);

public record ResetPasswordRequest(string TemporaryPassword);

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
public record LocationContentDto(int ItemId, string Sku, string Name, string UnitCode, decimal Quantity, long? ImageVersion = null);

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
    string? ReceivedBy,
    int? ActId = null,
    string? ActNumber = null);

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

// ---------- Акты на списание ----------

/// <summary>Строка акта: что, сколько и куда использовано (оборудование и/или описание).</summary>
public record ActLineRequest(int ItemId, decimal Quantity, int? EquipmentId, string? Usage, DateOnly? UsedOn = null);

/// <summary>Создание или изменение акта (черновика).</summary>
/// <param name="PeriodFrom">Период сводного акта — необязательно.</param>
public record SaveActRequest(
    int EmployeeId,
    DateOnly? PeriodFrom,
    DateOnly? PeriodTo,
    string? ExternalNumber,
    string? Comment,
    IReadOnlyList<ActLineRequest> Lines);

public record ActLineDto(
    int LineNo,
    int ItemId,
    string Sku,
    string ItemName,
    string UnitCode,
    decimal Quantity,
    int? EquipmentId,
    string? Equipment,
    string? Usage,
    DateOnly? UsedOn);

/// <summary>Что текущий пользователь может сделать с актом.</summary>
public record ActPermissions(bool Edit, bool Submit, bool Recall, bool Decide);

public record ActDto(
    int Id,
    string Number,
    ActStatus Status,
    int EmployeeId,
    string EmployeeName,
    string? EmployeePosition,
    DateOnly? PeriodFrom,
    DateOnly? PeriodTo,
    string? ExternalNumber,
    string? Comment,
    DateTimeOffset CreatedAt,
    string? CreatedBy,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SubmittedAt,
    int ApproverId,
    string ApproverName,
    int? DecidedById,
    string? DecidedBy,
    string? DecidedByPosition,
    DateTimeOffset? DecidedAt,
    string? ReturnRemark,
    long? DocumentId,
    string? DocumentNumber,
    IReadOnlyList<ActLineDto> Lines,
    ActPermissions Can);

/// <summary>Акт в списке (без строк).</summary>
public record ActSummaryDto(
    int Id,
    string Number,
    ActStatus Status,
    int EmployeeId,
    string EmployeeName,
    DateOnly? PeriodFrom,
    DateOnly? PeriodTo,
    string? ExternalNumber,
    int LineCount,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SubmittedAt,
    string ApproverName,
    string? DecidedBy,
    DateTimeOffset? DecidedAt);

/// <summary>Какие акты показать.</summary>
public enum ActScope
{
    /// <summary>Все, которые пользователь может видеть (свои, подчинённых; кладовщику — все).</summary>
    Visible,

    /// <summary>Ждут утверждения текущим пользователем.</summary>
    ToApprove
}

public record ReturnActRequest(string Remark);

// ---------- Сканирование и наклейки ----------

public enum ScanKind
{
    Location,
    Item
}

/// <summary>Что нашлось по отсканированному коду: место хранения или позиция.</summary>
public record ScanResultDto(ScanKind Kind, string Code, int Id, string Title, string? Subtitle);

public enum LabelKind
{
    Location,
    Item
}

/// <summary>Наклейки с QR-кодами на лист A4.</summary>
/// <param name="Layout">Раскладка листа (см. GET api/labels/layouts), например «3x8».</param>
/// <param name="Skip">Сколько наклеек пропустить в начале листа (лист уже частично использован).</param>
/// <param name="BaseUrl">Адрес веб-приложения: QR-код содержит ссылку, которую открывает камера телефона.</param>
public record LabelRequest(LabelKind Kind, IReadOnlyList<int> Ids, string? Layout, int Skip = 0, string? BaseUrl = null, int Copies = 1);

public record LabelLayoutDto(string Id, string Title, int Columns, int Rows, double WidthMm, double HeightMm);

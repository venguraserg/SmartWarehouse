using SmartWarehouse.Domain.Enums;

namespace SmartWarehouse.Application.DTOs;

// ---------- Справочники ----------

public record CreateCategoryRequest(string Name, string? Description, string? SkuPrefix = null);

/// <param name="SkuPrefix">Префикс автоартикула (BRG); null — общий SP.</param>
public record CategoryDto(int Id, string Name, string? Description, string? SkuPrefix = null);

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
    string? Barcode = null,
    int NoteCount = 0,
    ItemNoteHitDto? NoteHit = null);

/// <summary>Заметка, в которой нашлось слово поиска: вид и отрывок текста вокруг совпадения.</summary>
public record ItemNoteHitDto(NoteKind Kind, string Snippet);

/// <summary>Заметка к позиции. CanEdit — автор; CanDelete — автор, кладовщик, администратор.</summary>
public record ItemNoteDto(
    int Id,
    int ItemId,
    NoteKind Kind,
    string Text,
    bool Pinned,
    string AuthorName,
    bool Mine,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    bool CanEdit,
    bool CanDelete,
    bool CanPin);

/// <summary>Новая заметка или изменение своей.</summary>
public record SaveItemNoteRequest(NoteKind Kind, string Text, bool Pinned = false);

/// <summary>Закрепить или открепить заметку.</summary>
public record PinItemNoteRequest(bool Pinned);

/// <summary>Кто работает с заметками: учётная запись, имя для подписи, складские права (кладовщик, администратор).</summary>
public record NoteActor(int UserId, string Name, bool IsStaff);

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

/// <summary>Что числится за сотрудником: «выдано в работу» или «подотчёт по 1С» (<see cref="Kind"/>).</summary>
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
    decimal Reserved = 0,
    decimal? Value = null,
    bool ValueIncomplete = false,
    CustodyKind Kind = CustodyKind.Work,
    decimal OffBookQuantity = 0,
    decimal OffBookValue = 0,
    IReadOnlyList<string>? Mols = null);

// ---------- Учётные записи ----------

public record LoginRequest(string Login, string Password);

/// <summary>Вошедший пользователь.</summary>
public record CurrentUserDto(
    int Id,
    string Login,
    string DisplayName,
    int? EmployeeId,
    UserRole Roles,
    bool MustChangePassword,
    string? UiTheme = null);

/// <summary>Выбор оформления интерфейса (запоминается за учётной записью).</summary>
public record SetUiThemeRequest(string? Theme);

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
public record LocationContentDto(int ItemId, string Sku, string Name, string UnitCode, decimal Quantity, long? ImageVersion = null, decimal? Value = null, bool ValueIncomplete = false);

// ---------- Документы и движения ----------

/// <summary>
/// Строка документа. Количество положительное; места задаются по типу документа:
/// приход — только To, выдача и списание — только From, перемещение — оба,
/// корректировка — ровно одно (To увеличивает остаток, From уменьшает).
/// </summary>
/// <param name="BatchId">Партия: откуда взять (иначе — самая старая) или к какой добавить при корректировке «+».</param>
/// <param name="UnitPrice">Цена за единицу — обязательна для прихода и корректировки «+» без партии (создаётся новая партия).</param>
/// <param name="BatchDate">Дата новой партии (приход), если не дата документа — например, при вводе остатков из 1С.</param>
/// <param name="BatchNote">Примечание новой партии: документ поступления «ТТН 3834079 от 27.05.2026».</param>
public record DocumentLineRequest(
    int ItemId,
    decimal Quantity,
    int? FromLocationId,
    int? ToLocationId,
    int? BatchId = null,
    decimal? UnitPrice = null,
    DateTimeOffset? BatchDate = null,
    string? BatchNote = null);

public record CreateDocumentRequest(
    MovementType Type,
    string? ExternalNumber,
    int? EquipmentId,
    string? PerformedBy,
    string? Comment,
    IReadOnlyList<DocumentLineRequest> Lines,
    int? PerformedById = null,
    int? ReceivedById = null,
    bool OffBook = false,
    BatchOrigin Origin = BatchOrigin.Manual,
    CustodyKind? Custody = null,
    int? IssueRequestId = null,
    int? MolId = null);

public record DocumentLineDto(
    int LineNo,
    int ItemId,
    string Sku,
    string ItemName,
    string UnitCode,
    decimal Quantity,
    string? FromLocation,
    string? ToLocation,
    int? BatchId = null,
    decimal? UnitPrice = null,
    DateTimeOffset? BatchDate = null,
    string? FromLocationName = null,
    string? ToLocationName = null,
    bool OffBook = false,
    string? Mol = null);

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
    string? ReceivedBy,
    decimal? UnitPrice = null);

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
    bool BelowMinimum = false,
    bool? OffBook = null,
    int? MolId = null);

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
    decimal Quantity,
    decimal? Value = null,
    bool ValueIncomplete = false,
    decimal OffBookQuantity = 0,
    decimal OffBookValue = 0,
    IReadOnlyList<string>? Mols = null);

/// <param name="Truncated">Строк больше лимита — уточните фильтры.</param>
/// <param name="NoteHits">Позиции, найденные только по заметке: вид заметки и отрывок вокруг слова (ключ — позиция).</param>
public record StockSearchResult(IReadOnlyList<StockRowDto> Rows, bool Truncated, IReadOnlyDictionary<int, ItemNoteHitDto>? NoteHits = null);

/// <param name="LocationName">Описание места («Подшипники») — по нему место узнают в списках.</param>
/// <param name="OffBookQuantity">Сколько из количества — вне учёта (б/у, неучтённое; в 1С не числится).</param>
public record StockBalanceDto(int LocationId, string LocationCode, decimal Quantity, decimal? Value = null, bool ValueIncomplete = false, string? LocationName = null, decimal OffBookQuantity = 0);

/// <summary>Партия позиции и сколько её в выбранном месте (у сотрудника, всего).</summary>
/// <param name="Reserved">У сотрудника: сколько из этого — в актах на утверждении.</param>
public record BatchStockDto(
    int BatchId,
    DateTimeOffset ReceivedAt,
    decimal? UnitPrice,
    decimal Quantity,
    long? DocumentId,
    string? DocumentNumber,
    string? Note,
    decimal Reserved = 0,
    bool OffBook = false);

public record SetBatchPriceRequest(decimal UnitPrice);

/// <summary>Позиция, остаток которой ниже минимума.</summary>
/// <param name="ToOrder">Сколько нужно пополнить, чтобы дойти до максимума (или до минимума, если максимум не задан).</param>
/// <summary>Показатели склада для «Обзора».</summary>
/// <param name="ItemsInStock">Позиций, которые есть на складе (по всем местам).</param>
/// <param name="LocationsUsed">Мест, где что-то лежит.</param>
/// <param name="BelowMinimum">Есть, но меньше минимального остатка.</param>
/// <param name="OutOfStock">Задан минимум, а на складе нет совсем.</param>
/// <param name="CustodyEmployees">Сотрудников, за которыми что-то числится.</param>
/// <param name="CustodyItems">Позиций в подотчёте (сотрудник × позиция).</param>
public record StockSummaryDto(int ItemsInStock, int LocationsUsed, int BelowMinimum, int OutOfStock, int CustodyEmployees, int CustodyItems);

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
/// <param name="BatchId">Партия из подотчёта; не указана — самая старая (при нехватке строка делится по партиям).</param>
public record ActLineRequest(int ItemId, decimal Quantity, int? EquipmentId, string? Usage, DateOnly? UsedOn = null, int? BatchId = null);

/// <summary>Создание или изменение акта или отчёта об использовании (черновика).</summary>
/// <param name="PeriodFrom">Период сводного акта — необязательно.</param>
/// <param name="Kind">Акт на списание (подотчёт по 1С) или отчёт об использовании (выданное «в работу»).</param>
public record SaveActRequest(
    int EmployeeId,
    DateOnly? PeriodFrom,
    DateOnly? PeriodTo,
    string? ExternalNumber,
    string? Comment,
    IReadOnlyList<ActLineRequest> Lines,
    ActKind Kind = ActKind.WriteOff);

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
    DateOnly? UsedOn,
    int? BatchId = null,
    decimal? UnitPrice = null,
    DateTimeOffset? BatchDate = null,
    string? FromLocationName = null,
    string? ToLocationName = null,
    string? EmployeeName = null,
    string? SourceNumber = null,
    int? SourceId = null,
    bool OffBook = false,
    int? MolId = null,
    string? Mol = null);

/// <summary>Сводный акт кладовщика: какие отчёты включить и реквизиты.</summary>
/// <param name="ExternalNumber">Номер документа списания в 1С (можно вписать и после проведения).</param>
public record SaveSummaryRequest(
    DateOnly? PeriodFrom,
    DateOnly? PeriodTo,
    string? ExternalNumber,
    string? Comment,
    IReadOnlyList<int> ReportIds);

public record SetExternalNumberRequest(string? ExternalNumber);

/// <summary>Ссылка на складской документ.</summary>
public record DocumentRefDto(long Id, string Number);

// ---------- Инвентаризация ----------

/// <summary>Начать инвентаризацию места (обычно стеллажа) со всем вложенным.</summary>
public record StartInventoryRequest(int LocationId, string? Comment = null);

/// <summary>Кто работает с инвентаризацией: сотрудник (может не быть) и подпись.</summary>
public record InventoryActor(int? EmployeeId, string Name);

public record InventorySummaryDto(
    int Id,
    string Number,
    int RootLocationId,
    string RootCode,
    string RootTitle,
    InventoryStatus Status,
    DateTimeOffset StartedAt,
    string StartedBy,
    DateTimeOffset? FinishedAt,
    string? FinishedBy,
    string? Comment,
    int Places,
    int CheckedPlaces,
    int Differences);

/// <summary>Место в списке инвентаризации. Shelf — подзаголовок группы («Полка 1», «Под стеллажом»).</summary>
public record InventoryPlaceSummaryDto(
    int LocationId,
    string FullCode,
    string Title,
    string? Shelf,
    bool Checked,
    int BookPositions,
    int CountedPositions,
    int Differences);

public record InventoryDto(InventorySummaryDto Header, IReadOnlyList<InventoryPlaceSummaryDto> Places);

/// <summary>
/// Позиция в месте: Book — числится сейчас; Expected — числилось в момент подсчёта (null — не считали);
/// Counted — факт; Elsewhere — где ещё она лежит по учёту (для «лишнего»).
/// </summary>
public record InventoryItemDto(
    int ItemId,
    string Sku,
    string Name,
    string Unit,
    bool Fractional,
    decimal Book,
    decimal? Expected,
    decimal? Counted,
    string? Elsewhere);

public record InventoryPlaceDto(
    int InventoryId,
    string Number,
    bool Open,
    int LocationId,
    string FullCode,
    string Title,
    string Full,
    string? Area,
    bool Checked,
    IReadOnlyList<InventoryItemDto> Items,
    int? NextLocationId);

/// <summary>Факт по позиции в месте; Counted = null — отменить подсчёт.</summary>
public record InventoryCountRequest(int ItemId, decimal? Counted);

public record InventoryCheckRequest(bool Checked);

/// <summary>Что будет с остатком расхождения при проведении (после перемещений).</summary>
public enum InventoryAction
{
    None,
    Shortage,
    Surplus
}

/// <summary>
/// Строка ведомости расхождений. Moved/MoveText — сколько уйдёт перемещением (пересорт между местами);
/// Action — что с остатком разницы: списать недостачу или оприходовать излишек вне учёта.
/// </summary>
public record InventorySheetRowDto(
    int LocationId,
    string Place,
    bool PlaceChecked,
    int ItemId,
    string Sku,
    string Name,
    string Unit,
    decimal Expected,
    decimal Counted,
    decimal Delta,
    decimal Moved,
    string? MoveText,
    InventoryAction Action,
    decimal? Value);

public record InventorySheetDto(
    InventorySummaryDto Header,
    IReadOnlyList<InventorySheetRowDto> Rows,
    int CountedPositions,
    int ShortagePositions,
    decimal ShortageValue,
    int SurplusPositions,
    int MovePositions,
    IReadOnlyList<string> UncheckedPlaces);

public record InventoryPostResultDto(InventorySummaryDto Header, IReadOnlyList<DocumentRefDto> Documents);

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
    ActPermissions Can,
    ActKind Kind = ActKind.WriteOff,
    IReadOnlyList<ActSummaryDto>? Reports = null,
    IReadOnlyList<DocumentRefDto>? Documents = null,
    int? SummaryActId = null,
    string? SummaryActNumber = null,
    bool WrittenOff = false);

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
    DateTimeOffset? DecidedAt,
    ActKind Kind = ActKind.WriteOff,
    string? SummaryActNumber = null,
    bool WrittenOff = false);

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
    Item,

    /// <summary>Всё для стеллажа: табличка A5 и наклейки его полок и ячеек (сверху вниз).</summary>
    Rack
}

/// <summary>Наклейки с QR-кодами на лист A4.</summary>
/// <param name="Layout">Раскладка листа (см. GET api/labels/layouts), например «3x8».</param>
/// <param name="Skip">Сколько наклеек пропустить в начале листа (лист уже частично использован).</param>
/// <param name="BaseUrl">Адрес веб-приложения: QR-код содержит ссылку, которую открывает камера телефона.</param>
/// <param name="Sign">Для стеллажа: печатать табличку A5.</param>
/// <param name="Stickers">Для стеллажа: печатать наклейки полок и ячеек.</param>
public record LabelRequest(LabelKind Kind, IReadOnlyList<int> Ids, string? Layout, int Skip = 0, string? BaseUrl = null, int Copies = 1,
    bool Sign = true, bool Stickers = true);

public record LabelLayoutDto(string Id, string Title, int Columns, int Rows, double WidthMm, double HeightMm);

// ---------- Настройки (администратор) ----------

/// <summary>Что зашивается в QR-код наклейки.</summary>
public enum QrContent
{
    /// <summary>Сам код места хранения или артикул (WH1-A-01, SP-000012) — как напечатан на наклейке.</summary>
    Code,

    /// <summary>Ссылка на страницу приложения: открывается и обычной камерой телефона.</summary>
    Link
}

/// <summary>Настройки наклеек.</summary>
/// <param name="BaseUrl">Адрес приложения для ссылок (http://sklad:8081). Пусто — адрес, по которому открыт сайт.</param>
public record LabelSettingsDto(QrContent QrContent, string? BaseUrl);

/// <summary>Член комиссии в акте: должность и ФИО (что-то одно можно не указывать).</summary>
public record CommissionMemberDto(string? Position, string? Name);

/// <summary>Шаблон акта на списание.</summary>
/// <param name="Organization">Организация / подразделение в шапке.</param>
/// <param name="Subtitle">Строка под «АКТ №»: «на списание материальных ценностей».</param>
/// <param name="Intro">Текст перед таблицей.</param>
/// <param name="ApproverPosition">Должность в «УТВЕРЖДАЮ», пока акт не утверждён в программе.</param>
/// <param name="Chair">Председатель комиссии; null — пустая строка для подписи.</param>
/// <param name="Members">Члены комиссии, напечатанные заранее.</param>
/// <param name="BlankMembers">Сколько ещё пустых строк для членов комиссии.</param>
/// <param name="ResponsibleSigns">Подпись материально ответственного лица.</param>
public record ActTemplateDto(
    string? Organization,
    string Subtitle,
    string Intro,
    string ApproverPosition,
    CommissionMemberDto? Chair,
    IReadOnlyList<CommissionMemberDto> Members,
    int BlankMembers,
    bool ResponsibleSigns);

// ---------- Правка справочников (администратор) ----------

public record UpdateWarehouseRequest(string Code, string Name, string? Description);

/// <summary>Изменение места хранения: код, название, тип и куда вложено (ParentId = null — верхний уровень склада).</summary>
public record UpdateLocationRequest(int? ParentId, LocationType Type, string Code, string? Name);

public record UpdateCategoryRequest(string Name, string? Description, string? SkuPrefix = null);

public record UpdateUnitRequest(string Code, string Name, bool IsFractional);

public record UpdateEquipmentRequest(int? ParentId, string Code, string Name);

/// <summary>Итог правки: что изменилось и что из этого следует (например, перепечатать наклейки).</summary>
/// <param name="ChangedLocationIds">Места, у которых изменился полный код (их QR-наклейки нужно перепечатать).</param>
public record ReferenceChangeResult(string Message, IReadOnlyList<int>? ChangedLocationIds = null);

// ---------- Запросы на выдачу ----------

/// <param name="Places">Где лежит (для сборки заказа): место и количество — только тем, кто выдаёт.</param>
public record IssueRequestLineDto(
    int LineNo,
    int ItemId,
    string Sku,
    string Name,
    string UnitCode,
    decimal Quantity,
    decimal? Issued,
    decimal OnHand,
    IReadOnlyList<IssueRequestPlaceDto> Places);

public record IssueRequestPlaceDto(int LocationId, string LocationCode, string? LocationName, decimal Quantity);

/// <param name="Can">Что может текущий пользователь.</param>
public record IssueRequestDto(
    int Id,
    string Number,
    IssueRequestStatus Status,
    int EmployeeId,
    string EmployeeName,
    string? EmployeePosition,
    string? Comment,
    DateTimeOffset CreatedAt,
    string? CreatedBy,
    DateTimeOffset? ClosedAt,
    string? ClosedBy,
    string? CloseNote,
    long? DocumentId,
    string? DocumentNumber,
    IReadOnlyList<IssueRequestLineDto> Lines,
    IssueRequestPermissions Can);

public record IssueRequestPermissions(bool Issue, bool Reject, bool Cancel);

public record IssueRequestSummaryDto(
    int Id,
    string Number,
    IssueRequestStatus Status,
    int EmployeeId,
    string EmployeeName,
    DateTimeOffset CreatedAt,
    int LineCount,
    string? Comment,
    long? DocumentId,
    string? DocumentNumber);

public record IssueRequestLineRequest(int ItemId, decimal Quantity);

/// <param name="EmployeeId">Кому выдать: сотрудник — только себе; кладовщик и старший смены — любому (null — себе).</param>
public record CreateIssueRequestRequest(int? EmployeeId, string? Comment, IReadOnlyList<IssueRequestLineRequest> Lines);

public record RejectIssueRequestRequest(string? Reason);

// ---------- МОЛ ----------

/// <summary>Сменить МОЛ части партии в месте хранения: на другого кладовщика (накладная на перемещение, сверка с 1С).</summary>
public record MolChangeLine(int LocationId, int BatchId, decimal Quantity, int MolId);

/// <param name="ExternalNumber">Накладная 1С, по которой меняется МОЛ (повторно не принимается).</param>
public record ChangeMolRequest(IReadOnlyList<MolChangeLine> Lines, string? Comment = null, string? ExternalNumber = null);

/// <summary>МОЛ склада — кладовщик (сотрудник с учётной записью и ролью «Кладовщик»).</summary>
public record MolDto(int EmployeeId, string Name);


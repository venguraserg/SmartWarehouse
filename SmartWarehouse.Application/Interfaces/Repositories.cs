using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;

namespace SmartWarehouse.Application.Interfaces;

/// <summary>Позиция номенклатуры с суммарным остатком по всем местам.</summary>
public record ItemStock(Item Item, decimal Total);

/// <summary>Партия и сколько её в выбранном месте / у сотрудника / всего.</summary>
public record BatchStock(Batch Batch, decimal Quantity, string? DocumentNumber);

/// <summary>Шапка документа с числом строк — для списков.</summary>
public record DocumentHeader(StockDocument Document, int LineCount);

/// <summary>Нормализованные фильтры поиска остатков для репозитория.</summary>
/// <param name="OffBook">null — все партии; false — только учётные (по 1С); true — только вне учёта.</param>
/// <param name="MolId">null — все; 0 — основной МОЛ (партии без пометки); иначе — партии этого МОЛ.</param>
public record StockFilter(int? WarehouseId, string? LocationCode, string? Search, int? CategoryId, bool BelowMinimum, bool? OffBook = null, int? MolId = null);

/// <summary>Краткие сведения о месте хранения для проверок при проведении документа.</summary>
public record LocationInfo(int Id, string FullCode, bool HasChildren, string? Name = null);

/// <summary>Выдача позиции сотруднику (строка документа выдачи).</summary>
public record IssueRecord(int EmployeeId, int ItemId, DateTimeOffset OccurredAt, decimal Quantity, CustodyKind Kind = CustodyKind.Work);

/// <summary>Сколько позиции за сотрудником в актах на утверждении.</summary>
public record ReservedRecord(int EmployeeId, int ItemId, decimal Quantity, CustodyKind Kind = CustodyKind.Official);

/// <summary>Фильтр списка актов на списание.</summary>
/// <param name="EmployeeIds">Только акты этих сотрудников (null — всех).</param>
/// <param name="ApproverId">Только акты на утверждении, которые утверждает этот сотрудник.</param>
/// <param name="ReportsToConfirm">Вместе с <paramref name="ApproverId"/>: ещё и отчёты об использовании на подтверждении (для кладовщика).</param>
/// <param name="Kind">Только акты или только отчёты (null — все).</param>
public record ActFilter(IReadOnlyCollection<int>? EmployeeIds, int? ApproverId, ActStatus? Status, int? EmployeeId, int Take,
    bool ReportsToConfirm = false, ActKind? Kind = null);

/// <summary>Шапка акта с числом строк — для списков.</summary>
public record ActHeader(WriteOffAct Act, int LineCount);

/// <summary>Доступ к данным для складских документов и остатков.</summary>
public interface IStockRepository
{
    /// <summary>Позиции по идентификаторам (с единицей измерения). Отсутствующих в результате нет.</summary>
    Task<IReadOnlyDictionary<int, Item>> GetItemsAsync(IEnumerable<int> ids, CancellationToken ct = default);

    Task<bool> ItemExistsAsync(int id, CancellationToken ct = default);

    Task<IReadOnlyDictionary<int, LocationInfo>> GetLocationsAsync(IEnumerable<int> ids, CancellationToken ct = default);

    Task<bool> EquipmentExistsAsync(int equipmentId, CancellationToken ct = default);

    Task<Employee?> GetEmployeeAsync(int id, CancellationToken ct = default);

    /// <summary>Остатки позиции на месте по партиям (отслеживаемые, с партией), включая нулевые.</summary>
    Task<IReadOnlyList<StockBalance>> GetLocationBatchesAsync(int itemId, int locationId, CancellationToken ct = default);

    void AddBalance(StockBalance balance);

    /// <summary>Что числится за сотрудником по позиции — по партиям (отслеживаемые, с партией), включая нулевые.</summary>
    Task<IReadOnlyList<EmployeeBalance>> GetEmployeeBatchesAsync(int itemId, int employeeId, CancellationToken ct = default);

    void AddEmployeeBalance(EmployeeBalance balance);

    Task<Batch?> GetBatchAsync(int id, CancellationToken ct = default);

    void AddBatch(Batch batch);

    /// <summary>МОЛ склада — сотрудники, у чьей активной учётной записи есть роль «Кладовщик».</summary>
    Task<IReadOnlyList<int>> ListStorekeeperIdsAsync(CancellationToken ct = default);

    /// <summary>Зарезервировано актами на утверждении по партиям (кроме акта <paramref name="exceptActId"/>).</summary>
    /// <summary>
    /// Резерв подотчёта сотрудника по партиям и видам: строки актов и отчётов на утверждении/подтверждении,
    /// а также подтверждённых отчётов об использовании, которые ещё не списаны (кроме вне учёта).
    /// </summary>
    Task<IReadOnlyDictionary<(int BatchId, CustodyKind Kind), decimal>> GetReservedByBatchAsync(int itemId, int employeeId, int? exceptActId, CancellationToken ct = default);

    /// <summary>Партии позиции с остатком: в месте, у сотрудника или везде (склад + подотчёт).</summary>
    // ---------- Приёмка по накладным ----------

    /// <summary>Приход с таким номером документа уже есть (накладная уже загружена).</summary>
    Task<bool> ReceiptExistsAsync(string externalNumber, CancellationToken ct = default);   // приход или смена МОЛ по накладной

    /// <summary>Остатки в месте по партиям (с позицией, единицей, партией) — что ждёт размещения в «Приёмке».</summary>
    Task<IReadOnlyList<StockBalance>> ListLocationStockAsync(int locationId, CancellationToken ct = default);

    /// <summary>Учётные партии, введённые вручную, которые ещё есть на складе — чтобы предупредить о задвоении при загрузке накладной.</summary>
    Task<IReadOnlyList<ManualBatch>> ListManualAccountedAsync(IReadOnlyCollection<int> itemIds, CancellationToken ct = default);

    /// <summary>Сколько пришло этой партией (по документу прихода).</summary>
    Task<decimal> GetBatchReceivedAsync(int batchId, CancellationToken ct = default);

    void AddDiscrepancy(ReceiptDiscrepancy discrepancy);

    Task<ReceiptDiscrepancy?> GetDiscrepancyAsync(int id, CancellationToken ct = default);

    /// <summary>Расхождения приёмки (с партией и позицией); по умолчанию — только незакрытые.</summary>
    Task<IReadOnlyList<ReceiptDiscrepancy>> ListDiscrepanciesAsync(bool includeResolved, CancellationToken ct = default);

    /// <summary>Что числится за каждым МОЛ по позициям (склад + «в работе», без вне учёта) — для сверки с 1С.</summary>
    Task<IReadOnlyList<MolTotal>> ListMolTotalsAsync(CancellationToken ct = default);

    /// <summary>Учётные партии позиций на местах хранения (с местом и партией) — откуда можно сменить МОЛ.</summary>
    Task<IReadOnlyList<StockBalance>> ListMolSourcesAsync(IReadOnlyCollection<int> itemIds, CancellationToken ct = default);

    /// <summary>Сколько партии всего: во всех местах и у сотрудников.</summary>
    Task<decimal> GetBatchTotalAsync(int batchId, CancellationToken ct = default);


    Task<IReadOnlyList<BatchStock>> ListBatchStockAsync(int itemId, int? locationId, int? employeeId, CancellationToken ct = default);

    /// <summary>
    /// Сколько позиции за сотрудником зарезервировано актами на утверждении
    /// (кроме акта <paramref name="exceptActId"/>, который сейчас утверждается).
    /// </summary>
    Task<decimal> GetReservedAsync(int itemId, int employeeId, int? exceptActId, CancellationToken ct = default);

    void AddDocument(StockDocument document);

    /// <summary>Документ со строками, позициями, единицами и местами.</summary>
    Task<StockDocument?> GetDocumentAsync(long id, CancellationToken ct = default);

    /// <summary>Последние документы, новые первыми.</summary>
    /// <param name="onlyEmployeeIds">Только документы, где сотрудник из списка — получатель/сдающий или проводивший; null — все.</param>
    Task<IReadOnlyList<DocumentHeader>> ListDocumentsAsync(MovementType? type, int take, IReadOnlyCollection<int>? onlyEmployeeIds = null, CancellationToken ct = default);

    /// <summary>Остатки позиции по местам (с местом), без нулевых.</summary>
    Task<IReadOnlyList<StockBalance>> GetBalancesForItemAsync(int itemId, CancellationToken ct = default);

    /// <summary>Последние движения позиции (с документом и местами), новые первыми.</summary>
    Task<IReadOnlyList<StockMovement>> GetMovementsForItemAsync(int itemId, int take, CancellationToken ct = default);

    /// <summary>
    /// Ненулевые остатки по фильтрам (с позицией, единицей, категорией, местом и складом),
    /// упорядоченные по коду места и наименованию.
    /// </summary>
    Task<IReadOnlyList<StockBalance>> SearchBalancesAsync(StockFilter filter, int take, CancellationToken ct = default);

    /// <summary>Активные позиции (с единицей), у которых суммарный остаток ниже минимума.</summary>
    Task<IReadOnlyList<ItemStock>> GetItemsBelowMinimumAsync(CancellationToken ct = default);

    /// <summary>Позиций и мест с остатком; сотрудников и позиций в подотчёте.</summary>
    Task<(int Items, int Locations, int CustodyEmployees, int CustodyItems)> CountStockAsync(CancellationToken ct = default);

    /// <summary>Сохраняет все изменения одной транзакцией.</summary>
    Task SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>Доступ к данным справочников: категории, единицы измерения, номенклатура.</summary>
public interface ICatalogRepository
{
    Task<IReadOnlyList<Category>> ListCategoriesAsync(CancellationToken ct = default);

    Task<bool> CategoryExistsAsync(int id, CancellationToken ct = default);

    Task<bool> CategoryNameExistsAsync(string name, CancellationToken ct = default);

    void AddCategory(Category category);

    Task<IReadOnlyList<UnitOfMeasure>> ListUnitsAsync(CancellationToken ct = default);

    Task<bool> UnitExistsAsync(int id, CancellationToken ct = default);

    Task<bool> UnitCodeExistsAsync(string code, CancellationToken ct = default);

    void AddUnit(UnitOfMeasure unit);

    /// <summary>Позиция с категорией и единицей.</summary>
    Task<Item?> GetItemAsync(int id, CancellationToken ct = default);

    Task<bool> SkuExistsAsync(string sku, CancellationToken ct = default);

    /// <summary>Занят ли штрих-код другой позицией.</summary>
    Task<bool> BarcodeExistsAsync(string barcode, int? exceptItemId, CancellationToken ct = default);

    /// <summary>Позиция (с единицей) по артикулу без учёта регистра или по штрих-коду.</summary>
    Task<Item?> FindItemByCodeAsync(string code, CancellationToken ct = default);

    /// <param name="limit">Не больше стольких позиций; null — все.</param>
    Task<IReadOnlyList<Item>> ListItemsAsync(string? search, int? categoryId, int? limit, CancellationToken ct = default);

    void AddItem(Item item);

    /// <summary>Артикулы, начинающиеся с префикса (для подбора следующего свободного).</summary>
    Task<IReadOnlyList<string>> ListSkusStartingWithAsync(string prefix, CancellationToken ct = default);

    /// <summary>Позиция для изменения (отслеживаемая сущность).</summary>
    Task<Item?> GetItemForUpdateAsync(int id, CancellationToken ct = default);

    /// <summary>Были ли по позиции движения (тогда нельзя менять единицу измерения).</summary>
    Task<bool> ItemHasMovementsAsync(int id, CancellationToken ct = default);

    /// <summary>Фото позиции (отслеживаемая сущность) или null.</summary>
    Task<ItemImage?> GetImageAsync(int itemId, CancellationToken ct = default);

    void AddImage(ItemImage image);

    void RemoveImage(ItemImage image);

    Task<IReadOnlyList<Equipment>> ListEquipmentAsync(CancellationToken ct = default);

    Task<bool> EquipmentExistsAsync(int id, CancellationToken ct = default);

    Task<bool> EquipmentCodeExistsAsync(string code, CancellationToken ct = default);

    void AddEquipment(Equipment equipment);

    Task SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>Инвентаризации.</summary>
public interface IInventoryRepository
{
    /// <summary>Инвентаризация для изменения: строки (с позицией и единицей), проверенные места, корневое место.</summary>
    Task<InventoryCount?> GetAsync(int id, CancellationToken ct = default);

    /// <summary>Последние инвентаризации (новые первыми) со строками и местами — только чтение.</summary>
    Task<IReadOnlyList<InventoryCount>> ListAsync(int take, CancellationToken ct = default);

    /// <summary>Незавершённые инвентаризации (с корневым местом).</summary>
    Task<IReadOnlyList<InventoryCount>> ListOpenAsync(CancellationToken ct = default);

    /// <summary>Остатки по партиям в этих местах (с позицией, единицей, партией).</summary>
    Task<IReadOnlyList<StockBalance>> ListStockAsync(IReadOnlyCollection<int> locationIds, CancellationToken ct = default);

    /// <summary>Где ещё числятся позиции: место → количество (для подсказки «лежит на C3 · п.2»).</summary>
    Task<IReadOnlyList<(int ItemId, int LocationId, decimal Quantity)>> ListItemPlacesAsync(IReadOnlyCollection<int> itemIds, CancellationToken ct = default);

    /// <summary>Отметить места проверенными (дата последней инвентаризации).</summary>
    Task SetCheckedAsync(IReadOnlyCollection<int> locationIds, DateTimeOffset at, CancellationToken ct = default);

    void Add(InventoryCount inventory);

    void Remove(InventoryLine line);

    void Remove(InventoryPlace place);

    Task SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Всё действие — одной транзакцией базы (проведение из нескольких документов).</summary>
    Task InTransactionAsync(Func<Task> action, CancellationToken ct = default);
}

/// <summary>Заметки сотрудников к позициям номенклатуры.</summary>
public interface IItemNoteRepository
{
    Task<bool> ItemExistsAsync(int itemId, CancellationToken ct = default);

    /// <summary>Заметки позиции: закреплённые первыми, затем новые.</summary>
    Task<IReadOnlyList<ItemNote>> ListAsync(int itemId, CancellationToken ct = default);

    /// <summary>Заметка для изменения (отслеживаемая сущность).</summary>
    Task<ItemNote?> GetAsync(int id, CancellationToken ct = default);

    /// <summary>Сколько заметок у каждой из позиций (позиции без заметок не попадают).</summary>
    Task<IReadOnlyDictionary<int, int>> CountByItemAsync(IReadOnlyCollection<int> itemIds, CancellationToken ct = default);

    /// <summary>Заметки этих позиций, где есть хотя бы одно из слов (без учёта регистра); закреплённые первыми.</summary>
    Task<IReadOnlyList<ItemNote>> FindAsync(IReadOnlyCollection<int> itemIds, IReadOnlyList<string> words, CancellationToken ct = default);

    void Add(ItemNote note);

    void Remove(ItemNote note);

    Task SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>Доступ к данным справочника сотрудников.</summary>
public interface IEmployeeRepository
{
    Task<IReadOnlyList<Employee>> ListAsync(string? search, bool includeInactive, CancellationToken ct = default);

    /// <summary>Сотрудник (отслеживаемая сущность — для изменения).</summary>
    Task<Employee?> GetAsync(int id, CancellationToken ct = default);

    /// <summary>Занят ли табельный номер другим сотрудником.</summary>
    Task<bool> PersonnelNumberExistsAsync(string number, int? exceptId, CancellationToken ct = default);

    void Add(Employee employee);

    /// <summary>Движения по подотчёту сотрудника (с документом, позицией, единицей, местами), новые первыми.</summary>
    Task<IReadOnlyList<StockMovement>> GetHistoryAsync(int employeeId, int take, CancellationToken ct = default);

    /// <summary>
    /// Ненулевые остатки за сотрудниками (с сотрудником, позицией и единицей).
    /// Фильтры необязательны: сотрудник, позиция, текст (ФИО, артикул, наименование).
    /// </summary>
    /// <param name="onlyEmployeeIds">Ограничение видимости: только эти сотрудники (null — все).</param>
    Task<IReadOnlyList<EmployeeBalance>> ListCustodyAsync(
        int? employeeId, int? itemId, string? search, IReadOnlyCollection<int>? onlyEmployeeIds, CancellationToken ct = default);

    /// <summary>Выдачи сотрудникам (для расчёта, с какой даты числится подотчёт). null — всем сотрудникам.</summary>
    Task<IReadOnlyList<IssueRecord>> ListIssuesAsync(IReadOnlyCollection<int>? employeeIds, CancellationToken ct = default);

    /// <summary>Зарезервировано актами на утверждении: по сотруднику и позиции. null — всем сотрудникам.</summary>
    Task<IReadOnlyList<ReservedRecord>> ListReservedAsync(IReadOnlyCollection<int>? employeeIds, CancellationToken ct = default);

    /// <summary>Пары «сотрудник → руководитель» всех сотрудников (для обхода иерархии).</summary>
    Task<IReadOnlyList<(int Id, int? ManagerId)>> ListHierarchyAsync(CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>Доступ к данным складов и мест хранения.</summary>
public interface ILocationRepository
{
    Task<Warehouse?> GetWarehouseAsync(int id, CancellationToken ct = default);

    Task<IReadOnlyList<Warehouse>> ListWarehousesAsync(CancellationToken ct = default);

    Task<bool> WarehouseCodeExistsAsync(string code, CancellationToken ct = default);

    void AddWarehouse(Warehouse warehouse);

    Task<StorageLocation?> GetLocationAsync(int id, CancellationToken ct = default);

    Task<StorageLocation?> GetLocationByFullCodeAsync(string fullCode, CancellationToken ct = default);

    /// <summary>Места по идентификаторам (со складом).</summary>
    Task<IReadOnlyList<StorageLocation>> GetLocationsAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default);

    /// <summary>Все места склада одним списком (дерево собирает сервис).</summary>
    Task<IReadOnlyList<StorageLocation>> ListLocationsAsync(int warehouseId, CancellationToken ct = default);

    Task<bool> FullCodeExistsAsync(string fullCode, CancellationToken ct = default);

    /// <summary>Есть ли на месте ненулевой остаток хотя бы одной позиции.</summary>
    Task<bool> LocationHasStockAsync(int locationId, CancellationToken ct = default);

    /// <summary>Ненулевые остатки на месте (с позицией и единицей).</summary>
    Task<IReadOnlyList<StockBalance>> GetLocationContentAsync(int locationId, CancellationToken ct = default);

    void AddLocation(StorageLocation location);

    Task SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>Доступ к запросам на выдачу.</summary>
public interface IIssueRequestRepository
{
    void Add(IssueRequest request);

    /// <summary>Запрос с сотрудником, строками (позиция, единица) и документом выдачи (со строками).</summary>
    Task<IssueRequest?> GetAsync(int id, CancellationToken ct = default);

    /// <param name="employeeIds">Только запросы этих сотрудников; null — все.</param>
    Task<IReadOnlyList<IssueRequest>> ListAsync(IReadOnlyCollection<int>? employeeIds, IssueRequestStatus? status, int take, CancellationToken ct = default);

    Task<int> CountOpenAsync(CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>Доступ к актам на списание.</summary>
public interface IWriteOffActRepository
{
    /// <summary>Акт (отслеживаемый) с сотрудником и его руководителем, строками (позиция, единица, оборудование) и документом.</summary>
    Task<WriteOffAct?> GetAsync(int id, CancellationToken ct = default);

    /// <summary>Акты по фильтру (с сотрудником и руководителем), новые первыми.</summary>
    Task<IReadOnlyList<ActHeader>> ListAsync(ActFilter filter, CancellationToken ct = default);

    /// <summary>Сколько актов ждут утверждения этим сотрудником.</summary>
    /// <summary>
    /// Отчёты об использовании для сводного акта: подтверждённые, с несписанными учётными строками,
    /// ещё не включённые в другой сводный акт (включённые в <paramref name="summaryId"/> — тоже).
    /// Со строками, партиями, позициями и сотрудником.
    /// </summary>
    Task<IReadOnlyList<WriteOffAct>> ListSummaryCandidatesAsync(int? summaryId, CancellationToken ct = default);

    /// <summary>Отчёты по номерам — со строками (для включения в сводный акт).</summary>
    Task<IReadOnlyList<WriteOffAct>> GetManyAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default);

    /// <summary>Сколько ждут решения: акты, которые утверждает сотрудник, и (для кладовщика) отчёты на подтверждении.</summary>
    Task<int> CountToApproveAsync(int? approverId, bool reportsToConfirm, CancellationToken ct = default);

    void Add(WriteOffAct act);

    void Remove(WriteOffAct act);

    /// <summary>Сохраняет все изменения (акт и проведённый документ) одной транзакцией.</summary>
    Task SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>Настройки приложения (по разделам, значение — JSON).</summary>
public interface ISettingsRepository
{
    Task<AppSetting?> GetAsync(string key, CancellationToken ct = default);

    void Add(AppSetting setting);

    Task SaveChangesAsync(CancellationToken ct = default);
}

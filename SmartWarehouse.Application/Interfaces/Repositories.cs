using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;

namespace SmartWarehouse.Application.Interfaces;

/// <summary>Позиция номенклатуры с суммарным остатком по всем местам.</summary>
public record ItemStock(Item Item, decimal Total);

/// <summary>Шапка документа с числом строк — для списков.</summary>
public record DocumentHeader(StockDocument Document, int LineCount);

/// <summary>Нормализованные фильтры поиска остатков для репозитория.</summary>
public record StockFilter(int? WarehouseId, string? LocationCode, string? Search, int? CategoryId, bool BelowMinimum);

/// <summary>Краткие сведения о месте хранения для проверок при проведении документа.</summary>
public record LocationInfo(int Id, string FullCode, bool HasChildren);

/// <summary>Выдача позиции сотруднику (строка документа выдачи).</summary>
public record IssueRecord(int EmployeeId, int ItemId, DateTimeOffset OccurredAt, decimal Quantity);

/// <summary>Сколько позиции за сотрудником в актах на утверждении.</summary>
public record ReservedRecord(int EmployeeId, int ItemId, decimal Quantity);

/// <summary>Фильтр списка актов на списание.</summary>
/// <param name="EmployeeIds">Только акты этих сотрудников (null — всех).</param>
/// <param name="ApproverId">Только акты на утверждении, которые утверждает этот сотрудник.</param>
public record ActFilter(IReadOnlyCollection<int>? EmployeeIds, int? ApproverId, ActStatus? Status, int? EmployeeId, int Take);

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

    /// <summary>Остаток позиции на месте (отслеживаемая сущность) или null, если записи ещё нет.</summary>
    Task<StockBalance?> GetBalanceAsync(int itemId, int locationId, CancellationToken ct = default);

    void AddBalance(StockBalance balance);

    /// <summary>Сколько позиции числится за сотрудником (отслеживаемая сущность) или null.</summary>
    Task<EmployeeBalance?> GetEmployeeBalanceAsync(int itemId, int employeeId, CancellationToken ct = default);

    void AddEmployeeBalance(EmployeeBalance balance);

    /// <summary>
    /// Сколько позиции за сотрудником зарезервировано актами на утверждении
    /// (кроме акта <paramref name="exceptActId"/>, который сейчас утверждается).
    /// </summary>
    Task<decimal> GetReservedAsync(int itemId, int employeeId, int? exceptActId, CancellationToken ct = default);

    void AddDocument(StockDocument document);

    /// <summary>Документ со строками, позициями, единицами и местами.</summary>
    Task<StockDocument?> GetDocumentAsync(long id, CancellationToken ct = default);

    /// <summary>Последние документы, новые первыми.</summary>
    Task<IReadOnlyList<DocumentHeader>> ListDocumentsAsync(MovementType? type, int take, CancellationToken ct = default);

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

    Task<IReadOnlyList<Item>> ListItemsAsync(string? search, int? categoryId, CancellationToken ct = default);

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

/// <summary>Доступ к актам на списание.</summary>
public interface IWriteOffActRepository
{
    /// <summary>Акт (отслеживаемый) с сотрудником и его руководителем, строками (позиция, единица, оборудование) и документом.</summary>
    Task<WriteOffAct?> GetAsync(int id, CancellationToken ct = default);

    /// <summary>Акты по фильтру (с сотрудником и руководителем), новые первыми.</summary>
    Task<IReadOnlyList<ActHeader>> ListAsync(ActFilter filter, CancellationToken ct = default);

    /// <summary>Сколько актов ждут утверждения этим сотрудником.</summary>
    Task<int> CountToApproveAsync(int approverId, CancellationToken ct = default);

    void Add(WriteOffAct act);

    void Remove(WriteOffAct act);

    /// <summary>Сохраняет все изменения (акт и проведённый документ) одной транзакцией.</summary>
    Task SaveChangesAsync(CancellationToken ct = default);
}

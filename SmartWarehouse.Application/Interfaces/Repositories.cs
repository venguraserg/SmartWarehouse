using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;

namespace SmartWarehouse.Application.Interfaces;

/// <summary>Позиция номенклатуры с суммарным остатком по всем местам.</summary>
public record ItemStock(Item Item, decimal Total);

/// <summary>Шапка документа с числом строк — для списков.</summary>
public record DocumentHeader(StockDocument Document, int LineCount);

/// <summary>Краткие сведения о месте хранения для проверок при проведении документа.</summary>
public record LocationInfo(int Id, string FullCode, bool HasChildren);

/// <summary>Доступ к данным для складских документов и остатков.</summary>
public interface IStockRepository
{
    /// <summary>Позиции по идентификаторам (с единицей измерения). Отсутствующих в результате нет.</summary>
    Task<IReadOnlyDictionary<int, Item>> GetItemsAsync(IEnumerable<int> ids, CancellationToken ct = default);

    Task<bool> ItemExistsAsync(int id, CancellationToken ct = default);

    Task<IReadOnlyDictionary<int, LocationInfo>> GetLocationsAsync(IEnumerable<int> ids, CancellationToken ct = default);

    Task<bool> EquipmentExistsAsync(int equipmentId, CancellationToken ct = default);

    /// <summary>Остаток позиции на месте (отслеживаемая сущность) или null, если записи ещё нет.</summary>
    Task<StockBalance?> GetBalanceAsync(int itemId, int locationId, CancellationToken ct = default);

    void AddBalance(StockBalance balance);

    void AddDocument(StockDocument document);

    /// <summary>Документ со строками, позициями, единицами и местами.</summary>
    Task<StockDocument?> GetDocumentAsync(long id, CancellationToken ct = default);

    /// <summary>Последние документы, новые первыми.</summary>
    Task<IReadOnlyList<DocumentHeader>> ListDocumentsAsync(MovementType? type, int take, CancellationToken ct = default);

    /// <summary>Остатки позиции по местам (с местом), без нулевых.</summary>
    Task<IReadOnlyList<StockBalance>> GetBalancesForItemAsync(int itemId, CancellationToken ct = default);

    /// <summary>Последние движения позиции (с документом и местами), новые первыми.</summary>
    Task<IReadOnlyList<StockMovement>> GetMovementsForItemAsync(int itemId, int take, CancellationToken ct = default);

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

    Task<IReadOnlyList<Item>> ListItemsAsync(string? search, int? categoryId, CancellationToken ct = default);

    void AddItem(Item item);

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

/// <summary>Доступ к данным складов и мест хранения.</summary>
public interface ILocationRepository
{
    Task<Warehouse?> GetWarehouseAsync(int id, CancellationToken ct = default);

    Task<IReadOnlyList<Warehouse>> ListWarehousesAsync(CancellationToken ct = default);

    Task<bool> WarehouseCodeExistsAsync(string code, CancellationToken ct = default);

    void AddWarehouse(Warehouse warehouse);

    Task<StorageLocation?> GetLocationAsync(int id, CancellationToken ct = default);

    Task<StorageLocation?> GetLocationByFullCodeAsync(string fullCode, CancellationToken ct = default);

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
